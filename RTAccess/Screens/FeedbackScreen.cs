using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.FeedbackPopup;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The feedback popup (<c>MainMenuVM.FeedbackPopupVM</c>) — the main menu's Feedback entry: a short list
    /// of outward links (survey, Discord, social, website) built from the game's own feedback config, each
    /// opening its URL through the item VM's click handler, plus Close.
    /// </summary>
    public sealed class FeedbackScreen : Screen
    {
        public FeedbackScreen() { Wrap = true; }

        public override string Key => "overlay.feedback";
        public override string ScreenName => GameText.Or(() => UIStrings.Instance.MainMenu.Feedback, "screen.feedback");
        public override int Layer => 26;
        public override bool Exclusive => true;

        private static FeedbackPopupVM Vm()
            => Game.Instance?.RootUiContext?.MainMenuVM?.FeedbackPopupVM?.Value;

        public override bool IsActive() => Vm() != null;

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => vm.Close());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            b.BeginStop("links").PushContext(
                GameText.Or(() => UIStrings.Instance.MainMenu.Feedback, "screen.feedback"), Loc.T("role.list"));
            int i = 0;
            foreach (var item in vm.Items)
            {
                var e = item; // capture
                if (e == null) continue;
                b.AddItem(ControlId.Referenced(e, "feedback:" + i++),
                    GraphNodes.Button(() => e.Label, () => e.HandleClick()));
            }
            b.PopContext();

            b.BeginStop("close").AddItem(ControlId.Structural("feedback:close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.Close()));
        }
    }
}
