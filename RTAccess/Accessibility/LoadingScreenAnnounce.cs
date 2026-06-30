using Kingmaker.EntitySystem.Persistence;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Announces the game's post-load "press any key to continue" prompt. After every area transition the
/// game finishes loading and then pauses on the loading screen WAITING for a keypress
/// (<see cref="LoadingProcess.IsAwaitingUserInput"/>, the flag behind <c>LoadingScreenVM.NeedUserInput</c>).
/// A sighted player sees the on-screen "Press any key"; a blind player gets no cue and can sit stuck after
/// every transition. We edge-detect the awaiting state and speak the prompt (any key — including the mod's
/// own nav keys — dismisses it, since the game reads it via raw input, not the suppressed KeyboardAccess).
///
/// Discovered during the overnight in-game verification: the loading screen was "stuck" only because the
/// keypress was never sent. Driven from <c>Main.OnUpdate</c>.
/// </summary>
internal static class LoadingScreenAnnounce
{
    private static bool _announced;

    public static void Update()
    {
        bool awaiting;
        try
        {
            var lp = LoadingProcess.Instance;
            awaiting = lp != null && (bool)lp.IsAwaitingUserInput;
        }
        catch { return; }

        if (awaiting)
        {
            if (!_announced)
            {
                _announced = true;
                // Passive/event-driven → queue (it follows the "Loading."/area chatter without cutting it).
                Speaker.Speak("Press any key to continue.", interrupt: false);
            }
        }
        else
        {
            _announced = false;
        }
    }
}
