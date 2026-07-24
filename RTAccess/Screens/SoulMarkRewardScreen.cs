using Kingmaker.Blueprints.Root.Strings;                  // UIStrings (title / button words)
using Kingmaker.Code.UI.MVVM.VM.Dialog.RewardWindows;     // SoulMarkRewardVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The soul-mark reward popup (<see cref="SoulMarkRewardVM"/>, a <c>DialogContextVM</c> child) — the modal
    /// the game throws up MID-DIALOGUE whenever a conviction shift crosses a rank boundary
    /// (<c>DialogContextVM.HandleSoulMarkShift</c>: it only builds the VM when the new rank index is higher than
    /// the old one). <see cref="RTAccess.Accessibility.ConvictionEvents"/> already voices the underlying shift;
    /// this makes the popup itself reachable, so the granted feature can be read and the modal dismissed.
    ///
    /// Card parity: the sighted window shows exactly a title, an icon and the FEATURE NAME, with the feature's
    /// <c>TooltipTemplateSoulMarkFeature</c> on the main button's hover — so the row reads the name and the full
    /// write-up stays on Space ([[rt-label-mirror-visual]]).
    ///
    /// The two buttons are NAMED opposite to their VM methods, and the labels are what a player sees: the
    /// window's "Accept" is <see cref="SoulMarkRewardVM.OnDeclinePressed"/> (just close), and "See other ranks"
    /// is <see cref="SoulMarkRewardVM.OnAcceptPressed"/> (opens the character sheet, then closes). Both are
    /// driven by their LABEL here, so Escape = the sighted Accept.
    ///
    /// Layer 26, Exclusive: it has to clear <see cref="DialogueScreen"/> (15) — it is raised from inside a
    /// conversation — while staying under the confirm modal (30).
    /// </summary>
    public sealed class SoulMarkRewardScreen : Screen
    {
        public SoulMarkRewardScreen() { Wrap = true; } // a tiny two-stop modal: Tab wraps

        public override string Key => "overlay.soulmarkreward";
        public override int Layer => 26;
        public override bool Exclusive => true;
        public override string ScreenName => Vm() != null ? Title() : null;

        // Surface OR space — the dialog context hangs off whichever static part is live.
        private static SoulMarkRewardVM Vm() => UiContexts.Dialog()?.SoulMarkRewardVM?.Value;

        public override bool IsActive() => Vm() != null;

        // Escape = the window's own "Accept" button (dismiss without opening the sheet).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(AcceptLabel()),
                    _ => vm.OnDeclinePressed());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "soulmark:" + vm.GetHashCode() + ":"; // a new popup = fresh keys

            b.BeginStop("reward").PushContext(Title(), Loc.T("role.list"));
            // The feature blueprint can come back null (the VM leaves every field unset then, and the sighted
            // window shows an empty name) — say so rather than reading a blank row.
            string name = vm.FeatureName;
            if (string.IsNullOrEmpty(name))
            {
                b.AddLabel(ControlId.Structural(k + "feature"), () => Loc.T("soulmark.no_feature"));
            }
            else
            {
                var vt = GraphNodes.Text(() => name);
                vt.OnTooltip = () => TooltipChooser.OpenTemplate(name, vm.Tooltip);
                b.AddItem(ControlId.Structural(k + "feature"), vt);
            }
            b.PopContext();

            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                AcceptLabel, () => vm.OnDeclinePressed()));
            b.AddItem(ControlId.Structural(k + "ranks"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.PopUps.SeeOtherRanks, "soulmark.see_ranks"),
                () => vm.OnAcceptPressed()));
            b.PopContext();
        }

        private static string Title()
            => GameText.Or(() => UIStrings.Instance.PopUps.SoulMarkRewardTitle, "soulmark.screen");

        private static string AcceptLabel()
            => GameText.Or(() => UIStrings.Instance.CommonTexts.Accept, "action.accept");
    }
}
