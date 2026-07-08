using System.Collections.Generic;
using Kingmaker;                              // Game
using Kingmaker.Blueprints.Root.Strings;      // UIStrings.FormationTexts (game-localized labels)
using Kingmaker.Code.UI.MVVM.VM.Formation;    // FormationVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The party-formation window (<see cref="FormationVM"/> on <c>SurfaceStaticPartVM.FormationVM</c>),
    /// opened from the HUD menu / Esc menu / the game's relocated Ctrl+N — graph-native, ported from
    /// WrathAccess's FormationScreen (main-HUD audit #4). Tab stops, in order: the <b>formations list</b>
    /// (a radio over the game's predefined shapes, named from their blueprints), the WASD editing
    /// <b>field</b>, <b>Restore to default</b>, the <b>preserve-formation</b> toggle (keep relative
    /// positions when moving — the VM exposes it though the sighted PC window never shows it), and
    /// <b>Close</b>. The list drives the game's own SelectionGroupRadioVM (selection applies through
    /// GameCommandQueue.PartyFormationIndex); Restore/preserve only apply to the editable Custom formation,
    /// so they grey out elsewhere. The field's 2-D cursor is OUR editor state (like the exploration cursor),
    /// held on <see cref="FormationField"/> — reset each open, never mirroring game state. The window
    /// UI-pauses the game while open — a per-window UI pause the sighted PAUSED banner (and hence
    /// <see cref="RTAccess.Accessibility.PauseAnnouncer"/>) deliberately does NOT announce, exactly like
    /// the inventory; the screen-name announce carries the "game paused" word instead. Layer 16, Exclusive
    /// (owns the keyboard while open). Escape closes via FormationVM.Close — the same EscHotkeyManager path
    /// the sighted window binds.
    /// </summary>
    public sealed class FormationScreen : Screen
    {
        public FormationScreen() { Wrap = true; }

        public override string Key => "overlay.formation";
        public override string ScreenName => Loc.T("screen.formation");
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override bool AllowsTypeahead => false; // WASD drive the editor field; no name-search needed

        // While the WASD editor field is focused, claim the Formation category so WASD/glide/review keys
        // route to the field; on the other tab stops only UI is live (so those keys stay free).
        private static readonly RTAccess.Input.InputCategory[] FieldCats =
            { RTAccess.Input.InputCategory.Formation, RTAccess.Input.InputCategory.UI };
        private static readonly RTAccess.Input.InputCategory[] BaseCats =
            { RTAccess.Input.InputCategory.UI };
        public override IReadOnlyList<RTAccess.Input.InputCategory> InputCategories
            => IsFieldFocused ? FieldCats : BaseCats;

        public override bool IsActive() => Vm() != null;

        internal static FormationVM Vm()
            => Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM?.FormationVM?.Value;

        // The editor's 2-D cursor + held member — mod-owned editor state, fresh per open.
        private FormationField _field = new FormationField();

        private bool IsFieldFocused
            => ReferenceEquals(ScreenManager.Current, this)
               && Equals(Navigation.Active?.FocusedStopKey, "field");

        /// <summary>The editor field of the open, field-focused formation screen — the target the
        /// Formation-category input actions (WASD / Shift+WASD / Comma / Slash / C / Alt+digits) route to;
        /// null otherwise, so the handlers no-op anywhere else.</summary>
        internal static FormationField FocusedField
            => ScreenManager.Current is FormationScreen fs && fs.IsFieldFocused ? fs._field : null;

        public override void OnPush() { _field = new FormationField(); }

        // The glide tick (Shift+WASD held) runs only while the field is focused.
        public override void OnUpdate() { if (IsFieldFocused) _field.Tick(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The formations, in the game's order — names come from the formation blueprints
            // (game-localized, passed through). Selecting applies immediately, exactly like the sighted
            // radio (the SelectionGroup reactive routes into GameCommandQueue.PartyFormationIndex).
            var names = FormationNames();
            b.BeginStop("list");
            b.PushContext(Loc.T("formation.list"), Loc.T("role.list"));
            int i = 0;
            foreach (var item in vm.FormationSelector.EntitiesCollection)
            {
                if (item == null) { i++; continue; }
                var it = item; // capture for the live closures
                b.AddItem(ControlId.Referenced(it, "form:" + i), GraphNodes.ChoiceOption(
                    () => it.FormationIndex >= 0 && it.FormationIndex < names.Count ? names[it.FormationIndex] : "",
                    () => it.IsSelected.Value,
                    () => it.SetSelectedFromView(true)));
                i++;
            }
            b.PopContext();

            // The WASD editing field: one stop holding the 2-D cursor. Enter picks up / drops; the value
            // reads what the cursor is over (moves speak themselves, so the part isn't live).
            b.BeginStop("field").AddItem(ControlId.Structural("field"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("formation.field")),
                    new NodeAnnouncement(() => _field.CellReadout(), kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("formation.field"),
                OnActivate = () => _field.PickOrDrop(), // pick-up/placement speaks itself (no generic click)
            });

            // Footer. Restore + preserve act on the Custom formation only (the game greys Restore the same
            // way); Restore's label is the game's own localized string, preserve has no sighted PC string.
            var t = UIStrings.Instance.FormationTexts;
            b.BeginStop("restore").AddItem(ControlId.Structural("restore"), GraphNodes.Button(
                () => (string)t.RestoreToDefault,
                () => Vm()?.ResetCurrentFormation(),
                () => Vm()?.IsCustomFormation ?? false));
            b.BeginStop("preserve").AddItem(ControlId.Structural("preserve"), GraphNodes.Toggle(
                () => Loc.T("formation.preserve"),
                () => Vm()?.m_IsPreserveFormation.Value ?? false,
                () => Vm()?.SwitchPreserveFormation(),
                () => Vm()?.IsCustomFormation ?? false));
            b.BeginStop("close").AddItem(ControlId.Structural("close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
        }

        // The predefined formations' display names, in order (parallel to the selector items by index).
        private static List<string> FormationNames()
        {
            var list = new List<string>();
            var formations = Game.Instance?.BlueprintRoot?.Formations?.PredefinedFormations;
            if (formations != null)
                foreach (var f in formations) list.Add(f != null ? (string)f.Name : "");
            return list;
        }
    }
}
