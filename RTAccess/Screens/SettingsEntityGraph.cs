using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Decorative;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Difficulty;
using Owlcat.Runtime.UI.MVVM;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// Emits graph nodes from a settings-entity collection (the SettingsEntity* VMs): runs of entities
    /// under a header become an expandable GROUP (a tree section — collapse to skip it), each entity a
    /// typed control node, a key-binding row its own sub-group holding the two binding slots, and the
    /// statistics opt-out row a sub-group holding the card's two buttons. Node identity: the entity VM
    /// (tier 1) + a structural key of prefix:index (tier 2), so focus follows a control across renders
    /// and a rebuilt VM list still reconciles by position. Serves the Settings window (tree of
    /// sections) and the New Game difficulty step (<c>flat</c> — see Emit).
    /// </summary>
    internal static class SettingsEntityGraph
    {
        /// <summary>Emit the entities into the builder under <paramref name="keyPrefix"/>-scoped ids.
        /// <paramref name="flat"/> skips the headers entirely — the options become a plain vertical
        /// list. For a short single-section page whose label already carries the header (the New Game
        /// difficulty step): a group node you must expand — or a redundant header text row — is pure
        /// friction there (the WA ear-pass lesson).</summary>
        public static void Emit(GraphBuilder b, IEnumerable<VirtualListElementVMBase> entities, string keyPrefix,
            bool flat = false)
        {
            if (entities == null) return;
            bool open = false;
            int i = 0;
            foreach (var e in entities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    if (flat) { i++; continue; } // the page labels itself — no header/group node
                    if (open) b.EndGroup();
                    string title = header.Tittle?.Text; // (sic — the VM's field)
                    // Index in the key: two same-titled (or untitled) sections must not collide ControlIds
                    // (MakeNode throws on duplicates). i is stable across renders, so expansion memory holds.
                    b.BeginGroup(ControlId.Structural(keyPrefix + "sec:" + i + ":" + title), GraphNodes.Group(() => title));
                    open = true;
                    i++;
                    continue;
                }

                if (e is SettingEntityKeyBindingVM kb)
                {
                    // A key-binding row = an expandable sub-group (labeled with the control) holding its
                    // two binding slots: expand to reach them, Enter rebinds, Backspace clears.
                    string kbKey = keyPrefix + "kb:" + i;
                    b.BeginGroup(ControlId.Referenced(kb, kbKey), GraphNodes.Group(() => kb.Title?.Text));
                    b.AddItem(ControlId.Structural(kbKey + ":0"),
                        GraphNodes.KeyBindingSlot(kb, 0, Loc.T("bind.slot", new { index = 1 })));
                    b.AddItem(ControlId.Structural(kbKey + ":1"),
                        GraphNodes.KeyBindingSlot(kb, 1, Loc.T("bind.slot", new { index = 2 })));
                    b.EndGroup();
                    i++;
                    continue;
                }

                if (e is SettingsEntityStatisticsOptOutVM opt)
                {
                    // The privacy/statistics opt-out card shows its title plus TWO buttons (go to the
                    // browser settings page; delete collected data — the latter raises the game's own
                    // confirm box). Mirrored as a sub-group holding both, labels passing through the
                    // game's own card strings.
                    string optKey = keyPrefix + "opt:" + i;
                    b.BeginGroup(ControlId.Referenced(opt, optKey), GraphNodes.Group(() => opt.Title?.Text));
                    b.AddItem(ControlId.Structural(optKey + ":show"), GraphNodes.Button(
                        () => GameText.Or(() => Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.SettingsUI.ShowStatistics,
                            "settings.show_statistics"),
                        () => opt.OpenSettingsInBrowser()));
                    b.AddItem(ControlId.Structural(optKey + ":delete"), GraphNodes.Button(
                        () => GameText.Or(() => Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.SettingsUI.DeleteStatisticsData,
                            "settings.delete_statistics"),
                        () => opt.DeleteStatisticsData()));
                    b.EndGroup();
                    i++;
                    continue;
                }

                var vt = MakeVtable(e);
                if (vt != null)
                    b.AddItem(ControlId.Referenced(e, keyPrefix + "e:" + i), vt);
                i++;
            }
            if (open) b.EndGroup();
        }

        // The entity→control mapping (the old SettingsEntityBuilder.MakeProxy, factory-shaped).
        // Subclass checks come before their bases (difficulty before dropdown).
        private static NodeVtable MakeVtable(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return GraphNodes.GameToggle(b);
            if (e is SettingsEntityBoolOnlyOneSaveVM oneSave) return GraphNodes.GameToggle(oneSave);
            if (e is SettingsEntitySliderVM s) return GraphNodes.Slider(s);
            if (e is SettingsEntityDropdownGameDifficultyVM diff) return GraphNodes.GameDifficulty(diff);
            if (e is SettingsEntityDropdownVM d) return GraphNodes.GameDropdown(d);
            // Display/accessibility image rows (visual-only) — still announced, still not accessible.
            if (e is SettingsEntityVM sv) return GraphNodes.UnsupportedSetting(() => sv.Title?.Text);
            return null;
        }
    }
}
