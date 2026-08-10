using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.BackgroundBase;
using Kingmaker.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateChargenBackground
using Kingmaker.Blueprints;                   // BlueprintExtenstions.TryGetComponent
using Kingmaker.UI.Common;                    // UIUtilityTexts.UpdateDescriptionWithUIProperties
using Kingmaker.UnitLogic.Components;         // ReplaceDescriptionForCharGen
using Kingmaker.UnitLogic.Progression.Features; // BlueprintFeature
using Owlcat.Runtime.UI.Tooltips;
using RTAccess.UI;
using Access.Core.Graph;

namespace RTAccess.Screens.CharGen
{
    /// <summary>
    /// The shared builder for every "background" chargen phase — homeworld (+ its child world), occupation
    /// (+ its child careers), navigator, soul mark, and the story child phases. They all derive from the
    /// generic <c>CharGenBackgroundBasePhaseVM&lt;T&gt;</c>, whose items share
    /// <see cref="CharGenBackgroundBaseItemVM"/> (a DisplayName + a Feature). We pull the items off the
    /// generic <c>SelectionGroup</c> by reflection (the base type is open-generic, so there's no shared
    /// non-generic accessor) and render: a radio list of the choices, then — in its OWN Tab stop, so the
    /// arrows stay in the list and Tab reaches the panel — a live description line of the
    /// SELECTED one (the committed selection, never a hover-fed reactive). Space on an item opens the
    /// game's own chargen-background tooltip for THAT feature; Space on the description line reads the
    /// phase's info panel (the InfoVM fallback — the old console "details" source, rewired).
    /// </summary>
    public sealed class SelectionPhaseContent : CharGenPhaseContent
    {
        public SelectionPhaseContent(CharGenPhaseBaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var items = Items(Phase).ToList();
            if (items.Count == 0)
            {
                b.AddItem(ControlId.Structural(k + "empty"),
                    GraphNodes.Text(() => Loc.T("chargen.nothing_to_select")));
                return;
            }

            // The choice list is its own presentation level: positions group by (parent, stop), so
            // the description line below must stay outside or it counts into the items' "n of m".
            // The phase-name label duplicates the outer context's — the announcer dedupes it on entry.
            b.PushContext(Phase?.PhaseName?.Value ?? "", Loc.T("role.list"));
            int i = 0;
            foreach (var it in items)
            {
                var item = it; // capture for the live closures
                b.AddItem(ControlId.Referenced(item, k + "item:" + i++),
                    CharGenNodes.SelectionItem(item, () => item.DisplayName,
                        // The same template the game's info panel renders for this feature.
                        tooltip: () => item.Feature != null
                            ? (TooltipBaseTemplate)new TooltipTemplateChargenBackground(item.Feature,
                                isInfoWindow: true, isCharGen: true)
                            : null));
            }
            b.PopContext();

            // Live description of whichever choice is currently SELECTED (updates as you arrow the
            // list — doctrine-3-safe: reads the committed selection, not a hover reactive). Skipped
            // while empty (the old TextElement self-hid), so it appears once something is selected.
            // Space here reads the phase's full info panel (InfoVM tooltip) via the shared chooser —
            // the CharGenAnnounce description fallback, rewired from the retired console details key.
            var phase = Phase;
            if (!string.IsNullOrEmpty(SelectedDescription(items)))
            {
                // The description is its own Tab STOP, not a tail of the choice list. A stop is an arrow
                // boundary, so the arrows stay inside the choices and Tab moves to the panel — the
                // one-stop-per-zone convention, and the case the wizard shell explicitly anticipates
                // ("content may open further stops of its own — a description panel"). Emitted as part of
                // the list it was reachable only by arrowing off the end of the choices, where it reads as
                // one more option.
                b.BeginStop("desc").PushContext(Loc.T("chargen.details"));
                b.AddItem(ControlId.Structural(k + "desc"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(() => SelectedDescription(Items(phase))),
                    },
                    // Through the TEMPLATE path, not the flattened string: the info panel lists the
                    // background's granted talents as hover-for-detail rows, so going via OpenTemplate is
                    // what makes "what does Luck do" answerable from here as well as from the choice itself.
                    OnTooltip = () => TooltipChooser.OpenTemplate(phase?.PhaseName?.Value,
                        RTAccess.Accessibility.CharGenAnnounce.GetActivePhaseTooltip()),
                });
                b.PopContext();
            }
        }

        /// <summary>The SELECTED background's description, composed the way
        /// <c>TooltipTemplateChargenBackground.AddDescription</c> composes it — which is the panel this line
        /// mirrors. A feature that ships a chargen-specific write-up (ReplaceDescriptionForCharGen; real in
        /// shipped data — Arbitrator, Exaction Castigators and Subductors all carry one) must speak THAT, not
        /// its generic Description, and the result goes through the game's UI-property expansion so embedded
        /// values resolve. Reading Description directly spoke the wrong text and contradicted this file's own
        /// Space page, which already passes isCharGen: true.</summary>
        private static string SelectedDescription(IEnumerable<CharGenBackgroundBaseItemVM> items)
        {
            foreach (var it in items)
                if (it.IsSelected.Value) return FeatureDescription(it.Feature);
            return "";
        }

        private static string FeatureDescription(BlueprintFeature feature)
        {
            if (feature == null) return "";
            string text = feature.TryGetComponent<ReplaceDescriptionForCharGen>(out var c)
                ? (string)c.CharGenDescription
                : feature.Description;
            if (string.IsNullOrEmpty(text)) return "";
            try { return UIUtilityTexts.UpdateDescriptionWithUIProperties(text, null); }
            catch { return text; }
        }

        // The phase's SelectionGroup (and its EntitiesCollection) are public fields on the open-generic
        // base, so reflect them by name; the items are all CharGenBackgroundBaseItemVM.
        internal static IEnumerable<CharGenBackgroundBaseItemVM> Items(CharGenPhaseBaseVM phase)
        {
            var sg = phase?.GetType().GetField("SelectionGroup")?.GetValue(phase);
            var ec = sg?.GetType().GetField("EntitiesCollection")?.GetValue(sg) as IEnumerable;
            if (ec == null) yield break;
            foreach (var o in ec)
                if (o is CharGenBackgroundBaseItemVM bi) yield return bi;
        }
    }
}
