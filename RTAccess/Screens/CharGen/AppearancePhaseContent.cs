using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance.Components.Voice;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Appearance.Pages;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The appearance phase — organised as pages (Portrait / General / Hair / Tattoo / Implants / Voice),
    /// each with component selectors: a page-tab list, then the CURRENT page's components. Component keys
    /// carry the page identity, so switching pages re-keys only the components (page-tab focus stays put —
    /// the old components-only-rebuild guarantee, now by construction). Components:
    /// <list type="bullet">
    /// <item><b>Voice</b> — a radio list of named voices; selecting one plays its sample (the high-value
    /// part for a non-sighted player); Enter on the already-chosen voice replays it.</item>
    /// <item><b>Gender</b> — a cycler whose value is read from the doll (the game's localized
    /// Male/Female word), not just an index.</item>
    /// <item>Other cyclers (face/body/skin/hair/…) — navigable "Title, N of M" (the values are visual, so
    /// there's no name to read; cycling still works and the doll updates live). Single-option cyclers
    /// (IsAvailable false) drop out of nav, as in the game.</item>
    /// <item>Portrait — visual-only; surfaced as a note (a default portrait is kept).</item>
    /// </list>
    /// Appearance auto-completes with valid defaults, so none of this blocks finishing the character.
    /// </summary>
    public sealed class AppearancePhaseContent : CharGenPhaseContent
    {
        public AppearancePhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        private CharGenAppearanceComponentAppearancePhaseVM Vm => Phase as CharGenAppearanceComponentAppearancePhaseVM;

        public override void Build(GraphBuilder b, string k)
        {
            var vm = Vm;
            if (vm == null) { EmitUnavailable(b, k); return; }

            // Page tabs (stable keys: the phase carries them for its whole life).
            b.PushContext(Loc.T("chargen.appearance_pages"), Loc.T("role.list"));
            int pi = 0;
            foreach (var pg in vm.PagesSelectionGroupRadioVM.EntitiesCollection)
            {
                var p = pg; // capture
                b.AddItem(ControlId.Referenced(p, k + "page:" + pi++),
                    CharGenNodes.SelectionItem(p, () => p.PageLabel, type: ControlTypes.Tab));
            }
            b.PopContext();

            // The CURRENT page's components — keys carry the page, so a page switch re-keys only these.
            var page = vm.CurrentPageVM.Value;
            if (page == null) return;
            string pk = k + "pg:" + page.GetHashCode() + ":";

            b.PushContext(page.PageLabel, Loc.T("role.list"));
            int ci = 0;
            foreach (var c in page.Components)
            {
                var comp = c;
                if (comp is CharGenVoiceSelectorVM voice)
                {
                    b.PushContext(Loc.T("chargen.voices"), Loc.T("role.list"));
                    int vi = 0;
                    foreach (var v in voice.VoiceSelector.EntitiesCollection)
                    {
                        var item = v; // capture
                        b.AddItem(ControlId.Referenced(item, pk + "voice:" + vi++),
                            CharGenNodes.SelectionItem(item, () => item.DisplayName,
                                // Selecting plays the sample via the game's change-voice command; Enter on
                                // the already-chosen voice replays it (the sample IS the information here).
                                // The default click stays — the game plays the sample on top of it.
                                onActivate: () =>
                                {
                                    if (item.IsSelected.Value) item.Barks?.PlayPreview();
                                    else item.SetSelectedFromView(true);
                                }));
                    }
                    b.PopContext();
                }
                else if (CharGenNodes.SequentialHandles(comp))
                {
                    // A single-option cycler is unavailable in the game — drop it from nav entirely.
                    if (!CharGenNodes.SequentialAvailable(comp)) { ci++; continue; }
                    bool isGender = comp.Type == CharGenAppearancePageComponent.Gender;
                    System.Func<string> valueText = isGender
                        ? (System.Func<string>)(() => vm.DollState == null ? "" : CharGenNodes.GenderName(vm.DollState.Gender))
                        : null;
                    b.AddItem(ControlId.Referenced(comp, pk + "comp:" + ci),
                        CharGenNodes.SequentialSelector(comp, valueText, () => CharGenNodes.ComponentTitle(comp.Type)));
                }
                else
                {
                    // Portrait or any non-cycler component — visual, nothing to read out.
                    b.AddItem(ControlId.Structural(pk + "visual:" + ci),
                        GraphNodes.Text(() => Loc.T("chargen.appearance_visual")));
                }
                ci++;
            }
            b.PopContext();
        }
    }
}
