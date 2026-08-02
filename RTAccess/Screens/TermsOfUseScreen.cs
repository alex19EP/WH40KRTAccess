using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.FirstLaunchSettings;
using Kingmaker.Code.UI.MVVM.VM.TermOfUse;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The terms-of-use / licence window (<c>MainMenuVM.TermsOfUseVM</c>) — the main menu's "License" entry,
    /// and the agreement the game shows once on a first launch (after the first-launch settings).
    ///
    /// The licence is a long legal text, so it is declared as one navigable LINE PER SENTENCE (the same
    /// split the tooltip reader uses) rather than a single node that would read the whole document in one
    /// breath: arrow through it at your own pace, or Tab past it to the buttons.
    ///
    /// The buttons follow the game's own first-time gate (<c>TermsOfUseBaseView.IsShowFirstTime</c> =
    /// the first-launch settings have never been shown): first time it's Accept / Decline — and Decline
    /// runs the game's confirm box, which quits the game on Yes — while later visits get a single OK.
    /// Escape closes only when it isn't the first-time agreement, exactly as the view subscribes the
    /// Esc hotkey.
    /// </summary>
    public sealed class TermsOfUseScreen : Screen
    {
        public TermsOfUseScreen() { Wrap = true; }

        public override string Key => "overlay.termsofuse";
        public override string ScreenName => GameText.Or(() => Texts()?.Header, "screen.terms_of_use");
        public override int Layer => 27;   // over the menu (0) and the first-launch wizard (26)
        public override bool Exclusive => true;

        private static TermsOfUseVM Vm()
            => Game.Instance?.RootUiContext?.MainMenuVM?.TermsOfUseVM?.Value;

        private static UITermsOfUseTexts Texts()
        {
            var vm = Vm();
            return vm != null ? vm.TermsOfUseTexts : null;
        }

        // The game's own gate for "this is the mandatory first-launch agreement".
        private static bool FirstTime => !FirstLaunchSettingsVM.HasShown;

        public override bool IsActive() => Vm() != null;

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            // Never Escape out of the mandatory agreement — the game doesn't offer it either.
            if (!FirstTime && Vm() != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => Vm()?.TermsOfUseClose());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var texts = vm.TermsOfUseTexts;

            b.BeginStop("licence").PushContext(
                GameText.Or(() => texts.Header, "screen.terms_of_use"), Loc.T("role.list"));
            string licence = vm.GetLicenceText();
            if (!string.IsNullOrWhiteSpace(licence))
            {
                int i = 0;
                // Break-preserving strip: the licence is a document, so it reads one line per PARAGRAPH.
                // (SplitSpokenLines falls back to sentences only if it arrives with no breaks at all.)
                foreach (var line in TextUtil.SplitSpokenLines(TextUtil.StripRichTextLines(licence)))
                {
                    var l = line; // capture
                    b.AddItem(ControlId.Structural("terms:line:" + i++), GraphNodes.Text(() => l));
                }
            }
            // The short sub-licence line under the document ("by accepting you agree…").
            if (!string.IsNullOrWhiteSpace(texts.SubLicence))
                b.AddItem(ControlId.Structural("terms:sub"), GraphNodes.Text(() => texts.SubLicence));
            b.PopContext();

            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            if (FirstTime)
            {
                b.AddItem(ControlId.Structural("terms:accept"), GraphNodes.Button(
                    () => GameText.Or(() => texts.AcceptBtn, "action.accept"),
                    () => Vm()?.TermsOfUseAccept()));
                // Decline opens the game's own "really decline?" box (MessageBoxScreen reads it) and Yes
                // quits the game — the game's flow, driven, not reimplemented.
                b.AddItem(ControlId.Structural("terms:decline"), GraphNodes.Button(
                    () => GameText.Or(() => texts.DeclineBtn, "action.decline"),
                    () => Vm()?.TermsOfUseDecline()));
            }
            else
            {
                b.AddItem(ControlId.Structural("terms:ok"), GraphNodes.Button(
                    () => GameText.Or(() => texts.OkBtn, "action.close"),
                    () => Vm()?.TermsOfUseClose()));
            }
            b.PopContext();
        }
    }
}
