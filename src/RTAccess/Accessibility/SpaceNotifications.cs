using Kingmaker;                                         // Game (colony state)
using Kingmaker.Blueprints.Root.Strings;                 // UIStrings (the game's own toast wording)
using Kingmaker.Globalmap.Blueprints.Colonization;       // BlueprintResource
using Kingmaker.Globalmap.SystemMap;                     // StarSystemObjectEntity
using Kingmaker.PubSubSystem;                            // IMiningUIHandler + the two notification handlers
using Kingmaker.UI.MVVM.VM.SystemMapNotification;        // ColonyNotificationType
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Voices the SPACE HUD's toast layer — the notification cards that slide in over the system map, the sector
/// map and the planet-scan window. All five live on <c>SpaceStaticPartVM</c> (bound in
/// <c>SpaceStaticPartPCView</c>), and four of them have no game-log counterpart, so <see cref="LogTap"/>
/// never sees them and they were silent for us. Audit + per-toast evidence:
/// docs/ui-coverage-audit-2026-07-24.md ("Space HUD notification toasts").
///
/// Covered here (each spoken as the card reads: status line, then title line):
/// <list type="bullet">
/// <item><c>MiningNotificationVM</c> — a resource miner started / stopped.</item>
/// <item><c>EncyclopediaNotificationVM</c> — a scan added an entry to the encyclopedia.</item>
/// <item><c>ColonyNotificationVM</c> — a new event or chronicle at a colony.</item>
/// </list>
///
/// Deliberately NOT covered:
/// <list type="bullet">
/// <item><c>ExperienceNotificationVM</c> (the floating "+N xp" after a planet scan) — the same grant runs
/// through <c>GameHelper.GainExperience</c> → <c>GameLogEventPartyGainExperience</c> →
/// <c>PartyGainExperienceLogThread</c>, so LogTap already speaks it. Handling it here would double.</item>
/// <item><c>ColonyEventIngameMenuNotificatorVM</c> — a PERSISTENT icon, not a toast, and
/// <c>IColonizationEventHandler.HandleEventStarted</c> fires immediately before the colony toast for the
/// same event (<c>Colony.cs:333</c> then <c>:337</c>), so voicing it would double as well. Its accessible
/// equivalent is a live status line on the space screens, not an announcement — see
/// <see cref="ColonyEventLine"/>.</item>
/// </list>
///
/// The toasts carry an action button a sighted player can click ("To Encyclopedia" / "Colony Management").
/// We deliberately do not mirror those as verbs: the card auto-hides after
/// <c>UIConsts.QuestNotificationTime</c>, so a transient verb would go stale, and both destinations are
/// already reachable from the space screens' Actions zone at any time.
///
/// All lines are passive/event speech → QUEUED (interrupt: false), per [[rt-interrupt-speech-rule]].
/// Text comes from the game's own <c>UIStrings</c> so it follows the player's language; the mod only
/// supplies the "{status}: {text}" join.
///
/// Status: BUILT FROM THE DECOMPILE, UNTESTED IN-HARNESS (mining and colony events are progression-gated).
/// </summary>
internal sealed class SpaceNotifications :
    IMiningUIHandler,
    IEncyclopediaNotificationUIHandler,
    IColonyNotificationUIHandler
{
    internal static readonly SpaceNotifications Instance = new SpaceNotifications();

    // ---- mining (MiningNotificationPCView: status = "Resource miner", title = the start/stop message) ----

    void IMiningUIHandler.HandleStartMining(StarSystemObjectEntity starSystemObjectEntity, BlueprintResource blueprintResource)
        => Card(() => UIStrings.Instance.ExplorationTexts.ResourceMiner, "space.miner",
                () => UIStrings.Instance.ExplorationTexts.StartMiningNotificationText, "space.mining_started");

    void IMiningUIHandler.HandleStopMining(StarSystemObjectEntity starSystemObjectEntity, BlueprintResource blueprintResource)
        => Card(() => UIStrings.Instance.ExplorationTexts.ResourceMiner, "space.miner",
                () => UIStrings.Instance.ExplorationTexts.StopMiningNotificationText, "space.mining_stopped");

    // ---- encyclopedia (EncyclopediaNotificationPCView: title = "<name> " + AddedToEncyclopedia) ----

    public void HandleEncyclopediaNotification(string link, string encyclopediaName)
    {
        if (string.IsNullOrWhiteSpace(encyclopediaName)) return; // the view renders nothing without a name
        Speak(Loc.T("space.notify", new
        {
            status = GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.EncyclopediaGlossaryButton, "space.glossary"),
            text = encyclopediaName + " "
                   + GameText.Or(() => UIStrings.Instance.EncyclopediaTexts.AddedToEncyclopedia, "space.added_to_encyclopedia"),
        }));
    }

    // ---- colony (ColonyNotificationPCView.SetData: status word + name-formatted message) ----

    public void HandleColonyNotification(string colonyName, ColonyNotificationType type)
    {
        // Mirror the VM's own gate: colonization disabled → the game raises no card at all.
        if (Game.Instance?.Player?.ColoniesState?.ForbidColonization != false) return;
        if (string.IsNullOrWhiteSpace(colonyName)) return;

        bool chronicle = type == ColonyNotificationType.Chronicle;
        string status = chronicle
            ? GameText.Or(() => UIStrings.Instance.ColonyNotificationTexts.NewChronicleStatus, "space.colony_chronicle")
            : GameText.Or(() => UIStrings.Instance.ColonyNotificationTexts.NewEventStatus, "space.colony_event");
        string format = chronicle
            ? GameText.Or(() => UIStrings.Instance.ColonyNotificationTexts.ChronicleMessage, "space.colony_message")
            : GameText.Or(() => UIStrings.Instance.ColonyNotificationTexts.EventMessage, "space.colony_message");

        string body;
        try { body = string.Format(format, colonyName); }
        catch (Exception) { body = colonyName; } // a game string shipped without the {0} placeholder

        Speak(Loc.T("space.notify", new { status, text = body }));
    }

    // ---- the persistent "a colony needs a visit" indicator ----

    /// <summary>The space HUD's standing colony-event icon (<c>ColonyEventIngameMenuNotificatorVM</c>) as a
    /// line for the space screens' Status zone, or null when no colony has a started event. Read live each
    /// render — this is STATE, mirroring an icon that stays lit until the player resolves the event, not an
    /// announcement. Text is the icon's own hint string.</summary>
    internal static string ColonyEventLine()
    {
        try
        {
            var colonies = Game.Instance?.Player?.ColoniesState?.Colonies;
            if (colonies == null) return null;
            foreach (var data in colonies)
            {
                var started = data?.Colony?.StartedEvents;
                if (started == null) continue;
                // Match the notificator's own test (FirstOrDefault() != null): a list holding only nulls
                // does not light the icon.
                foreach (var evt in started)
                    if (evt != null)
                        return GameText.Or(() => UIStrings.Instance.ColonyEventsTexts.NeedsVisitMechanicString,
                            "space.colony_needs_visit");
            }
        }
        catch (Exception e) { Main.Log?.Log("colony event line failed: " + e.Message); }
        return null;
    }

    // ---- helpers ----

    private static void Card(System.Func<Kingmaker.Localization.LocalizedString> status, string statusFallback,
        System.Func<Kingmaker.Localization.LocalizedString> text, string textFallback)
        => Speak(Loc.T("space.notify", new
        {
            status = GameText.Or(status, statusFallback),
            text = GameText.Or(text, textFallback),
        }));

    private static void Speak(string line)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(line)) Speaker.Speak(line, interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("space notification announce failed: " + e.Message); }
    }
}
