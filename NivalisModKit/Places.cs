using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;
using Nivalis.Apartment;
using Nivalis.Economy;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Navigation;
using Nivalis.UI;
using UnityEngine;

namespace NivalisModKit;

/// <summary>Kinds of place the game's compass can point at.</summary>
[Experimental("New in 0.4.")]
public enum PlaceKind
{
    /// <summary>An apartment (by default only the player's: owned or rented).</summary>
    Apartment,
    /// <summary>A shelter: a free apartment to sleep in.</summary>
    Shelter,
    /// <summary>A venue (see <see cref="Venues.Entrances"/>; the compass itself rarely points at them).</summary>
    Venue,
    /// <summary>A greenhouse.</summary>
    Greenhouse,
    /// <summary>A travel point (taxi rank, portal) to another district.</summary>
    TravelPoint,
    /// <summary>A boat.</summary>
    Boat,
    /// <summary>A train.</summary>
    Train,
    /// <summary>A lift.</summary>
    Lift,
}

/// <summary>A place in the loaded scene: see <see cref="World.Places"/>.</summary>
[Experimental("New in 0.4.")]
public sealed class Place
{
    /// <summary>What kind of place.</summary>
    public PlaceKind Kind { get; internal set; }
    /// <summary>Its transform (it may be switched off while far from the player; the position still holds).</summary>
    public Transform Transform { get; internal set; }
    /// <summary>Its position.</summary>
    public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;
    /// <summary>A short name: "Train", "Shelter", "Your apartment".</summary>
    public string Name { get; internal set; }
    /// <summary>Travel places: the district it takes you to, else null.</summary>
    public WorldLocation Destination { get; internal set; }
    /// <summary>Homes: the apartment, else null.</summary>
    public Apartment Apartment { get; internal set; }
    /// <summary>The compass's icon for this kind (may be null).</summary>
    public Sprite Icon { get; internal set; }
    /// <summary>The compass's colour for this kind.</summary>
    public Color Colour { get; internal set; }

    /// <summary>The name with its destination: "Train > Docks".</summary>
    public override string ToString() =>
        Destination != null && World.NameOf(Destination) is { Length: > 0 } where ? $"{Name} > {where}" : Name;
}

public static partial class World
{
    static readonly Dictionary<NavigationMarkerType, PlaceKind> PlaceKinds = new()
    {
        [NavigationMarkerType.Apartment] = PlaceKind.Apartment,
        [NavigationMarkerType.ApartmentCurfew] = PlaceKind.Apartment,
        [NavigationMarkerType.Shelter] = PlaceKind.Shelter,
        [NavigationMarkerType.ShelterCurfew] = PlaceKind.Shelter,
        [NavigationMarkerType.Venue] = PlaceKind.Venue,
        [NavigationMarkerType.Greenhouse] = PlaceKind.Greenhouse,
        [NavigationMarkerType.TravelPoint] = PlaceKind.TravelPoint,
        [NavigationMarkerType.Boat] = PlaceKind.Boat,
        [NavigationMarkerType.Train] = PlaceKind.Train,
        [NavigationMarkerType.Lift] = PlaceKind.Lift,
    };

    static readonly Dictionary<PlaceKind, string> PlaceNames = new()
    {
        [PlaceKind.Apartment] = "Your apartment", [PlaceKind.Shelter] = "Shelter", [PlaceKind.Venue] = "Venue",
        [PlaceKind.Greenhouse] = "Greenhouse", [PlaceKind.TravelPoint] = "Travel point", [PlaceKind.Boat] = "Boat",
        [PlaceKind.Train] = "Train", [PlaceKind.Lift] = "Lift",
    };

    /// <summary>
    /// Every place in the loaded scene the game's compass can point at, near or far (the compass itself only lists what's
    /// near the player): travel points, trains, boats and lifts with their destinations, greenhouses, shelters, and the
    /// player's apartments (others too with <paramref name="allApartments"/>; every apartment door has a marker, rented or
    /// not). Two sources: scene transitions (doors, taxis, trains, boats, lifts) carry their compass type and a portal key
    /// naming where they go; apartment doors carry CompassMarker components. A full scene search: call it when a scene
    /// loads (it fills in over a few seconds), not every frame.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static List<Place> Places(bool allApartments = false)
    {
        var places = new List<Place>();
        var apartments = new HashSet<IntPtr>();
        try
        {
            foreach (var portal in Scenes.Objects<SceneTransitionInteractableBase>(includeInactive: true))
            {
                NavigationMarkerType type;
                PortalKey key;
                try { type = portal.compassMarkerType; key = portal.keyTo; } catch { continue; }
                if (!PlaceKinds.TryGetValue(type, out var kind)) continue;
                Apartment apartment = null;
                try { apartment = key?.apartment; } catch { }
                if (kind is PlaceKind.Apartment or PlaceKind.Shelter || apartment != null)
                {
                    if (!Home(apartment, allApartments, out kind) || !apartments.Add(apartment.Pointer)) continue;
                    places.Add(NewPlace(kind, portal.transform, null, apartment));
                }
                else
                {
                    WorldLocation where = null;
                    try { where = key?.targetLocation; } catch { }
                    places.Add(NewPlace(kind, portal.transform, where, null));
                }
            }
            foreach (var marker in Scenes.Objects<CompassMarker>(includeInactive: true))
            {
                NavigationMarkerType type;
                try { type = marker.type; } catch { continue; }
                if (!PlaceKinds.TryGetValue(type, out var kind)) continue;
                Apartment apartment = null;
                if (kind is PlaceKind.Apartment or PlaceKind.Shelter)
                {
                    apartment = ApartmentAbove(marker.transform);
                    if (!Home(apartment, allApartments, out kind) || !apartments.Add(apartment.Pointer)) continue;
                }
                places.Add(NewPlace(kind, marker.transform, null, apartment));
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"World.Places: {e.Message}"); }
        return places;
    }

    /// <summary>The compass's icon for a kind of place (null if it has none), and its colour.</summary>
    [Experimental("New in 0.4.")]
    public static Sprite CompassIcon(PlaceKind kind, out Color colour)
    {
        var type = PlaceKinds.First(kv => kv.Value == kind).Key;
        return CompassLook(type, out colour);
    }

    internal static Sprite CompassLook(NavigationMarkerType type, out Color colour)
    {
        colour = new Color(0.8f, 0.8f, 0.8f);
        try
        {
            if (Singleton<NavigationManager>.InstanceExist(out var nav) && nav?.navigationDisplayDictionary != null &&
                nav.navigationDisplayDictionary.TryGetValue(type, out var pip) && pip != null)
            {
                if (pip.color.a > 0f) colour = pip.color;
                return pip.icon;
            }
        }
        catch { }
        return null;
    }

    static Place NewPlace(PlaceKind kind, Transform t, WorldLocation destination, Apartment apartment)
    {
        var icon = CompassIcon(kind, out var colour);
        string name = PlaceNames[kind];
        if (kind == PlaceKind.Apartment && apartment != null && !IsPlayers(apartment)) name = "Apartment";
        return new Place
        {
            Kind = kind, Transform = t, Name = name, Destination = destination, Apartment = apartment,
            Icon = icon, Colour = colour,
        };
    }

    // A home worth listing: a shelter, or an apartment the player owns or rents (any apartment with all).
    static bool Home(Apartment apartment, bool all, out PlaceKind kind)
    {
        kind = PlaceKind.Apartment;
        if (apartment == null) return false;
        try { if (apartment.shelter) { kind = PlaceKind.Shelter; return true; } } catch { }
        return all || IsPlayers(apartment);
    }

    /// <summary>True if the player owns or rents the property (an apartment or a venue).</summary>
    [Experimental("New in 0.4.")]
    public static bool IsPlayers(BaseProperty property)
    {
        if (property == null) return false;
        try
        {
            string id = property.Guid;
            if (Singleton<PlayerManager>.InstanceExist(out var pm) && pm.LocalPlayer?._ownedProperties != null &&
                pm.LocalPlayer._ownedProperties.Contains(id)) return true;
            // Rented by the player: the rent records include other tenants, so check whose rent it is.
            if (Singleton<RentManager>.InstanceExist(out var rm) && rm.rents != null && rm.rents.TryGetValue(id, out var rent) &&
                rent?.rentier != null && pm?.LocalPlayer != null)
            {
                var renter = rent.rentier.TryCast<PlayerManager.Player>();
                if (renter != null && renter.Pointer == pm.LocalPlayer.Pointer) return true;
            }
        }
        catch { }
        return false;
    }

    // The apartment an apartment door's marker belongs to: an ApartmentKeyMarkerController above it.
    static Apartment ApartmentAbove(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            var controller = p.GetComponent<ApartmentKeyMarkerController>();
            if (controller != null)
            {
                try { return controller.apartment; } catch { return null; }
            }
        }
        return null;
    }
}

/// <summary>A vendor's stall in the loaded scene: see <see cref="Economy.Stalls"/>.</summary>
[Experimental("New in 0.4.")]
public sealed class Stall
{
    /// <summary>The vendor.</summary>
    public Vendor Vendor { get; internal set; }
    /// <summary>The stall's transform (where the player talks to the vendor).</summary>
    public Transform Transform { get; internal set; }
    /// <summary>Its position.</summary>
    public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;
}

public static partial class Economy
{
    /// <summary>
    /// Every vendor stall in the loaded scene, near or far: where vendors physically stand. Stalls appear as a district
    /// fills in, so a search right after a scene loads may find fewer; search again a few seconds later. A full scene
    /// search: not every frame.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static List<Stall> Stalls()
    {
        var stalls = new List<Stall>();
        try
        {
            foreach (var stall in Scenes.Objects<VendorInteraction>(includeInactive: true))
            {
                Vendor vendor = null;
                try { vendor = stall.definition; } catch { }
                if (vendor != null) stalls.Add(new Stall { Vendor = vendor, Transform = stall.transform });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Economy.Stalls: {e.Message}"); }
        return stalls;
    }

    /// <summary>The vendor's stall in the loaded scene, or null (not in this scene).</summary>
    [Experimental("New in 0.4.")]
    public static Transform StallOf(Vendor vendor) =>
        vendor == null ? null : Stalls().FirstOrDefault(s => s.Vendor.Pointer == vendor.Pointer)?.Transform;
}

/// <summary>A venue's entrance in the loaded scene: see <see cref="Venues.Entrances"/>.</summary>
[Experimental("New in 0.4.")]
public sealed class VenueEntrance
{
    /// <summary>The venue.</summary>
    public Venue Venue { get; internal set; }
    /// <summary>The venue sign at its entrance.</summary>
    public Transform Transform { get; internal set; }
    /// <summary>Its position.</summary>
    public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;
    /// <summary>The venue's name as the game shows it.</summary>
    public string Name { get; internal set; }
    /// <summary>The player owns or rents it.</summary>
    public bool IsPlayers { get; internal set; }
    /// <summary>Not the player's and on offer (the game's IsAcquireable).</summary>
    public bool IsForSale { get; internal set; }
    /// <summary>Price to buy.</summary>
    public int BuyCost { get; internal set; }
    /// <summary>Rent.</summary>
    public int RentCost { get; internal set; }
}

public static partial class Venues
{
    /// <summary>
    /// Every venue's entrance in the loaded scene (its sign), near or far, with whether it's the player's or for sale, and
    /// its prices. A full scene search: not every frame.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static List<VenueEntrance> Entrances()
    {
        var list = new List<VenueEntrance>();
        try
        {
            foreach (var sign in Scenes.Objects<VenueSignInteraction>(includeInactive: true))
            {
                var venue = sign.venue;
                if (venue == null) continue;
                bool players = World.IsPlayers(venue) || PlayerOwned.Any(a => a?.Venue != null && a.Venue.Pointer == venue.Pointer);
                bool forSale = false;
                if (!players) { try { forSale = venue.IsAcquireable; } catch { } }
                int buy = 0, rent = 0;
                try { buy = venue.BuyCost; rent = venue.RentCost; } catch { }
                list.Add(new VenueEntrance
                {
                    Venue = venue, Transform = sign.transform, Name = DisplayNameOf(venue), IsPlayers = players,
                    IsForSale = forSale, BuyCost = buy, RentCost = rent,
                });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.Entrances: {e.Message}"); }
        return list;
    }

    /// <summary>The venue's entrance (sign) in the loaded scene, or null.</summary>
    [Experimental("New in 0.4.")]
    public static Transform EntranceOf(Venue venue) =>
        venue == null ? null : Entrances().FirstOrDefault(e => e.Venue.Pointer == venue.Pointer)?.Transform;

    /// <summary>
    /// A property's name as the game shows it (localized): a venue's, an apartment's. Falls back to its asset name,
    /// tidied ("_venue_noodlebar" becomes "Noodlebar"). <see cref="NameOf"/> gives the asset name itself.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static string DisplayNameOf(BaseProperty property)
    {
        if (property == null) return null;
        try
        {
            string shown = property.GetName();
            if (!string.IsNullOrWhiteSpace(shown)) return shown.Trim();
        }
        catch { }
        string asset = null;
        try { asset = property.name; } catch { }
        if (string.IsNullOrEmpty(asset)) return null;
        string s = System.Text.RegularExpressions.Regex.Replace(asset, "^_*venue_*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim();
        return s.Length == 0 ? asset : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    /// <summary>The venue's name as the game shows it; see <see cref="DisplayNameOf(BaseProperty)"/>.</summary>
    [Experimental("New in 0.4.")]
    public static string DisplayNameOf(VenueAreaGhost area) => DisplayNameOf(area?.Venue);
}

/// <summary>Where a quest points: see <see cref="Quests.Markers"/>.</summary>
[Experimental("New in 0.4.")]
public sealed class QuestMarker
{
    /// <summary>The quest.</summary>
    public RuntimeQuest Quest { get; internal set; }
    /// <summary>What the compass points at: the objective, or the way there (a portal) when it's in another district.</summary>
    public Transform Target { get; internal set; }
    /// <summary>Its position.</summary>
    public Vector3 Position => Target != null ? Target.position : Vector3.zero;
    /// <summary>The player has pinned (tracks) this quest.</summary>
    public bool Pinned { get; internal set; }
    /// <summary>The quest's number in the journal.</summary>
    public int Number { get; internal set; }
    /// <summary>The quest's title.</summary>
    public string Title { get; internal set; }
    /// <summary>The compass's quest icon (may be null).</summary>
    public Sprite Icon { get; internal set; }
    /// <summary>The compass's quest colour.</summary>
    public Color Colour { get; internal set; }
}

public static partial class Quests
{
    /// <summary>
    /// Where the game's compass points for quests now: each objective's target, or, when it's in another district, the
    /// portal towards it (the game routes markers onto portals). Pinned quests only unless <paramref name="pinnedOnly"/>
    /// is false. Cheap (reads the compass's list); fine once a second.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static List<QuestMarker> Markers(bool pinnedOnly = true)
    {
        var list = new List<QuestMarker>();
        try
        {
            var quests = new Dictionary<IntPtr, RuntimeQuest>();
            foreach (var rq in Active) if (rq?.Quest != null) quests[rq.Quest.Pointer] = rq;
            if (!Singleton<NavigationManager>.InstanceExist(out var nav) || nav?._markers == null) return list;
            var icon = World.CompassLook(NavigationMarkerType.Quest, out var colour);
            foreach (var d in nav._markers)
            {
                if (d == null || d.Type != NavigationMarkerType.Quest || d.Target == null || d.Quest == null) continue;
                if (!quests.TryGetValue(d.Quest.Pointer, out var rq)) continue;
                bool pinned = false;
                try { pinned = rq.Pinned; } catch { }
                if (pinnedOnly && !pinned) continue;
                string title = null;
                int number = 0;
                try { title = rq.Quest.Title; number = rq.QuestNumber; } catch { }
                list.Add(new QuestMarker
                {
                    Quest = rq, Target = d.Target, Pinned = pinned, Number = number, Title = title, Icon = icon, Colour = colour,
                });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Quests.Markers: {e.Message}"); }
        return list;
    }
}
