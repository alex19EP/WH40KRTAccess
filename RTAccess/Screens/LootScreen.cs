using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Loot;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's loot window (<see cref="LootVM"/>) as a mod-owned navigable screen. Interacting with a
    /// container already opens this window today — but invisibly, silently swallowing the keyboard. This screen
    /// makes it usable: a flat item list (<see cref="ProxyLootItem"/> — name/badges/tooltip, Enter takes the item
    /// to the party inventory), a leading <b>Take all</b> button (<see cref="LootVM.TryCollectLoot"/> — collects
    /// normal items to inventory + trash to cargo, the game's own routing), and Escape to close
    /// (<see cref="LootVM.Close"/>). Exclusive: while loot is open the mod owns the keyboard, so the always-active
    /// scanner/tile-cursor below don't eat the arrows. Layer 24, alongside the other world-interaction modals
    /// (Variative / Transition) — loot is triggered from exploration, so it never stacks with a service window.
    ///
    /// Pass 1 (per docs/plans/tiered-gathering-knuth.md) covers the three read-and-take modes: StandardChest, Short
    /// (environment / dropped loot), ShortUnit (a body's inventory) — ~90% of looting. Pass 2 adds ZoneExit — the
    /// mass-loot prompt raised when the party reaches an area exit with unlooted loot: it lists everything lootable
    /// in the area with <b>Take all and leave</b> (<see cref="LootVM.TryCollectLoot"/>, which then fires the area
    /// transition), <b>Leave without taking</b> (<see cref="LootVM.LeaveZone"/>), and Escape = <b>Stay</b>
    /// (<see cref="LootVM.Close"/> cancels the transition). OneSlot (Pass 3, device insert) and PlayerChest (Pass 4,
    /// two-way stash + cargo) have distinct flows and stay gated off — <see cref="IsActive"/> allows only the
    /// supported modes. Some loot (star-system finds) also carries a skill-check result, read as a header line.
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

        private Panel _content;
        private FlowSheet _sheet;
        private bool _built;
        private string _sig;
        private string _lastRestoreLabel; // dedupe the restore announce across a multi-frame settle burst

        public override void OnPush() { _built = false; _sig = null; _lastRestoreLabel = null; }
        public override void OnPop() { Clear(); _content = null; _sheet = null; _built = false; }

        // Back (Escape) closes the loot window via the window's own close callback. For a ZoneExit prompt, Close()
        // does NOT fire the transition — it CANCELS leaving — so it reads as "Stay"; for the other modes it's a plain
        // "Close". (Leaving IS available on the explicit Leave/Take-all-and-leave buttons.)
        public override IEnumerable<ElementAction> GetActions()
        {
            var key = IsZoneExit(Vm()) ? "loot.stay" : "action.close";
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", key), _ => Vm()?.Close());
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (!_built) BuildShell();
            var sig = ContentSig(vm);
            if (sig != _sig) { _sig = sig; RefillContent(vm); }
            else _lastRestoreLabel = null; // settled: the next change is a fresh take, so announce its landing
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
        // (device insert) and PlayerChest (two-way stash + cargo) have distinct flows and stay gated off for now.
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

        // Refill on any content change — the visible collections rebuild when an item is taken (per-item or
        // take-all), so the list drops taken items and NoLoot flips when the container empties.
        private static string ContentSig(LootVM vm)
        {
            var sb = new StringBuilder();
            sb.Append((int)vm.Mode).Append('|').Append(vm.NoLoot.Value ? '1' : '0').Append('|');
            foreach (var group in vm.ContextLoot)
            {
                var vis = group?.SlotsGroup?.VisibleCollection;
                if (vis == null) continue;
                foreach (var s in vis)
                    if (s != null && s.HasItem)
                        sb.Append(s.DisplayName.Value).Append('#').Append(s.Count.Value).Append(',');
            }
            return sb.ToString();
        }

        private void BuildShell()
        {
            _built = true;
            Clear();
            _content = new Panel();
            Add(_content);
        }

        private void RefillContent(LootVM vm)
        {
            if (_content == null) return;

            // The item list is virtualized and rebuilt on every take, so capture where the cursor sits and
            // restore it afterwards (the next item after a take, the Take-all button after the last one).
            var cap = CaptureFocus();

            _content.Clear();

            bool zone = IsZoneExit(vm);
            var sheet = new FlowSheet();
            var list = sheet.List(Title(vm.Mode));
            AddSkillCheck(list, vm);
            if (vm.NoLoot.Value)
            {
                // Nothing to take (an empty container, or everything already collected in extended view). A ZoneExit
                // with no loot auto-leaves in its ctor and never shows; if it somehow does, offer Leave. Otherwise a
                // focusable line that closes, so Enter or Escape both dismiss.
                if (zone) list.Item(new ProxyActionButton(Loc.T("loot.leave"), () => true, Leave));
                else list.Item(new ProxyActionButton(Loc.T("loot.empty"), () => true, () => Vm()?.Close()));
            }
            else
            {
                // Take all → the game's collect-all (LootCollectorVM.CollectAll). For a ZoneExit that opens the
                // "collect all before leaving?" confirm (ExitLocationScreen); otherwise it collects and closes.
                list.Item(new ProxyActionButton(Loc.T("loot.take_all"), () => true, TakeAll));
                // ZoneExit adds an explicit "leave the area without grabbing anything" (the game's Leave-zone button).
                if (zone) list.Item(new ProxyActionButton(Loc.T("loot.leave"), () => true, Leave));
                foreach (var group in vm.ContextLoot)
                {
                    var vis = group?.SlotsGroup?.VisibleCollection;
                    if (vis == null) continue;
                    foreach (var slot in vis)
                        if (slot != null && slot.HasItem)
                            list.Item(new ProxyLootItem(slot));
                }
            }
            sheet.Reflow();
            _sheet = sheet;
            _content.Add(sheet);

            RestoreFocus(cap);
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

        // Some loot (chiefly star-system exploration finds) is annotated with a SKILL-CHECK result — the roll that
        // gated or graded the find. It's set once when the window opens and never changes, so read it as a plain
        // focusable header line at the top (RT's only real "extra loot-window state"; RT has no skinning). Absent on
        // ordinary containers, where LootCollector.HasSkillCheck is false.
        private static void AddSkillCheck(ListRegion list, LootVM vm)
        {
            var collector = vm.LootCollector;
            if (collector == null || !collector.HasSkillCheck) return;
            var text = collector.SkillCheckText;
            if (!string.IsNullOrWhiteSpace(text)) list.Item(new TextElement(() => text));
        }

        // (row, col) of the focused cell within the sheet, or row = -1 when focus is outside it (first build).
        private (int row, int col) CaptureFocus()
        {
            var cur = Navigation.Current;
            if (cur != null && _sheet != null && _sheet.TryCoords(cur, out int r, out int c)) return (r, c);
            return (-1, 0);
        }

        // Re-focus the same position (clamped) in the rebuilt sheet, announcing the landing — but suppress the
        // repeat while the virtualized collection settles over several frames onto the same row. Falls back to
        // the first focusable if that slot's gone (the item taken).
        private void RestoreFocus((int row, int col) cap)
        {
            if (cap.row < 0 || _sheet == null) return;
            UIElement cell = null;
            if (_sheet.RowCount > 0)
            {
                int r = Math.Min(cap.row, _sheet.RowCount - 1);
                int c = _sheet.Visitable(r, cap.col) ? cap.col : _sheet.LeftmostVisitable(r);
                if (c >= 0) cell = _sheet.CellAt(r, c);
            }
            cell = cell ?? _sheet.FirstFocusable();
            if (cell == null) return;
            var label = cell.GetLabelText();
            bool announce = label != _lastRestoreLabel;
            _lastRestoreLabel = label;
            Navigation.Focus(cell, announce);
        }
    }
}
