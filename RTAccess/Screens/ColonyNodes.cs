using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Colonization;             // ColonyRewardsVM
using Kingmaker.PubSubSystem;                             // IColonizationProjectsUIHandler
using Kingmaker.PubSubSystem.Core;                        // EventBus
using Kingmaker.UI.MVVM.VM.Colonization.Events;           // ColonyEventsVM
using Kingmaker.UI.MVVM.VM.Colonization.Projects;         // ColonyProjectsVM, ColonyProjectVM, built list
using Kingmaker.UI.MVVM.VM.Colonization.Stats;            // ColonyStatsVM
using Kingmaker.UI.MVVM.VM.Colonization.Traits;           // ColonyTraitsVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Shared graph builders for the colony UI components — the game composes the SAME component VMs into
    /// two windows (the exploration tablet's Colony section, and the ship-side Colony Management service
    /// window with <c>isColonyManagement: true</c>), so <see cref="ExplorationScreen"/> and
    /// <see cref="ColonyManagementScreen"/> render them through one set of builders. Stop keys are fixed
    /// ("colony"/"projlist"/"projpage"/"rewards") — stable within either screen, never co-hosted.
    /// </summary>
    internal static class ColonyNodes
    {
        /// <summary>The Colony section: stats (tooltip = the game's stat template), trait chips, event
        /// chips (Enter starts the event dialog in-system; the remote window's VM warns "needs visit"
        /// itself), built/building projects, and the projects-window opener.</summary>
        internal static void BuildComponents(GraphBuilder b, string k,
            ColonyStatsVM stats, ColonyTraitsVM traits, ColonyEventsVM events,
            ColonyProjectsBuiltListVM built, Action openProjects, Func<bool> locked)
        {
            b.BeginStop("colony").PushContext(Loc.T("exploration.colony"), role: "list");

            int si = 0;
            if (stats != null)
                foreach (var stat in stats.StatVMs)
                {
                    var s = stat; // capture
                    if (s == null) continue;
                    b.AddItem(ControlId.Referenced(s, k + "stat:" + si++), GraphNodes.Button(
                        () => s.StatName.Value + ": " + s.StatValue.Value
                              + (s.IsNegativelyModified.Value ? ", " + Loc.T("exploration.stat_reduced") : ""),
                        () => { },
                        () => false,
                        tooltip: () => s.Tooltip.Value));
                }

            int ti = 0;
            if (traits != null)
                foreach (var trait in traits.TraitsVMs)
                {
                    var t = trait; // capture
                    if (t == null) continue;
                    b.AddItem(ControlId.Referenced(t, k + "trait:" + ti++), GraphNodes.Button(
                        () => Loc.T("exploration.trait") + ": " + t.Name.Value,
                        () => { },
                        () => false,
                        tooltip: () => t.Tooltip.Value));
                }

            int ei = 0;
            if (events != null)
                foreach (var ev in events.EventsVMs)
                {
                    var e = ev; // capture
                    if (e == null) continue;
                    b.AddItem(ControlId.Referenced(e, k + "event:" + ei++), GraphNodes.Button(
                        () => Loc.T("exploration.event") + ": " + e.Name.Value,
                        () => { if (locked == null || !locked()) e.HandleColonyEvent(); },
                        tooltip: () => e.Tooltip?.Value));
                }

            int bi = 0;
            if (built != null)
                foreach (var proj in built.ProjectsVMs)
                {
                    var p = proj; // capture
                    if (p == null) continue;
                    b.AddLabel(ControlId.Referenced(p, k + "built:" + bi++), () => BuiltProjectLabel(p));
                }

            b.AddItem(ControlId.Structural(k + "projects"), GraphNodes.Button(
                () => Loc.T("exploration.open_projects"),
                () => { if (locked == null || !locked()) openProjects?.Invoke(); }));
            b.PopContext();
        }

        internal static string BuiltProjectLabel(ColonyProjectVM p)
        {
            var s = p.Title.Value ?? "";
            if (p.IsBuilding.Value)
                s += ", " + Loc.T("exploration.project_building",
                    new { done = p.Progress.Value, total = p.SegmentsToBuild.Value });
            return s;
        }

        /// <summary>The projects window: the rank-tiered card list (selection = the game's SelectPage) and
        /// the selected project's page (description, requirements with met/unmet, rewards, Start, the two
        /// show-blocked/finished toggles).</summary>
        internal static void BuildProjects(GraphBuilder b, string k, ColonyProjectsVM pvm, Func<bool> locked)
        {
            if (pvm == null) return;

            b.BeginStop("projlist").PushContext(Loc.T("exploration.projects"), role: "list");
            int pi = 0;
            foreach (var proj in pvm.NavigationVM.NavigationElements)
            {
                var p = proj; // capture
                if (p == null || !p.ShouldShow.Value) continue;
                b.AddItem(ControlId.Referenced(p, k + "proj:" + pi++), GraphNodes.ChoiceOption(
                    () => ProjectCardLabel(p),
                    () => p.IsSelected.Value,
                    () => p.SelectPage()));
            }
            b.PopContext();

            var page = pvm.ColonyProjectPageVM;
            b.BeginStop("projpage").PushContext(Loc.T("exploration.project_page"), role: "list");
            b.AddLabel(ControlId.Structural(k + "pg:title"), () => page.Title.Value ?? "");
            b.AddLabel(ControlId.Structural(k + "pg:desc"), () => page.Description.Value ?? "");
            int i = 0;
            foreach (var req in page.Requirements)
            {
                var r = req; // capture
                if (r == null) continue;
                b.AddLabel(ControlId.Referenced(r, k + "pg:req:" + i++), () =>
                    Loc.T("exploration.requires") + ": " + r.Description.Value
                    + (string.IsNullOrEmpty(r.CountText.Value) ? "" : " " + r.CountText.Value)
                    + ", " + Loc.T(r.IsChecked.Value ? "exploration.req_met" : "exploration.req_unmet"));
            }
            int j = 0;
            foreach (var rew in page.Rewards)
            {
                var r = rew; // capture
                if (r == null) continue;
                b.AddLabel(ControlId.Referenced(r, k + "pg:rew:" + j++), () =>
                    Loc.T("exploration.reward") + ": " + r.Description.Value
                    + (string.IsNullOrEmpty(r.CountText.Value) ? "" : " " + r.CountText.Value));
            }
            b.AddItem(ControlId.Structural(k + "pg:start"), GraphNodes.Button(
                () => Loc.T("exploration.start_project"),
                () => { if (locked == null || !locked()) pvm.StartProject(); },
                () => pvm.StartAvailable.Value));
            b.AddItem(ControlId.Structural(k + "pg:blocked"), GraphNodes.Toggle(
                () => Loc.T("exploration.show_blocked"),
                () => pvm.ShowBlockedProjects.Value,
                () => pvm.SwitchBlockedProjects()));
            b.AddItem(ControlId.Structural(k + "pg:finished"), GraphNodes.Toggle(
                () => Loc.T("exploration.show_finished"),
                () => pvm.ShowFinishedProjects.Value,
                () => pvm.SwitchFinishedProjects()));
            b.PopContext();
        }

        internal static string ProjectCardLabel(ColonyProjectVM p)
        {
            var parts = new List<string> { p.Title.Value ?? "" };
            if (p.IsFinished.Value) parts.Add(Loc.T("exploration.project_finished"));
            else if (p.IsBuilding.Value)
                parts.Add(Loc.T("exploration.project_building",
                    new { done = p.Progress.Value, total = p.SegmentsToBuild.Value }));
            if (p.IsExcluded.Value) parts.Add(Loc.T("state.disabled"));
            else if (p.IsNotMeetRequirements.Value) parts.Add(Loc.T("exploration.req_unmet"));
            return string.Join(", ", parts);
        }

        /// <summary>The "since your last visit" rewards popup (finished project + stat shifts + loot).
        /// No-op unless the VM says ShouldShow. Collect runs the VM's own <c>HandleHide</c> — the game's
        /// claim path (receive loot + clear finished projects), exactly what dismissing the popup does.</summary>
        internal static void BuildRewards(GraphBuilder b, string k, ColonyRewardsVM rvm)
        {
            if (rvm == null || !rvm.ShouldShow.Value) return;

            b.BeginStop("rewards").PushContext(Loc.T("colony.rewards"), role: "list");
            b.AddLabel(ControlId.Structural(k + "rw:colony"), () => rvm.ColonyName.Value ?? "");
            if (rvm.HasFinishedProject.Value)
                b.AddLabel(ControlId.Structural(k + "rw:proj"), () =>
                    Loc.T("colony.finished_project", new { name = rvm.FinishedProjectName.Value }));
            if (rvm.HasStats.Value)
            {
                b.AddLabel(ControlId.Structural(k + "rw:stat:c"), () => rvm.ContentmentStatText.Value ?? "");
                b.AddLabel(ControlId.Structural(k + "rw:stat:e"), () => rvm.EfficiencyStatText.Value ?? "");
                b.AddLabel(ControlId.Structural(k + "rw:stat:s"), () => rvm.SecurityStatText.Value ?? "");
            }
            if (rvm.HasStatsAllColonies.Value)
            {
                b.AddLabel(ControlId.Structural(k + "rw:all:c"), () => rvm.ContentmentStatAllColoniesText.Value ?? "");
                b.AddLabel(ControlId.Structural(k + "rw:all:e"), () => rvm.EfficiencyStatAllColoniesText.Value ?? "");
                b.AddLabel(ControlId.Structural(k + "rw:all:s"), () => rvm.SecurityStatAllColoniesText.Value ?? "");
            }
            if (rvm.HasItems.Value)
                b.AddLabel(ControlId.Structural(k + "rw:items"), () => Loc.T("colony.items_received"));
            if (rvm.HasCargo.Value)
                b.AddLabel(ControlId.Structural(k + "rw:cargo"), () =>
                    Loc.T("colony.cargo_received", new { n = rvm.CargoRewards.Count }));
            if (rvm.HasOtherRewards.Value)
                b.AddLabel(ControlId.Structural(k + "rw:other"), () =>
                    Loc.T("colony.other_rewards", new { n = rvm.OtherRewards.Count }));
            b.AddItem(ControlId.Structural(k + "rw:collect"), GraphNodes.Button(
                () => Loc.T("colony.collect"),
                () => rvm.HandleHide()));
            b.PopContext();
        }

        /// <summary>Escape inside a projects window: the game's own close handler (both windows share it).</summary>
        internal static void CloseProjects()
            => EventBus.RaiseEvent<IColonizationProjectsUIHandler>(h => h.HandleColonyProjectsUIClose());
    }
}
