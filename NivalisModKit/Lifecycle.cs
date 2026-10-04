using System;
using System.Collections.Generic;

namespace NivalisModKit;

// The game's lifecycle as one sequence modders can rely on:
//   kit loaded (your Load) -> NewGameStarted or GameLoaded -> GameReady -> ... -> GameEnded -> (title screen)
public static partial class GameEvents
{
    /// <summary>
    /// True from <see cref="GameReady"/> until <see cref="GameEnded"/> or the next load: the world, its managers
    /// and the HUD exist, so queries, tuning and UI calls work.
    /// </summary>
    public static bool IsInGame { get; private set; }

    /// <summary>
    /// Gameplay is ready to use, after a save loaded or a new game started. Raised once per session, after
    /// <see cref="GameLoaded"/> (or <see cref="NewGameStarted"/>) once the game's managers exist. The safest
    /// place to read the world, open windows or apply settings.
    /// </summary>
    public static event Action GameReady;

    /// <summary>
    /// The player is leaving gameplay for the title screen (the game's own exit-to-title event). Release
    /// anything you hold on game objects here. Not raised when the game is closed outright.
    /// </summary>
    public static event Action GameEnded;

    static readonly List<Action> whenInGame = new();

    /// <summary>
    /// Runs <paramref name="action"/> now if <see cref="IsInGame"/>, otherwise once at the next
    /// <see cref="GameReady"/>. For work that needs the world, called from code that may run at the menu.
    /// </summary>
    public static void WhenInGame(Action action)
    {
        if (action == null) return;
        if (IsInGame)
        {
            try { action(); }
            catch (Exception e) { LogFailure(nameof(WhenInGame), action, e); }
        }
        else whenInGame.Add(action);
    }

    internal static void MarkReady()
    {
        if (IsInGame) return;
        IsInGame = true;
        Raise(nameof(GameReady), GameReady);
        var pending = whenInGame.ToArray();
        whenInGame.Clear();
        foreach (var a in pending)
        {
            try { a(); }
            catch (Exception e) { LogFailure(nameof(WhenInGame), a, e); }
        }
    }

    // A load or new game is starting: the old world is going away.
    internal static void MarkLeaving() => IsInGame = false;

    internal static void RaiseGameEnded()
    {
        IsInGame = false;
        Raise(nameof(GameEnded), GameEnded);
    }
}
