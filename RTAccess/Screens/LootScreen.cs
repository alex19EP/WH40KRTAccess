using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's loot window (<see cref="LootVM"/>) as a mod-owned navigable screen. Interacting with a
    /// container already opens this window today — but invisibly, silently swallowing the keyboard. This screen
    /// makes it usable — graph-native, declared fresh from the live VM each render: one Tab-stop list per loot
    /// group (the game's inventory-bound and cargo-bound panels, titled by their own card names), where ONLY
    /// slots still holding an item emit (<see cref="ItemNodes.LootItem"/> — name/badges/tooltip, Enter takes the
    /// item to the party inventory) — taking an item drops its node from the next render, so focus slides to the
    /// nearest remaining item and announces it. Then a <b>Take all</b> stop (<see cref="LootVM.TryCollectLoot"/>
    /// via the collector — collects normal items to inventory + trash to cargo, the game's own routing), and
    /// Escape to close (<see cref="LootVM.Close"/>). Exclusive: while loot is open the mod owns the keyboard, so
    /// the always-active scanner/tile-cursor below don't eat the arrows. Layer 24, alongside the other
    /// world-interaction modals (Variative / Transition) — loot is triggered from exploration, so it never stacks
    /// with a service window.
    ///
    /// Pass 1 (per docs/plans/tiered-gathering-knuth.md) covers the three read-and-take modes: StandardChest, Short
    /// (environment / dropped loot), ShortUnit (a body's inventory) — ~90% of looting. Pass 2 adds ZoneExit — the
    /// mass-loot prompt raised when the party reaches an area exit with unlooted loot: it lists everything lootable
    /// in the area with <b>Take all and leave</b> (<see cref="LootVM.TryCollectLoot"/>, which then fires the area
    /// transition), <b>Leave without taking</b> (<see cref="LootVM.LeaveZone"/>), and Escape = <b>Stay</b>
    /// (<see cref="LootVM.Close"/> cancels the transition). OneSlot (Pass 3, device insert) has a distinct flow and
    /// lives in its own <see cref="OneSlotLootScreen"/>; PlayerChest (Pass 4, two-way stash + cargo) has
    /// <see cref="PlayerChestScreen"/> — <see cref="IsActive"/> allows only the supported modes. Some loot
    /// (star-system finds) also carries a skill-check result, read as a header line.
    /// </summary>
    public sealed class LootScreen : Screen
    {
        public override string Key => "loot";
        public override int Layer => 24;
        public override bool Exclusive => true;

        // Spoken on open (OnFocus). No ServiceWindowAnnounce patch fires for loot (it's not a ServiceWindowsType),
        // so the screen names itself — by mode, since the container's own name isn't uniformly reachable here.
        public override string ScreenName
        {
            get { var vm = Vm(); return vm != null ? Title(vm.Mode) : null; }
        }

        public override bool IsActive() { var vm = Vm(); return vm != null && IsSupportedMode(vm.Mode); }

        // Back (Escape) closes the loot window via the window's own close callback. For a ZoneExit prompt, Close()
        // does NOT fire the transition — it CANCELS leaving — so it reads as "Stay"; for the other modes it's a plain
        // "Close". (Leaving IS available on the explicit Leave/Take-all-and-leave buttons.)
        public override IEnumerable<ElementAction> GetActions()
        {
            var key = IsZoneExit(Vm()) ? "loot.stay" : "action.close";
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", key), _ => Vm()?.Close());
        }

        // Loot opens on the planet surface AND in the star-system/space context; resolve from whichever static
        // part is live (the LootContextVM is a sibling of ServiceWindowsVM on both).
        private static LootVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.SurfaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value
                ?? rc?.SpaceVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        // Pass 1 (read/take: chest, environment, body) + Pass 2 (ZoneExit: mass-loot before leaving). OneSlot
        // (device insert) and PlayerChest (two-way stash) each have their own screen.
        private static bool IsSupportedMode(LootContextVM.LootWindowMode mode)
            => mode == LootContextVM.LootWindowMode.StandardChest
            || mode == LootContextVM.LootWindowMode.Short
            || mode == LootContextVM.LootWindowMode.ShortUnit
            || mode == LootContextVM.LootWindowMode.ZoneExit;

        private static bool IsZoneExit(LootVM vm) => vm != null && vm.Mode == LootContextVM.LootWindowMode.ZoneExit;

        private static string Title(LootContextVM.LootWindowMode mode)
        {
            switch (mode)
            {
                case LootContextVM.LootWindowMode.StandardChest: return Loc.T("loot.title.chest");
                case LootContextVM.LootWindowMode.ShortUnit: return Loc.T("loot.title.remains");
                case LootContextVM.LootWindowMode.ZoneExit: return Loc.T("loot.title.zone");
                default: return Loc.T("loot.container");
            }
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "loot:" + vm.GetHashCode() + ":"; // a new LootVM = a fresh window = fresh keys
            bool zone = IsZoneExit(vm);

            // Some loot (chiefly star-system exploration finds) is annotated with a SKILL-CHECK result — the roll
            // that gated or graded the find. Set once when the window opens and never changes, so read it as a
            // plain focusable header line at the top (RT's only real "extra loot-window state"; RT has no
            // skinning). Absent on ordinary containers, where LootCollector.HasSkillCheck is false.
            var collector = vm.LootCollector;
            if (collector != null && collector.HasSkillCheck && !string.IsNullOrWhiteSpace(collector.SkillCheckText))
                b.BeginStop("check").AddItem(ControlId.Structural(k + "check"),
                    GraphNodes.Text(() => collector.SkillCheckText));

            if (vm.NoLoot.Value)
            {
                // Nothing to take (an empty container, or everything already collected in extended view). A ZoneExit
                // with no loot auto-leaves in its ctor and never shows; if it somehow does, offer Leave. Otherwise a
                // focusable line that closes, so Enter or Escape both dismiss.
                if (zone)
                    b.BeginStop("leave").AddItem(ControlId.Structural(k + "leave"),
                        GraphNodes.Button(() => Loc.T("loot.leave"), Leave));
                else
                    b.BeginStop("empty").AddItem(ControlId.Structural(k + "empty"),
                        GraphNodes.Button(() => Loc.T("loot.empty"), () => Vm()?.Close()));
                return;
            }

            // One Tab-stop per loot group (the game's own panels: LootObjectType.Normal → inventory,
            // Trash → cargo), titled by the group's game-localized card name (fallback: the mode title).
            // ONLY slots still holding an item emit — taking one removes its node next render, so the differ
            // slides focus to the nearest remaining item and announces it; an emptied group drops out whole.
            // Keys ride the ITEM ENTITY, not the slot VM: the game REBUILDS every slot VM on each take, so an
            // entity-based key is what keeps the cursor put while neighbours are collected (the entity is also
            // the id's reference tier, following a row the game re-sorts).
            int s = 0;
            foreach (var group in vm.ContextLoot)
            {
                int src = s++;
                var vis = group?.SlotsGroup?.VisibleCollection;
                if (vis == null) continue;
                bool any = false;
                foreach (var slot in vis)
                    if (slot != null && slot.HasItem) { any = true; break; }
                if (!any) continue;

                b.BeginStop("src:" + src);
                b.PushContext(string.IsNullOrWhiteSpace(group.DisplayName) ? Title(vm.Mode) : group.DisplayName,
                    Loc.T("role.list"));
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item.Value;
                    b.AddItem(ControlId.Referenced(ent, k + "src:" + src + ":item:" + ent.GetHashCode()),
                        ItemNodes.LootItem(slot));
                }
                b.PopContext();
            }

            // Take all → the game's collect-all (LootCollectorVM.CollectAll). For a ZoneExit that opens the
            // "collect all before leaving?" confirm (ExitLocationScreen); otherwise it collects and closes.
            b.BeginStop("takeall").AddItem(ControlId.Structural(k + "takeall"),
                GraphNodes.Button(() => Loc.T("loot.take_all"), TakeAll));
            // ZoneExit adds an explicit "leave the area without grabbing anything" (the game's Leave-zone button).
            if (zone)
                b.BeginStop("leave").AddItem(ControlId.Structural(k + "leave"),
                    GraphNodes.Button(() => Loc.T("loot.leave"), Leave));
        }

        // Take everything — the game's OWN collect-all handler (the same one the loot window's button calls), NOT a
        // reimplementation: LootCollectorVM.CollectAll → for a normal container, TryCollectLoot + Close (so it closes
        // regardless of the LootExtendedView setting); for a ZoneExit, it opens the game's "collect all before you
        // leave?" confirm (ExitLocationWindowVM), which our ExitLocationScreen surfaces. Reusing the game method means
        // we inherit its exact close/leave/routing semantics instead of guessing them.
        private static void TakeAll() => Vm()?.LootCollector?.CollectAll();

        // ZoneExit only: leave the area now, taking nothing. LeaveZone() closes the prompt AND fires the area
        // transition (the callback the window was opened with) — the game's own "just leave" path.
        private static void Leave() => Vm()?.LeaveZone();
    }
}
