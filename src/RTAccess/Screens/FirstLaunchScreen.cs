using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.FirstLaunchSettings;
using RTAccess.UI;
using Access.Core.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The first-launch settings wizard (<c>MainMenuVM.FirstLaunchSettings</c>) — the very first thing the
    /// game shows on a fresh install, before the licence and the menu itself. Without this screen a blind
    /// player's first moment in the game is silence, with the language of the whole game riding on it.
    ///
    /// A page at a time, driven through the VM's own paging (<c>NextPage</c> / <c>PreviousPage</c>, which
    /// walk the same selection group the side menu shows): Language (a radio list of locales — picking one
    /// applies it immediately, as the game's own item does), Safe zone (gamepad boots only), Display
    /// (gamma / brightness / contrast) and Accessibility (font size + the three colour-blindness sliders).
    /// Tab-stops: the page menu, the page's own controls, then Back / Default / Continue — the last two
    /// hidden on the language page exactly as the view hides them, and Continue reading "Apply" on the
    /// final page. Continuing off the last page runs the game's photosensitivity notice and closes the
    /// wizard, which is what raises the licence next.
    /// </summary>
    public sealed class FirstLaunchScreen : Screen
    {
        public FirstLaunchScreen() { Wrap = true; }

        public override string Key => "overlay.firstlaunch";
        public override string ScreenName => Loc.T("screen.first_launch");
        public override int Layer => 26;
        public override bool Exclusive => true;
        // Land on the page's own controls, not the page menu — the wizard's point is the settings.
        public override object InitialFocusStop => "page";

        private static FirstLaunchSettingsVM Vm()
            => Game.Instance?.RootUiContext?.MainMenuVM?.FirstLaunchSettings?.Value;

        public override bool IsActive() => Vm() != null;

        // No Back action: the wizard is mandatory, and its own Back button is page-wise (PreviousPage).


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The page menu (Language / [Safe zone] / Display / Accessibility). Selecting an entry runs
            // the game's own page switch, which applies the current page's values first.
            b.BeginStop("pages").PushContext(Loc.T("label.pages"), Loc.T("role.list"));
            var pages = vm.SelectionGroup?.EntitiesCollection;
            if (pages != null)
            {
                int i = 0;
                foreach (var page in pages)
                {
                    var e = page; // capture
                    var id = ControlId.Referenced(e, "firstlaunch:page:" + i++);
                    // Selection goes through the VM's own reactive, NOT the entity's SetSelectedFromView:
                    // this wizard builds its menu entities with a null confirm action, so the page switch
                    // hangs entirely off the SelectedMenuEntity subscription.
                    b.AddItem(id, GraphNodes.ChoiceOption(
                        () => e.Title?.Value ?? "",
                        () => ReferenceEquals(Vm()?.SelectedMenuEntity?.Value, e),
                        () => { var v = Vm(); if (v != null) v.SelectedMenuEntity.Value = e; }));
                    if (ReferenceEquals(vm.SelectedMenuEntity?.Value, e)) b.SetStart(id);
                }
            }
            b.PopContext();

            // The current page's controls. Keys carry the page, so paging re-keys the content only.
            var current = vm.SelectedMenuEntity?.Value;
            string k = "firstlaunch:" + (current != null ? current.SettingsScreenType.ToString() : "?") + ":";
            b.BeginStop("page").PushContext(current?.Title?.Value ?? Loc.T("screen.first_launch"), Loc.T("role.list"));
            BuildPage(b, vm, k);
            b.PopContext();

            // Back / Default / Continue. The view hides the first two on the language page (there is no
            // previous page, and the language is applied on pick, not reverted), and labels Continue
            // "Apply" on the final (accessibility) page.
            bool onLanguagePage = vm.LanguagePageVM?.Value != null;
            bool onLastPage = vm.AccessiabilityPageVM?.Value != null;
            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            if (!onLanguagePage)
            {
                b.AddItem(ControlId.Structural("firstlaunch:back"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.ContextMenu.Back, "action.back"),
                    () => Vm()?.PreviousPage()));
                b.AddItem(ControlId.Structural("firstlaunch:default"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.SettingsUI.Default, "action.default"),
                    () => Vm()?.RevertSettings()));
            }
            b.AddItem(ControlId.Structural("firstlaunch:continue"), GraphNodes.Button(
                () => onLastPage
                    ? GameText.Or(() => UIStrings.Instance.SettingsUI.Apply, "action.apply")
                    : GameText.Or(() => UIStrings.Instance.MainMenu.Continue, "action.next"),
                () => Vm()?.NextPage()));
            b.PopContext();
        }

        private static void BuildPage(GraphBuilder b, FirstLaunchSettingsVM vm, string k)
        {
            var language = vm.LanguagePageVM?.Value;
            if (language != null)
            {
                // The locale list: each item applies the language immediately (the page VM builds its items
                // with SetValueAndConfirm), so the value part is the selection itself.
                var items = language.Languages?.Items;
                if (items != null)
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        if (item == null) continue;
                        var id = ControlId.Referenced(item, k + "lang:" + i);
                        b.AddItem(id, GraphNodes.ChoiceOption(
                            () => item.Title, () => item.IsSelected.Value, () => item.SetSelected()));
                        if (item.IsSelected.Value) b.SetStart(id);
                    }
                return;
            }

            var safeZone = vm.SafeZonePageVM?.Value;
            if (safeZone != null)
            {
                Slider(b, k + "safezone", safeZone.Offset);
                return;
            }

            var display = vm.DisplayPageVM?.Value;
            if (display != null)
            {
                Slider(b, k + "gamma", display.GammaCorrection);
                Slider(b, k + "brightness", display.Brightness);
                Slider(b, k + "contrast", display.Contrast);
                return;
            }

            var accessibility = vm.AccessiabilityPageVM?.Value;
            if (accessibility != null)
            {
                Slider(b, k + "fontsize", accessibility.FontSize);
                Slider(b, k + "protanopia", accessibility.Protanopia);
                Slider(b, k + "deuteranopia", accessibility.Deuteranopia);
                Slider(b, k + "tritanopia", accessibility.Tritanopia);
            }
        }

        private static void Slider(GraphBuilder b, string key,
            Kingmaker.Code.UI.MVVM.VM.Settings.Entities.SettingsEntitySliderVM vm)
        {
            if (vm == null) return;
            b.AddItem(ControlId.Referenced(vm, key), GraphNodes.Slider(vm));
        }
    }
}
