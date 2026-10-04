using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using UnityEngine;

namespace NivalisModKit;

// Dev bridge: an HTTP endpoint on 127.0.0.1 so tools (and Claude Code) can query the running game, and, with
// [DevBridge] AllowCommands, run DevCommands. Off by default. Requests are parsed on a worker thread; anything that
// touches the game runs on the main thread through KitLoop, and the worker waits for the answer.
// GET paths only read. POST /cmd/<name> runs a command and needs the X-Kit-Token header (a random token written to
// BepInEx\cache at each start): web pages can't send custom headers to localhost, so a website can't run commands.
internal static class Bridge
{
    const int MaxRequestBytes = 8192;
    const int MainThreadTimeoutMs = 3000;

    static TcpListener listener;
    static int port;
    static string token;

    internal static bool Running => listener != null;

    /// <summary>The file holding this session's command token.</summary>
    internal static string TokenPath => System.IO.Path.Combine(BepInEx.Paths.CachePath, "nivalismodkit-bridge.token");
    static readonly ConcurrentQueue<(Func<object> work, TaskCompletionSource<object> done)> pending = new();
    // Relaxed escaping keeps apostrophes and generic backticks readable; output is never put in HTML.
    static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly Dictionary<string, (string help, Func<Dictionary<string, string>, object> handler)> routes = new()
    {
        ["/"] = ("This list", _ => routes.ToDictionary(r => r.Key, r => r.Value.help)),
        ["/status"] = ("Kit and game version, game time, event status", _ => Status()),
        ["/events"] = ("How often each kit event fired, and when last", _ => Events()),
        ["/time"] = ("Game day, clock and day of week", _ => new
        {
            day = GameTime.Day, time = $"{GameTime.Hour:00}:{GameTime.Minute:00}",
            dayOfWeek = GameTime.DayOfWeek.ToString(), totalHours = GameTime.TotalHours,
            paused = GameClock.IsPaused, clockSpeed = GameClock.ClockSpeed, simulationSpeed = GameClock.SimulationSpeed,
            baseGameSecondsPerSecond = GameClock.BaseGameSecondsPerSecond,
            clockFactors = GameClock.ClockFactors, simulationFactors = GameClock.SimulationFactors,
        }),
        ["/money"] = ("The player's money", _ => new { money = Economy.PlayerMoney }),
        ["/quests"] = ("Active and completed quests", _ => QuestList()),
        ["/venues"] = ("All venues; ?owned=1 for the player's", q => VenueList(q.ContainsKey("owned"))),
        ["/vendors"] = ("?item=Name: vendors selling it, with price, stock, district, hops from you", q => VendorList(q)),
        ["/items"] = ("All item names; ?name= to look one up", q => ItemList(q)),
        ["/recipes"] = ("Recipes with inputs; ?known=1 for discovered ones", q => RecipeList(q.ContainsKey("known"))),
        ["/restock"] = ("Per owned venue: the shopping list's low ingredients and active venue setup quest objectives", _ => Restock()),
        ["/ui"] = ("Open UI panels; ?panel=TypeName lists its buttons, &texts=1 its labels, &rects=1&path=Child/Path the layout of that child's children", q =>
            q.TryGetValue("panel", out var panelName) && panelName != ""
                ? (q.ContainsKey("texts") ? UiTexts(panelName) : q.ContainsKey("rects") ? UiRects(panelName, q) : (object)UiButtons(panelName))
                : new
                {
                    visible = Ui.IsVisible, dialogOpen = Ui.IsDialogOpen,
                    open = Ui.OpenPanels.Select(p => new { type = Ui.NameOf(p), gameObject = Try(() => p.gameObject.name) }).ToArray(),
                }),
        ["/priorities"] = ("Action priority lists: each character type's task order, first = most important", _ =>
            Staff.PriorityLists.Select(l => new
            {
                list = Try(() => l.name),
                order = Staff.Order(l).Select(Staff.NameOf).ToArray(),
            }).ToArray()),
        ["/perf"] = ("Frame timing ([Debug] FrameTiming): slow frames, and time spent in the kit per event/hook", _ => Perf.Summary()),
        ["/security"] = ("Curfew, awareness, caught, security level", _ => new
        {
            curfew = Security.IsCurfew, securityActive = Security.IsSecurityActive,
            awareness = Security.Awareness, caught = Security.IsCaught, level = Security.Level,
        }),
        ["/districts"] = ("Districts, with hops from the current one", _ => Districts()),
        ["/object"] = ("?type=Full.Type.Name: fields and properties of the first live instance", q => Inspect(q)),
        ["/cmd"] = ("Dev commands (POST /cmd/name?arg=value with the X-Kit-Token header; needs [DevBridge] AllowCommands)", _ => new
        {
            enabled = DevCommands.Enabled,
            tokenFile = TokenPath,
            commands = DevCommands.All.Select(c => new { name = c.Name, help = c.Help, owner = c.Owner }).ToArray(),
        }),
    };

    internal static void Start(int listenPort)
    {
        port = listenPort;
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        listener = l;
        token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        try
        {
            System.IO.Directory.CreateDirectory(BepInEx.Paths.CachePath);
            System.IO.File.WriteAllText(TokenPath, token);
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Dev bridge: could not write the command token ({e.Message}); commands unavailable"); token = null; }
        KitLoop.Tick += RunPending;
        new Thread(AcceptLoop) { IsBackground = true, Name = "NivalisModKit bridge" }.Start();
        KitPlugin.L.LogInfo($"Dev bridge: listening on http://127.0.0.1:{port}/ " +
                            (KitPlugin.BridgeCommands.Value ? "(commands on: POST /cmd/name)" : "(read-only)"));
    }

    // ---------- worker thread ----------

    static void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception e) { KitPlugin.L.LogError($"Dev bridge stopped: {e.Message}"); return; }
            ThreadPool.QueueUserWorkItem(_ => Handle(client));
        }
    }

    static void Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 2000;
                var stream = client.GetStream();
                string request = ReadHead(stream);
                if (request == null) { Send(stream, 400, new { error = "bad request" }); return; }

                var lines = request.Split("\r\n");
                var parts = lines[0].Split(' ');
                if (parts.Length < 2 || (parts[0] != "GET" && parts[0] != "POST")) { Send(stream, 405, new { error = "GET, or POST for /cmd" }); return; }
                string Header(string name) => lines.Skip(1)
                    .FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?.Substring(name.Length + 1).Trim();

                // Only answer requests addressed to this machine by name; blocks DNS rebinding from web pages.
                string host = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    ?.Substring(5).Trim() ?? "";
                string hostName = host.Split(':')[0];
                if (hostName != "127.0.0.1" && hostName != "localhost") { Send(stream, 403, new { error = "bad host" }); return; }

                var (path, query) = SplitUrl(parts[1]);
                Func<object> work;
                if (parts[0] == "POST")
                {
                    // Commands: POST /cmd/<name>, opted in, with this session's token, and not from a web page.
                    if (!path.StartsWith("/cmd/", StringComparison.Ordinal)) { Send(stream, 405, new { error = "POST is only for /cmd/name" }); return; }
                    if (!KitPlugin.BridgeCommands.Value) { Send(stream, 403, new { error = "commands are off: set [DevBridge] AllowCommands = true" }); return; }
                    if (Header("Origin") != null) { Send(stream, 403, new { error = "not from a browser" }); return; }
                    if (token == null || Header("X-Kit-Token") != token) { Send(stream, 403, new { error = "missing or wrong X-Kit-Token", tokenFile = TokenPath }); return; }
                    string name = path.Substring("/cmd/".Length);
                    work = () => DevCommands.Run(name, query);
                }
                else
                {
                    if (!routes.TryGetValue(path, out var route)) { Send(stream, 404, new { error = "no such endpoint", see = "/" }); return; }
                    work = () => route.handler(query);
                }

                var done = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Enqueue((work, done));
                if (!done.Task.Wait(MainThreadTimeoutMs)) { Send(stream, 503, new { error = "game didn't answer in time" }); return; }
                Send(stream, 200, done.Task.Result);
            }
            catch (Exception e)
            {
                try { Send(client.GetStream(), 500, new { error = e.InnerException?.Message ?? e.Message }); } catch { }
            }
        }
    }

    static string ReadHead(NetworkStream stream)
    {
        var buffer = new byte[MaxRequestBytes];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
            string text = Encoding.ASCII.GetString(buffer, 0, read);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text.Substring(0, end);
        }
        return null;
    }

    static (string path, Dictionary<string, string> query) SplitUrl(string url)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int q = url.IndexOf('?');
        string path = q < 0 ? url : url.Substring(0, q);
        if (q >= 0)
            foreach (var pair in url.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string k = Uri.UnescapeDataString(eq < 0 ? pair : pair.Substring(0, eq));
                query[k] = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        return (path.TrimEnd('/') is "" ? "/" : path.TrimEnd('/'), query);
    }

    static void Send(NetworkStream stream, int status, object body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, json));
        string reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found",
            405 => "Method Not Allowed", 503 => "Service Unavailable", _ => "Error" };
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\n" +
                      $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(payload, 0, payload.Length);
    }

    // ---------- main thread ----------

    static void RunPending()
    {
        while (pending.TryDequeue(out var item))
        {
            try { item.done.SetResult(item.work()); }
            catch (Exception e) { item.done.SetException(e); }
        }
    }

    static string Name(Il2CppSystem.Object o)
    {
        if (o == null) return null;
        try
        {
            string s = o.ToString();
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return "?"; }
    }

    static object Status()
    {
        string gameVersion = null;
        try { gameVersion = Application.version; } catch { }
        return new
        {
            kit = ModKit.Version,
            game = gameVersion,
            gameBuild = GameBuild.Describe(),
            buildTested = GameBuild.IsTested,
            buildFingerprint = GameBuild.Fingerprint,
            steamBuild = GameBuild.SteamBuildId,
            day = Try(() => TimeOfDayManager.GameplayGameDay),
            time = Try(() => $"{TimeOfDayManager.ClockHour:00}:{TimeOfDayManager.ClockMinute:00}"),
            purchasingPipeline = Purchasing.IsAvailable,
            eventsLive = GameEvents.Live.OrderBy(e => e).ToArray(),
        };
    }

    static object Events() =>
        GameEvents.Fired.OrderBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => new { kv.Value.count, last = kv.Value.last.ToString("HH:mm:ss") });

    static object QuestList()
    {
        object Describe(RuntimeQuest rq) => new
        {
            id = Try(() => rq.Quest?.Guid),
            title = Try(() => rq.Quest?.Title),
            number = Try(() => rq.QuestNumber),
            state = Try(() => rq.currentState.ToString()),
            pinned = Try(() => rq.Pinned),
        };
        return new
        {
            active = Quests.Active.Select(Describe).ToArray(),
            completed = Quests.Completed.Select(Describe).ToArray(),
        };
    }

    static object UiButtons(string panelName)
    {
        var panel = Ui.Find(panelName);
        if (panel == null) return new { error = $"no panel named {panelName} (see /ui for open ones)" };
        return new
        {
            panel = panelName, visible = Try(() => panel.IsVisible),
            buttons = Ui.ButtonsIn(panel).Select(b => new { path = b.path, label = b.label }).ToArray(),
        };
    }

    // Layout of a container's children (default: the parent of the panel's first button).
    static object UiRects(string panelName, Dictionary<string, string> q)
    {
        var panel = Ui.Find(panelName);
        if (panel == null) return new { error = $"no panel named {panelName} (see /ui for open ones)" };
        UnityEngine.Transform container = null;
        if (q.TryGetValue("path", out var path) && path == ".") container = panel.transform;
        else if (q.TryGetValue("path", out path) && path != "") container = panel.transform.Find(path);
        else container = Ui.ButtonsIn(panel).FirstOrDefault().button?.transform.parent;
        if (container == null) return new { error = "container not found; pass &path=Child/Path" };
        var children = new List<object>();
        for (int i = 0; i < container.childCount; i++)
        {
            var c = container.GetChild(i);
            var rt = c.TryCast<UnityEngine.RectTransform>();
            children.Add(new
            {
                index = i, name = c.name, active = Try(() => c.gameObject.activeSelf),
                pos = rt == null ? null : $"{rt.anchoredPosition.x:0.0},{rt.anchoredPosition.y:0.0}",
                size = rt == null ? null : $"{rt.rect.width:0}x{rt.rect.height:0}",
                pivot = rt == null ? null : $"{rt.pivot.x:0.##},{rt.pivot.y:0.##}",
                rotation = $"{c.localEulerAngles.x:0},{c.localEulerAngles.y:0},{c.localEulerAngles.z:0}",
                scale = $"{c.localScale.x:0.##},{c.localScale.y:0.##}",
                components = Try(() => string.Join(", ", c.GetComponents<UnityEngine.Component>().Select(k => k.GetIl2CppType().Name))),
            });
        }
        var crt = container.TryCast<UnityEngine.RectTransform>();
        return new
        {
            panel = panelName, container = container.name,
            containerSize = crt == null ? null : $"{crt.rect.width:0}x{crt.rect.height:0}",
            containerComponents = Try(() => string.Join(", ", container.GetComponents<UnityEngine.Component>().Select(k => k.GetIl2CppType().Name))),
            children,
        };
    }

    static object UiTexts(string panelName)
    {
        var panel = Ui.Find(panelName);
        if (panel == null) return new { error = $"no panel named {panelName} (see /ui for open ones)" };
        return new
        {
            panel = panelName, visible = Try(() => panel.IsVisible),
            texts = Ui.TextsIn(panel).Select(t => new { path = t.path, text = t.value, active = Try(() => t.text.gameObject.activeInHierarchy) }).ToArray(),
        };
    }

    static object VenueList(bool ownedOnly) =>
        (ownedOnly ? Venues.PlayerOwned : Venues.All).Select(a => new
        {
            venue = Venues.NameOf(a),
            playerOwned = Try(() => a.PlayerOwned),
            district = World.NameOf(Venues.DistrictOf(a)),
            isOpen = Try(() => a.IsOpen?.Value ?? false),
            inStaffHours = Try(() => a.IsInStaffHours),
            storage = Venues.StorageOf(a) is VenueStorage st
                ? $"{st.Normal}/{st.NormalCapacity?.ToString() ?? "-"} normal, {st.Refrigerated}/{st.RefrigeratedCapacity?.ToString() ?? "-"} fridge"
                : null,
        }).ToArray();

    static object VendorList(Dictionary<string, string> query)
    {
        if (!query.TryGetValue("item", out var name) || name == "") return new { error = "pass ?item=Name" };
        var item = Items.ByName(name);
        if (item == null) return new { error = $"no item named {name}" };

        WorldLocation here = null;
        try { if (Singleton<GameSceneManager>.InstanceExist(out var gsm)) here = gsm.CurrentWorldLocation; } catch { }
        return new
        {
            item = Items.NameOf(item),
            vendors = Economy.VendorsFor(item).Select(v => new
            {
                vendor = Name(v),
                district = World.NameOf(Economy.DistrictOf(v)),
                hops = here == null ? (int?)null : World.Hops(here, Economy.DistrictOf(v)),
                price = Economy.Price(v, item),
                stock = Economy.Stock(v, item),
                unlocked = Economy.IsUnlocked(v),
            }).OrderBy(v => v.price ?? int.MaxValue).ToArray(),
        };
    }

    static object ItemList(Dictionary<string, string> query)
    {
        if (query.TryGetValue("name", out var name) && name != "")
        {
            var item = Items.ByName(name);
            return item == null ? new { error = $"no item named {name}" }
                : new { name = Items.NameOf(item), id = Try(() => item.Guid), vendors = Economy.VendorsFor(item).Count };
        }
        return Items.All.Select(Items.NameOf).Where(n => n != null).OrderBy(n => n).ToArray();
    }

    static object RecipeList(bool knownOnly) =>
        (knownOnly ? Recipes.Known : Recipes.All).Select(r => new
        {
            dish = Items.NameOf(Recipes.OutputOf(r)),
            inputs = Recipes.InputsOf(r).Select(i => $"{Items.NameOf(i.Item)} x{i.Amount}").ToArray(),
        }).OrderBy(r => r.dish).ToArray();

    // The game's runtime class name of an IL2CPP object (the interop wrapper type may be a base class).
    static string ClassName(Il2CppSystem.Object o)
    {
        try
        {
            if (o == null) return null;
            IntPtr klass = IL2CPP.il2cpp_object_get_class(o.Pointer);
            return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass));
        }
        catch { return null; }
    }

    static object Restock()
    {
        var low = new Dictionary<string, object>();
        try
        {
            if (Singleton<ShoppingListManager>.InstanceExist(out var sl) && sl.VenuesLowOnIngredientsMap != null)
                foreach (var kv in sl.VenuesLowOnIngredientsMap)
                {
                    var items = new List<string>();
                    if (kv.Value?.LowIngredients != null)
                        foreach (var li in kv.Value.LowIngredients)
                            items.Add($"{Items.NameOf(li.Key)} {li.Value.amount}/{li.Value.demand}");
                    low[Name(kv.Key) ?? "?"] = items.OrderBy(i => i).ToArray();
                }
        }
        catch (Exception e) { low["error"] = e.Message; }

        var quests = new List<object>();
        try
        {
            if (Singleton<Nivalis.VenueSupplyQuest.VenueSetupManager>.InstanceExist(out var vsm) && vsm._activeSetupQuests != null)
                foreach (var kv in vsm._activeSetupQuests)
                {
                    var q = kv.Value;
                    object Objectives(Il2CppSystem.Collections.Generic.List<Nivalis.VenueSupplyQuest.VenueSupplyObjective.RuntimeTracker> list)
                    {
                        var r = new List<object>();
                        if (list == null) return r;
                        foreach (var t in list)
                            r.Add(new
                            {
                                type = ClassName(Try(() => t.Objective)),
                                state = Try(() => t.State.ToString()),
                                text = Try(() => t.StateEntry?.GetText()),
                            });
                        return r;
                    }
                    quests.Add(new
                    {
                        venue = Name(kv.Key),
                        title = Try(() => q?.Title),
                        state = Try(() => q?.State.ToString()),
                        active = Objectives(Try(() => q?._activeObjectives)),
                        completed = Objectives(Try(() => q?._completedObjectives)),
                    });
                }
        }
        catch (Exception e) { quests.Add(new { error = e.Message }); }

        return new { lowIngredients = low, setupQuests = quests };
    }

    static object Districts()
    {
        WorldLocation here = null;
        try { if (Singleton<GameSceneManager>.InstanceExist(out var gsm)) here = gsm.CurrentWorldLocation; } catch { }
        return new
        {
            current = World.NameOf(here),
            districts = World.Locations.Select(l => new
            {
                name = World.NameOf(l),
                hops = here == null ? (int?)null : World.Hops(here, l),
            }).OrderBy(d => d.hops is null or < 0 ? int.MaxValue : d.hops.Value).ToArray(),   // unreachable last
        };
    }

    // Reads public instance properties of the interop wrapper: IL2CPP fields and C# properties.
    static object Inspect(Dictionary<string, string> query)
    {
        if (!query.TryGetValue("type", out var typeName) || typeName == "") return new { error = "pass ?type=Full.Type.Name" };
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType(typeName, false); } catch { return null; } })
            .FirstOrDefault(t => t != null);
        if (type == null) return new { error = $"type {typeName} not found" };
        if (!typeof(UnityEngine.Object).IsAssignableFrom(type)) return new { error = "only Unity objects (components, assets) can be looked up" };

        var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.From(type));
        if (found == null || found.Length == 0) return new { type = typeName, instances = 0 };

        var instance = Activator.CreateInstance(type, found[0].Pointer);
        var values = new SortedDictionary<string, string>();
        for (var t = type; t != null && t != typeof(UnityEngine.Object) && t != typeof(Il2CppObjectBase); t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length > 0 || values.ContainsKey(p.Name) || values.Count >= 300) continue;
                try
                {
                    object v = p.GetValue(instance);
                    values[p.Name] = v switch
                    {
                        null => "null",
                        Il2CppSystem.Object o => Name(o),
                        _ => v.ToString(),
                    };
                }
                catch (Exception e) { values[p.Name] = $"<{(e.InnerException ?? e).GetType().Name}>"; }
            }
        }
        return new { type = typeName, instances = found.Length, first = Name(found[0]), values };
    }

    static T Try<T>(Func<T> f)
    {
        try { return f(); } catch { return default; }
    }
}
