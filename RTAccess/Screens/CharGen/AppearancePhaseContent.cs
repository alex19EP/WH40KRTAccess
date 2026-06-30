using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance.Components.Voice;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance.Pages;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The appearance phase — organised as pages (Portrait / General / Hair / Tattoo / Implants / Voice),
    /// each with component selectors. We render a stable page-tab list plus a components panel that's
    /// rebuilt when the current page changes (or its components finish loading async). Components:
    /// <list type="bullet">
    /// <item><b>Voice</b> — a radio list of named voices; selecting one plays its sample (the high-value
    /// part for a non-sighted player).</item>
    /// <item><b>Gender</b> — a cycler whose value is read from the doll (Male/Female), not just an index.</item>
    /// <item>Other cyclers (face/body/skin/hair/…) — navigable "Title, N of M" (the values are visual, so
    /// there's no name to read; cycling still works and the doll updates live).</item>
    /// <item>Portrait — visual-only; surfaced as a note (a default portrait is kept).</item>
    /// </list>
    /// Appearance auto-completes with valid defaults, so none of this blocks finishing the character.
    /// </summary>
    public sealed class AppearancePhaseContent : CharGenPhaseContent
    {
        public AppearancePhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenAppearanceComponentAppearancePhaseVM Vm => Phase as CharGenAppearanceComponentAppearancePhaseVM;

        private ListContainer _pagesList;
        private Panel _componentsPanel;
        private int _builtPageCount = -1;
        private object _builtPage;
        private int _builtComps = -1;

        protected override void OnBuild()
        {
            var vm = Vm;
            if (vm == null) { base.OnBuild(); return; }

            // Page tabs + a components panel below. The pages load async (the game's view creates them when
            // the phase binds), so both are refreshed in Tick once they appear.
            _pagesList = new ListContainer(Loc.T("chargen.appearance_pages"));
            FillPages(vm);
            Content.Add(_pagesList);

            _componentsPanel = new Panel();
            Content.Add(_componentsPanel);
            BuildComponents();
        }

        private void FillPages(CharGenAppearanceComponentAppearancePhaseVM vm)
        {
            _pagesList.Clear();
            var pages = vm.PagesSelectionGroupRadioVM.EntitiesCollection;
            _builtPageCount = pages.Count;
            foreach (var pg in pages)
            {
                var p = pg; // capture
                _pagesList.Add(new ProxySelectionItem(p, () => p.PageLabel, role: "tab"));
            }
        }

        // Refresh the page tabs once they populate; rebuild the components panel when the current page
        // changes or its components finish loading (page-tab focus stays put on a components-only rebuild).
        public override void Tick()
        {
            var vm = Vm;
            if (vm == null || _pagesList == null) return;
            if (vm.PagesSelectionGroupRadioVM.EntitiesCollection.Count != _builtPageCount) FillPages(vm);
            var page = vm.CurrentPageVM.Value;
            int n = page != null ? page.Components.Count : 0;
            if (!ReferenceEquals(page, _builtPage) || n != _builtComps) BuildComponents();
        }

        private void BuildComponents()
        {
            var vm = Vm;
            var page = vm?.CurrentPageVM.Value;
            _componentsPanel.Clear();
            _builtPage = page;
            _builtComps = page != null ? page.Components.Count : 0;
            if (page == null) return;

            var list = new ListContainer(page.PageLabel);
            foreach (var c in page.Components)
            {
                var comp = c;
                if (comp is CharGenVoiceSelectorVM voice)
                {
                    var voices = new ListContainer(Loc.T("chargen.voices"));
                    foreach (var v in voice.VoiceSelector.EntitiesCollection)
                    {
                        var item = v; // capture
                        voices.Add(new ProxySelectionItem(item, () => item.DisplayName));
                    }
                    list.Add(voices);
                }
                else if (ProxySequentialSelector.Handles(comp))
                {
                    var isGender = comp.Type == CharGenAppearancePageComponent.Gender;
                    System.Func<string> valueText = isGender ? (System.Func<string>)(() => vm.DollState?.Gender.ToString() ?? "") : null;
                    list.Add(new ProxySequentialSelector(comp, valueText, comp.Type.ToString()));
                }
                else
                {
                    // Portrait or any non-cycler component — visual, nothing to read out.
                    list.Add(new TextElement(() => Loc.T("chargen.appearance_visual")));
                }
            }
            _componentsPanel.Add(list);
        }
    }
}
