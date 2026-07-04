using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using Kingmaker.Code.UI.MVVM.VM.SaveLoad;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The save / load window (<c>CommonVM.SaveLoadVM</c>) as a navigable screen — so a blind player can
    /// load and create saves. One screen with a Save or Load <see cref="SaveLoadMode"/>, opened single-mode
    /// (just Save / just Load, from the Esc menu) or dual-mode (with a Save/Load tab selector, from the main
    /// menu).
    ///
    /// The saves are a <b>collapsible tree</b> (mirroring the game's own structure — <c>SaveSlotGroupVM</c>
    /// is natively an <c>ExpandableTitleVM</c>): one <see cref="TreeGroup"/> node per playthrough (character),
    /// its saves nested inside. The current playthrough opens; the rest start collapsed (the game's own
    /// <c>IsFirst</c> default), so a many-playthrough list stays skimmable — Left/Right fold a run you don't
    /// want. Navigation is node-centric, matching the loot/inventory idiom: <b>Enter</b> on a save loads it
    /// (or overwrites, in Save mode); <b>Backspace</b> deletes it; <b>Space</b> reads its required-DLC names;
    /// <b>Backspace</b> on a playthrough node deletes that whole character (the game's own confirming
    /// <c>DeleteAll</c>). Acting on the focused node directly means there is no "selected slot" to drift out
    /// of sync — the original "loads a save I didn't pick" bug is gone by construction, and browsing never
    /// drives the game's selection so it stays silent. Tab-stops: the mode selector (dual-mode only), New save
    /// (Save mode only — it isn't a slot in any playthrough), then the tree.
    ///
    /// Layer 22: above the Esc menu (20) it's launched from — though they never actually coexist, since
    /// OnSave/OnLoad close the Esc menu first — and below the MessageBox confirm (30) that Load / Delete /
    /// overwrite raise. Escape closes through the VM's own OnClose.
    ///
    /// Verified against the decompiled SaveLoadVM/SaveSlotVM/SaveSlotGroupVM: RT differs from WOTR (no
    /// SaveTime string — SystemSaveTime is a DateTime; no ShowReadOnlyMark — delete is gated on the slot
    /// being actually saved).
    /// </summary>
    public sealed class SaveLoadScreen : Screen
    {
        public SaveLoadScreen() { Wrap = true; } // Tab cycles mode <-> New save <-> saves tree

        public override string Key => "overlay.saveload";
        public override string ScreenName => Loc.T("screen.saveload");
        public override int Layer => 22;

        public override bool IsActive() => Vm() != null;

        private static SaveLoadVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.SaveLoadVM?.Value;

        private SaveLoadVM _builtFor;
        private SaveLoadMode _modeBuilt;
        private int _slotsBuilt = -1;
        private bool _wasUpdating;      // last-seen SaveListUpdating, to announce the refresh on its rising edge

        public override void OnPush() { _builtFor = null; _wasUpdating = false; Rebuild(); }
        public override void OnPop() { Clear(); _builtFor = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // The save list refreshes asynchronously — on open (before the slots exist) and after a save. Voice
            // the wait on its rising edge so a momentarily-empty list isn't heard as "no saves". Queued: it's a
            // passive status cue, not a keypress response.
            bool updating = vm.SaveListUpdating.Value;
            if (updating && !_wasUpdating && FocusMode.Active) Tts.Speak(Loc.T("save.updating"));
            _wasUpdating = updating;
            // Rebuild on VM swap, mode flip (Save<->Load tab), or the slot list changing (save/delete settles
            // asynchronously after the list refresh).
            if (vm != _builtFor || vm.Mode.Value != _modeBuilt || SlotCount(vm) != _slotsBuilt)
            {
                Rebuild();
                Navigation.Attach(this);
            }
        }

        // Escape / Back closes the window through the VM's own close (the same path the game's close uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.OnClose());
        }

        // AllTitlesAndSlots (public) holds the group titles + real slots; its count is a cheap change signal.
        private static int SlotCount(SaveLoadVM vm)
            => vm.SaveSlotCollectionVm?.AllTitlesAndSlots?.Count ?? 0;

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFor = vm;
            if (vm == null) return;
            _modeBuilt = vm.Mode.Value;
            _slotsBuilt = SlotCount(vm);
            bool saveMode = vm.Mode.Value == SaveLoadMode.Save;

            // 1) Mode selector — only when both modes are offered (dual-mode, from the main menu).
            var modes = vm.SaveLoadMenuVM?.SelectionGroup?.EntitiesCollection;
            if (modes != null && modes.Count > 1)
            {
                var modeList = new ListContainer(Loc.T("save.mode"));
                foreach (var e in modes)
                    if (e != null) { var me = e; modeList.Add(new ProxySelectionItem(me, () => ModeLabel(me.Mode), role: "tab")); }
                Add(modeList);
            }

            // 2) New save — its own Tab-stop (Save mode only; it isn't a slot in any playthrough).
            if (saveMode)
                Add(new ProxyActionButton(Loc.T("save.new"), () => vm.NewSaveSlotVM != null, NewSave));

            // 3) The saves as a collapsible tree — one node per playthrough, its saves nested inside.
            var tree = BuildTree(vm);
            if (tree != null) Add(tree);
        }

        // The saves as a tree: a structural root TreeGroup holding one collapsible node per playthrough, each
        // holding its saves. Enter loads/overwrites a save, Backspace deletes it, Backspace on the playthrough
        // node deletes that character — all on the focused node, so nothing drifts out of sync. The first
        // playthrough opens; the rest start collapsed (the game's own IsFirst default).
        private TreeGroup BuildTree(SaveLoadVM vm)
        {
            // SaveSlotGroups is a private auto-property exposed by Code.dll's publicize (same mechanism
            // GraphNodes.MenuEntry relies on for ContextMenuEntityVM.m_Entity).
            var groups = vm.SaveSlotCollectionVm?.SaveSlotGroups;
            if (groups == null) return null;

            // Two playthroughs can share a character name (the game keys groups by GameId, not name, and its own
            // headers collide too — it leans on the screenshot to tell them apart, which a blind player can't).
            // Find the colliding names so their headers get a date suffix to distinguish them.
            var ambiguous = AmbiguousGroupNames(groups);

            var root = new TreeGroup();
            bool first = true;
            foreach (var g in groups)
            {
                if (g == null || g.SaveLoadSlots == null || g.SaveLoadSlots.Count == 0) continue;
                // Keep the game VM's slots available/actionable regardless of OUR fold state — the two are
                // independent (the game's IsExpanded gates slot availability; our tree node gates navigation).
                g.IsExpanded.Value = true;
                var group = g; // capture for the delete-all closure
                var node = new TreeGroup(GroupNodeLabel(group, ambiguous))
                {
                    ContextLabel = () => Loc.T("save.delete_all", new { character = group.CharacterName }),
                    ContextAction = () => group.ExpandableTitleVM.DeleteAll(), // the game's own confirming delete-all
                };
                foreach (var slot in group.SaveLoadSlots)
                {
                    if (slot == null) continue;
                    var s = slot;
                    // Node-centric: Enter loads/overwrites THIS save (SaveOrLoad self-gates on mode/type),
                    // Backspace deletes it (the game's confirm), Space reads the required-DLC names.
                    node.Add(new ProxySelectionItem(s, () => SlotBrowseLabel(s), role: "item",
                        onActivate: () => s.SaveOrLoad(),
                        onContext: () => s.Delete(), contextLabel: () => Loc.T("save.action.delete"),
                        detail: () => DlcDetail(s)));
                }
                root.Add(node);
                if (first) { node.Expand(); first = false; }
            }
            return root.Children.Count > 0 ? root : null;
        }

        // Create a new save with a player-typed name. The game's own new-save flow uses an inline field that
        // isn't mounted under our screen, so we prompt with the game's text-field dialog instead: it's surfaced
        // by MessageBoxScreen (which drives the box's live field for typing), pre-filled with the default name.
        // Accept saves with the typed/kept name — TrySetSaveName then SaveOrLoad → RequestSaveNew, which dedupes
        // a name clash by appending a number — Decline (empty result) cancels.
        private void NewSave()
        {
            var newVm = Vm()?.NewSaveSlotVM;
            if (newVm == null) return;
            EventBus.RaiseEvent<IDialogMessageBoxUIHandler>(h => h.HandleOpen(
                Loc.T("save.name_prompt"), DialogMessageBoxBase.BoxType.TextField, null, null, null, null,
                name =>
                {
                    var v = Vm()?.NewSaveSlotVM;
                    if (v == null || string.IsNullOrEmpty(name)) return; // Decline / empty → cancel
                    v.TrySetSaveName(name);
                    v.SaveOrLoad();
                },
                inputText: newVm.SaveName.Value));
        }

        private static string ModeLabel(SaveLoadMode mode) => Loc.T(mode == SaveLoadMode.Save ? "save.mode.save" : "save.mode.load");

        private static string SlotName(SaveSlotVM s)
        {
            var n = s.SaveName.Value;
            return string.IsNullOrEmpty(n) ? Loc.T("save.unnamed") : n;
        }

        // The save's browse label mirrors what the CARD shows (minus the character — that's the parent
        // playthrough node): name, kind, when saved, where, in-game time, then any description. The required-DLC
        // NAMES stay on Space (detail); only the "DLC required" mark is on the card, and it's folded into kind.
        private static string SlotBrowseLabel(SaveSlotVM s)
        {
            var parts = new List<string>();
            AppendIf(parts, SlotName(s));
            AppendIf(parts, SlotType(s));
            AppendIf(parts, SavedTime(s));
            AppendIf(parts, s.LocationName.Value);
            AppendIf(parts, s.TimeInGame.Value);
            AppendIf(parts, s.Description.Value);
            return string.Join(", ", parts);
        }

        private static void AppendIf(List<string> list, string v) { if (!string.IsNullOrWhiteSpace(v)) list.Add(v); }

        // The playthrough node's label: the disambiguated character name + its save count.
        private static string GroupNodeLabel(SaveSlotGroupVM g, HashSet<string> ambiguous)
        {
            var name = GroupDisplayName(g, ambiguous);
            int n = g.SaveLoadSlots?.Count ?? 0;
            return Loc.T(n == 1 ? "save.group_label_one" : "save.group_label", new { name, count = n });
        }

        private static string GroupLabel(SaveSlotGroupVM g)
        {
            if (!string.IsNullOrEmpty(g.CharacterName)) return g.CharacterName;
            if (!string.IsNullOrEmpty(g.GameName)) return g.GameName;
            return Loc.T("save.group.default");
        }

        // Character names shared by more than one playthrough (the game keys groups by GameId, so same-named
        // runs are distinct groups) — those headers get a date suffix in GroupDisplayName to tell them apart.
        private static HashSet<string> AmbiguousGroupNames(IEnumerable<SaveSlotGroupVM> groups)
        {
            var seen = new HashSet<string>();
            var dup = new HashSet<string>();
            foreach (var g in groups)
            {
                if (g == null || g.SaveLoadSlots == null || g.SaveLoadSlots.Count == 0) continue;
                var name = GroupLabel(g);
                if (!seen.Add(name)) dup.Add(name);
            }
            return dup;
        }

        // The playthrough header. Plain character name, except when two runs share it — then suffix the newest
        // save's date so the regions are distinguishable (a sighted player uses the screenshot; we can't).
        private static string GroupDisplayName(SaveSlotGroupVM g, HashSet<string> ambiguous)
        {
            var name = GroupLabel(g);
            if (ambiguous == null || !ambiguous.Contains(name)) return name;
            var newest = default(DateTime);
            if (g.SaveLoadSlots != null)
                foreach (var s in g.SaveLoadSlots)
                    if (s != null && s.SystemSaveTime.Value > newest) newest = s.SystemSaveTime.Value;
            return newest == default(DateTime) ? name
                : Loc.T("save.group_disambig", new { name, date = newest.ToString("g") });
        }

        private static string SavedTime(SaveSlotVM s)
        {
            var t = s.SystemSaveTime.Value;
            return t == default(DateTime) ? "" : t.ToString("g");
        }

        // The slot's kind, from the game's reference type + marks. IronMan (permadeath) shares the auto-save
        // mark on the card (ShowAutoSaveMark is set for BOTH Auto and IronMan) but is its own SaveType — and
        // the CURRENT run's IronMan save can't be loaded at all (SaveOrLoad refuses it), so we surface that up
        // front instead of letting the player discover it after pressing Load. A DLC-gated save of ANY kind is
        // refused until the DLC is present (not just manual saves) — append that wherever it applies.
        private static string SlotType(SaveSlotVM s)
        {
            string kind;
            if (s.Reference != null && s.Reference.Type == SaveInfo.SaveType.IronMan)
                kind = s.IsCurrentIronManSave.Value ? Loc.T("save.ironman_current") : Loc.T("save.ironman");
            else if (s.ShowAutoSaveMark.Value) kind = Loc.T("save.mark.auto");
            else if (s.ShowQuickSaveMark.Value) kind = Loc.T("save.mark.quick");
            else kind = Loc.T("save.mark.manual");
            return s.ShowDlcRequiredLabel.Value ? Loc.T("save.type_dlc", new { type = kind }) : kind;
        }

        // The card shows only "DLC required" (SlotType); on Space, name the missing DLC(s) so the player knows
        // what to enable. DlcRequiredMap is a list of requirement groups — flatten to the distinct names.
        private static string DlcDetail(SaveSlotVM s)
        {
            if (s == null || !s.ShowDlcRequiredLabel.Value) return null;
            var names = new List<string>();
            if (s.DlcRequiredMap != null)
                foreach (var grp in s.DlcRequiredMap)
                    if (grp != null)
                        foreach (var n in grp)
                            if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            return names.Count == 0 ? Loc.T("save.requires_dlc_unknown")
                : Loc.T("save.requires_dlc", new { dlc = string.Join(", ", names) });
        }
    }
}
