using System.Text;
using Kingmaker.Blueprints.Root.Strings;                              // UIStrings (window title + flag words)
using Kingmaker.Code.UI.Common;                                       // UIUtilitySpaceColonization
using Kingmaker.Code.UI.MVVM.VM.SectorMap.AllSystemsInformationWindow; // AllSystemsInformationWindowVM + row VM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The sector map's "known star systems" panel (<see cref="AllSystemsInformationWindowVM"/>, a
    /// <c>SectorMapVM</c> child) — the roster of every VISITED system, alphabetical, that the map's own
    /// toggle button raises (<c>SectorMapVM.ShowHideAllSystemsInformation</c>; the same toggle is offered as an
    /// action on <see cref="SectorMapScreen"/>, since the sighted entry point is a mouse-only button).
    ///
    /// Card parity: a row shows the system NAME plus up to four status icons — colonised, quests, rumours,
    /// extractum — each of which carries its detail on hover. So the row reads the name and whichever flags are
    /// lit, and the details (the colony write-up, the quest objectives, the rumour titles, the discovered /
    /// mined resources) go on Space ([[rt-label-mirror-visual]]). The row's one verb is the card's own button:
    /// centre the map camera on that system.
    ///
    /// Layer 11 (paired with <see cref="SectorSystemInfoScreen"/>, which the game closes whenever this one
    /// opens and vice versa), not Exclusive — a side panel over a live map.
    /// </summary>
    public sealed class AllSystemsInfoScreen : Screen
    {
        public AllSystemsInfoScreen() { Wrap = true; }

        public override string Key => "sectormap.allsystems";
        public override string ScreenName => Title();
        public override int Layer => 11;

        internal static AllSystemsInformationWindowVM Vm()
        {
            var vm = SectorMapScreen.Sector()?.AllSystemsInformationWindowVM;
            return vm != null && vm.ShowAllSystemsWindow.Value ? vm : null;
        }

        /// <summary>Open / close the panel the way the sighted toggle button does — through the map VM, so the
        /// button's own lit state and the mutual exclusion with the per-system panel both stay correct.</summary>
        internal static void Toggle()
        {
            var sector = SectorMapScreen.Sector();
            if (sector == null) return;
            try { sector.ShowHideAllSystemsInformation(!sector.AllSystemInformationIsShowed.Value); }
            catch (Exception e) { Main.Log?.Error("AllSystemsInfoScreen.Toggle: " + e); }
        }

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(CloseLabel()), _ => vm.CloseWindow());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            b.BeginStop("systems").PushContext(Title(), Loc.T("role.list"));
            var systems = vm.Systems;
            if (systems == null || systems.Count == 0)
            {
                b.AddLabel(ControlId.Structural("allsys:none"), () => Loc.T("allsystems.none"));
                b.PopContext();
                return;
            }

            for (int i = 0; i < systems.Count; i++)
            {
                var s = systems[i];
                if (s == null) continue;
                var row = s; // capture
                // The four Check* calls recompute the row's flags AND fill the hint properties they gate, so
                // they run once per render here and both the label and the tooltip read the same snapshot.
                string label = RowLabel(row, out string detail);
                var vt = GraphNodes.Button(() => label, () => Focus(row));
                if (detail != null) vt.OnTooltip = () => TooltipChooser.Open(label, detail, null, null);
                b.AddItem(ControlId.Referenced(row, "allsys:" + i), vt);
            }
            b.PopContext();
        }

        private static void Focus(SystemInfoAllSystemsInformationWindowVM row)
        {
            try { row.SetCameraOnSystem(); }
            catch (Exception e) { Main.Log?.Error("AllSystemsInfoScreen.Focus: " + e); }
        }

        // "Janus, colonised, quests, rumours" + the hover detail behind it.
        private static string RowLabel(SystemInfoAllSystemsInformationWindowVM row, out string detail)
        {
            var label = new StringBuilder(row.SystemName ?? "");
            var body = new StringBuilder();
            detail = null;
            try
            {
                if (Safe(row.CheckColonization))
                {
                    label.Append(", ").Append(GameText.Or(
                        () => UIStrings.Instance.GlobalMap.SystemColonized, "sysinfo.colonized"));
                    string info = null;
                    try { info = UIUtilitySpaceColonization.GetColonizationInformation(row.Colony?.Value); } catch { }
                    Append(body, info);
                }
                if (Safe(row.CheckQuests))
                {
                    label.Append(", ").Append(GameText.Or(
                        () => UIStrings.Instance.QuesJournalTexts.Quests, "sysinfo.quests"));
                    Append(body, row.QuestObjectiveName?.Value);
                }
                if (Safe(row.CheckRumours))
                {
                    label.Append(", ").Append(GameText.Or(
                        () => UIStrings.Instance.QuesJournalTexts.Rumours, "sysinfo.rumours"));
                    Append(body, row.RumourObjectiveName?.Value);
                }
                if (Safe(row.CheckExtractum))
                {
                    label.Append(", ").Append(GameText.Or(
                        () => UIStrings.Instance.ExplorationTexts.DiscoveredResources, "sysinfo.resources"));
                    Append(body, row.ResourcesHint?.Value);
                }
            }
            catch (Exception e) { Main.Log?.Error("AllSystemsInfoScreen.RowLabel: " + e); }
            if (body.Length > 0) detail = body.ToString();
            return label.ToString();
        }

        private static bool Safe(Func<bool> check)
        {
            try { return check(); }
            catch { return false; }
        }

        private static void Append(StringBuilder sb, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text.Trim());
        }

        private static string Title()
            => GameText.Or(() => UIStrings.Instance.GlobalMap.KnownStarSystems, "allsystems.screen");

        private static string CloseLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.CloseWindow, "action.close");
    }
}
