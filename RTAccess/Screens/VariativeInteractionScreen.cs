using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.VariativeInteraction;
using RTAccess.Accessibility; // InteractableDescriber (object name)
using RTAccess.UI;
using RTAccess.UI.Proxies;

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
    /// <c>UIUtility.GetInteractionVariantActorText</c>). We mirror that live collection into
    /// <see cref="ProxyActionButton"/>s; activating one runs <see cref="InteractionVariantVM.Interact"/> — which
    /// dispatches through the game's own <c>ClickMapObjectHandler.TryInteract</c> and then closes the VM. The
    /// win/fail OUTCOME is still voiced separately by <see cref="RTAccess.Accessibility.InteractionEvents"/>
    /// (<c>IPickLockHandler</c>).
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
                if (vm?.MapObjectView == null) return "Interaction";
                try { var n = InteractableDescriber.ResolveName(vm.MapObjectView, out _); return string.IsNullOrWhiteSpace(n) ? "Interaction" : n + ", interaction"; }
                catch { return "Interaction"; }
            }
        }

        public override bool IsActive() => Vm() != null;

        private static VariativeInteractionVM Vm()
            => Game.Instance?.RootUiContext?.SurfaceVM?.DynamicPartVM?.VariativeInteractionVM?.Value;

        private VariativeInteractionVM _builtVm;

        // Build in OnPush so the screen-name + first-focus announce right after push. Rebuild only if the game
        // swaps the VM under us (a fresh interaction request).
        public override void OnPush() { _builtVm = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtVm = null; }
        public override void OnUpdate() { Rebuild(); }

        private void Rebuild()
        {
            var vm = Vm();
            if (vm == null || vm == _builtVm) return;
            _builtVm = vm;
            Clear();

            var list = new ListContainer();
            bool any = false;
            foreach (var variant in vm.Variants)
            {
                if (variant == null) continue;
                var v = variant; // capture per iteration
                list.Add(new ProxyActionButton(() => VariantLabel(v), () => !v.Disabled, () => v.Interact(), actionVerb: "choose"));
                any = true;
            }
            // Defensive: a variative object whose actors all got filtered (all UnlockRestriction / !CanUse) would
            // leave an empty list; give the player something to hear and a focus target so Escape still cancels.
            if (!any)
                list.Add(new ProxyActionButton("No interaction options.", () => false, () => { }));
            Add(list);
        }

        // The VM's row text is already "<name>: <chance>% [<unit>]" (or "<name>: Locked%"); append the tool/ammo
        // requirement and a spoken "unavailable" note so a disabled choice reads clearly.
        private static string VariantLabel(InteractionVariantVM v)
        {
            var text = v.InteractionName != null ? v.InteractionName.Value : null;
            if (string.IsNullOrEmpty(text)) text = "Interaction";
            if (v.RequiredResourceCount.HasValue && v.RequiredResourceCount.Value > 0 && !string.IsNullOrEmpty(v.ResourceName))
                text += ", " + (v.ResourceCount ?? 0) + " of " + v.RequiredResourceCount.Value + " " + v.ResourceName;
            if (v.Disabled) text += ", unavailable";
            return text;
        }

        // Escape / Back cancels the whole choice through the VM's own close (the game's DisposeLockpick callback).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw("Cancel"), _ => vm.Close());
        }
    }
}
