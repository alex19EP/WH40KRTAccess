using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root;                   // LocalizedTexts, BlueprintRoot (reload-ability filter)
using Kingmaker.Blueprints.Root.Strings;           // UIStrings (the game's own filter / visual-settings labels)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // AbilitiesOrdered
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.SkillsAndWeapons.Skills;        // SkillsOrdered
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateAbility (the weapons block's ability cards)
using Kingmaker.Code.UI.MVVM.View.Slots;           // ItemsFilterSearchBaseView / ItemsFilterPCView (live chrome)
using Kingmaker.EntitySystem.Entities;             // BaseUnitEntity
using Kingmaker.GameCommands;
using Kingmaker.Items;                             // ItemEntityWeapon (the weapons block)
using Kingmaker.Stores;                            // StoreManager (the augmentations filter's DLC3 gate)
using Kingmaker.Stores.DlcInterfaces;              // DlcNameEnum
using Kingmaker.UI.Common;                         // ItemsFilterType, ItemsSorterType
using Owlcat.Runtime.UI.Tooltips;                  // TooltipBaseTemplate
using RTAccess.Accessibility;                       // ViewedCharacter (switch announce + header + pet swap)
using RTAccess.UI;
using RTAccess.UI.Graph;
using TMPro;                                        // TMP_InputField

namespace RTAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>) as a graph-native screen — one labelled
    /// Tab-STOP per pane the game window binds: character (name/level/XP/careers/level-up), characteristics,
    /// skills, weapons (attack modes), the equipment doll (+ carry weight + the visual-settings opener),
    /// a defensive-stat readout, the party-load summary, and the shared party stash with its search +
    /// filter/sort chrome — all declared IMMEDIATE-MODE from the live VM on every render (no content
    /// signature, no focus-capture/restore dance). Tab cycles the panes (wrapping; arrows never leave one).
    /// The characteristics/skills stops reuse <see cref="CharacterInfoScreen.BuildStatSection"/> — the game
    /// binds the SAME CharInfo block VMs into this window's left panel, so the speech matches the sheet.
    /// The stash stop keeps its chrome (search, filters/sort) as Ctrl+arrow REGIONS above the list they
    /// operate on. Initial focus stays on the graph start — the character readout — so opening speaks whose
    /// inventory is shown. The stash is a plain LIST — one focusable row per item, its label mirroring the
    /// card (name + badges + count); Type/Weight/Value are tooltip-only on the card, so they stay on Space
    /// (the item's own tooltip). The game stash is a filtered/sorted flat list of icon cards (2-D position is
    /// sort-order only), so a list — not a column table — is the faithful model.
    ///
    /// Doll slots read their item LIVE from the doll VM inside the node each render (re-fetching
    /// <c>DollVM.Armor</c> etc. every <see cref="Build"/>) and are keyed STRUCTURALLY by slot position + the
    /// viewed unit — never by the <see cref="EquipSlotVM"/> object, which the doll replaces on every equip. That
    /// keeps focus put across an equip while the shown item updates live (the differ re-announces the now-filled
    /// slot under focus), fixing the old adapter bug where the doll read EMPTY until the window was reopened
    /// (its ContentSig sampled the stash but not the doll). Stash rows key by the item ENTITY, so an equipped /
    /// dropped / moved item's node vanishes and focus slides to a genuinely different row the differ reads out.
    /// Doll + defense keys carry the viewed unit so a character switch re-keys them; party-wide summary / stash
    /// keys don't. Escape closes the whole service-window stack. Layer 10. ScreenName stays null — the existing
    /// ServiceWindowAnnounce Harmony patch already speaks "Inventory" on open.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public override string Key => "service.inventory";
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Inventory" on open
        public override int Layer => 10;

        // Tab wraps from the stash back around to the character pane — the service-window convention
        // (CharacterInfo / Settings / SaveLoad do the same).
        public InventoryScreen() { Wrap = true; }

        // Type-ahead OFF here: bare letters pass to the game (its search hotkey), and stash search is the
        // game's OWN field (BuildSearch), not our type-ahead. Shift+A/D character switching is the mod's own
        // party chords (PartyHotkeys window branch — the claim suppresses the game's binds here).
        public override bool AllowsTypeahead => false;

        public override bool IsActive() => Vm() != null;

        // Re-baseline the switch guard on open, then announce each character switch (Shift+A/D or the
        // header's prev/next buttons: SelectedUnitInUI changes but nothing speaks it; our doll/defenses
        // re-key, but only ViewedCharacter voices WHO). OnUpdate runs each frame on the focused screen.
        public override void OnPush() => ViewedCharacter.Reset();
        public override void OnUpdate() => ViewedCharacter.Tick(Vm()?.Unit?.Value);

        // Back (Escape) closes the whole service-window stack — the same call the window's own close uses.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        // Inventory opens on the planet surface AND in the star-system/space context; resolve from whichever
        // static part is live (RootUIContext checks both everywhere — IsInventoryShow / CurrentServiceWindow).
        private static InventoryVM Vm() => UiContexts.Inventory();

        private static ServiceWindowsVM ServiceWindows() => UiContexts.ServiceWindows();


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "inv:" + vm.GetHashCode() + ":";           // a new window = fresh keys
            var unit = vm.Unit?.Value;
            string uk = k + "u:" + (unit?.UniqueId ?? "") + ":";  // per-character keys re-home on a unit switch

            BuildCharacter(b, k, uk, vm);        // stop: character (readout, XP, careers, level-up, switch)
            if (unit != null)
            {
                // The window's left panel binds the SAME characteristics/skills block VMs the character
                // sheet shows — mirror them with the sheet's shared builder, so the speech matches there.
                CharacterInfoScreen.BuildStatSection(b, "chars", uk + "abil:",
                    Loc.T("charinfo.characteristics"), unit,
                    CharInfoAbilityScoresBlockVM.AbilitiesOrdered, withWounds: false);
                CharacterInfoScreen.BuildStatSection(b, "skills", uk + "skill:",
                    Loc.T("charinfo.skills"), unit,
                    CharInfoSkillsBlockVM.SkillsOrdered, withWounds: false);
                BuildWeapons(b, uk, unit);       // stop: weapons (attack modes per equipped weapon)
            }
            BuildEquipment(b, k, uk, vm.DollVM); // stop: equip
            BuildDefenses(b, k, uk, vm);         // stop: defenses
            BuildSummary(b, k, vm.StashVM);      // stop: summary

            // The stash pane is ONE stop — the search + filter/sort chrome belongs with the list it
            // operates on. Within it the chrome and the list are Ctrl+arrow REGIONS, so Ctrl+Up from
            // deep in a 120-row list jumps straight back to the controls. One pane-wide context labels
            // the stop (Tab entry says "Stash" whichever region focus lands in — first entry lands on
            // the search edit, which otherwise never names the pane); the announcer dedupes it against
            // the identically-labelled inner Stash list context, so items still read "Stash, list, …".
            b.BeginStop("stash");
            b.PushContext(Loc.T("inv.stash"));
            BuildSearch(b, k, vm.StashVM);
            BuildStashControls(b, k, vm.StashVM);
            BuildStash(b, k, vm.StashVM);
            b.PopContext();
        }

        // The header a sighted player reads beside the portrait: who's shown (name / level / wounds), the
        // prev/next member switch (the chrome's portrait arrows — also on Shift+A/D via PartyHotkeys), and
        // the pet/master swap (the game's m_PetButton). The readout is keyed per-unit (uk) so a switch
        // re-homes and the differ re-reads it under focus; ALL the switch buttons — prev/next AND the pet
        // swap — are keyed to the WINDOW (k) so focus stays on the button across the switch while
        // ViewedCharacter.Tick announces who's shown (the pet label reads the LIVE unit, so it flips to
        // the other direction; a switch onto a unit with no pet axis drops the node and reconcile slides).
        private static void BuildCharacter(GraphBuilder b, string k, string uk, InventoryVM vm)
        {
            var unit = vm.Unit?.Value;
            if (unit == null) return;
            b.BeginStop("character");
            b.PushContext(Loc.T("inv.character"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(uk + "char:readout"),
                GraphNodes.Text(() => ViewedCharacter.HeaderLine(vm.Unit?.Value)));
            // The level block beside the portrait (the window binds CharInfoLevelClassScoresVM here):
            // XP + psy rating off the live Experience VM, the careers list, and the Level Up entry while
            // a rank is pending — driving the VM's OWN LevelUp() (raises ILevelUpInitiateUIHandler, the
            // same event the XP block's affordance fires; the game hides this window and opens the
            // progression page our LevelUpScreen mirrors).
            var exp = vm.LevelClassScoresVM?.Experience;
            if (exp != null)
            {
                b.AddItem(ControlId.Structural(uk + "char:xp"), GraphNodes.Text(
                    () => Loc.T("inv.xp", new { current = exp.CurrentExp.Value, next = exp.NextLevelExp.Value })));
                if (exp.HasPsyRating.Value)
                    b.AddItem(ControlId.Structural(uk + "char:psy"), StatLine(
                        () => Loc.T("inv.psy_rating", new { value = exp.PsyRating.Value }),
                        () => exp.PsyRatingTooltip));
            }
            int ci = 0;
            foreach (var career in unit.Progression.AllCareerPaths)
            {
                var c = career; // capture (a value tuple — keyed by blueprint, not reference)
                if (c.Blueprint == null) continue;
                b.AddItem(ControlId.Structural(uk + "char:career:" + (c.Blueprint.AssetGuid ?? (ci++).ToString())),
                    GraphNodes.Text(() => Loc.T("charinfo.career", new { name = c.Blueprint.Name, rank = c.Rank })));
            }
            if (exp?.CanLevelup.Value == true)
                b.AddItem(ControlId.Structural(uk + "char:levelup"), GraphNodes.Button(
                    () => Loc.T("levelup.button"), exp.LevelUp));
            b.AddItem(ControlId.Structural(k + "char:prev"), GraphNodes.Button(
                () => Loc.T("char.prev_member"), () => ViewedCharacter.SwitchMember(next: false)));
            b.AddItem(ControlId.Structural(k + "char:next"), GraphNodes.Button(
                () => Loc.T("char.next_member"), () => ViewedCharacter.SwitchMember(next: true)));
            if (ViewedCharacter.HasPetAxis(unit))
                b.AddItem(ControlId.Structural(k + "char:pet"), GraphNodes.Button(
                    () => ViewedCharacter.PetLabel(vm.Unit?.Value), () => ViewedCharacter.SwapPet(vm.Unit?.Value)));
            b.PopContext();
        }

        // The game's OWN item search over the (up-to-120-slot) stash — the accessible replacement for our
        // type-ahead (off here). Activating hands the live search TMP field to TextEntry: Unity/TMP own the
        // caret and typing, each keystroke is echoed, and the game's own onValueChanged → SetSearchString
        // drives SlotsGroupVM.SearchString → UpdateVisibleCollection, so the next immediate-mode BuildStash
        // render reflects the filtered list. The label carries the active query (read on re-focus after an
        // edit); a "Clear" button appears only while a query is set. Keyed party-wide (search is shared).
        internal static void BuildSearch(GraphBuilder b, string k, InventoryStashVM stash) // shared with CargoScreen (same InventoryStashVM)
        {
            if (stash?.ItemSlotsGroup == null) return;
            b.SetRegion(k + "search");
            b.PushContext(Loc.T("inv.search"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "search:edit"), GraphNodes.Button(
                () =>
                {
                    var q = stash.ItemSlotsGroup.SearchString?.Value;
                    return string.IsNullOrEmpty(q) ? Loc.T("inv.search") : Loc.T("inv.search_active", new { query = q });
                },
                BeginSearch));
            if (!string.IsNullOrEmpty(stash.ItemSlotsGroup.SearchString?.Value))
                b.AddItem(ControlId.Structural(k + "search:clear"), GraphNodes.Button(
                    () => Loc.T("inv.search_clear"), () => ClearSearch(stash)));
            b.PopContext();
        }

        // Focus the game's live search field and let TextEntry own the keyboard; the game's binding filters.
        private static void BeginSearch()
        {
            var field = SearchField();
            if (field != null) TextEntry.Begin(field, Loc.T("inv.search"));
            else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
        }

        // Clear via the game's own field (its observer sets SearchString=""), falling back to the reactive.
        private static void ClearSearch(InventoryStashVM stash)
        {
            var field = SearchField();
            if (field != null) field.text = "";
            else if (stash?.ItemSlotsGroup?.SearchString != null && !string.IsNullOrEmpty(stash.ItemSlotsGroup.SearchString.Value))
                stash.ItemSlotsGroup.SearchString.Value = "";
            Tts.Speak(Loc.T("search.cleared"), interrupt: true);
        }

        // The live inventory search field: the game's own TMP input in the filter bar (publicized field on
        // ItemsFilterSearchBaseView). Pick the active, interactable one — the PC view in our forced mouse mode.
        private static TMP_InputField SearchField()
        {
            var views = UnityEngine.Object.FindObjectsByType<ItemsFilterSearchBaseView>(UnityEngine.FindObjectsSortMode.None);
            foreach (var v in views)
            {
                if (v == null || !v.isActiveAndEnabled) continue;
                var f = v.m_InputField;
                if (f != null && f.isActiveAndEnabled && f.IsInteractable()) return f;
            }
            return null;
        }

        // The equipment doll: the weapon sets, then the worn gear and quick slots, as a flat "Slot: item" list.
        // The roster and its order mirror what the RT doll VIEW actually binds (InventoryDollView.UpdateSlots):
        // armor, head, gloves, boots, back (the VM's Shoulders — cloaks), neck, rings, the pet protocol, quick
        // slots. The VM also carries Belt/Wrist/Glasses/Shirt, but those are Pathfinder leftovers no RT view
        // binds and no RT item type fills — declaring them would speak permanently-empty slots the sighted
        // player never sees. Each slot's EquipSlotVM is resolved FRESH here every render (the doll replaces
        // them on equip); the node keys are structural by slot position + viewed unit, so focus stays put
        // across an equip. After the slots: the character's own carry weight (the doll pane's encumbrance
        // bar) and the visual-settings opener (the doll's cosmetics button — VisualSettingsScreen mirrors
        // the CharacterVisualSettingsVM it raises).
        private static void BuildEquipment(GraphBuilder b, string k, string uk, InventoryDollVM doll)
        {
            if (doll == null) return;
            b.BeginStop("equip");
            b.PushContext(Loc.T("inv.equipment"), Loc.T("role.list"));
            BuildWeaponSets(b, uk, doll);
            AddDollSlot(b, uk, "armor", Loc.T("slot.armor"), doll.Armor, doll);
            AddDollSlot(b, uk, "head", Loc.T("slot.head"), doll.Head, doll);
            AddDollSlot(b, uk, "gloves", Loc.T("slot.gloves"), doll.Gloves, doll);
            AddDollSlot(b, uk, "feet", Loc.T("slot.feet"), doll.Feet, doll);
            AddDollSlot(b, uk, "back", Loc.T("slot.back"), doll.Shoulders, doll);
            AddDollSlot(b, uk, "neck", Loc.T("slot.neck"), doll.Neck, doll);
            AddDollSlot(b, uk, "ring1", Loc.T("slot.ring1"), doll.Ring1, doll);
            AddDollSlot(b, uk, "ring2", Loc.T("slot.ring2"), doll.Ring2, doll);
            AddDollSlot(b, uk, "protocol", Loc.T("slot.protocol"), doll.Protocol, doll);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    AddDollSlot(b, uk, "quick:" + i, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i], doll);
            var enc = doll.EncumbranceVM;
            if (enc?.EncumbranceVm != null)
                b.AddItem(ControlId.Structural(uk + "doll:enc"), StatLine(() =>
                {
                    var status = enc.EncumbranceVm.LoadStatus?.Value;
                    var load = (enc.EncumbranceVm.LoadWeight?.Value ?? "")
                        + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                    return Loc.T("inv.encumbrance_char", new { value = load });
                }, () => enc.Tooltip?.Value));
            // Window-keyed (like the switch buttons): focus stays on the opener across a character switch.
            b.AddItem(ControlId.Structural(k + "doll:visual"), GraphNodes.Button(
                () => UIStrings.Instance.CharGen.ShowVisualSettings.Text, doll.ShowVisualSettings));
            b.PopContext();
        }

        // The two hand loadouts (I/II). When both are usable, a combo box drives the game's own set selection
        // (SetSelected → SwitchHandEquipment) and BOTH sets' hands are listed, set-numbered, so the inactive
        // loadout is visible without committing a switch. A unit with a single usable set (mechadendrites / a
        // unique companion) just gets the active hands, unlabelled.
        private static void BuildWeaponSets(GraphBuilder b, string uk, InventoryDollVM doll)
        {
            var sets = doll.WeaponSets;
            if (sets == null || sets.Count == 0)
            {
                var only = doll.CurrentSet?.Value;
                AddDollSlot(b, uk, "primary", Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddDollSlot(b, uk, "secondary", Loc.T("slot.secondary_hand"), only?.Secondary, doll);
                return;
            }

            var usable = new List<WeaponSetVM>();
            foreach (var s in sets) if (s != null && s.IsEnabled?.Value == true) usable.Add(s);
            if (usable.Count == 0) usable.Add(sets[0]);

            if (usable.Count > 1)
            {
                var opts = usable;
                b.AddItem(ControlId.Structural(uk + "wset"), GraphNodes.Cycler(
                    () => Loc.T("inv.weapon_sets"),
                    () => opts.ConvertAll(s => Loc.T("inv.weapon_set", new { index = s.Index + 1 })),
                    () => Math.Max(0, opts.IndexOf(doll.CurrentSet?.Value)),
                    i => { if (i >= 0 && i < opts.Count && opts[i] != doll.CurrentSet?.Value) opts[i].SetSelected(true); }));
                foreach (var s in usable)
                {
                    AddDollSlot(b, uk, "set" + (s.Index + 1) + ":primary",
                        Loc.T("slot.set_primary", new { set = s.Index + 1 }), s.Primary, doll);
                    AddDollSlot(b, uk, "set" + (s.Index + 1) + ":secondary",
                        Loc.T("slot.set_secondary", new { set = s.Index + 1 }), s.Secondary, doll);
                }
            }
            else
            {
                var only = doll.CurrentSet?.Value ?? usable[0];
                AddDollSlot(b, uk, "primary", Loc.T("slot.primary_hand"), only?.Primary, doll);
                AddDollSlot(b, uk, "secondary", Loc.T("slot.secondary_hand"), only?.Secondary, doll);
            }
        }

        private static void AddDollSlot(GraphBuilder b, string uk, string posKey, string name,
            EquipSlotVM slot, InventoryDollVM doll)
        {
            if (slot == null) return;
            // Structural key = slot position + viewed unit (uk). NEVER key by the EquipSlotVM object — the doll
            // rebuilds it on equip, which would teleport focus; the item is read live inside the node instead.
            b.AddItem(ControlId.Structural(uk + "doll:" + posKey), ItemNodes.EquipSlot(name, slot, doll));
        }

        // The left panel's Weapons block (CharInfoWeaponsBlockVM): per equipped weapon, the attack modes a
        // sighted player reads there — each weapon a collapsible group of its non-hidden weapon abilities
        // (reload excluded, exactly the block VM's UpdateAbilities filter), Space = the game's own ability
        // card built against this weapon (TooltipTemplateAbility(ability, weapon, unit) — the same template
        // CharInfoWeaponSetAbilityVM constructs). Sets read straight off the body like the block VM does;
        // empty sets vanish, and set numbers only appear while both sets hold weapons.
        private static void BuildWeapons(GraphBuilder b, string uk, BaseUnitEntity unit)
        {
            var sets = unit.Body?.HandsEquipmentSets;
            if (sets == null) return;
            var rows = new List<(int set, ItemEntityWeapon weapon)>();
            for (int i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                if (set == null || set.IsEmpty()) continue;
                if (set.PrimaryHand?.MaybeItem is ItemEntityWeapon p) rows.Add((i, p));
                if (set.SecondaryHand?.MaybeItem is ItemEntityWeapon s) rows.Add((i, s));
            }
            if (rows.Count == 0) return;

            bool multi = rows.Exists(r => r.set == 0) && rows.Exists(r => r.set == 1);
            b.BeginStop("weapons");
            b.PushContext(UIStrings.Instance.CharacterSheet.Weapons.Text, Loc.T("role.list"));
            var reload = BlueprintRoot.Instance?.UIConfig?.ReloadAbility;
            foreach (var row in rows)
            {
                var weapon = row.weapon;
                string prefix = multi ? Loc.T("inv.weapon_set", new { index = row.set + 1 }) + ": " : "";
                string wkey = uk + "weap:" + row.set + ":" + (weapon.UniqueId ?? weapon.Name);
                b.BeginGroup(ControlId.Structural(wkey), GraphNodes.Group(() => prefix + weapon.Name));
                int ai = 0;
                var abilities = weapon.Blueprint?.WeaponAbilities;
                if (abilities != null)
                    foreach (var wa in abilities)
                    {
                        var ability = wa?.Ability;
                        if (ability == null || ability == reload || ability.HiddenInUI) continue;
                        var ab = ability; // capture for the label/tooltip factories
                        var vt = GraphNodes.Text(() => ab.Name);
                        vt.SearchText = () => ab.Name;
                        vt.OnTooltip = () => TooltipChooser.OpenTemplate(ab.Name,
                            new TooltipTemplateAbility(ab, weapon.Blueprint, unit));
                        b.AddItem(ControlId.Structural(wkey + ":ab:" + ai), vt);
                        ai++;
                    }
                b.EndGroup();
            }
            b.PopContext();
        }

        // The derived defensive stats the game shows beside the doll — read live from the reachable
        // InventoryDollAdditionalStatsVM (already-formatted strings + breakdown tooltips on Space). Resolve is
        // hidden for pets (the VM reports "—"). Per-character, so keyed on the viewed unit.
        private static void BuildDefenses(GraphBuilder b, string k, string uk, InventoryVM vm)
        {
            var s = vm.LevelClassScoresVM?.AdditionalStatsVM;
            if (s == null) return;
            b.BeginStop("defenses");
            b.PushContext(Loc.T("inv.defenses"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(uk + "def:deflection"),
                StatLine(() => Loc.T("stat.deflection", new { value = s.ArmorDeflection?.Value }), () => s.DeflectionTooltip?.Value));
            b.AddItem(ControlId.Structural(uk + "def:absorption"),
                StatLine(() => Loc.T("stat.absorption", new { value = s.ArmorAbsorption?.Value }), () => s.AbsorptionTooltip?.Value));
            b.AddItem(ControlId.Structural(uk + "def:dodge"),
                StatLine(() => Loc.T("stat.dodge", new { value = s.Dodge?.Value }), () => s.DodgeTooltip?.Value));
            b.AddItem(ControlId.Structural(uk + "def:dodge_reduction"),
                StatLine(() => Loc.T("stat.dodge_reduction", new { value = s.DodgeReduction?.Value })));
            if (!string.IsNullOrEmpty(s.Resolve?.Value) && s.Resolve?.Value != "—")
                b.AddItem(ControlId.Structural(uk + "def:resolve"),
                    StatLine(() => Loc.T("stat.resolve", new { value = s.Resolve?.Value })));
            b.AddItem(ControlId.Structural(uk + "def:parry"),
                StatLine(() => Loc.T("stat.parry", new { value = s.Parry?.Value })));
            b.PopContext();
        }

        // A read-only stat line whose Space drills into the game's own breakdown template (rendered body +
        // inline glossary links) through the shared chooser — the game shows a full stat breakdown on hover.
        private static NodeVtable StatLine(Func<string> text, Func<TooltipBaseTemplate> tooltip = null)
        {
            var vt = GraphNodes.Text(text);
            if (tooltip != null) vt.OnTooltip = () => TooltipChooser.OpenTemplate(text(), tooltip());
            return vt;
        }

        // Party-wide readout: carry weight + load status. Lives on the shared stash, so it sits between the
        // per-character equipment and the stash list; keyed party-wide (not on the viewed unit). The old
        // "Gold" line was a Pathfinder leftover — RT has no player currency UI (Player.Money feeds only
        // designer script conditions; no UIStrings names it), so the number was never shown to a sighted
        // player and is dropped.
        private static void BuildSummary(GraphBuilder b, string k, InventoryStashVM stash)
        {
            if (stash == null) return;
            b.BeginStop("summary");
            b.PushContext(Loc.T("inv.inventory"), Loc.T("role.list"));
            var enc = stash.EncumbranceVM;
            if (enc != null)
                b.AddItem(ControlId.Structural(k + "sum:enc"), GraphNodes.Text(() =>
                {
                    var status = enc.LoadStatus?.Value;
                    var load = (enc.LoadWeight?.Value ?? "") + (string.IsNullOrEmpty(status) ? "" : ", " + status);
                    return Loc.T("inv.encumbrance", new { value = load });
                }));
            b.PopContext();
        }

        // The filter + sort control bar above the stash — the real chrome a sighted player uses to operate a
        // 120-slot list. The filter and sorter are combo boxes (Enter → a submenu of localized options) that
        // drive the game's OWN filter VM / sort command; both persist to UISettings and rebuild the visible
        // collection, which the next immediate-mode render reflects (the live value part re-announces the
        // landing). Alongside them: the header's force-sort button (SortItems — re-applies the current order
        // now) and, when the window's live filter bar carries it (m_ShowToggle is per-prefab), the
        // "show unavailable items" toggle driving the game's own ShowUnavailable reactive (→ UISettings).
        // A horizontal row, so Left/Right walks the controls. (The search field is its own region above.)
        internal static void BuildStashControls(GraphBuilder b, string k, InventoryStashVM stash) // shared with CargoScreen (same InventoryStashVM + EventBus sorter route)
        {
            var filter = stash?.ItemsFilter;
            if (filter == null) return;
            b.SetRegion(k + "filters");
            b.PushContext(Loc.T("inv.filters"), Loc.T("role.list"));
            b.StartRow(k + "filtersrow");

            var filters = ActiveFilterOptions();
            b.AddItem(ControlId.Structural(k + "filter"), GraphNodes.Cycler(
                () => Loc.T("inv.filters"),
                () => filters.ConvertAll(FilterName),
                () => { var cf = stash?.ItemsFilter?.CurrentFilter; return cf != null ? Math.Max(0, filters.IndexOf(cf.Value)) : 0; },
                i => { if (i >= 0 && i < filters.Count) filter.SetCurrentFilter(filters[i]); }));

            var sorters = SortOptions;
            b.AddItem(ControlId.Structural(k + "sort"), GraphNodes.Cycler(
                () => Loc.T("inv.sort"),
                () => sorters.ConvertAll(t => LocalizedTexts.Instance.ItemsFilter.GetText(t)),
                () => Math.Max(0, sorters.IndexOf(stash.CurrentSorter.Value)),
                i => { if (i >= 0 && i < sorters.Count) Game.Instance.GameCommandQueue.SetInventorySorter(sorters[i]); }));

            b.AddItem(ControlId.Structural(k + "sortnow"), GraphNodes.Button(
                () => Loc.T("inv.sort_now"), () => stash.ItemSlotsGroup?.SortItems()));

            if (ShowsUnavailableToggle(stash))
                b.AddItem(ControlId.Structural(k + "unavail"), GraphNodes.Toggle(
                    () => UIStrings.Instance.InventoryScreen.ShowUnavailableItems.Text,
                    () => filter.ShowUnavailable.Value,
                    () => filter.ShowUnavailable.Value = !filter.ShowUnavailable.Value));

            b.EndRow();
            b.PopContext();
        }

        // The personal-inventory filter set (mirrors ItemsFilterPCView.m_SortedFiltersList) and the sort modes
        // (every ItemsSorterType except the cargo-only CargoValue, matching the game's sort dropdown).
        private static readonly List<ItemsFilterType> FilterOptions = new List<ItemsFilterType>
        {
            ItemsFilterType.NoFilter, ItemsFilterType.Weapon, ItemsFilterType.Armor,
            ItemsFilterType.Accessories, ItemsFilterType.Usable, ItemsFilterType.Notable,
            ItemsFilterType.NonUsable, ItemsFilterType.AugmentationsAll, ItemsFilterType.ShipNoFilter,
        };

        // The game removes the Augmentations tab when DLC3 (The Infinite Museion) isn't installed
        // (ItemsFilterPCView.ApplyAugmentationsAvailability) — mirror the gate. Checked once (DLC state
        // can't change mid-run).
        private static bool? s_HasAugmentsDlc;
        private static List<ItemsFilterType> ActiveFilterOptions()
        {
            s_HasAugmentsDlc ??= StoreManager.CheckIfDlcPurchasedAndInstalled(DlcNameEnum.DLC3TheInfiniteMuseion);
            if (s_HasAugmentsDlc == true) return FilterOptions;
            var list = new List<ItemsFilterType>(FilterOptions);
            list.Remove(ItemsFilterType.AugmentationsAll);
            return list;
        }

        // Filter names come from the strings the game's own filter bar shows as its toggle hints
        // (ItemsFilterPCView.SetHints) — NOT LocalizedTexts.ItemsFilter, whose FILTER-type entries the RT
        // asset leaves empty (only the SORTER entries are filled; reading it spoke blank filter options).
        private static string FilterName(ItemsFilterType t)
        {
            var inv = UIStrings.Instance.InventoryScreen;
            string s = t switch
            {
                ItemsFilterType.NoFilter => inv.FilterTextAll.Text,
                ItemsFilterType.Weapon => inv.FilterTextWeapon.Text,
                ItemsFilterType.Armor => inv.FilterTextArmor.Text,
                ItemsFilterType.Accessories => inv.FilterTextAcessories.Text, // (the game's own field typo)
                ItemsFilterType.Usable => inv.FilterTextUsable.Text,
                ItemsFilterType.Notable => inv.FilterTextNotable.Text,
                ItemsFilterType.NonUsable => inv.FilterTextOther.Text,
                ItemsFilterType.AugmentationsAll => UIStrings.Instance.UIAugmentations.FilterAll.Text,
                ItemsFilterType.ShipNoFilter => inv.FilterTextShipItem.Text,
                _ => null,
            };
            return string.IsNullOrEmpty(s) ? LocalizedTexts.Instance.ItemsFilter.GetText(t) : s;
        }

        // Whether the game's inventory filter bar carries the "show unavailable" toggle — a serialized
        // per-prefab flag (m_ShowToggle), so read it off the LIVE view once per window and cache (the
        // FindObjects sweep is too heavy for every immediate-mode render). No live view yet → try again
        // next render, don't cache the miss.
        private static int s_UnavailCheckedFor;
        private static bool s_UnavailShown;
        private static bool ShowsUnavailableToggle(InventoryStashVM stash)
        {
            int key = stash.GetHashCode();
            if (s_UnavailCheckedFor == key) return s_UnavailShown;
            var views = UnityEngine.Object.FindObjectsByType<ItemsFilterPCView>(UnityEngine.FindObjectsSortMode.None);
            foreach (var v in views)
            {
                if (v == null || !v.isActiveAndEnabled) continue;
                s_UnavailCheckedFor = key;
                s_UnavailShown = v.m_ShowToggle;
                return s_UnavailShown;
            }
            return false;
        }

        internal static readonly List<ItemsSorterType> SortOptions = new List<ItemsSorterType> // shared with CargoScreen (the cargo sorter uses the same non-vendor set)
        {
            ItemsSorterType.NotSorted, ItemsSorterType.TypeUp, ItemsSorterType.TypeDown,
            ItemsSorterType.CharacteristicsUp, ItemsSorterType.CharacteristicsDown,
            ItemsSorterType.NameUp, ItemsSorterType.NameDown,
            ItemsSorterType.DateUp, ItemsSorterType.DateDown, ItemsSorterType.Favorite,
        };

        // The stash: one focusable row per item, its label mirroring the card (name + badges + count via
        // ItemNodes.InventoryItem). Type/weight/value are tooltip-only on the card, so they stay on Space (the
        // item's own tooltip). Keyed by the item ENTITY, so equipping / dropping / moving an item removes its
        // node and focus slides to a genuinely different row the differ reads. Empty (an active filter matched
        // nothing) reads a placeholder.
        private static void BuildStash(GraphBuilder b, string k, InventoryStashVM stash)
        {
            b.SetRegion(k + "stash");
            b.PushContext(Loc.T("inv.stash"), Loc.T("role.list"));
            bool any = false;
            var vis = stash?.ItemSlotsGroup?.VisibleCollection;
            if (vis != null)
                foreach (var slot in vis)
                {
                    if (slot == null || !slot.HasItem) continue;
                    var ent = slot.Item?.Value;
                    if (ent == null) continue;
                    b.AddItem(ControlId.Referenced(ent, k + "stash:" + ent.UniqueId), ItemNodes.InventoryItem(slot));
                    any = true;
                }
            if (!any) b.AddItem(ControlId.Structural(k + "stash:empty"), GraphNodes.Text(() => Loc.T("inv.no_items")));
            b.PopContext();
        }
    }
}
