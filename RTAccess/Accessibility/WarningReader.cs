using Kingmaker.Blueprints.Root;                     // LocalizedTexts
using Kingmaker.Code.UI.MVVM.VM.WarningNotification; // WarningNotificationType, WarningNotificationFormat
using Kingmaker.PubSubSystem;                        // IWarningNotificationUIHandler
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Speaks the game's own "you can't do that" warnings (<see cref="IWarningNotificationUIHandler"/>) — the
/// toast it shows when an action is refused: ability-cast refusals (with the exact restriction reason),
/// "not enough action points", "no path", "target too far", etc. So a refused action reports the game's
/// precise reason instead of a generic guess. A persistent <see cref="Kingmaker.PubSubSystem.Core.EventBus"/>
/// subscriber, subscribed at mod load and unsubscribed at unload (see <see cref="Main"/>); purely reactive,
/// so no per-frame tick.
///
/// The game raises two forms: a localized <c>string</c> (already the reason text — e.g. an ability target
/// restriction's message) which we speak directly, and a <see cref="WarningNotificationType"/> enum which we
/// resolve through the same localized table the on-screen warnings use. We voice EVERY warning toast the game
/// displays — this is the only reader of that on-screen toast, and it is the complete set (many warnings,
/// e.g. the <c>addToLog:false</c> refusals, are shown on screen but never written to the combat log, so
/// <see cref="LogTap"/> cannot see them). The matching <c>WarningNotificationLogThread</c> stays owned here
/// (suppressed in <see cref="LogTap"/>) so the <c>addToLog:true</c> ones are not read twice (toast + log).
/// Speech is queued (interrupt:false), like the rest of the combat/event feedback (see [[rt-interrupt-speech-rule]]).
/// </summary>
internal sealed class WarningReader : IWarningNotificationUIHandler
{
    internal static readonly WarningReader Instance = new WarningReader();

    /// <summary>Bumped each time the game raises a warning — callers may snapshot it before an action and
    /// compare after to tell whether the game already explained a refusal (so they can supply a generic
    /// fallback only when it stayed silent).</summary>
    public static int Count { get; private set; }

    // Enum form: resolve to the game's localized text (same source the on-screen warnings use).
    public void HandleWarning(WarningNotificationType warningType, bool addToLog = true,
        WarningNotificationFormat warningFormat = WarningNotificationFormat.Common, bool withSound = true)
    {
        Count++;
        Speak(Localize(warningType));
    }

    // Text form: already the reason text (e.g. an ability target restriction's message).
    public void HandleWarning(string text, bool addToLog = true,
        WarningNotificationFormat warningFormat = WarningNotificationFormat.Common, bool withSound = true)
    {
        Count++;
        if (IsTurnChromeToast(text)) return;
        Speak(text);
    }

    // The initiative tracker's coop "your turn" banner (OnCurrentUnitChanged) duplicates CombatEvents'
    // whose-turn cue, which carries more (name, movement, speed) — drop it. Its round banner ("Раунд 2" —
    // RoundChanged) is deliberately KEPT: it is the round announcement (CombatEvents cues no round of its
    // own), already localized in the game's language.
    private static bool IsTurnChromeToast(string text)
    {
        try
        {
            string youTurn = Kingmaker.Blueprints.Root.Strings.UIStrings.Instance?.TurnBasedTexts?.YouTurn;
            return !string.IsNullOrEmpty(youTurn) && text == youTurn;
        }
        catch { return false; }
    }

    private static string Localize(WarningNotificationType t)
    {
        try { return LocalizedTexts.Instance?.WarningNotification?.GetText(t); }
        catch { return null; }
    }

    // Same-frame identical-text dedupe: twin game subscribers can raise the SAME toast twice within one
    // EventBus dispatch (the ship window's two ShipUpgradeSlotVM instances both toast every system-component
    // upgrade result — docs/ship-management-ui-exploration.md, trap #5). Frame-scoped on purpose: a player
    // re-triggering the same refusal on a later keypress lands on a later frame and is never suppressed.
    private static int _lastFrame = -1;
    private static string _lastText;

    private static void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        int frame = UnityEngine.Time.frameCount;
        if (frame == _lastFrame && text == _lastText) return;
        _lastFrame = frame;
        _lastText = text;
        Speaker.Speak(text, interrupt: false); // reactive feedback — queue behind any current line
    }
}
