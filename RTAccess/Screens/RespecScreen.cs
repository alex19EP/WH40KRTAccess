using Kingmaker;                                          // Game (root UI context + the PF pool)
using Kingmaker.Blueprints.Root.Strings;                  // UIStrings (header / warning / Accept)
using Kingmaker.Code.UI.MVVM.VM.Retrain;                  // RespecVM, RespecCharacterVM
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;        // TooltipTemplateSimple
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The respecialisation window (<see cref="RespecVM"/>, a <c>SurfaceStaticPartVM.RespecContextVM</c>
    /// child) — the "rebuild this companion from scratch" service the <c>RespecCompanion</c> game action
    /// raises. Three Tab stops mirroring the game's window: the character radio grid, the details block
    /// (the standing warning, the Profit Factor cost, the live PF pool), and the Accept / Close buttons.
    ///
    /// Card parity: <c>RespecCharacterCommonView</c> binds exactly a portrait and a name, so a row reads
    /// the NAME and nothing else — no level, no career (see [[rt-label-mirror-visual]]). The cost is
    /// per-character (the first three respecs of a given companion are free; after that it is
    /// <c>respecCount - 2</c> PF, per <c>RespecInfo</c>), so it is read off the LIVE selection, exactly as
    /// the game's cost label is.
    ///
    /// Accept drives <see cref="RespecVM.OnConfirm"/>, which raises the game's OWN confirm message box
    /// (<c>UIStrings.CharGen.RespecSelectCharacter</c>) — that lands on <see cref="MessageBoxScreen"/>
    /// (layer 30), and on Yes the game queues <c>FinishRespec</c>, closes this window and hands the player
    /// to the level-up screen. Nothing to reimplement.
    ///
    /// Layer 26, Exclusive: the raising game action commonly fires from a dialogue answer, so this has to
    /// clear <see cref="DialogueScreen"/> (15) while staying under the confirm modal (30). Surface-only —
    /// <c>SpaceStaticPartVM</c> has no <c>RespecContextVM</c>, so there is no space half to resolve.
    /// Teardown: docs/respec-appearance-teardown.md.
    /// </summary>
    public sealed class RespecScreen : Screen
    {
        public override string Key => "respec";
        public override int Layer => 26;
        public override bool Exclusive => true;
        public override string ScreenName => Vm() != null
            ? GameText.Or(() => UIStrings.Instance.CharGen.RespecWindowHeader, "respec.screen")
            : null;

        public RespecScreen() { Wrap = true; } // a small closed-loop modal: Tab cycles its three stops

        public override bool IsActive() => Vm() != null;

        private static RespecVM Vm()
            => Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM?.RespecContextVM?.RespecVM?.Value;

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(CloseLabel()), _ => vm.OnClose());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "respec:" + vm.GetHashCode() + ":"; // a new window = fresh keys

            BuildCharacters(b, k, vm);
            BuildDetails(b, k, vm);
            BuildActions(b, k, vm);
        }

        // -- the character grid: a radio list, one row per respecable companion --
        private static void BuildCharacters(GraphBuilder b, string k, RespecVM vm)
        {
            b.BeginStop("chars").PushContext(Loc.T("respec.characters"), Loc.T("role.list"));
            var col = vm.CharacterSelectionGroupRadioVM?.EntitiesCollection;
            int i = 0;
            bool any = false;
            if (col != null)
                foreach (var c in col)
                {
                    if (c == null) { i++; continue; }
                    var ch = c; // capture
                    any = true;
                    b.AddItem(ControlId.Referenced(ch, k + "ch:" + i++),
                        CharGenNodes.SelectionItem(ch, () => ch.CharacterName ?? ""));
                }
            // The paid path can't open empty (RespecCompanion aborts first), but a ForFree grant can.
            if (!any)
                b.AddLabel(ControlId.Structural(k + "ch:none"), () => Loc.T("respec.no_characters"));
            b.PopContext();
        }

        // -- the details block: the standing warning, the cost, and the pool it comes out of --
        private static void BuildDetails(GraphBuilder b, string k, RespecVM vm)
        {
            b.BeginStop("details").PushContext(Loc.T("respec.details"), Loc.T("role.list"));
            b.AddLabel(ControlId.Structural(k + "warning"),
                () => GameText.Or(() => UIStrings.Instance.CharGen.RespecWindowWarning, "respec.warning"));
            b.AddLabel(ControlId.Structural(k + "cost"), () => CostLine(vm));
            // The window's own resource bar carries the PF pool beside the cost; mirror it as a line, with
            // the game's Profit Factor write-up on Space (the sighted hover on that widget).
            b.AddItem(ControlId.Structural(k + "pf"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(ProfitFactorLine) },
                SearchText = ProfitFactorLine,
                OnTooltip = () => TooltipChooser.OpenTemplate(
                    UIStrings.Instance.ProfitFactorTexts.Title.Text,
                    new TooltipTemplateSimple(UIStrings.Instance.ProfitFactorTexts.Title.Text,
                        UIStrings.Instance.ProfitFactorTexts.Description.Text)),
            });
            b.PopContext();
        }

        private static void BuildActions(GraphBuilder b, string k, RespecVM vm)
        {
            b.BeginStop("actions").PushContext(Loc.T("respec.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.CommonTexts.Accept, "action.accept"),
                () => vm.OnConfirm(),
                // The game greys Accept the same way: m_AcceptButton.SetInteractable(CanRespec).
                () => vm.CanRespec.Value));
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(
                CloseLabel, () => vm.OnClose()));
            b.PopContext();
        }

        // "Cost: N Profit Factor", plus the game's own not-enough-PF wording when the pool can't cover it
        // (the sighted cue is the PF widget turning negative — JournalOrderProfitFactorVM.IsNegative).
        private static string CostLine(RespecVM vm)
        {
            string line = Loc.T("respec.cost", new
            {
                n = vm.RespecCost.Value,
                pf = UIStrings.Instance.ProfitFactorTexts.Title.Text,
            });
            if (!vm.CanRespec.Value)
                line += ", " + UIStrings.Instance.Vendor.NotEnoughProfitFactor.Text;
            return line;
        }

        private static string ProfitFactorLine() => Loc.T("char.profit_factor", new
        {
            title = UIStrings.Instance.ProfitFactorTexts.Title.Text,
            value = (Game.Instance?.Player?.ProfitFactor?.Total ?? 0f).ToString("0.#"),
        });

        private static string CloseLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.CloseWindow, "action.close");
    }
}
