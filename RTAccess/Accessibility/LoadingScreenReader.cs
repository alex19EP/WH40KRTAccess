using HarmonyLib;
using Kingmaker;                                    // Game
using Kingmaker.Blueprints.Root.Strings;            // UIStrings
using Kingmaker.Code.UI.MVVM.View.LoadingScreen;    // LoadingScreenBaseView
using Kingmaker.EntitySystem.Persistence;           // LoadingProcess
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Voices the loading screen the way a sighted player reads it: the area (or companion) name plus the tip on
/// the bottom text — a lore/gameplay HINT, the area's own description, an active quest-objective hint, or a
/// companion story — and then the "press any key to continue" prompt when the game pauses awaiting input at
/// the end of the load.
///
/// The tip is chosen with a stateful RNG and written straight to the VIEW's TMP fields (the VM carries none of
/// it), so we READ the displayed text via a postfix on <c>LoadingScreenBaseView.SetupLoadingArea</c> —
/// re-deriving it (re-calling <c>TakeHint</c>) would roll a DIFFERENT hint than the one on screen. The continue
/// prompt is polled from <c>Main.OnUpdate</c> (<see cref="LoadingProcess.IsAwaitingUserInput"/>), edge-detected,
/// and speaks the game's OWN localized text so it matches the on-screen line exactly (and stays localized).
/// Replaces the old LoadingScreenAnnounce. All speech is passive → queued (see [[rt-interrupt-speech-rule]]).
/// </summary>
public static class LoadingScreenReader
{
    private static string _lastTip;        // dedupe within one screen; reset when the screen closes
    private static bool _announcedInput;   // edge-detect the "awaiting keypress" state

    // ---- Tip announce: postfix on the view's per-area setup, after the TMP fields have been populated. ----
    [HarmonyPatch(typeof(LoadingScreenBaseView), "SetupLoadingArea")]
    private static class TipPatch
    {
        private static void Postfix(LoadingScreenBaseView __instance)
        {
            try { Announce(__instance); }
            catch (Exception e) { Main.Log?.Log("loading tip read failed: " + e.Message); }
        }
    }

    private static void Announce(LoadingScreenBaseView view)
    {
        if (view == null) return;

        string name, body;
        if (view.m_IsCharacterScreen)
        {
            name = view.m_CharacterNameText?.text;          // companion story title
            body = view.m_CharacterDescriptionText?.text;   // companion story text
        }
        else
        {
            var loc = view.m_LocationName;                  // area name (its parent is hidden for the empty screen)
            name = (loc != null && loc.transform.parent.gameObject.activeSelf) ? loc.text : null;
            body = view.m_BottomDescriptionText?.text;      // area description / hint / quest-objective hint
        }

        name = Clean(name);
        body = Clean(body);
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(body)) return;

        string line =
            string.IsNullOrEmpty(name) ? body :
            string.IsNullOrEmpty(body) ? Loc.T("loading.now", new { name }) :
            Loc.T("loading.now", new { name }) + " " + body;

        if (line == _lastTip) return;   // absorb a same-screen re-bind (e.g. null → real area)
        _lastTip = line;
        Speaker.Speak(line, interrupt: false);
    }

    // ---- Continue prompt: polled once per frame from Main.OnUpdate. ----
    // While a loading screen is up, the game pauses at the end awaiting a keypress (IsAwaitingUserInput); a
    // sighted player sees "Press any key", a blind player gets no cue and can sit stuck. We edge-detect it and
    // speak the game's own localized prompt. Per-screen state resets once the screen is gone, so the next load
    // re-announces its tip and prompt afresh even if the text is identical.
    public static void Update()
    {
        LoadingProcess lp;
        try { lp = LoadingProcess.Instance; }
        catch { return; }
        if (lp == null) return;

        bool active;
        try { active = lp.IsLoadingScreenActive; }
        catch { active = false; }
        if (!active)
        {
            _lastTip = null;
            _announcedInput = false;
            return;
        }

        bool awaiting;
        try { awaiting = (bool)lp.IsAwaitingUserInput; }
        catch { return; }
        if (awaiting && !_announcedInput)
        {
            _announcedInput = true;
            Speaker.Speak(PressAnyKeyText(), interrupt: false);
        }
    }

    // The game's own localized "press any key" line — matches what the loading screen shows (mouse vs console).
    private static string PressAnyKeyText()
    {
        try
        {
            var ct = UIStrings.Instance?.CommonTexts;
            if (ct != null)
            {
                var ls = Game.Instance.IsControllerMouse ? ct.PressAnyKey : ct.PressAnyKeyConsole;
                var s = Clean((string)ls);
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        catch { }
        return Loc.T("loading.press_any_key");   // localized fallback
    }

    private static string Clean(string s)
        => string.IsNullOrWhiteSpace(s) ? null : TextUtil.StripRichText(s).Trim();
}
