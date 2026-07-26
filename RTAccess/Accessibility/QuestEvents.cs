using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker;                                      // QuestObjectiveState
using Kingmaker.AreaLogic.QuestSystem;                // QuestState
using Kingmaker.UI.Common;                            // JournalHelper.CurrentQuest (the tracked quest)
using Kingmaker.Blueprints.Root.Strings;              // UIStrings.QuestNotificationTexts (localized state words)
using RTAccess.Speech;                                // Speaker (keypress-caused readout -> interrupt)
using Kingmaker.Code.UI.MVVM.View.QuestNotification; // QuestNotificatorBaseView (the banner)
using Kingmaker.Code.UI.MVVM.VM.QuestNotification;   // QuestNotificationQuestVM / QuestNotificationEntityVM

namespace RTAccess.Accessibility;

/// <summary>
/// Voices the quest / objective notification banners (main-HUD audit #1). No game-log thread exists for
/// quests (the CombatLog thread system has no quest/objective thread), so <see cref="LogTap"/> structurally
/// cannot carry these — the banner is the ONLY sighted surface, and objective-level banners do not even play
/// a sound. Postfixing the view's two show actions (not the EventBus) inherits every gate for free: the VM's
/// add-time gates (<c>IsSilentQuestNotification</c>, <c>objective.IsVisible</c>, <c>silentStart</c>,
/// <c>quest.State != None</c>, the addendum-parent check, the already-queued dedupe), the view's
/// errand/Rumours/RumourAboutUs exclusions, the <c>ForbiddenQuestNotification</c> deferral
/// (cutscene / loading screen / GroupChanger), and the banner's own one-at-a-time pacing — so the spoken line
/// lands exactly when the sighted banner appears, once. Lines flow into <see cref="CombatEvents"/>' shared
/// passive queue: queued speech, never interrupting (per the provenance rule).
/// </summary>
internal static class QuestEvents
{
    /// <summary>
    /// Speak the tracked quest and its live objectives, including each objective's authored DESTINATION — the
    /// "where do I go next" key.
    ///
    /// This is the whole of what Rogue Trader offers on that question. The engine has no world-space quest
    /// markers: no per-entity "this is a current objective" flag exists on map objects, units, interaction parts
    /// or the overtip VMs, and nothing converts an objective into a world position. What a sighted player gets is
    /// the journal card, and its destination line is authored prose ("Secret chambers, Kiava Gamma, Kranach
    /// system"). Reading it aloud on a keypress puts a blind player level with that, without inventing guidance
    /// the game does not have.
    ///
    /// Hidden and clue objectives are skipped — the same gates the sighted card applies.
    /// </summary>
    public static void SpeakTracked()
    {
        try
        {
            var quest = JournalHelper.CurrentQuest;
            if (quest == null || quest.State != QuestState.Started)
            {
                Speaker.Speak(Loc.T("quest.none_tracked"), true);
                return;
            }

            var parts = new List<string> { TextUtil.StripRichText(quest.Blueprint.Title?.Text) };
            var place = quest.Blueprint.Place?.Text;
            if (!string.IsNullOrWhiteSpace(place))
                parts.Add(Loc.T("journal.place", new { place = TextUtil.StripRichText(place) }));

            bool anyObjective = false;
            foreach (var objective in quest.Objectives)
            {
                if (objective == null || objective.State != QuestObjectiveState.Started) continue;
                if (!objective.IsVisible || objective.IsClue) continue;
                anyObjective = true;
                var title = TextUtil.StripRichText(objective.Blueprint.Title?.Text);
                if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
                var destination = objective.Blueprint.Destination?.Text;
                if (!string.IsNullOrWhiteSpace(destination))
                    parts.Add(Loc.T("journal.destination",
                        new { place = TextUtil.StripRichText(destination) }));
            }
            if (!anyObjective) parts.Add(Loc.T("quest.no_active_objective"));

            Speaker.Speak(string.Join(". ", parts), true);
        }
        catch (Exception e)
        {
            Main.Log?.Error("QuestEvents.SpeakTracked failed: " + e);
            Speaker.Speak(Loc.T("quest.none_tracked"), true);
        }
    }

    // "New Quest: <title>. <description>" / "Quest Completed: <title>" — the state words are the game's own
    // localized banner strings (UIQuestNotificationTexts, including the Quest/Rumour/Order noun); the
    // description is spoken only for a NEW quest, exactly as QuestNotificationQuestView binds it.
    [HarmonyPatch(typeof(QuestNotificatorBaseView), "QuestNotificationsAction")]
    internal static class QuestShown
    {
        private static void Postfix(QuestNotificationQuestVM quest)
        {
            try
            {
                var bp = quest?.Quest?.Blueprint;
                if (bp == null) return;
                string state = UIStrings.Instance.QuestNotificationTexts
                    .GetQuestNotificationStateText(quest.State, bp.Type, bp.Group);
                string line = string.IsNullOrWhiteSpace(state)
                    ? quest.Title
                    : state + ": " + quest.Title;
                if (quest.State == QuestNotificationState.New && !string.IsNullOrWhiteSpace(quest.Description))
                    line += ". " + TextUtil.StripRichText(quest.Description);
                CombatEvents.Instance.EnqueueLogLine(line);
            }
            catch (Exception e) { Main.Log?.Log("QuestEvents quest read failed: " + e.Message); }
        }
    }

    // Spoken-once markers: the show postfix speaks a banner's primary + any already-attached extra, and the
    // AddObjective postfix below speaks a LATE extra grouped onto a banner that already spoke (the sighted
    // view live-binds it onto the visible banner — review finding). The weak table dedupes both orders
    // without pinning disposed VMs.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QuestNotificationEntityVM, object>
        Spoken = new System.Runtime.CompilerServices.ConditionalWeakTable<QuestNotificationEntityVM, object>();

    private static bool MarkSpoken(QuestNotificationEntityVM vm)
    {
        if (Spoken.TryGetValue(vm, out _)) return false;
        Spoken.Add(vm, null);
        return true;
    }

    // "<quest name>, new objective, <objective title>" — the objective banner titles itself with the quest
    // name and marks the state with an ICON only (clue replacing the state mark entirely — SetMark's
    // precedence, mirrored here); the body is the objective title, or the addendum text for an addendum
    // (QuestNotificationAddendumView binds Description).
    private static string Line(QuestNotificationEntityVM o)
    {
        string mark =
              o.IsClue ? Loc.T("quest.clue")
            : o.State == QuestNotificationState.Completed ? Loc.T("quest.obj_completed")
            : o.State == QuestNotificationState.Failed ? Loc.T("quest.obj_failed")
            : o.IsAddendum ? Loc.T("quest.obj_addendum")
            : Loc.T("quest.obj_new");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.QuestName)) parts.Add(o.QuestName);
        parts.Add(mark);
        string body = TextUtil.StripRichText(o.IsAddendum ? o.Description : o.Title);
        if (!string.IsNullOrWhiteSpace(body)) parts.Add(body);
        return string.Join(", ", parts);
    }

    [HarmonyPatch(typeof(QuestNotificatorBaseView), "ObjectiveNotificationsAction")]
    internal static class ObjectiveShown
    {
        private static void Postfix(QuestNotificationEntityVM objective)
        {
            try
            {
                if (objective == null) return;
                if (MarkSpoken(objective)) CombatEvents.Instance.EnqueueLogLine(Line(objective));
                var extra = objective.AdditionalObjective?.Value;
                if (extra != null && MarkSpoken(extra)) CombatEvents.Instance.EnqueueLogLine(Line(extra));
            }
            catch (Exception e) { Main.Log?.Log("QuestEvents objective read failed: " + e.Message); }
        }
    }

    // A second same-quest objective grouped onto a banner AFTER it started showing: the game attaches it to
    // any entry still in ObjectiveEntities — including the one on screen (entries leave only on hide) — and
    // the view live-binds it, so sighted players see it appear on the visible banner while the show postfix
    // has already run. Speak it here exactly when its parent has already spoken; a pre-display attach stays
    // with the show postfix (the parent hasn't spoken yet), keeping banner order.
    [HarmonyPatch(typeof(QuestNotificationEntityVM), nameof(QuestNotificationEntityVM.AddObjective))]
    internal static class ObjectiveGrouped
    {
        private static void Postfix(QuestNotificationEntityVM __instance, QuestNotificationEntityVM objectiveVM)
        {
            try
            {
                if (objectiveVM == null || !Spoken.TryGetValue(__instance, out _)) return;
                if (MarkSpoken(objectiveVM)) CombatEvents.Instance.EnqueueLogLine(Line(objectiveVM));
            }
            catch (Exception e) { Main.Log?.Log("QuestEvents grouped-objective read failed: " + e.Message); }
        }
    }
}
