using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Transition; // TransitionVM, TransitionEntryVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The area-transition "map" (RT's <c>BlueprintMultiEntrance</c> fast-travel screen) as a navigable list —
    /// so a blind player can read which rooms/decks exist and travel between them. This is the window the map key
    /// opens INSIDE a ship (<c>ServiceWindowsVM.BindKeys</c> unbinds Local Map when <c>UIUtility.IsShipArea()</c>),
    /// and the same window any multi-entrance object raises elsewhere; one screen covers them all.
    ///
    /// The game builds <c>SurfaceStaticPartVM.TransitionVM</c> (or the Space equivalent) from an
    /// <c>IMultiEntranceHandler.HandleMultiEntrance</c> request — a <see cref="TransitionVM"/> whose
    /// <see cref="TransitionVM.EntryVms"/> are the rooms, each a <see cref="TransitionEntryVM"/> carrying its
    /// name + reactive state (visible / interactable / current-location / has-quest). It's entirely
    /// mouse/console-driven (hover a hotspot → light beam + pantograph label; click → area transition), with no
    /// keyboard a11y and no service-window announce (Transition isn't a <c>ServiceWindowsType</c>), so on open a
    /// blind player otherwise hears only the book-open sound.
    ///
    /// Graph-native: the reachable rooms declared fresh from the live VM every render, each a button whose
    /// Enter runs <see cref="TransitionEntryVM.Enter"/> — the game's real <c>GameCommandQueue.AreaTransition</c>,
    /// which also closes the map. Node keys carry the VM's identity, so a window swapped under us drops the
    /// old keys and focus re-homes with a fresh readout — no rebuild bookkeeping. An Exclusive modal (owns the
    /// keyboard while open) at layer 24; Escape / Back closes through the VM's own
    /// <see cref="TransitionVM.Close"/>.
    /// </summary>
    public sealed class TransitionScreen : Screen
    {
        public override string Key => "transition";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on focus (no ServiceWindowAnnounce covers this window): "<map name>, N destinations".
        public override string ScreenName
        {
            get
            {
                var vm = Vm();
                if (vm == null) return Loc.T("screen.map");
                var name = string.IsNullOrWhiteSpace(vm.Name) ? Loc.T("screen.map") : vm.Name;
                int n = Travelable(vm);
                if (n <= 0) return name;
                return name + ", " + (n == 1 ? Loc.T("transition.count_one") : Loc.T("transition.count_many", new { count = n }));
            }
        }

        public override bool IsActive() => Vm() != null;

        // Held on Surface OR Space static part (the map opens in both contexts), like ServiceWindows in JournalScreen.
        private static TransitionVM Vm()
        {
            var ctx = Game.Instance?.RootUiContext;
            return ctx?.SurfaceVM?.StaticPartVM?.TransitionVM?.Value
                ?? ctx?.SpaceVM?.StaticPartVM?.TransitionVM?.Value;
        }

        // Rooms worth listing: every reachable destination + your current location (for orientation). Undiscovered
        // rooms (IsVisible == false and not interactable) are hidden on the visual map too, so we omit them.
        private static IEnumerable<TransitionEntryVM> Listed(TransitionVM vm)
            => vm.EntryVms.Where(e => e != null && (e.IsInteractable.Value || e.IsVisible.Value));

        // How many you can actually travel to (drives the "N destinations" count; current-location isn't one).
        private static int Travelable(TransitionVM vm)
            => vm.EntryVms.Count(e => e != null && e.IsInteractable.Value);


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "trans:" + vm.GetHashCode() + ":"; // re-keys on a VM swap → focus re-homes, fresh readout

            bool any = false;
            int i = 0;
            foreach (var entry in Listed(vm))
            {
                var e = entry; // capture per iteration
                b.AddItem(ControlId.Referenced(e, k + i), GraphNodes.Button(
                    () => EntryLabel(e),
                    () => e.Enter(),
                    () => e.IsInteractable.Value,
                    tooltip: () => e.GetTooltipTemplate())); // quest objectives here, on the details key
                any = true;
                i++;
            }
            // Defensive: a map with nothing discovered/reachable still needs a focus target so Escape can close.
            if (!any)
                b.AddItem(ControlId.Structural(k + "none"), GraphNodes.Button(
                    () => Loc.T("transition.none"), () => { }, () => false));
        }

        // "<room name>[, you are here | , unavailable][, quest objective]". The literal current spot is
        // current-location AND not interactable (you can't travel to where you stand); a same-area spot you can
        // still walk to stays a plain travel target.
        private static string EntryLabel(TransitionEntryVM e)
        {
            var name = e.Name.Value ?? "";
            var tags = new List<string>();
            if (e.IsCurrentlyLocation && !e.IsInteractable.Value) tags.Add(Loc.T("transition.you_are_here"));
            else if (!e.IsInteractable.Value) tags.Add(Loc.T("transition.unavailable"));
            if (e.Attention.Value) tags.Add(Loc.T("transition.quest_here"));
            return tags.Count == 0 ? name : name + ", " + string.Join(", ", tags);
        }

        // Escape / Back closes the map through the VM's own close (guarded ship/capital-party path).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ => vm.Close());
        }
    }
}
