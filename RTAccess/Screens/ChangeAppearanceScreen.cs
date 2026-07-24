using Kingmaker;                                          // Game (root UI context)
using Kingmaker.Blueprints.Root.Strings;                  // UIStrings (phase name, warnings, buttons)
using Kingmaker.Code.UI.MVVM.VM.ChangeAppearance;         // ChangeAppearanceVM
using Kingmaker.Code.UI.MVVM.VM.MessageBox;               // DialogMessageBoxBase
using Kingmaker.UI.Common;                                // UIUtility.ShowMessageBox
using RTAccess.Screens.CharGen;                           // AppearancePhaseContent
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The standalone Change Appearance window (<see cref="ChangeAppearanceVM"/>) — the character-generation
    /// APPEARANCE phase on its own, raised in play by the <c>ChangeAppearance</c> game action (an
    /// appearance-change service). Everything else in the game's window is the 3-D doll, the portrait and the
    /// pantograph: decoration with no text.
    ///
    /// Two Tab stops. The content stop reuses <see cref="AppearancePhaseContent"/> verbatim — the VM here is
    /// the same <c>CharGenAppearanceComponentAppearancePhaseVM</c> the chargen wizard renders, and the spoken
    /// contract (page tabs, per-page cyclers, the voice list) must not fork between the two entry points, so
    /// it stays ONE stop rather than being re-split into zones. The actions stop carries the window's real
    /// buttons; it is suppressed wholesale for a non-host in co-op, mirroring
    /// <c>ChangeAppearancePCView.CheckCoopButtons(IsMainCharacter)</c>.
    ///
    /// Accept and Cancel are NOT direct VM calls: the game's view wraps each in its own message box
    /// (<c>UIStrings.ChangeAppearance.ConfirmWarning</c> / <c>CancelWarning</c>) and only calls
    /// <c>Complete()</c> / <c>Close()</c> on Yes. Same here — the prompt lands on
    /// <see cref="MessageBoxScreen"/> (30) and the player gets the confirmation a sighted player gets.
    ///
    /// Layer 16, Exclusive: above <see cref="DialogueScreen"/> (15), since the action can fire from a
    /// dialogue answer, and below the Esc menu (20). <see cref="VisualSettingsScreen"/> rides above it (17)
    /// while this window's cosmetics panel is open. Teardown: docs/respec-appearance-teardown.md.
    /// </summary>
    public sealed class ChangeAppearanceScreen : Screen
    {
        public override string Key => "ctx.appearance";
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override string ScreenName => Vm() != null
            ? GameText.Or(() => UIStrings.Instance.CharGen.Appearance, "appearance.screen")
            : null;

        public ChangeAppearanceScreen() { Wrap = true; }

        public override bool IsActive() => Vm() != null;

        /// <summary>The live window. Two <c>CharGenContextVM</c>s can hold one — the main menu's and the
        /// surface's — but never at the same time (<c>MainMenuVM</c> is null in play, <c>SurfaceVM</c> in the
        /// menu), and the raising action needs a MainCharacter, so in practice this is always the surface
        /// one. Both are checked so the resolution can't silently depend on that.</summary>
        internal static ChangeAppearanceVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return null;
            return rc.SurfaceVM?.StaticPartVM?.CharGenContextVM?.ChangeAppearanceVM?.Value
                ?? rc.MainMenuVM?.CharGenContextVM?.ChangeAppearanceVM?.Value;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            // Escape is the game's own close path (EscHotkeyManager → OnClose → the cancel prompt). Non-hosts
            // have no close button in the game either, so they get no Back here.
            if (vm != null && vm.IsMainCharacter.Value)
                yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("cancel")),
                    _ => AskThenClose(vm));
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            var phase = vm?.CharGenAppearancePhaseVM;
            if (phase == null) return;

            // The phase VM only materialises its pages inside OnBeginDetailedView, which fires when the
            // game's OWN detailed view binds — that can lag under a parallel UI. Same guard CharGenScreen uses.
            if (!phase.IsInDetailedView.Value) phase.BeginDetailedView();

            string k = "appear:" + vm.GetHashCode() + ":"; // a new window = fresh keys

            b.BeginStop("content");
            new AppearancePhaseContent(phase).Build(b, k);

            if (!vm.IsMainCharacter.Value) return; // the game hides every button for a co-op guest
            b.BeginStop("actions").PushContext(Loc.T("appearance.actions"), Loc.T("role.list"));
            // The game shows this button only while the panel is closed; VisualSettingsScreen takes over
            // (layer 17) once it opens, so declaring it here unconditionally would offer a dead verb.
            if (vm.VisualSettingsVM.Value == null)
                b.AddItem(ControlId.Structural(k + "visual"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.CharGen.ShowVisualSettings,
                        "appearance.visual_settings"),
                    () => vm.ShowVisualSettings()));
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.CommonTexts.Accept, "action.accept"),
                () => AskThenComplete(vm)));
            b.AddItem(ControlId.Structural(k + "cancel"), GraphNodes.Button(
                () => GameText.Action("cancel"), () => AskThenClose(vm)));
            b.PopContext();
        }

        // ChangeAppearanceView.OnConfirm / OnClose, verbatim: the game's own message box over the game's own
        // warning string, committing only on Yes.
        private static void AskThenComplete(ChangeAppearanceVM vm) => Ask(
            UIStrings.Instance.ChangeAppearance.ConfirmWarning, () => vm.Complete());

        private static void AskThenClose(ChangeAppearanceVM vm) => Ask(
            UIStrings.Instance.ChangeAppearance.CancelWarning, () => vm.Close());

        private static void Ask(Kingmaker.Localization.LocalizedString warning, Action onYes)
        {
            try
            {
                UIUtility.ShowMessageBox(warning, DialogMessageBoxBase.BoxType.Dialog,
                    button => { if (button == DialogMessageBoxBase.BoxButton.Yes) onYes(); });
            }
            catch (Exception e) { Main.Log?.Error("change-appearance prompt failed: " + e); }
        }
    }
}
