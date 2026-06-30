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
/// resolve through the same localized table the on-screen warnings use. The routine save/load notifications
/// (manual/quick/auto save succeeded, game loaded) are pure spam for a blind player, so they're filtered;
/// genuine save FAILURES still read (they're a refusal). Speech is queued (interrupt:false), like the rest of
/// the combat/event feedback (see [[rt-interrupt-speech-rule]]).
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
        if (IsSaveLoadNoise(warningType)) return;
        Speak(Localize(warningType));
    }

    // Text form: already the reason text (e.g. an ability target restriction's message).
    public void HandleWarning(string text, bool addToLog = true,
        WarningNotificationFormat warningFormat = WarningNotificationFormat.Common, bool withSound = true)
    {
        Count++;
        Speak(text);
    }

    // Routine save/load notifications are constant background spam (autosave especially). Genuine save
    // failures / "saving impossible" are refusals and still read.
    private static bool IsSaveLoadNoise(WarningNotificationType t)
    {
        switch (t)
        {
            case WarningNotificationType.GameLoaded:
            case WarningNotificationType.GameSaved:
            case WarningNotificationType.GameSavedQuick:
            case WarningNotificationType.GameSavedAuto:
            case WarningNotificationType.GameSavedInProgress:
                return true;
            default:
                return false;
        }
    }

    private static string Localize(WarningNotificationType t)
    {
        try { return LocalizedTexts.Instance?.WarningNotification?.GetText(t); }
        catch { return null; }
    }

    private static void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Speaker.Speak(text, interrupt: false); // reactive feedback — queue behind any current line
    }
}
