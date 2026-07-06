using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.VariativeInteraction;
using RTAccess.Accessibility; // InteractableDescriber (object name)
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The variative-interaction chooser (RT's generalization of Pathfinder's lockpick prompt) as a navigable
    /// modal — so a blind player can PICK how to open a locked/variative object (a skill check vs Tech-Use vs a
    /// Key vs a Melta charge vs Destroy, each with its own success chance) instead of the game silently
    /// auto-running the first available actor.
    ///
    /// RT has no <c>LockpickVM</c>; a locked/variative object raises <see cref="IVariativeInteractionUIHandler"/>
    /// and the game builds <c>SurfaceDynamicPartVM.VariativeInteractionVM.Value</c> — a collection of
    /// <see cref="InteractionVariantVM"/> rows, each already carrying its localized label + chance (via
    /// <c>UIUtility.GetInteractionVariantActorText</c>). Graph-native: one button per variant, declared fresh
    /// from that live collection every render; activating one runs <see cref="InteractionVariantVM.Interact"/> —
    /// which dispatches through the game's own <c>ClickMapObjectHandler.TryInteract</c> and then closes the VM.
    /// Node keys carry the VM's identity, so a fresh interaction request (a new VM) re-keys the rows and focus
    /// re-homes with a fresh readout — no rebuild bookkeeping. The win/fail OUTCOME is voiced by the game's own
    /// combat log (PickLockLogThread / InteractionRestrictionLogThread) via
    /// <see cref="RTAccess.Accessibility.LogTap"/>.
    ///
    /// We only reach this screen because <see cref="RTAccess.Exploration.ProxyMapObject.Interact"/> raises the
    /// request event when <see cref="VariativeInteractionVM.HasVariativeInteraction"/> is true — the static
    /// <c>ClickMapObjectHandler.Interact</c> the mod otherwise calls skips that branch (it lives only in the mouse
    /// <c>OnClick</c> / overtip paths, so without this the choice never surfaces).
    ///
    /// Layer 24 (above the in-game context / service windows / EscMenu, below Settings/Tutorial/MessageBox) and
    /// <see cref="Exclusive"/> so it owns the keyboard while open. Escape / Back closes via the VM's own
    /// <see cref="VariativeInteractionVM.Close"/> (the game's <c>DisposeLockpick</c> callback nulls the Value,
    /// deactivating this screen and returning control to exploration).
    /// </summary>
    public sealed class VariativeInteractionScreen : Screen
    {
        public override string Key => "variative_interaction";
        public override int Layer => 24;
        public override bool Exclusive => true;

        public override string ScreenName
        {
            get
            {
                var vm = Vm();
                if (vm?.MapObjectView == null) return Loc.T("vi.interaction");
                try
                {
                    var n = InteractableDescriber.ResolveName(vm.MapObjectView, out _);
                    return string.IsNullOrWhiteSpace(n) ? Loc.T("vi.interaction") : Loc.T("vi.screen_name", new { name = n });
                }
                catch { return Loc.T("vi.interaction"); }
            }
        }

        public override bool IsActive() => Vm() != null;

        private static VariativeInteractionVM Vm()
            => Game.Instance?.RootUiContext?.SurfaceVM?.DynamicPartVM?.VariativeInteractionVM?.Value;

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "vi:" + vm.GetHashCode() + ":";

            bool any = false;
            int i = 0;
            foreach (var variant in vm.Variants)
            {
                i++;
                if (variant == null) continue;
                var v = variant; // capture per iteration
                b.AddItem(ControlId.Referenced(v, k + i), GraphNodes.Button(
                    () => VariantLabel(v),
                    () => v.Interact(),
                    () => !v.Disabled));
                any = true;
            }
            // Defensive: a variative object whose actors all got filtered (all UnlockRestriction / !CanUse) would
            // leave an empty list; give the player something to hear and a focus target so Escape still cancels.
            if (!any)
                b.AddItem(ControlId.Structural(k + "none"), GraphNodes.Text(() => Loc.T("vi.no_options")));
        }

        // The VM's row text is already "<name>: <chance>% [<unit>]" (or "<name>: Locked%") — game-localized,
        // passed through; append the tool/ammo requirement. Disabled is spoken by the button's standard
        // disabled part, not the label.
        private static string VariantLabel(InteractionVariantVM v)
        {
            var text = v.InteractionName != null ? v.InteractionName.Value : null;
            if (string.IsNullOrEmpty(text)) text = Loc.T("vi.interaction");
            if (v.RequiredResourceCount.HasValue && v.RequiredResourceCount.Value > 0 && !string.IsNullOrEmpty(v.ResourceName))
                text += ", " + Loc.T("vi.resource", new { have = v.ResourceCount ?? 0, need = v.RequiredResourceCount.Value, name = v.ResourceName });
            return text;
        }

        // Escape / Back cancels the whole choice through the VM's own close (the game's DisposeLockpick callback).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"), _ => vm.Close());
        }
    }
}
