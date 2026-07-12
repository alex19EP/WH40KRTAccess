using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.AreaLogic.TimeSurvival;               // TimeSurvival ("survive N rounds" areas)
using Kingmaker.Blueprints;                           // BlueprintScriptableObject.GetComponent<T>()
using Kingmaker.Code.UI.MVVM.VM.ActionBar;            // ActionBarSlotVM (ship weapons/abilities are bar slots)
using Kingmaker.Code.UI.MVVM.VM.SpaceCombat.Components; // ShipPostVM
using Warhammer.SpaceCombat.StarshipLogic.Posts;      // Post (officer / skill / blocked state)
using Kingmaker.Code.UI.MVVM.VM.Space;                // SpaceStaticComponentType
using Kingmaker.Code.UI.MVVM.VM.SpaceCombat;          // SpaceCombatVM (+ service panel)
using Kingmaker.EntitySystem.Entities;                // StarshipEntity
using Kingmaker.EntitySystem.Stats.Base;              // StatType (per-sector armour)
using Kingmaker.SpaceCombat.StarshipLogic.Parts;      // PartStarshipNavigation, StarshipSectorShieldsType
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;                                    // Mathf

namespace RTAccess.Screens
{
    /// <summary>
    /// Voidship (space) combat as a navigable base context — the SpaceCombat-mode sibling of
    /// <see cref="InGameScreen"/> / <see cref="SystemMapScreen"/>. Phase 1 of
    /// docs/plans/inertial-broadsiding-tsiolkovsky.md: FOLLOW the battle — two Tab stops mirroring the
    /// sighted HUD's chrome. <b>Ship</b> — the player ship's panel block (hull, the four sector shields +
    /// armour, speed mode + movement budget + facing, crew/morale/military rating, active effects).
    /// <b>Battle</b> — round (+ the TimeSurvival rounds-left counter), the End-turn control, the
    /// still-have-actions nudge, and the initiative order rendered through
    /// <see cref="InGameScreen.InitiativeLabel"/> so both trackers read alike. Weapons / posts / aiming
    /// are Phases 3–4. Phase 2 (move the ship) declares the Exploration input category like
    /// <see cref="InGameScreen"/>: the tile cursor scans the battle grid, Backspace arms/commits the
    /// guarded ship move (previewed via <see cref="RTAccess.Exploration.ShipPathInfo"/> — cost, arrival
    /// facing, stop legality), Z speaks the end-position fan, and Semicolon reads the ship-path verdict
    /// at the cursor. Chord shadowing mirrors the surface screen: bare keys go to the grid while the HUD
    /// is unfocused, Tab enters the zones and flips priority to ui.*.
    ///
    /// ACTIVE exactly while the game's own space-combat HUD component exists:
    /// <c>SpaceStaticPartVM.CreateVMs</c> skips unmapped modes (Dialog/Cutscene/Pause return a null
    /// component list), so the component — and this screen — survive a conversation layered over the
    /// battle and are swapped out when a mapped mode (StarSystem/GlobalMap/GameOver) takes over. That is
    /// the same lifetime the sighted HUD has, and it keeps this context mutually exclusive with
    /// <see cref="SystemMapScreen"/> even for in-system random-encounter fights (whose AREA stays a
    /// BlueprintStarSystemMap — the area type can't be the gate).
    /// </summary>
    public sealed class SpaceCombatScreen : Screen
    {
        public override string Key => "ctx.spacecombat";
        public override string ScreenName => Loc.T("spacecombat.screen");
        public override int Layer => 0;                     // base context, sibling of ctx.ingame / ctx.systemmap
        public override bool StartUnfocused => true;        // the game keeps the keys; Tab enters the HUD zones

        public override bool IsActive() => Component() != null;

        // Same category flip as InGameScreen: with world control and the HUD unfocused the Exploration
        // set (tile cursor / scanner / move verbs) leads and shadows the ui.* arrows; focusing the HUD
        // reverses the priority so arrows browse the zones. A dialog/cutscene layered over the battle
        // drops world control (ClickEventsController unregisters) and with it every world verb.
        private static readonly RTAccess.Input.InputCategory[] FocusedCats =
        {
            RTAccess.Input.InputCategory.UI, RTAccess.Input.InputCategory.InGame,
            RTAccess.Input.InputCategory.Exploration, RTAccess.Input.InputCategory.Windows,
        };
        private static readonly RTAccess.Input.InputCategory[] UnfocusedCats =
        {
            RTAccess.Input.InputCategory.Exploration, RTAccess.Input.InputCategory.InGame,
            RTAccess.Input.InputCategory.UI, RTAccess.Input.InputCategory.Windows,
        };
        private static readonly RTAccess.Input.InputCategory[] NoControlCats =
        {
            RTAccess.Input.InputCategory.InGame, RTAccess.Input.InputCategory.UI,
        };
        public override System.Collections.Generic.IReadOnlyList<RTAccess.Input.InputCategory> InputCategories =>
            !ControlState.HasControl ? NoControlCats
          : Navigation.HasFocus     ? FocusedCats
          :                           UnfocusedCats;

        /// <summary>The game's live space-combat HUD component, or null — the screen's activation gate,
        /// and the VM root every zone reads. Also consulted by <see cref="SystemMapScreen.IsActive"/> for
        /// mutual exclusion (an in-system encounter keeps the star-system AREA while this component swaps in).</summary>
        internal static SpaceCombatVM Component()
        {
            try
            {
                return Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?
                    .TryGetComponentVM(SpaceStaticComponentType.SpaceCombat) as SpaceCombatVM;
            }
            catch { return null; }
        }

        // ---- the graph ----

        public override void Build(GraphBuilder b)
        {
            var vm = Component();
            var ship = Game.Instance?.Player?.PlayerShip;
            if (vm == null || ship == null) return;

            // -- Ship (the sighted HUD's ship block: hull slider + shield diamond + crew bar) --
            b.BeginStop("ship").PushContext(Loc.T("spacecombat.ship"), Loc.T("role.list"));
            b.AddLabel(ControlId.Structural("ship:hull"), () => HullLine(ship));
            b.AddLabel(ControlId.Structural("ship:shields"), () => ShieldsLine(ship));
            b.AddLabel(ControlId.Structural("ship:armour"), () => ArmourLine(ship));
            b.AddLabel(ControlId.Structural("ship:speed"), () => SpeedLine(ship));
            b.AddLabel(ControlId.Structural("ship:crew"), () => CrewLine(ship));
            b.AddLabel(ControlId.Structural("ship:effects"), () => EffectsLine(ship));
            b.PopContext();

            // -- Weapons (the sighted weapons panel's four arc groups; Phase 3). The slots are the game's
            // own ActionBarSlotVMs, so the shared factory carries the whole contract — availability-first
            // readout, the game's why-not reason, arsenal variants via the convert flyout, tooltip on Space —
            // and Enter arms the weapon into the aim flow (Targeting announces the controls; the scanner's
            // Period cycle + the tile cursor then read hit/shield/hull odds via StarshipAim).
            var wp = vm.ShipWeaponsPanelVM;
            if (wp?.WeaponAbilitiesGroups != null)
            {
                b.BeginStop("weapons").PushContext(Loc.T("spacecombat.weapons"), Loc.T("role.list"));
                int wi = 0;
                foreach (var kv in wp.WeaponAbilitiesGroups)
                {
                    var group = kv.Value;
                    if (group?.Slots == null) continue;
                    for (int i = 0; i < group.Slots.Count; i++)
                        if (UsableSlot(group.Slots[i]))
                            AddShipSlot(b, group.Slots[i], "weapons", wi++, group.GroupLabel);
                }
                b.PopContext();

                // -- Ship abilities (Reload Shields, Ram, Swing Run, torpedo control, …) — the panel's
                // non-weapon group. Custom-path movement abilities (Ram/Swing Run) arm like any other;
                // their cell-target commit flow is verified live in Phase 3's aim pass.
                var ag = wp.AbilitiesGroup;
                if (ag?.Slots != null && ag.Slots.Count > 0)
                {
                    b.BeginStop("abilities").PushContext(Loc.T("spacecombat.abilities"), Loc.T("role.list"));
                    for (int i = 0; i < ag.Slots.Count; i++)
                        if (UsableSlot(ag.Slots[i]))
                            AddShipSlot(b, ag.Slots[i], "abilities", i, null);
                    b.PopContext();
                }
            }

            // -- Posts (Phase 4: the six bridge stations of the sighted posts panel). Each post reads as a
            // header line — post name, officer, skill value, blocked/penalty state — followed by that
            // officer's post abilities as ordinary slots (same aim flow as weapons). The header reads the
            // live Post part, so a mid-battle block (boarding, fire) re-reads truthfully.
            var posts = vm.ShipPostsPanelVM;
            if (posts?.Posts != null && posts.Posts.Count > 0)
            {
                b.BeginStop("posts").PushContext(Loc.T("spacecombat.posts"), Loc.T("role.list"));
                for (int p = 0; p < posts.Posts.Count; p++)
                {
                    var pvm = posts.Posts[p];
                    if (pvm == null) continue;
                    var captured = pvm; // loop-local for the closure
                    b.AddLabel(ControlId.Structural("posts:head:" + p), () => PostLine(captured));
                    var slots = pvm.AbilitiesGroup?.Slots;
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Count; i++)
                        if (UsableSlot(slots[i]))
                            AddShipSlot(b, slots[i], "posts:" + p, i, null);
                }
                b.PopContext();
            }

            // -- Battle (service panel: round/turn state, End turn, initiative order) --
            b.BeginStop("battle").PushContext(Loc.T("spacecombat.battle"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("battle:status"), GraphNodes.Text(() => BattleStatusLine(ship)));

            // End turn — the surface HUD's exact node (TryEndPlayerTurnManually plays the game's own
            // end-turn sting, so no ActivateSound). While the ship still MUST move (forced movement:
            // PartUnitCombatState.CanEndTurn's starship branch) the button reads ", disabled" and the
            // status line above carries the why ("must keep moving").
            var game = Game.Instance;
            Func<string> endTurnLabel = () => GameText.Or(
                () => Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.HUDTexts.EndTurn, "turn.end");
            // The "you still have moves" nudge the sighted panel pulses on the end-turn button rides the
            // button itself (Phase 6): focusing End turn speaks "…, N actions still available" LIVE, so
            // spending the last action under focus re-reads — the spoken twin of the pulsing highlight
            // (CombatActionsCount = action holders with anything possible, movement included).
            var svc = vm.SpaceCombatServicePanelVM;
            b.AddItem(ControlId.Structural("battle:endturn"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(endTurnLabel),
                    GraphNodes.DisabledPart(() => game.TurnController.CanEndTurn),
                    new NodeAnnouncement(() => svc != null && svc.IsPlayerTurn.Value && svc.CombatActionsCount.Value > 0
                        ? Loc.T("spacecombat.actions_left", new { n = svc.CombatActionsCount.Value })
                        : null, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = endTurnLabel,
                OnActivate = () => { if (game.TurnController.CanEndTurn) game.TurnController.TryEndPlayerTurnManually(); },
                ActivateSound = null,
            });

            // Initiative order — the shared tracker VM, rendered through the surface recipe so both
            // trackers read alike (faction, HP with the HideRealHealthInUI mask, current/order markers,
            // the next-round divider). Rows are keyed by unit, so focus follows a ship as the order shifts.
            var tracker = svc?.InitiativeTrackerVM?.Value;
            if (tracker?.Units != null)
            {
                var units = tracker.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    var row = units[i];
                    if (row == null) continue;
                    if (i == tracker.RoundIndex + 1)
                        b.AddItem(ControlId.Structural("battle:round"), GraphNodes.Text(
                            () => InGameScreen.RoundDividerLabel(Component()?.SpaceCombatServicePanelVM?.InitiativeTrackerVM?.Value)));
                    if (row.IsInSquad.Value && !row.IsSquadLeader.Value && !row.NeedToShow.Value) continue;
                    var vmRow = row; // loop-local for the closure
                    b.AddItem(ControlId.Referenced(vmRow, "battle:init:" + (vmRow.Unit?.UniqueId ?? "slot" + i)),
                        GraphNodes.Text(() => InGameScreen.InitiativeLabel(vmRow)));
                }
            }

            b.AddItem(ControlId.Structural("battle:log"), GraphNodes.Button(
                () => Loc.T("hud.log"), LogReviewScreen.Open));
            b.PopContext();
        }

        // ---- Weapon / ability slots ----

        // One slot + its open variant flyout rows (the InGameScreen action-bar recipe: converted rows render
        // right after their parent while the flyout is open — that is how an arsenal's weapon variants switch,
        // MechanicActionBarShipWeaponSlot.GetConvertedAbilityData feeds the same flyout). The ship-specific
        // part (arc + burst size) is spoken after the availability reason: a ship weapon's tactical identity
        // is WHICH ARC it bears on, so it comes before the generic detail.
        private static void AddShipSlot(GraphBuilder b, ActionBarSlotVM slot, string zone, int index, string arcLabel)
        {
            var vt = UI.ActionBarNodes.Slot(slot);
            // Announcements is an IReadOnlyList field — rebuild it with the ship part slotted in ahead of
            // the factory's detail part (same Value kind, so ActionSlot's kind order keeps it right there).
            var parts = new List<NodeAnnouncement>(vt.Announcements);
            parts.Insert(3, new NodeAnnouncement(() => ShipSlotPart(slot, arcLabel), kind: AnnouncementKinds.Value));
            vt.Announcements = parts;
            b.AddItem(ControlId.Referenced(slot, zone + ":slot:" + index), vt);

            var conv = slot.ConvertedVm?.Value;
            if (conv == null || conv.IsDisposed) return;
            for (int j = 0; j < conv.Slots.Count; j++)
                if (UsableSlot(conv.Slots[j]))
                    b.AddItem(ControlId.Referenced(conv.Slots[j], zone + ":var:" + index + ":" + j),
                        UI.ActionBarNodes.Slot(conv.Slots[j]));
        }

        // Arc (the game's own localized group label — passed through, never re-translated) + shots per salvo
        // (DamageInstances — the count the sighted overtip shows; ship bursts share one hit chance).
        private static string ShipSlotPart(ActionBarSlotVM slot, string arcLabel)
        {
            try
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(arcLabel)) sb.Append(arcLabel);
                int shots = slot?.AbilityData?.StarshipWeapon?.Blueprint?.DamageInstances ?? 0;
                if (shots > 1)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Loc.T("spacecombat.shots", new { n = shots }));
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        // One post header: "Master Helmsman, Abelard, Pilot 45[, blocked, 2 rounds][, penalty]" —
        // what the sighted panel shows as portrait + skill badge + the lock overlay. The post name is
        // position-keyed in the game's own strings (UIStrings.SpaceCombatTexts.PostStrings[index], the
        // ShipCustomizationScreen recipe); everything else reads the live Post part (the VM's m_Post,
        // reachable thanks to the publicized reference assemblies).
        private static string PostLine(ShipPostVM pvm)
        {
            try
            {
                var sb = new StringBuilder();
                string title = null;
                try { title = Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.SpaceCombatTexts.GetPostStrings(pvm.Index).Title?.Text; }
                catch { }
                sb.Append(!string.IsNullOrEmpty(title) ? title : Loc.T("ship.post_n", new { index = pvm.Index + 1 }));

                var post = pvm.m_Post;
                if (post?.CurrentUnit != null)
                {
                    sb.Append(", ").Append(post.CurrentUnit.CharacterName).Append(", ");
                    string skill = null;
                    try { skill = Kingmaker.Blueprints.Root.LocalizedTexts.Instance.Stats.GetText(post.PostData.AssociatedSkill); }
                    catch { }
                    if (!string.IsNullOrEmpty(skill)) sb.Append(skill).Append(' ');
                    sb.Append(post.CurrentSkillValue);
                }
                else sb.Append(", ").Append(Loc.T("spacecombat.post_vacant"));

                if (pvm.IsPostBlocked.Value)
                {
                    sb.Append(", ").Append(Loc.T("spacecombat.post_blocked"));
                    string dur = pvm.BlockDuration.Value;
                    if (!string.IsNullOrEmpty(dur) && dur != "0")
                        sb.Append(", ").Append(Loc.T("spacecombat.post_blocked_rounds", new { n = dur }));
                }
                if (post != null && post.CurrentUnit != null && post.HasPenalty)
                    sb.Append(", ").Append(Loc.T("spacecombat.post_penalty"));
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.PostLine: " + e); return ""; }
        }

        /// <summary>The game's localized name for a player-ship post (position-keyed strings), for event
        /// cues (see <see cref="Accessibility.SpaceCombatEvents"/>). Falls back to the numbered post.</summary>
        internal static string PostTitle(Post post)
        {
            try
            {
                var hullPosts = Game.Instance?.Player?.PlayerShip?.Hull?.Posts;
                int idx = hullPosts != null ? hullPosts.IndexOf(post) : -1;
                if (idx >= 0)
                {
                    var t = Kingmaker.Blueprints.Root.Strings.UIStrings.Instance.SpaceCombatTexts.GetPostStrings(idx).Title?.Text;
                    if (!string.IsNullOrEmpty(t)) return t;
                    return Loc.T("ship.post_n", new { index = idx + 1 });
                }
            }
            catch { }
            return Loc.T("spacecombat.posts");
        }

        private static bool UsableSlot(ActionBarSlotVM s)
            => s != null && !s.IsFake.Value && !s.IsEmpty.Value
               && s.MechanicActionBarSlot != null && !s.MechanicActionBarSlot.IsBad();

        // Close any open variant flyout across both slot groups (Escape's first job, like the sighted
        // ActionBarConvertedPCView / InGameScreen.CloseOpenConverts).
        private static bool CloseOpenConverts()
        {
            bool any = false;
            var wp = Component()?.ShipWeaponsPanelVM;
            if (wp == null) return false;
            foreach (var kv in wp.WeaponAbilitiesGroups)
                foreach (var s in kv.Value.Slots)
                {
                    var c = s?.ConvertedVm?.Value;
                    if (c != null && !c.IsDisposed) { c.Close(); any = true; }
                }
            foreach (var s in wp.AbilitiesGroup.Slots)
            {
                var c = s?.ConvertedVm?.Value;
                if (c != null && !c.IsDisposed) { c.Close(); any = true; }
            }
            return any;
        }

        // ---- Ship lines (read the parts directly; the panel VMs are orphaned recipes — plan §1.5) ----

        private static string HullLine(StarshipEntity ship)
        {
            try
            {
                return Loc.T("spacecombat.hull", new
                {
                    name = ship.CharacterName,
                    cur = ship.Health?.HitPointsLeft ?? 0,
                    max = ship.Health?.MaxHitPoints ?? 0,
                });
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.HullLine: " + e); return ""; }
        }

        private static string ShieldsLine(StarshipEntity ship)
        {
            try
            {
                var sh = ship.Shields;
                if (sh == null) return "";
                string One(StarshipSectorShieldsType sector)
                {
                    var s = sh.GetShields(sector);
                    return s == null ? "0" : Loc.T("spacecombat.of", new { cur = s.Current, max = s.Max });
                }
                return Loc.T("spacecombat.shields", new
                {
                    fore = One(StarshipSectorShieldsType.Fore),
                    port = One(StarshipSectorShieldsType.Port),
                    starboard = One(StarshipSectorShieldsType.Starboard),
                    aft = One(StarshipSectorShieldsType.Aft),
                });
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.ShieldsLine: " + e); return ""; }
        }

        private static string ArmourLine(StarshipEntity ship)
        {
            try
            {
                return Loc.T("spacecombat.armour", new
                {
                    fore = ship.Stats.GetStat(StatType.ArmourFore)?.ModifiedValue ?? 0,
                    port = ship.Stats.GetStat(StatType.ArmourPort)?.ModifiedValue ?? 0,
                    starboard = ship.Stats.GetStat(StatType.ArmourStarboard)?.ModifiedValue ?? 0,
                    aft = ship.Stats.GetStat(StatType.ArmourAft)?.ModifiedValue ?? 0,
                });
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.ArmourLine: " + e); return ""; }
        }

        // Speed mode + movement budget + facing. Movement is the blue-AP pool (the sighted HUD's
        // CombatMovementActionHint number); facing via the shared 8-way compass, +z = north like the
        // tile cursor.
        private static string SpeedLine(StarshipEntity ship)
        {
            try
            {
                var nav = ship.Navigation;
                var cs = ship.CombatState;
                var sb = new StringBuilder(Loc.T("spacecombat.speed", new
                {
                    mode = SpeedModeWord(nav?.SpeedMode ?? PartStarshipNavigation.SpeedModeType.Normal),
                    mp = Mathf.RoundToInt(cs?.ActionPointsBlue ?? 0f),
                    max = Mathf.RoundToInt(cs?.ActionPointsBlueMax ?? 0f),
                }));
                if (Exploration.Geo.CompassSector(ship.Forward.x, ship.Forward.z, out int sector))
                    sb.Append(", ").Append(Loc.T("spacecombat.facing",
                        new { dir = Loc.T(Accessibility.InteractableDescriber.Compass8[sector]) }));
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.SpeedLine: " + e); return ""; }
        }

        internal static string SpeedModeWord(PartStarshipNavigation.SpeedModeType mode)
        {
            switch (mode)
            {
                case PartStarshipNavigation.SpeedModeType.Deccelerating: return Loc.T("spacecombat.speed_decelerating");
                case PartStarshipNavigation.SpeedModeType.LowSpeed: return Loc.T("spacecombat.speed_low");
                case PartStarshipNavigation.SpeedModeType.FullStop: return Loc.T("spacecombat.speed_full_stop");
                default: return Loc.T("spacecombat.speed_normal");
            }
        }

        private static string CrewLine(StarshipEntity ship)
        {
            try
            {
                int moralePct = ship.Morale != null && ship.Morale.MaxMorale != 0
                    ? Mathf.RoundToInt(100f * ship.Morale.MoraleLeft / ship.Morale.MaxMorale) : 0;
                return Loc.T("spacecombat.crew", new
                {
                    crew = ship.Crew?.Count ?? 0,
                    morale = moralePct,
                    rating = ship.Hull?.CurrentMilitaryRating ?? 0,
                });
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.CrewLine: " + e); return ""; }
        }

        private static string EffectsLine(StarshipEntity ship)
        {
            try
            {
                var names = new List<string>();
                foreach (var buff in ship.Buffs.Enumerable)
                    if (buff != null && !buff.Hidden) names.Add(buff.Name);
                return names.Count == 0
                    ? Loc.T("spacecombat.effects_none")
                    : Loc.T("spacecombat.effects", new { list = string.Join(", ", names) });
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.EffectsLine: " + e); return ""; }
        }

        // ---- Battle status ----

        // "Round 3, 5 rounds left, your turn: Righteous Absolution, must keep moving, 7 movement left".
        // The must-move tail mirrors the forced-movement law (CanEndTurn's starship branch): budget still
        // at/above the finishing window AND somewhere to go — the single most confusing space-combat state
        // for a blind player ("why won't my turn end?").
        private static string BattleStatusLine(StarshipEntity ship)
        {
            try
            {
                var game = Game.Instance;
                var tc = game?.TurnController;
                if (tc == null || !tc.TurnBasedModeActive) return Loc.T("spacecombat.no_battle");

                var sb = new StringBuilder(Loc.T("combat.round", new { round = tc.CombatRound }));

                var ts = game.CurrentlyLoadedArea?.GetComponent<TimeSurvival>();
                if (ts != null && !ts.UnlimitedTime)
                    sb.Append(", ").Append(Loc.T("spacecombat.rounds_left", new { n = ts.RoundsLeft }));

                var cur = tc.CurrentUnit;
                if (cur != null)
                {
                    string name = (cur as Kingmaker.Mechanics.Entities.AbstractUnitEntity)?.CharacterName ?? cur.Name;
                    sb.Append(", ").Append(Loc.T(
                        tc.IsPlayerTurn ? "combat.turn" : "combat.turn_enemy", new { name }));
                }

                var nav = ship.Navigation;
                if (tc.IsPlayerTurn && ReferenceEquals(tc.CurrentUnit, ship) && nav != null
                    && ship.CombatState.ActionPointsBlue >= nav.FinishingTilesCount && nav.HasAnotherPlaceToStand)
                    sb.Append(", ").Append(Loc.T("spacecombat.must_move",
                        new { mp = Mathf.RoundToInt(ship.CombatState.ActionPointsBlue) }));

                // The dead-end state (nowhere standable ahead with budget left): the game swaps the reachable
                // set for 1-tile escape moves — without this line the tiny fan reads like a movement bug.
                if (tc.IsPlayerTurn && game.StarshipPathController.IsCurrentShipInDeadEnd)
                    sb.Append(", ").Append(Loc.T("spacecombat.dead_end"));

                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("SpaceCombatScreen.BattleStatusLine: " + e); return ""; }
        }

        // ---- input ----

        // Escape while focused backs out of the zones to the bare battle (keys return to the game);
        // while unfocused the yield hands Escape to the game (cancel targeting / pause menu).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Back"), _ =>
            {
                if (!Navigation.HasFocus) return;
                // An open variant/convert flyout claims Escape first — mirroring the sighted
                // ActionBarConvertedPCView (same rule as InGameScreen's action bar).
                if (CloseOpenConverts())
                {
                    Tts.Speak(Loc.T("slot.variants_closed"), interrupt: true);
                    return;
                }
                Navigation.Blur();
                Tts.Speak(Loc.T("spacecombat.screen"), interrupt: true);
            });
        }
    }
}
