using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root;                                                        // LocalizedTexts (stat names, filter labels)
using Kingmaker.Blueprints.Root.Strings;                                                // UIStrings (the game's ship-customization vocabulary)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;                                         // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ShipCustomization;                                      // ShipCustomizationVM + tab enum + Upgrade-tab VMs
using Kingmaker.Code.UI.MVVM.VM.Slots;                                                  // ItemSlotVM, IInventoryHandler
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Abilities;        // CharInfoFeatureVM (Abilities tab rows)
using Kingmaker.GameCommands;                                                           // UpgradeSystemComponent / SetInventorySorter extensions
using Kingmaker.PubSubSystem.Core;                                                      // EventBus
using Kingmaker.UI.Common;                                                              // InventoryHelper, ItemsFilterType, ItemsSorterType
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.CareerPath;    // CareerPathVM (the ship path)
using Kingmaker.UI.MVVM.VM.ShipCustomization;                                           // ShipAbilitiesVM, ShipProgressionVM, ShipInfoExperienceVM
using Kingmaker.UI.MVVM.VM.ShipCustomization.Posts;                                     // ShipPostsVM, PostEntityVM, PostOfficerVM, PostAbilityVM
using Owlcat.Runtime.UI.Tooltips;                                                       // TooltipBaseTemplate
using RTAccess.Accessibility;                                                           // TooltipReader
using RTAccess.UI;
using RTAccess.UI.Graph;
using Warhammer.SpaceCombat.StarshipLogic.Equipment;                                    // SystemComponent (hull upgrade types)
using Warhammer.SpaceCombat.StarshipLogic.Posts;                                        // Post

namespace RTAccess.Screens
{
    /// <summary>
    /// The voidship management service window (<see cref="ShipCustomizationVM"/>, Ctrl+V / the HUD Windows
    /// list) as a graph-native screen. Four tabs, mirrored from the game's tab bar and driven through the
    /// game's own <see cref="ShipCustomizationVM.SetCurrentTab"/> (the per-tab sub-VM is disposed/recreated
    /// by the game on every activation, so every per-tab read re-resolves the field and is GATED on
    /// <c>ActiveTab.Value</c> — the Upgrade VM always exists even while another tab is showing):
    /// <list type="bullet">
    /// <item><b>Components</b> (Upgrade) — the hull's component slots ("Engine: item"), the scrap-based
    /// Internal Structure / Prow Ram upgrade tracks, and the ship stash (the shared party inventory under a
    /// ship filter) with the game's filter/sort bar. Enter on a slot opens the game's own item picker
    /// (<c>HandleChangeItem</c> → surfaced by <see cref="ShipItemSelectorScreen"/>); Backspace takes the
    /// component off (<see cref="InventoryHelper.TryUnequip(ShipComponentSlotVM)"/> — the game refuses the
    /// engine slot and in-combat changes itself, voiced by WarningReader).</item>
    /// <item><b>Upgrade</b> (Skills) — the ship's single career path through the same rank machinery as the
    /// character level-up, rendered with the shared <see cref="CareerNodes"/> factories + a Commit stop.</item>
    /// <item><b>Posts</b> — post rows (title, officer, skill), the officer candidates for the selected post
    /// (Enter appoints / vacates via the game's queued <c>SetUnitOnPost</c>), and the selected post's
    /// abilities with the attune action (<see cref="PostAbilityVM.TryAttune"/>).</item>
    /// <item><b>Accolades</b> (Abilities) — the ship's active/passive ability lists, read-only.</item>
    /// </list>
    /// A trailing "Ship status" stop reads the always-alive summary VMs (name, hull + the two repair verbs,
    /// scrap, level/XP, morale/crew, per-facing armor, per-sector shields, speed/inertia, ratings).
    /// <see cref="ShipCustomizationVM.CanChangeEquipment"/> is INVERTED upstream (true = opened during space
    /// combat, everything locked) — surfaced as a status line and as disabled actions. All mutations are
    /// queued GameCommands that land frames later: labels read live and the game's own result warnings are
    /// voiced by WarningReader, so nothing is announced synchronously from a keypress here. Layer 10;
    /// ScreenName null (ServiceWindowAnnounce already speaks the window name).
    /// </summary>
    public sealed class ShipCustomizationScreen : Screen
    {
        public override string Key => "service.ship";
        public override int Layer => 10;
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Ship" on open

        // Per-page latches for the Skills outline (the LevelUpScreen recipe): group folds + the
        // once-per-selection FilteredGroupList materialization. Reset when the path VM instance flips
        // (every Skills-tab activation) and on push/pop.
        private readonly Dictionary<string, bool> _fold = new Dictionary<string, bool>();
        private readonly HashSet<Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.RankEntrySelectionVM> _materialized
            = new HashSet<Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.RankEntrySelectionVM>();
        private string _skillsPage;

        public override void OnPush() => ResetSkillsState();
        public override void OnPop() => ResetSkillsState();

        private void ResetSkillsState()
        {
            _fold.Clear();
            _materialized.Clear();
            _skillsPage = null;
        }

        public override bool IsActive()
        {
            var sw = ServiceWindows();
            // The window VM can be null even when "opened" (ServiceWindowsVM skips creation under a dialog).
            return sw != null && sw.CurrentWindow == ServiceWindowsType.ShipCustomization && Vm() != null;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                // Closing mid-level-up silently destroys the staged picks (ShipProgressionVM.Dispose →
                // DestroyLevelUpManager, no game-side confirm unlike the character flow) — say so.
                bool discarding = HasStagedSkillPicks();
                ServiceWindows()?.HandleCloseAll();
                if (discarding) Tts.Speak(Loc.T("ship.unsaved_levelup"));
            });
        }

        // Staged-but-uncommitted rank picks on the ship path — the game's own signal
        // (CareerPathVM.HasNewValidSelections = any selection made within the current level-up range).
        private static bool HasStagedSkillPicks()
        {
            var cp = Vm()?.ShipSkillsVM?.ShipProgressionVM?.CareerPathVM;
            return cp != null && cp.HasNewValidSelections.Value;
        }

        // The window opens in every context (surface, star system, sector map, space combat) — resolve off
        // whichever static part is live.
        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();

        internal static ShipCustomizationVM Vm() => ServiceWindows()?.ShipCustomizationVM?.Value;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "ship:" + vm.GetHashCode() + ":";
            var tab = vm.ActiveTab.Value;
            bool locked = vm.CanChangeEquipment.Value; // inverted upstream: true = space combat, LOCKED

            BuildTabs(b, k, vm, tab, locked);

            // Per-tab content keys carry the tab, so a switch re-keys the whole content area. The tab
            // fields are gated on ActiveTab (a stale outgoing VM stays alive behind an inactive tab).
            string kt = k + "t" + (int)tab + ":";
            switch (tab)
            {
                case ShipCustomizationTab.Upgrade: BuildUpgrade(b, kt, vm.ShipUpgradeVm, locked); break;
                case ShipCustomizationTab.Skills: BuildSkills(b, kt, vm.ShipSkillsVM); break;
                case ShipCustomizationTab.Posts: BuildPosts(b, kt, vm.ShipPostsVM); break;
                case ShipCustomizationTab.Abilities: BuildAbilities(b, kt, vm.ShipAbilitiesVM); break;
            }

            BuildStatus(b, k, vm);
        }

        // ---- the tab bar: a horizontal radio row driving the game's own SetCurrentTab; selected state
        // reads the SELECTION (ActiveTab), and initial focus lands on the active tab (the Settings recipe).
        private static void BuildTabs(GraphBuilder b, string k, ShipCustomizationVM vm,
            ShipCustomizationTab active, bool locked)
        {
            b.BeginStop("tabs").PushContext(ServiceWindowInfo.Label(ServiceWindowsType.ShipCustomization) ?? "",
                Loc.T("role.list"));
            if (locked)
                b.AddItem(ControlId.Structural(k + "locked"), GraphNodes.Text(() => Loc.T("ship.locked")));
            b.StartRow(k + "tabsrow");
            ControlId start = null;
            foreach (ShipCustomizationTab t in Enum.GetValues(typeof(ShipCustomizationTab)))
            {
                var tb = t; // capture
                var id = ControlId.Structural(k + "tab:" + (int)tb);
                b.AddItem(id, GraphNodes.ChoiceOption(
                    () => TabLabel(tb),
                    () => Vm()?.ActiveTab?.Value == tb,
                    () =>
                    {
                        // Leaving the Skills tab is the last moment staged rank picks can still be
                        // committed — re-entering the tab rebuilds its VM and destroys them silently.
                        bool discarding = Vm()?.ActiveTab?.Value == ShipCustomizationTab.Skills
                            && tb != ShipCustomizationTab.Skills && HasStagedSkillPicks();
                        Vm()?.SetCurrentTab(tb);
                        if (discarding) Tts.Speak(Loc.T("ship.unsaved_levelup"));
                    }));
                if (tb == active) start = id;
            }
            b.EndRow();
            b.PopContext();
            if (start != null) b.SetStart(start);
        }

        // The game's own tab-bar labels (ShipTabsNavigationPCView.SetLabels), with mod fallbacks.
        private static string TabLabel(ShipCustomizationTab tab)
        {
            switch (tab)
            {
                case ShipCustomizationTab.Upgrade:
                    return GameText.Or(() => UIStrings.Instance.ShipCustomization.MenuItemComponents, "ship.tab_upgrade");
                case ShipCustomizationTab.Skills:
                    return GameText.Or(() => UIStrings.Instance.ShipCustomization.MenuItemUpgrade, "ship.tab_skills");
                case ShipCustomizationTab.Posts:
                    return GameText.Or(() => UIStrings.Instance.HUDTexts.PostsBar, "ship.tab_posts");
                default:
                    return GameText.Or(() => UIStrings.Instance.ShipCustomization.Accolades, "ship.tab_abilities");
            }
        }

        // ---- Components (Upgrade) tab: slots + hull upgrade tracks + the ship stash, three regions in one
        // stop (the InventoryScreen shape — Ctrl+Up/Down jumps between them). ----

        private static void BuildUpgrade(GraphBuilder b, string kt, ShipUpgradeVm up, bool locked)
        {
            if (up == null) return;
            b.BeginStop("upgrade");

            // Component slots — labels mirror the CARD (the short "Engine"/"Shields" words; the precise
            // component-type wording is the card's hover hint, which the equipped item's tooltip carries).
            // Keys are STRUCTURAL by slot position: the slot VM persists but its content changes async — a
            // LIVE label re-speaks the landing under focus (the doll-slot lesson).
            b.SetRegion(kt + "slots");
            b.PushContext(Loc.T("ship.components"), Loc.T("role.list"));
            AddSlot(b, kt + "slot:engine", GameText.Or(() => UIStrings.Instance.ShipCustomization.Engine, "ship.slot_engine"), up.PlasmaDrives, up, locked);
            AddSlot(b, kt + "slot:shields", GameText.Or(() => UIStrings.Instance.ShipCustomization.Shields, "ship.slot_shields"), up.VoidShieldGenerator, up, locked);
            AddSlot(b, kt + "slot:auspex", GameText.Or(() => UIStrings.Instance.ShipCustomization.Auspex, "ship.slot_auspex"), up.AugerArray, up, locked);
            AddSlot(b, kt + "slot:armor", GameText.Or(() => UIStrings.Instance.ShipCustomization.Armor, "ship.slot_armor"), up.ArmorPlating, up, locked);
            int prow = 0;
            for (int i = 0; i < up.Weapons.Count; i++)
            {
                var slot = up.Weapons[i];
                if (slot == null) continue;
                b.AddItem(ControlId.Structural(kt + "slot:w:" + i),
                    SlotNode(WeaponSlotName(slot, ref prow), slot, up, locked));
            }
            for (int i = 0; i < up.Arsenals.Count; i++)
            {
                var slot = up.Arsenals[i];
                if (slot == null) continue;
                b.AddItem(ControlId.Structural(kt + "slot:ars:" + i),
                    SlotNode(GameText.Or(() => UIStrings.Instance.ShipCustomization.Arsenal, "ship.slot_arsenal") + " " + (i + 1),
                        slot, up, locked));
            }
            b.PopContext();

            // The two scrap-based hull upgrade tracks. State reads GROUND TRUTH off the hull (the twin
            // ShipUpgradeSlotVM instances are undifferentiated upstream); actions are the exact queue calls
            // behind the game's left-click context menu. Results arrive async as the game's own warning
            // toasts (WarningReader voices them).
            b.SetRegion(kt + "upgrades");
            b.PushContext(Loc.T("ship.upgrades"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(kt + "up:structure"), UpgradeNode(
                GameText.Or(() => UIStrings.Instance.ShipCustomization.UpgradeInternalStructure, "ship.upgrade_structure"),
                SystemComponent.SystemComponentType.InternalStructure, locked,
                () => up.InternalStructure?.ShipInternalStructureTooltip));
            b.AddItem(ControlId.Structural(kt + "up:ram"), UpgradeNode(
                GameText.Or(() => UIStrings.Instance.ShipCustomization.UpgradeProwRam, "ship.upgrade_ram"),
                SystemComponent.SystemComponentType.ProwRam, locked,
                () => up.ProwRam?.ShipUpgradeTooltip));
            b.PopContext();

            BuildStash(b, kt, up.ShipInventoryStashVM);
        }

        private static void AddSlot(GraphBuilder b, string key, string name,
            ShipComponentSlotVM slot, ShipUpgradeVm up, bool locked)
        {
            if (slot == null) return;
            b.AddItem(ControlId.Structural(key), SlotNode(name, slot, up, locked));
        }

        // "Weapon, Dorsal" / "Weapon, Prow 1" — the card's weapon word plus the slot's facing, numbered
        // for the double prow (the game tells them apart spatially).
        private static string WeaponSlotName(ShipComponentSlotVM slot, ref int prow)
        {
            var sc = UIStrings.Instance.ShipCustomization;
            string facing;
            switch (slot.SlotType)
            {
                case ShipComponentSlotType.Dorsal: facing = GameText.Or(() => sc.Dorsal, "ship.facing_dorsal"); break;
                case ShipComponentSlotType.Port: facing = GameText.Or(() => sc.Port, "ship.facing_port"); break;
                case ShipComponentSlotType.Starboard: facing = GameText.Or(() => sc.Starboard, "ship.facing_starboard"); break;
                default: facing = GameText.Or(() => sc.Prow, "ship.facing_prow") + " " + (++prow); break;
            }
            return GameText.Or(() => sc.ShipWeapon, "ship.slot_weapon") + ", " + facing;
        }

        /// <summary>One component slot — "Engine: Voidsunder Plasma Drives (badges)" / "…: empty", label
        /// LIVE (the queued equip/unequip lands frames later under focus). Enter opens the game's own item
        /// picker for the slot (<c>HandleChangeItem</c> — warns "nothing to insert" itself when no
        /// compatible item exists); Backspace takes the component off through the game's guarded unequip
        /// (engine-slot and in-combat refusals are the game's own warnings). Space = the equipped item's
        /// card.</summary>
        private static NodeVtable SlotNode(string name, ShipComponentSlotVM slot, ShipUpgradeVm up, bool locked)
        {
            Func<string> label = () => name + ": " +
                (slot.HasItem ? ItemNodes.ItemLabel(slot) : Loc.T("slot.empty"));
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    GraphNodes.DisabledPart(() => !locked),
                },
                SearchText = label,
                OnActivate = locked ? (Action)null : () => up.HandleChangeItem(slot),
                OnSecondary = locked || !slot.HasItem ? (Action)null : () => UnequipShipSlot(slot),
                OnTooltip = () => TooltipChooser.OpenTemplate(label(), OwnTemplate(slot)),
                ActivateSound = locked ? null : Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // The game's own guarded take-off (refuses in combat / the engine slot with its own warnings); on
        // success raise the same Refresh the game's selector raises so the stash panel rebuilds.
        private static void UnequipShipSlot(ShipComponentSlotVM slot)
        {
            if (InventoryHelper.TryUnequip(slot))
                EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
        }

        // The item's OWN card: last template of the comparative list (the ShowInfo convention).
        private static TooltipBaseTemplate OwnTemplate(ItemSlotVM slot)
        {
            var t = slot.Tooltip?.Value;
            return t != null && t.Count > 0 ? t[t.Count - 1] : null;
        }

        /// <summary>One hull-upgrade track — "Upgrade internal structure: level 2, next level 150 scrap".
        /// Enter upgrades, Backspace downgrades (refunds), both through the game's queued
        /// UpgradeSystemComponent/DowngradeSystemComponent; gating mirrors the game's own context-menu
        /// conditions (never probe Upgrade() at max level — the InternalStructure bounds check is broken
        /// upstream). Space = the game's upgrade tooltip.</summary>
        private static NodeVtable UpgradeNode(string name, SystemComponent.SystemComponentType type,
            bool locked, Func<TooltipBaseTemplate> tooltip)
        {
            Func<SystemComponent> comp = () =>
            {
                var hull = Game.Instance?.Player?.PlayerShip?.Hull;
                return hull == null ? null
                    : type == SystemComponent.SystemComponentType.ProwRam ? (SystemComponent)hull.ProwRam : hull.InternalStructure;
            };
            Func<string> label = () =>
            {
                var c = comp();
                if (c == null) return name;
                var s = name + ": " + Loc.T("ship.level", new { level = c.UpgradeLevel });
                if (IsMaxLevel(c)) return s + ", " + Loc.T("ship.max_level");
                var cost = c.Blueprint?.UpgradeCost;
                if (cost != null && c.UpgradeLevel + 1 < cost.Length)
                    s += ", " + Loc.T("ship.next_cost", new { cost = cost[c.UpgradeLevel + 1] });
                if (!IsEnoughScrap(c))
                    s += ", " + GameText.Or(() => UIStrings.Instance.ShipCustomization.NotEnoughScrap, "ship.not_enough_scrap");
                return s;
            };
            bool canUp = !locked && comp() != null && !IsMaxLevel(comp()) && IsEnoughScrap(comp());
            bool canDown = !locked && comp() != null && comp().UpgradeLevel > 0;
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    GraphNodes.DisabledPart(() => !locked),
                },
                SearchText = () => name,
                OnActivate = canUp ? (Action)(() => Game.Instance.GameCommandQueue.UpgradeSystemComponent(type)) : null,
                OnSecondary = canDown ? (Action)(() => Game.Instance.GameCommandQueue.DowngradeSystemComponent(type)) : null,
                OnTooltip = () => TooltipChooser.OpenTemplate(label(), tooltip?.Invoke()),
                ActivateSound = canUp ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick : null,
            };
        }

        // The concrete subclasses carry these (SystemComponent itself doesn't).
        private static bool IsMaxLevel(SystemComponent c)
            => c is ProwRam pr ? pr.IsMaxLevel : c is InternalStructure ist && ist.IsMaxLevel;

        private static bool IsEnoughScrap(SystemComponent c)
            => c is ProwRam pr ? pr.IsEnoughScrap : c is InternalStructure ist && ist.IsEnoughScrap;

        // The ship stash: the game's ship filter bar (three ship filters + the shared sorter) above the
        // filtered item list — the InventoryScreen recipe over ShipInventoryStashVM. Rows are the standard
        // stash cards: Enter quick-equips through the game's own TryEquip (auto-slot), Backspace opens the
        // stash context menu, Space the item card.
        private static void BuildStash(GraphBuilder b, string kt, ShipInventoryStashVM stash)
        {
            if (stash == null) return;
            var filter = stash.ItemsFilter;
            if (filter != null)
            {
                b.SetRegion(kt + "filters");
                b.PushContext(Loc.T("inv.filters"), Loc.T("role.list"));
                b.StartRow(kt + "filtersrow");
                var filters = ShipFilterOptions;
                b.AddItem(ControlId.Structural(kt + "filter"), GraphNodes.Cycler(
                    () => Loc.T("inv.filters"),
                    () => filters.ConvertAll(ShipFilterLabel),
                    () => Math.Max(0, filters.IndexOf(stash.CurrentFilter.Value)),
                    i => { if (i >= 0 && i < filters.Count) filter.SetCurrentFilter(filters[i]); }));
                var sorters = ShipSortOptions;
                b.AddItem(ControlId.Structural(kt + "sort"), GraphNodes.Cycler(
                    () => Loc.T("inv.sort"),
                    () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                    () => Math.Max(0, sorters.IndexOf(filter.CurrentSorter.Value)),
                    i => { if (i >= 0 && i < sorters.Count) Game.Instance.GameCommandQueue.SetInventorySorter(sorters[i]); }));
                b.EndRow();
                b.PopContext();
            }

            b.SetRegion(kt + "stash");
            b.PushContext(Loc.T("ship.stash"), Loc.T("role.list"));
            bool any = false;
            var vis = stash.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item?.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, kt + "stash:" + ent.UniqueId), ItemNodes.InventoryItem(slot));
                    any = true;
                }
            if (!any) b.AddItem(ControlId.Structural(kt + "stash:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            b.PopContext();
        }

        // The ship stash filter bar's own set (ShipItemsFilter*: everything / weapons / components) and the
        // shared sorter modes (the same UISettings.InventorySorter the inventory drives).
        private static readonly List<ItemsFilterType> ShipFilterOptions = new List<ItemsFilterType>
        {
            ItemsFilterType.ShipNoFilter, ItemsFilterType.ShipWeapon, ItemsFilterType.ShipOther,
        };

        // The ship filter types have NO ItemsFilterStrings blueprint entries (the game's ship bar is
        // icon+glossary-tooltip only), so GetText would speak the raw enum name — use the inventory
        // filter words instead.
        private static string ShipFilterLabel(ItemsFilterType type)
        {
            switch (type)
            {
                case ItemsFilterType.ShipWeapon:
                    return GameText.Or(() => UIStrings.Instance.InventoryScreen.FilterTextWeapon, "ship.filter_weapon");
                case ItemsFilterType.ShipOther:
                    return GameText.Or(() => UIStrings.Instance.InventoryScreen.FilterTextOther, "ship.filter_other");
                default:
                    return GameText.Or(() => UIStrings.Instance.InventoryScreen.FilterTextAll, "ship.filter_all");
            }
        }

        private static readonly List<ItemsSorterType> ShipSortOptions = new List<ItemsSorterType>
        {
            ItemsSorterType.NotSorted, ItemsSorterType.TypeUp, ItemsSorterType.TypeDown,
            ItemsSorterType.NameUp, ItemsSorterType.NameDown,
            ItemsSorterType.DateUp, ItemsSorterType.DateDown,
        };

        // ---- Upgrade (Skills) tab: the ship's single career path — the LevelUpScreen outline over the
        // shared CareerNodes factories, plus the XP header and a Commit stop. There is no career list state:
        // the game force-selects ProgressionRoot.ShipPath at tab construction. ----

        private void BuildSkills(GraphBuilder b, string kt, ShipSkillsVM skills)
        {
            var prog = skills?.ShipProgressionVM;
            var cp = prog?.CareerPathVM;
            if (cp == null) return;

            // Page signature: the path VM instance (fresh on every Skills-tab activation) PLUS the
            // level-up range — commit does NOT rebuild the VM (it only destroys the manager), but the
            // range collapses when the queued commit lands, and that flip drops the fold/materialization
            // latches like LevelUpScreen's page flips.
            var range = cp.GetCurrentLevelupRange();
            string page = kt + cp.GetHashCode() + ":" + range.Min + ":" + range.Max;
            if (_skillsPage != page)
            {
                if (_skillsPage != null) { _fold.Clear(); _materialized.Clear(); }
                _skillsPage = page;
            }
            string k = page + ":";

            var xp = prog.ShipInfoExperienceVM;
            b.BeginStop("skills").PushContext(TabLabel(ShipCustomizationTab.Skills), Loc.T("role.list"));
            if (xp != null)
                b.AddItem(ControlId.Structural(k + "xp"), GraphNodes.Text(() =>
                {
                    var s = Loc.T("ship.xp_line", new { level = xp.ShipLvl.Value, xp = xp.ShipExperience.Value });
                    if (xp.Ranks.Value > 0) s += ", " + Loc.T("ship.ranks", new { count = xp.Ranks.Value });
                    return s;
                }));

            // The per-rank outline: automatic features + choice groups, gaining ranks open by default
            // (latched so a made pick doesn't snap the group shut under focus).
            int actual = cp.Unit.Progression.GetPathRank(cp.CareerPath);
            bool hasRange = range.Min > 0 && range.Max >= range.Min;
            ControlId start = null;
            foreach (var re in cp.RankEntries)
            {
                if (re == null || re.IsEmpty) continue;
                var entry = re; // capture
                int rank = entry.Rank;
                bool gaining = hasRange && rank >= range.Min && rank <= range.Max;
                bool taken = rank <= actual;

                string rkey = k + "rank:" + rank;
                var rvt = GraphNodes.Group(() => CareerNodes.RankLabel(entry, rank, taken, gaining));
                rvt.OnExpand = () => _fold[rkey] = true;
                rvt.OnCollapse = () => _fold[rkey] = false;
                var rid = ControlId.Referenced(entry, rkey);
                b.BeginGroup(rid, rvt, expanded: Fold(rkey, gaining));
                if (gaining && start == null) start = rid;

                int fi = 0;
                foreach (var f in entry.Features)
                {
                    if (f == null) continue;
                    var feat = f; // capture
                    b.AddItem(ControlId.Referenced(feat, rkey + ":feat:" + fi++), CareerNodes.RankFeature(feat));
                }

                int si = 0;
                foreach (var sel in entry.Selections)
                {
                    if (sel == null) continue;
                    var s = sel; // capture
                    string skey = rkey + ":sel:" + si++;
                    if (_materialized.Add(s)) s.UpdateFeatures(); // lazy option list, built once per page
                    var svt = CareerNodes.SelectionGroup(s);
                    svt.OnExpand = () => _fold[skey] = true;
                    svt.OnCollapse = () => _fold[skey] = false;
                    b.BeginGroup(ControlId.Referenced(s, skey), svt,
                        expanded: Fold(skey, gaining && !s.SelectionMadeAndValid));
                    int oi = 0;
                    foreach (var opt in s.FilteredGroupList.OfType<Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers.RankEntry.Feature.BaseRankEntryFeatureVM>())
                    {
                        var o = opt; // capture
                        b.AddItem(ControlId.Referenced(o, skey + ":opt:" + oi++), CareerNodes.RankOption(o));
                    }
                    b.EndGroup();
                }
                b.EndGroup();
            }
            b.PopContext();
            // Opened directly on this tab (the game's ship level-up opener), first focus lands on the rank
            // being gained, not the tab row — the LevelUpScreen landing. Last-writer-wins over BuildTabs'
            // SetStart, and only while Skills is the active tab.
            if (start != null) b.SetStart(start);

            // Outstanding-choices line + Commit, as their own stops (Tab reaches Commit directly). Commit
            // goes through the game's own validated CareerPathVM.Commit → AddStarshipLevel (async).
            b.BeginStop("skillsstatus").AddItem(ControlId.Structural(k + "status"),
                GraphNodes.Text(() => CareerNodes.OutstandingText(cp)));
            b.BeginStop("commit").AddItem(ControlId.Structural(k + "commit"),
                GraphNodes.Button(() => Loc.T("levelup.commit"), () => CommitSkills(cp), () => cp.CanCommit.Value));
        }

        // No completion speech here (unlike LevelUpScreen.Commit): the commit is an async queued game
        // command, and when it lands the game logs the starship level-up — LogTap voices that line.
        private static void CommitSkills(CareerPathVM cp)
        {
            if (!cp.CanCommit.Value) return;
            cp.Commit();
        }

        private bool Fold(string key, bool def)
        {
            bool v;
            if (_fold.TryGetValue(key, out v)) return v;
            _fold[key] = def;
            return def;
        }

        // ---- Posts tab: post rows / officer candidates / the selected post's abilities, three regions in
        // one stop. Selection is the game's radio group (CurrentSelectedPost); assignment and attunement are
        // queued GameCommands — the LIVE labels speak the landing. ----

        private static void BuildPosts(GraphBuilder b, string kt, ShipPostsVM posts)
        {
            if (posts == null) return;
            bool locked = posts.IsLocked.Value;
            var selector = posts.PostsSelectorVM?.Selector;
            if (selector == null) return;

            b.BeginStop("posts");
            b.SetRegion(kt + "posts");
            b.PushContext(TabLabel(ShipCustomizationTab.Posts), Loc.T("role.list"));
            foreach (var pe in selector.EntitiesCollection)
            {
                if (pe == null) continue;
                var p = pe; // capture
                b.AddItem(ControlId.Referenced(p, kt + "post:" + p.Index), PostNode(posts, p));
            }
            b.PopContext();

            var sel = posts.CurrentSelectedPost.Value;
            int selIndex = sel?.Index ?? -1;

            var officers = posts.PostOfficerSelectorVM?.Selector;
            if (officers != null && sel != null)
            {
                b.SetRegion(kt + "officers");
                b.PushContext(Loc.T("ship.officers"), Loc.T("role.list"));
                foreach (var off in officers.EntitiesCollection)
                {
                    if (off?.Unit == null) continue; // padded placeholder rows
                    var o = off; // capture
                    b.AddItem(ControlId.Referenced(o.Unit, kt + "off:" + o.Unit.UniqueId),
                        OfficerNode(posts, o, locked));
                }
                b.PopContext();
            }

            if (sel?.AbilitiesGroup != null)
            {
                // Keyed on the selected post, so a post switch re-keys the ability list.
                b.SetRegion(kt + "postabilities");
                b.PushContext(Loc.T("ship.post_abilities"), Loc.T("role.list"));
                bool any = false;
                var seen = new HashSet<string>();
                foreach (var pab in sel.AbilitiesGroup.CurrentAbilities)
                {
                    if (pab?.Ability == null) continue;
                    var a = pab; // capture (instances churn on every HandleNewUnit — key structurally)
                    b.AddItem(ControlId.Structural(UniqueKey(seen,
                            kt + "pab:" + selIndex + ":" + a.Ability.AssetGuid)),
                        PostAbilityNode(a, locked));
                    any = true;
                }
                if (!any)
                    b.AddItem(ControlId.Structural(kt + "pab:" + selIndex + ":none"), GraphNodes.Text(
                        () => GameText.Or(() => UIStrings.Instance.ShipCustomization.NoSpecialAbilities, "ship.no_abilities")));
                b.PopContext();
            }
        }

        /// <summary>One post row — "Master of Ordnance, officer: Abelard, Ballistic Skill 45[, penalty]
        /// [, blocked][, selected]". Enter selects the post (the officer list and abilities follow);
        /// Space reads the post's description.</summary>
        private static NodeVtable PostNode(ShipPostsVM posts, PostEntityVM pe)
        {
            Func<string> label = () =>
            {
                var post = pe.Post;
                var s = PostTitle(pe.Index);
                var unit = post?.CurrentUnit;
                s += ", " + (unit != null
                    ? unit.CharacterName
                    : GameText.Or(() => UIStrings.Instance.ShipCustomization.NoOfficer, "ship.vacant"));
                if (post != null && unit != null)
                {
                    s += ", " + SkillText(post) + " " + post.CurrentSkillValue;
                    if (post.HasPenalty)
                        s += ", " + GameText.Or(() => UIStrings.Instance.ShipCustomization.HasPenalty, "ship.penalty");
                }
                if (pe.IsPostBlocked.Value) s += ", " + Loc.T("ship.blocked");
                return s;
            };
            var vt = new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    GraphNodes.SelectedPart(() => posts.CurrentSelectedPost.Value == pe),
                },
                SearchText = () => PostTitle(pe.Index),
                OnActivate = () => posts.PostsSelectorVM.Selector.SelectedEntity.Value = pe,
                OnTooltip = () => TooltipChooser.Open(PostTitle(pe.Index), PostDescription(pe), sections: null, links: null),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
            return vt;
        }

        // The localized post name/description are keyed by list POSITION, not on the entity
        // (UIStrings.SpaceCombatTexts.PostStrings[index]).
        private static string PostTitle(int index)
        {
            try
            {
                var t = UIStrings.Instance.SpaceCombatTexts.GetPostStrings(index).Title?.Text;
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch { }
            return Loc.T("ship.post_n", new { index = index + 1 });
        }

        private static string PostDescription(PostEntityVM pe)
        {
            string desc = null;
            try { desc = UIStrings.Instance.SpaceCombatTexts.GetPostStrings(pe.Index).Description?.Text; }
            catch { }
            var post = pe.Post;
            if (post?.PostData != null)
                desc = (string.IsNullOrEmpty(desc) ? "" : desc + "\n") +
                    Loc.T("ship.post_skill", new { skill = SkillText(post) });
            return desc;
        }

        private static string SkillText(Post post)
        {
            try { return LocalizedTexts.Instance.Stats.GetText(post.PostData.AssociatedSkill); }
            catch { return ""; }
        }

        /// <summary>One officer candidate — "Abelard, Ballistic Skill 45[, recommended / poor fit]
        /// [, on post: X][, selected]". Enter appoints them to the selected post (or vacates it when they
        /// already hold it) via the game's queued SetUnitOnPost; the live label + selected part speak the
        /// landing. Space = the game's officer-on-post card.</summary>
        private static NodeVtable OfficerNode(ShipPostsVM posts, PostOfficerVM off, bool locked)
        {
            Func<Post> selPost = () => posts.CurrentSelectedPost.Value?.Post;
            Func<bool> assignedHere = () => selPost()?.CurrentUnit == off.Unit;
            Func<string> label = () =>
            {
                var s = off.Unit.CharacterName;
                if (!string.IsNullOrEmpty(off.SkillName)) s += ", " + off.SkillName + " " + off.SkillValue;
                if (off.SkillRecommendation == UIUtilityUnit.SkillRecommendationEnum.Best)
                    s += ", " + Loc.T("chargen.recommended");
                else if (off.SkillRecommendation == UIUtilityUnit.SkillRecommendationEnum.Bad)
                    s += ", " + Loc.T("ship.poor_fit");
                if (!assignedHere())
                {
                    // Seated somewhere else? Say where — the sighted grid shows the post's hologram icon.
                    var hull = Game.Instance?.Player?.PlayerShip?.Hull;
                    var posts2 = hull?.Posts;
                    if (posts2 != null)
                        for (int i = 0; i < posts2.Count; i++)
                            if (posts2[i]?.CurrentUnit == off.Unit)
                            {
                                s += ", " + Loc.T("ship.on_post", new { post = PostTitle(i) });
                                break;
                            }
                }
                return s;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    GraphNodes.SelectedPart(assignedHere),
                    GraphNodes.DisabledPart(() => !locked),
                },
                SearchText = () => off.Unit.CharacterName,
                OnActivate = locked ? (Action)null
                    : () => { if (assignedHere()) off.DoUnselect(); else off.DoSelect(); },
                OnTooltip = () => TooltipChooser.OpenTemplate(off.Unit.CharacterName, off.TooltipTemplate()),
                ActivateSound = locked ? null : Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        /// <summary>One ability of the selected post — name + locked/cooldown state + attunement: an
        /// attunable ability reads its attune offer (cost, or the prerequisites still missing) and Enter
        /// attunes through the game's queued AttuneAbilityForPost when every gate is green. Space = the
        /// ability card (+ the attuned version as a drill-in section).</summary>
        private static NodeVtable PostAbilityNode(PostAbilityVM pab, bool locked)
        {
            Func<string> label = () =>
            {
                var s = pab.DisplayName;
                if (!pab.IsUnlocked) s += ", " + Loc.T("ship.ability_locked", new { reason = pab.LockedReason });
                if (pab.HasCooldown && pab.Cooldown > 0)
                    s += ", " + Loc.T("ship.cooldown_penalty", new { rounds = pab.Cooldown });
                if (pab.IsAlreadyAttuned) s += ", " + Loc.T("ship.attuned");
                return s;
            };
            Func<string> attune = () =>
            {
                if (!pab.IsAttunable || pab.IsAlreadyAttuned) return null;
                if (pab.CanAttune) return Loc.T("ship.can_attune", new { cost = pab.ScrapRequired });
                var missing = new List<string>();
                if (!pab.IsEnoughScrapForAttune)
                    missing.Add(GameText.Or(() => UIStrings.Instance.ShipCustomization.NotEnoughScrap, "ship.not_enough_scrap"));
                if (!pab.IsFullHP) missing.Add(Loc.T("ship.req_full_hp"));
                if (!pab.IsUsed) missing.Add(Loc.T("ship.req_used"));
                return Loc.T("ship.attune_missing", new { requirements = string.Join(", ", missing) });
            };
            bool canAttune = !locked && pab.CanAttune;
            return new NodeVtable
            {
                ControlType = canAttune ? ControlTypes.Button : ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(label, live: true, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(attune, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => pab.DisplayName,
                OnActivate = canAttune ? (Action)(() => pab.TryAttune()) : null,
                OnTooltip = () => OpenAbilityTooltip(pab),
                ActivateSound = canAttune ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick : null,
            };
        }

        // The ability's card, with the attuned variant (when the seated officer has the expertise) as a
        // drill-in section — the sighted view shows both cards side by side.
        private static void OpenAbilityTooltip(PostAbilityVM pab)
        {
            var templates = pab.TooltipTemplates();
            var own = templates != null && templates.Count > 0 ? templates[0] : null;
            string body = own != null ? TooltipReader.GetFull(own) : null;
            List<(string, string)> sections = null;
            if (templates != null && templates.Count > 1)
            {
                var attuned = TooltipReader.GetFull(templates[1]);
                if (!string.IsNullOrWhiteSpace(attuned))
                    sections = new List<(string, string)>
                    {
                        (GameText.Or(() => UIStrings.Instance.ShipCustomization.AttunedAbility, "ship.attuned"), attuned),
                    };
            }
            TooltipChooser.Open(pab.DisplayName, body, sections, links: null);
        }

        // ---- Accolades (Abilities) tab: the ship's active/passive lists, read-only with Space cards. The
        // VM is a one-shot snapshot built at tab activation. ----

        private static void BuildAbilities(GraphBuilder b, string kt, ShipAbilitiesVM ab)
        {
            if (ab == null) return;
            b.BeginStop("abilities").PushContext(TabLabel(ShipCustomizationTab.Abilities), Loc.T("role.list"));
            bool any = false;
            any |= FeatureGroup(b, kt + "act", Loc.T("charinfo.abilities_active"), ab.ActiveAbilities);
            any |= FeatureGroup(b, kt + "pas", Loc.T("charinfo.abilities_passive"), ab.PassiveAbilities);
            if (!any)
                b.AddItem(ControlId.Structural(kt + "none"), GraphNodes.Text(
                    () => GameText.Or(() => UIStrings.Instance.ShipCustomization.NoSpecialAbilities, "ship.no_abilities")));
            b.PopContext();
        }

        private static bool FeatureGroup(GraphBuilder b, string key, string title, List<CharInfoFeatureVM> items)
        {
            if (items == null || items.Count == 0) return false;
            b.BeginGroup(ControlId.Structural(key), GraphNodes.Group(() => title));
            var seen = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
            {
                var f = items[i];
                if (f == null) continue;
                var feat = f; // capture
                Func<string> label = () => feat.Rank > 1
                    ? Loc.T("charinfo.feature_ranked", new { name = feat.DisplayName, rank = feat.Rank })
                    : feat.DisplayName ?? "";
                var vt = GraphNodes.Text(label);
                vt.SearchText = label;
                vt.OnTooltip = () => TooltipChooser.OpenTemplate(label(), feat.Tooltip?.Value);
                b.AddItem(ControlId.Structural(UniqueKey(seen, key + ":" + (feat.DisplayName ?? i.ToString()))), vt);
            }
            b.EndGroup();
            return true;
        }

        // ---- the always-alive ship summary (the sighted left pane): name, hull + repair, scrap, level,
        // morale/crew, armor, shields, speed, ratings. Read from the summary VMs the window keeps alive on
        // every tab. ShipVM.ShipShieldValue is hardcoded "0/0" upstream — the four per-sector reactives are
        // the real values. ----

        private static void BuildStatus(GraphBuilder b, string k, ShipCustomizationVM vm)
        {
            var ship = vm.SpaceShipVM;
            var hr = vm.ShipHealthAndRepairVM;
            var stats = vm.ShipStatsVM;
            b.BeginStop("status").PushContext(Loc.T("ship.status"), Loc.T("role.list"));
            if (ship != null)
                b.AddItem(ControlId.Structural(k + "st:name"), GraphNodes.Text(() => ship.ShipName.Value));
            if (hr != null)
            {
                b.AddItem(ControlId.Structural(k + "st:hull"), GraphNodes.Text(
                    () => Loc.T("ship.hull", new { current = hr.CurrentShipHealth.Value, max = hr.MaxShipHealth.Value })));
                b.AddItem(ControlId.Structural(k + "st:repair"), GraphNodes.Button(
                    () => Loc.T("ship.repair_full", new { cost = hr.ScrapNeedForRepair.Value }),
                    () => hr.RepairShipFull(),
                    () => hr.CanRepair.Value && hr.ScrapWeHave.Value >= hr.ScrapNeedForRepair.Value));
                b.AddItem(ControlId.Structural(k + "st:repair_all"), GraphNodes.Button(
                    () => Loc.T("ship.repair_all", new { scrap = hr.ScrapWeHave.Value }),
                    () => hr.RepairShipForAllScrap(),
                    () => hr.CanRepair.Value && hr.ScrapWeHave.Value < hr.ScrapNeedForRepair.Value));
                b.AddItem(ControlId.Structural(k + "st:scrap"), GraphNodes.Text(
                    () => Loc.T("ship.scrap", new { value = hr.ScrapWeHave.Value })));
            }
            if (ship != null)
            {
                b.AddItem(ControlId.Structural(k + "st:xp"), GraphNodes.Text(
                    () => Loc.T("ship.xp_line", new { level = ship.ShipLvl.Value, xp = ship.ShipExperience.Value })));
                b.AddItem(ControlId.Structural(k + "st:crew"), GraphNodes.Text(
                    () => Loc.T("ship.morale_crew", new { morale = ship.ShipMoraleValue.Value, crew = ship.ShipCrewValue.Value })));
                b.AddItem(ControlId.Structural(k + "st:armor"), GraphNodes.Text(
                    () => Loc.T("ship.armor", new
                    {
                        fore = (int)ship.ShipArmorFront.Value,
                        port = (int)ship.ShipArmorLeft.Value,
                        starboard = (int)ship.ShipArmorRight.Value,
                        aft = (int)ship.ShipArmorRear.Value,
                    })));
                b.AddItem(ControlId.Structural(k + "st:shields"), GraphNodes.Text(
                    () => Loc.T("ship.shields", new
                    {
                        fore = ship.ShipFrontShield.Value,
                        port = ship.ShipLeftShield.Value,
                        starboard = ship.ShipRightShield.Value,
                        aft = ship.ShipRearShield.Value,
                    })));
            }
            if (stats != null)
                b.AddItem(ControlId.Structural(k + "st:speed"), GraphNodes.Text(
                    () => Loc.T("ship.speed", new { speed = stats.Speed.Value, inertia = stats.Inertia.Value })));
            if (ship != null)
                b.AddItem(ControlId.Structural(k + "st:rating"), GraphNodes.Text(
                    () => Loc.T("ship.rating", new { military = ship.ShipMilitaryRating.Value, turret = ship.ShipTurretRating.Value })));
            b.PopContext();
        }

        private static string UniqueKey(HashSet<string> seen, string baseKey)
        {
            if (seen.Add(baseKey)) return baseKey;
            int i = 2;
            while (!seen.Add(baseKey + "#" + i)) i++;
            return baseKey + "#" + i;
        }
    }
}
