using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.SaveLoad;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The save / load window (<c>CommonVM.SaveLoadVM</c>) as a navigable screen — so a blind player can
    /// load and create saves. One screen with a Save or Load <see cref="SaveLoadMode"/>, opened single-mode
    /// (just Save / just Load, from the Esc menu) or dual-mode (with a Save/Load tab selector, from the main
    /// menu). Tab-stops: the mode selector (dual-mode only), the slots as a <see cref="FlowSheet"/> table
    /// (one region per playthrough; column 0 is the save name AND the row's selection radio, the rest are
    /// metadata), then the action buttons. Selecting a slot (Enter) just sets the selection radio (no
    /// destructive load on a stray keypress); the Save / Load and Delete buttons then act on
    /// <c>SelectedSaveSlot</c>. New save creates a save with the auto name (RT's RequestSaveNew dedupes it).
    ///
    /// Layer 22: above the Esc menu (20) it's launched from — though they never actually coexist, since
    /// OnSave/OnLoad close the Esc menu first — and below the MessageBox confirm (30) that Load / Delete /
    /// overwrite raise. Escape closes through the VM's own OnClose.
    ///
    /// Verified against the decompiled SaveLoadVM/SaveSlotVM/SaveSlotGroupVM: RT differs from WOTR (no
    /// SaveTime string — SystemSaveTime is a DateTime; no ShowReadOnlyMark — delete is gated on the slot
    /// being actually saved; the slot/mode VMs are SelectionGroupEntityVMs driven through ProxySelectionItem).
    /// </summary>
    public sealed class SaveLoadScreen : Screen
    {
        public SaveLoadScreen() { Wrap = true; } // Tab cycles mode <-> slots <-> buttons

        public override string Key => "overlay.saveload";
        public override string ScreenName => "Save and Load";
        public override int Layer => 22;

        public override bool IsActive() => Vm() != null;

        private static SaveLoadVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.SaveLoadVM?.Value;

        private SaveLoadVM _builtFor;
        private SaveLoadMode _modeBuilt;
        private int _slotsBuilt = -1;

        public override void OnPush() { _builtFor = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFor = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // Rebuild on VM swap, mode flip (Save<->Load tab), or the slot list changing (save/delete settles
            // asynchronously after the list refresh).
            if (vm != _builtFor || vm.Mode.Value != _modeBuilt || SlotCount(vm) != _slotsBuilt)
            {
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        // Escape / Back closes the window through the VM's own close (the same path the game's close uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw("Close"), _ => vm.OnClose());
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
                var modeList = new ListContainer("Mode");
                foreach (var e in modes)
                    if (e != null) { var me = e; modeList.Add(new ProxySelectionItem(me, () => ModeLabel(me.Mode), role: "tab")); }
                Add(modeList);
            }

            // 2) The slots, grouped by playthrough into one flow-sheet table.
            var sheet = BuildSlots(vm);
            if (sheet != null) Add(sheet);

            // 3) Action buttons — each its own Tab-stop, acting on the selected slot.
            if (saveMode)
                Add(new ProxyActionButton("New save", () => vm.NewSaveSlotVM != null, NewSave));
            Add(new ProxyActionButton(saveMode ? "Save" : "Load",
                () => { var s = vm.SelectedSaveSlot.Value; return s != null && s.ShowSaveLoadButton; },
                () => vm.SelectedSaveSlot.Value?.SaveOrLoad()));
            Add(new ProxyActionButton("Delete",
                () => { var s = vm.SelectedSaveSlot.Value; return s != null && s.IsActuallySaved; },
                () => vm.SelectedSaveSlot.Value?.Delete(), actionVerb: "delete"));
        }

        private FlowSheet BuildSlots(SaveLoadVM vm)
        {
            // SaveSlotGroups is a private auto-property exposed by Code.dll's publicize (same mechanism the
            // shipped MainMenuButton relies on for ContextMenuEntityVM.m_Entity).
            var groups = vm.SaveSlotCollectionVm?.SaveSlotGroups;
            if (groups == null) return null;

            var sheet = new FlowSheet();
            bool any = false;
            foreach (var g in groups)
            {
                if (g == null || g.SaveLoadSlots == null || g.SaveLoadSlots.Count == 0) continue;
                g.IsExpanded.Value = true; // make the group's slots available/selectable (collapsed = unavailable)
                var region = sheet.Table(GroupLabel(g), "Location", "Saved", "Playtime", "Type", "Description").Associate(0);
                foreach (var slot in g.SaveLoadSlots)
                {
                    if (slot == null) continue;
                    var s = slot;
                    region.Row(new ProxySelectionItem(s, () => SlotName(s)), new UIElement[]
                    {
                        new TextElement(() => s.LocationName.Value),
                        new TextElement(() => SavedTime(s)),
                        new TextElement(() => s.TimeInGame.Value),
                        new TextElement(() => SlotType(s)),
                        new TextElement(() => s.Description.Value),
                    });
                    any = true;
                }
            }
            if (!any) return null;
            sheet.Reflow();
            return sheet;
        }

        // Create a new save with the auto/default name (RequestSaveNew auto-increments on a name clash). The
        // game's own slot lets you type a name; that text-entry path isn't wired here yet.
        private void NewSave() => Vm()?.NewSaveSlotVM?.SaveOrLoad();

        private static string ModeLabel(SaveLoadMode mode) => mode == SaveLoadMode.Save ? "Save" : "Load";

        private static string SlotName(SaveSlotVM s)
        {
            var n = s.SaveName.Value;
            return string.IsNullOrEmpty(n) ? "Unnamed save" : n;
        }

        private static string GroupLabel(SaveSlotGroupVM g)
        {
            if (!string.IsNullOrEmpty(g.CharacterName)) return g.CharacterName;
            if (!string.IsNullOrEmpty(g.GameName)) return g.GameName;
            return "Saves";
        }

        private static string SavedTime(SaveSlotVM s)
        {
            var t = s.SystemSaveTime.Value;
            return t == default(DateTime) ? "" : t.ToString("g");
        }

        // The slot's kind, from the game's marks (auto/quick are their own thing; a manual save may be DLC-gated).
        private static string SlotType(SaveSlotVM s)
        {
            if (s.ShowAutoSaveMark.Value) return "Auto";
            if (s.ShowQuickSaveMark.Value) return "Quick";
            return s.ShowDlcRequiredLabel.Value ? "Manual, DLC required" : "Manual";
        }
    }
}
