# Combat accessibility — verified-API appendix

Companion to `phased-skirmishing-liskov.md`. Raw output of an 8-agent audit of the decompiled RT source (Code.dll + RogueTrader.GameCore.dll), one section per combat facet: verified game APIs (with decompiled file/line), what the mod already builds, the gaps, a recommended approach, risks, and open questions. Cite this for exact signatures; re-verify live before relying on any API.


################################################################
## AREA: Turn lifecycle, initiative & combat state   [effort: M]
################################################################

### SUMMARY
RT's turn engine is fully event-driven on the game's EventBus: TurnController (Game.Instance.TurnController) owns whose-turn / round / TB-mode state, and raises well-typed handlers on combat-enter/leave, round-start/end, per-unit turn-start/end, and deployment. RTAccess already reads all the state statically (InGameScreen Status + Combat regions, End-turn button) but subscribes to NONE of the turn-lifecycle events, so a blind player gets zero automatic cues — no "combat started", no "your turn"/"enemy turn", no round announce. The core gap is a thin EventBus subscriber that turns those existing handlers into spoken cues; the data and act-APIs are all present and verified.

### VERIFIED GAME APIs
- **TurnController (Game.Instance.TurnController)** (OK) @ Kingmaker.Controllers.TurnBased/TurnController.cs
    Central turn state. Verified members: TurnBasedModeActive/TbActive/InCombat (bool), IsPlayerTurn (bool, l.109), IsAiTurn (l.125), CurrentUnit (MechanicEntity CanBeNull, l.206 = TurnOrder.CurrentUnit), CombatRound (int; 0 out of combat, 1 = first round, l.210), GameRound (int, l.222), CanEndTurn (bool, l.242), EndTurnRequested/EndingTurn (l.224-226), IsPreparationTurn/IsRoamingTurn/IsManualCombatTurn (l.185-189), IsUltimateAbilityUsedThisRound (l.147), UnitsAndSquadsByInitiativeForCurrentTurn / ...ForNextTurn (IEnumerable<MechanicEntity>, l.161/173), TurnOrder (TurnOrderQueue, l.159).
- **TurnController.TryEndPlayerTurnManually()** (OK) @ Kingmaker.Controllers.TurnBased/TurnController.cs:555
    Gated on IsPlayerTurn + CanEndTurn; plays EndTurn sound and queues Game.Instance.GameCommandQueue.EndTurnManually(CurrentUnit). Already wired to the End-turn proxy in InGameScreen. Also RequestEndTurn() (l.610) and static [Cheat] TryEndPlayerTurnStatic() (l.1091).
- **ITurnBasedModeHandler.HandleTurnBasedModeSwitched(bool isTurnBased)** (OK) @ Kingmaker.Controllers.TurnBased/ITurnBasedModeHandler.cs; raised TurnController.cs:376 (EnterTb, true) and :414 (ExitTb, false)
    Global (ISubscriber). Fires exactly on TB-combat enter/leave. Primary combat-start/combat-end signal at the TB-mode level.
- **IPartyCombatHandler.HandlePartyCombatStateChanged(bool inCombat)** (OK) @ Kingmaker.PubSubSystem.Core/IPartyCombatHandler.cs; raised Kingmaker.Controllers.Combat/UnitCombatJoinController.cs:46,78 and UnitCombatLeaveController.cs:70
    Global. Fires when Game.Instance.Player.IsInCombat toggles (party enters/leaves combat overall). Semantic 'combat started/ended' cue; already consumed by pause-button/NecronTimer/PartyCharacterVM. Cleaner than TB-switch for a party-facing 'Combat started' line.
- **IRoundStartHandler.HandleRoundStart(bool isTurnBased) / IRoundEndHandler.HandleRoundEnd(bool isTurnBased,bool isFirst)** (OK) @ Kingmaker.Controllers.TurnBased/IRoundStartHandler.cs, IRoundEndHandler.cs; raised TurnController.NextRound() l.638/648
    Global. WARNING: NextRound is ALSO called out of combat by TickRoundRT->NextRoundRT every ~5s (l.502-508,654-663). Must gate on isTurnBased AND TurnBasedModeActive or you spam 'Round N' while exploring. Read Game.Instance.TurnController.CombatRound for the number.
- **ITurnStartHandler.HandleUnitStartTurn(bool isTurnBased) / ITurnEndHandler.HandleUnitEndTurn(bool)** (OK) @ Kingmaker.Controllers.TurnBased/ITurnStartHandler.cs, ITurnEndHandler.cs; raised TurnController.StartUnitTurn l.723 / EndUnitTurn l.831
    Entity-targeted (ISubscriber<IMechanicEntity>). CurrentUnit is already set before HandleUnitStartTurn is raised, so a global subscriber can just read TurnController.CurrentUnit + IsPlayerTurn to branch your-turn vs enemy-turn. Also IContinueTurnHandler.HandleUnitContinueTurn(bool) (l.712) for resumed turns, and interrupt-turn handlers IInterruptTurnStartHandler/Continue/End (l.697-813).
- **IPreparationTurnBeginHandler.HandleBeginPreparationTurn(bool canDeploy) / IPreparationTurnEndHandler.HandleEndPreparationTurn()** (OK) @ Kingmaker.Controllers.TurnBased/IPreparationTurnBeginHandler.cs, IPreparationTurnEndHandler.cs; raised TurnController.BeginPreparationTurn l.1141 / ForceEndPreparationTurn l.1177
    Global. The deployment phase before round 1 (non-space combat, TurnController.NeedDeploymentPhase). canDeploy = IsDeploymentAllowed. During prep CurrentUnit is null (TurnOrderQueue.NextTurn returns null for Preparation), so turn-start won't fire — use this to announce the deployment phase.
- **IUnitCombatHandler.HandleUnitJoin/LeaveCombat() + IAnyUnitCombatHandler.HandleUnitJoinCombat(BaseUnitEntity)** (OK) @ Kingmaker.PubSubSystem/IUnitCombatHandler.cs; raised PartUnitCombatState.JoinCombat l.348/362, LeaveCombat l.392/396
    Per-unit (entity-targeted) and a global IAnyUnitCombatHandler variant carrying the BaseUnitEntity. Useful for 'X joined the fight' mid-combat reinforcement cues.
- **PartUnitCombatState (MechanicEntity.GetCombatStateOptional())** (OK) @ Kingmaker.Controllers.Combat/PartUnitCombatState.cs
    Per-unit turn data. Verified: ActionPointsYellow (int AP, l.105), ActionPointsBlue (float MP, l.93), ActionPointsBlueMax (l.101), Surprised (bool, l.90 — the surprise concept, set at JoinCombat(surprised)), IsInCombat (l.200), IsEngaged (l.204), InitiativeRoll (l.198), CanEndTurn() (l.437), StartedCombatNearEnemy (l.130). Already used by InGameScreen.StatusLine for AP/MP.
- **Initiative (MechanicEntity.Initiative)** (OK) @ Kingmaker.Controllers.TurnBased/Initiative.cs; MechanicEntity.cs:74
    Per-entity initiative: Roll (float, l.26), Value (float, l.29), Order (int, l.32), TurnOrderPriority (double, l.43), ActedThisRound (bool, l.45 = LastTurn==GameRound), WasPreparedForRound (l.38), InterruptingOrder (l.35). Basis for ordering the tracker.
- **InitiativeTrackerVM (SurfaceHUDVM.InitiativeTrackerVM.Value)** (OK) @ Kingmaker.Code.UI.MVVM.VM.SurfaceCombat/InitiativeTrackerVM.cs
    Units (List<InitiativeTrackerUnitVM>, ordered current-round then next-round), CurrentUnit (ReactiveProperty<InitiativeTrackerUnitVM>, l.44), RoundCounter (IntReactiveProperty, l.42), RoundIndex (int, l.28 — the boundary index between current-round and next-round units in Units; currently UNUSED by the mod), UnitsUpdated (ReactiveCommand), RoundVM (the 'round N+1' divider). Already read by InGameScreen via SurfaceHUD().InitiativeTrackerVM.Value.
- **InitiativeTrackerUnitVM : SurfaceCombatUnitVM** (OK) @ Kingmaker.Code.UI.MVVM.VM.SurfaceCombat/InitiativeTrackerUnitVM.cs
    OrderIndex (IntReactiveProperty), Round (IntReactiveProperty). Base SurfaceCombatUnitVM exposes Unit (MechanicEntity) and IsCurrent (used by InGameScreen.InitiativeLabel).
- **IUnitMissedTurnHandler.HandleOnMissedTurn()** (OK) @ raised TurnController.HandleCurrentUnitUnableToAct l.983
    Entity-targeted; fires when a unit that cannot act (stunned/unconscious) has its turn force-ended. Candidate for a 'turn skipped' cue.
- **CombatTurnType enum** (OK) @ Kingmaker.Controllers.TurnBased/CombatTurnType.cs
    Default, Preparation, ManualCombat, Roaming. NOTE: there is NO 'delay turn' concept in RT — no IDelayTurnHandler / delay API exists anywhere in the decompiled source (searched). Delay-turn from the WOTR facet brief does not apply.

### ALREADY BUILT (seams)
RTAccess reads turn/combat state correctly but purely on-demand (never auto-announced):
- InGameScreen.cs (E:\Games\modding\WH40KRTAccess\RTAccess\Screens\InGameScreen.cs): StatusLine() (l.341) reads AP/MP via GetCombatStateOptional() and appends "<name>'s turn" + "(enemy)" using TurnController.CurrentUnit / IsPlayerTurn. The Combat region (RebuildCombat l.375) shows the status line, an "End turn" ProxyActionButton bound to TurnController.CanEndTurn / TryEndPlayerTurnManually(), and the initiative list built from SurfaceHUD().InitiativeTrackerVM.Value.Units with InitiativeLabel() marking ", current". CombatSig() (l.401) only rebuilds on membership change; labels are live Funcs. This all requires the player to Tab into the HUD and step to the Combat region.
- CombatEvents.cs (E:\Games\modding\WH40KRTAccess\RTAccess\Accessibility\CombatEvents.cs): a persistent EventBus subscriber with a per-frame _pending queue (flushed in Tick from Main.OnUpdate) that voices damage/heal/death/downed/buff. It implements IDamageHandler/IHealingHandler/IUnitDeathHandler/IUnitBuffHandler ONLY — no turn-lifecycle interfaces. This is the natural home/pattern for lifecycle cues: its frame-ordered queue keeps a burst like "Round 2 … Argenta's turn" reading as a clean sequence with the trailing damage lines.
- Main.cs registers EventBus subscribers (BarkEvents, CombatEvents, WarningReader, ExplorationEvents, InteractionEvents) at load and unsubscribes at unload — the exact seam to add a lifecycle subscriber.
The seam: add turn-lifecycle handlers (either as new interfaces on CombatEvents, or a sibling subscriber sharing a queue) + optionally enrich InGameScreen's initiative list.

### GAPS
- No automatic 'Combat started' / 'Combat ended' cue. A sighted player sees the HUD swap in; a blind player gets nothing until they Tab into the Combat region. (IPartyCombatHandler / ITurnBasedModeHandler unused.)
- No automatic 'Your turn' / 'Enemy turn: <name>' cue when control passes. This is the single most important combat signal and is completely missing — the player cannot tell when it becomes their turn without polling the Status region. (ITurnStartHandler unused.)
- No round-number announce ('Round 2'). CombatRound is read on-demand only. (IRoundStartHandler unused.)
- No deployment/preparation-phase announce ('Deployment phase — position your party, or start battle'). (IPreparationTurnBeginHandler unused.)
- No next-in-order / 'who acts after me' preview cue. The data exists (UnitsAndSquadsByInitiativeForCurrentTurn, tracker.Units + RoundIndex) but is only reachable by manually stepping the initiative list, which also does not separate this-round from next-round (RoundIndex is ignored) and omits AP / initiative for enemies.
- No 'turn skipped' cue when a stunned/unconscious unit's turn is force-ended (IUnitMissedTurnHandler unused) — the queue silently advances.
- Surprise is invisible: PartUnitCombatState.Surprised is never surfaced, so a blind player is never told they were surprised at combat start.
- Mid-combat reinforcements ('X joined the fight') are not announced (IAnyUnitCombatHandler unused).

### RECOMMENDED APPROACH
Two-part, real-API design.

1) NEW automatic turn-lifecycle cues — fold into the existing per-frame speech queue.
Preferred: extend `RTAccess/Accessibility/CombatEvents.cs` (or add a sibling `Accessibility/CombatLifecycleEvents.cs` that shares the same `_pending`/Enqueue+Tick flush; sharing one queue preserves arrival-order interleave of "unit died / round advanced / your turn"). Add these interfaces + handlers:
- `IPartyCombatHandler.HandlePartyCombatStateChanged(bool inCombat)` → Enqueue "Combat started" / "Combat ended". (This is the party-level truth used by the rest of the HUD.)
- `IRoundStartHandler.HandleRoundStart(bool isTurnBased)` → **gate**: `if (!isTurnBased || !Game.Instance.TurnController.TurnBasedModeActive) return;` then Enqueue "Round " + TurnController.CombatRound. (Prevents the out-of-combat 5s real-time round ticks from spamming.)
- `ITurnStartHandler.HandleUnitStartTurn(bool isTurnBased)` → `if(!isTurnBased) return;` read `var tc = Game.Instance.TurnController; var u = tc.CurrentUnit;` (already set before the raise). If `tc.IsPlayerTurn` → "Your turn: " + name (optionally + AP/MP from GetCombatStateOptional()); else → name + "'s turn" (+ " (enemy)" when !IsPlayerTurn). Reuse InGameScreen's NameOf pattern (AbstractUnitEntity.CharacterName ?? Name). Subscribe globally and read CurrentUnit rather than the event invoker — mirrors how InitiativeTrackerVM handles it.
- `IPreparationTurnBeginHandler.HandleBeginPreparationTurn(bool canDeploy)` → Enqueue "Deployment phase" (+ ", position your party" when canDeploy). Optionally `IPreparationTurnEndHandler` → "Battle begins".
- Optional: `IUnitMissedTurnHandler.HandleOnMissedTurn()` → "<name>'s turn skipped"; `IAnyUnitCombatHandler.HandleUnitJoinCombat(BaseUnitEntity)` for reinforcements.
Speech policy per [rt-interrupt-speech-rule]: these are automatic/event, so Enqueue through the frame queue with interrupt:false (keeps clean ordering after damage lines). If live testing shows "Your turn" is buried behind long enemy log bursts, promote just the player-turn line to interrupt:true — flag as a tuning decision. Register the subscriber in Main.cs next to CombatEvents (Subscribe at load, Unsubscribe at unload).

2) On-demand enrichment of the initiative readout (InGameScreen.cs, Combat region).
- Use `InitiativeTrackerVM.RoundIndex` to insert a "next round" divider when building the list in RebuildCombat (l.375): entries with tracker index > RoundIndex are next-round. Add `RoundCounter.Value` as a header ("Round N").
- Enrich InitiativeLabel (l.413): append AP (`u.Unit.GetCombatStateOptional()?.ActionPointsYellow`) and ", surprised" when Surprised, and keep ", current" for CurrentUnit. Optionally add a dedicated "Next up" element reading UnitsAndSquadsByInitiativeForCurrentTurn.Skip-past-current.
- Add CombatRound to CombatSig() only if you add static round text; the live-Func labels already track CurrentUnit without a rebuild, so no structural change is required for the current-marker.

Everything above is entity-agnostic reads on Game.Instance.TurnController + SurfaceHUDVM.InitiativeTrackerVM (both already resolved in InGameScreen), so no new game-access plumbing is needed.

### RISKS
- IRoundStartHandler/IRoundEndHandler fire out of combat too (real-time round tick every ~5s via TickRoundRT->NextRoundRT->NextRound). MUST gate on isTurnBased AND TurnBasedModeActive or the player hears 'Round N' constantly while exploring. Verified in TurnController.cs l.502-508 / 636-663.
- ITurnStartHandler fires once per unit each round, including every AI unit. Long enemy phases with many units could be chatty. Mitigation: announce player-turn transitions fully, and for AI either announce succinctly or only the first enemy after control leaves the party. Needs live tuning.
- Do NOT use ITurnBasedModeStartHandler.HandleTurnBasedModeStarted for 'combat started' — it is raised on every TurnController.OnStart (area load / save-load while already in combat, l.296), not on combat begin. Use IPartyCombatHandler or ITurnBasedModeHandler.HandleTurnBasedModeSwitched.
- CurrentUnit can be a UnitSquad (enemy squads) or an InitiativePlaceholderEntity/MeteorStreamEntity, not a BaseUnitEntity — NameOf must fall back to .Name (InGameScreen already does). Squad/placeholder naming may read poorly.
- Interrupt policy tension: queuing 'Your turn' keeps ordering but may delay the most time-critical cue behind a damage burst; interrupting it fixes latency but can clip a death line. This is a genuine UX trade-off to settle in-game.
- Roaming turn (CombatTurnType.Roaming) is a brief real-time window inside combat where CurrentUnit is null and no ITurnStartHandler fires; ensure handlers null-check CurrentUnit.

### OPEN QUESTIONS
- How verbose should enemy turn-start cues be? Announce every AI unit, only the first enemy of a phase, or suppress AI names entirely and just say 'Enemy turn'? Needs playtesting with a real encounter.
- Should 'Your turn' interrupt current speech (lowest latency) or queue (clean ordering)? Decide against a live combat with a busy combat log.
- Confirm CombatRound value at the exact moment HandleRoundStart fires (NextRound increments Data.CombatRound before raising IRoundStartHandler, so it should read the NEW round — verify live that 'Round 1' is announced on first combat round, not 'Round 0').
- Is there a meaningful 'surprise round' distinct from the deployment/Preparation phase, or is surprise purely the per-unit PartUnitCombatState.Surprised flag consumed during deployment? Verify what the sighted UI shows on a surprise start.
- For squads/placeholder initiative entries, what naming reads acceptably to a blind player? May need a squad-aware label.


################################################################
## AREA: Action economy & the action bar (abilities/weapons/psychic)   [effort: M]
################################################################

### SUMMARY
RT's action economy is two pools on PartUnitCombatState — integer ActionPointsYellow (AP, spent by abilities/attacks) and float ActionPointsBlue (MP, spent by movement) — and the action bar is a set of ActionBarSlotVM objects each wrapping a MechanicActionBarSlot (which for abilities/items carries an AbilityData). Every fact a blind player needs (AP cost, range, cooldown, charges/uses, why-can't-use, and self-vs-targeted) is exposed on AbilityData / the slot VM. RTAccess already enumerates usable slots (ProxyActionBarSlot) and reads AP cost/ammo/cooldown/targeting-active, but the core gaps are: no range, no charges/uses, no unavailability reason on disabled slots, no self-vs-needs-target signal, and activating a targeted ability drops the player into the game's mouse-driven targeting pointer with no accessible follow-through.

### VERIFIED GAME APIs
- **PartUnitCombatState.ActionPointsYellow / ActionPointsBlue / ActionPointsBlueMax / ActionPointsBlueSpentThisTurn / MovedCellsThisTurn** (OK) @ decompiled/Kingmaker.Controllers.Combat/PartUnitCombatState.cs:105,93,102,99,96
    Yellow is int (AP), Blue is float (MP). Read via unit.GetCombatStateOptional() (already used in InGameScreen.cs:355). PrepareForNewTurn (l.460) recalculates them via Rulebook RuleCalculateActionPoints / RuleCalculateMovementPoints; SpendActionPoints/GainYellowPoint/SetActionPoints mutate + raise IUnitSpentActionPoints / IUnitGainActionPoints / IUnitSpentMovementPoints EventBus events (l.584-718).
- **ActionBarSlotVM (reactive mirrors)** (OK) @ decompiled/Kingmaker.Code.UI.MVVM.VM.ActionBar/ActionBarSlotVM.cs:36-114
    ReactiveProperty fields: ActionPointCost(int), ResourceCount/ResourceCost/ResourceAmount(int), AmmoCost(int), IsReload, CurrentAmmo/MaxAmmo, IsOnCooldown, CooldownText(string), IsSelected, IsPossibleActive, IsCanConvert, HasConvert, HasAvailableConvert, IsEmpty, IsFake, Tooltip. MechanicActionBarSlot property (l.114); AbilityData getter (l.118) unwraps ability/item slot. IsHeroicAct (l.134), IsDesperateMeasure (l.136). UpdateResources() (l.211) pulls all of these off the mechanic slot each refresh.
- **ActionBarSlotVM.OnMainClick()** (OK) @ decompiled/Kingmaker.Code.UI.MVVM.VM.ActionBar/ActionBarSlotVM.cs:238
    The activation entry point RTAccess uses. Plays slot sound, routes convert/variants sub-menus (OnShowConvertRequest) for spontaneous/memorized/variant abilities, then calls MechanicActionBarSlot.OnClick(). Also fires OnClickCommand. In MP-coop pings instead of acting if not your net role.
- **MechanicActionBarSlot (abstract)** (OK) @ decompiled/Kingmaker.UI.Models.UnitSettings/MechanicActionBarSlot.cs:27
    GetTitle()/GetDescription() (l.241-243), ActionPointCost() (l.186), GetResource/GetResourceCost/GetResourceAmount (abstract, l.196-200), IsPossibleActive (l.64) & IsPossibleActiveWithoutNetRole (l.77), IsActive() (l.181, toggle state), IsBad() (l.224), IsWeaponAttackThatRequiresAmmo/MaxAmmo/CurrentAmmo (l.202-215), GetTooltipTemplate() (l.300), OnClick() (l.140), WarningMessage(Vector3) (l.172) -> localized refusal text, CanUseIfTurnBased() (l.131) gates on it being your turn + no active command.
- **MechanicActionBarSlotAbility.OnClick() — activation branch** (OK) @ decompiled/Kingmaker.UI.Models.UnitSettings/MechanicActionBarSlotAbility.cs:137
    THE key activation fork. If IsPossibleActive: if Ability.TargetAnchor != AbilityTargetAnchor.Owner -> Game.Instance.SelectedAbilityHandler.SetAbility(Ability) (enters mouse targeting). Else (self/owner) -> UnitCommandsRunner.CancelMoveCommand() then UnitCommandsRunner.TryUnitUseAbility(Ability, Unit) (immediate cast) + raises IAbilityOwnerTargetSelectionHandler. WarningMessage (l.280) returns Ability.GetUnavailableReason(castPosition) for disabled slots. Ability getter (l.33), ActionPointCost()->Ability.CalculateActionPointCost() (l.207).
- **MechanicActionBarSlotActivableAbility.OnClick()** (OK) @ decompiled/Kingmaker.UI.Models.UnitSettings/MechanicActionBarSlotActivableAbility.cs:60
    Toggle abilities (stances/auras). OnClick flips ActivatableAbility.IsOn (immediate, no targeting). IsActive() (l.105) returns IsOn; GetResource() (l.75) returns ActivatableAbility.ResourceCount. GetTitle/GetDescription from ActivatableAbility.
- **AbilityData.CalculateActionPointCost()** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:2131
    Returns 0 if IsFreeAction, else RuleCalculateAbilityActionPointCost.TryGetCachedOrTrigger(this).Result. This is the yellow-AP cost the bar shows (ActionBarSlotVM.ActionPointCost mirrors MechanicActionBarSlotAbility.ActionPointCost() -> this).
- **AbilityData.RangeCells / MinRangeCells** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:376,378
    RangeCells => RuleCalculateAbilityRange.TryGetCachedOrTrigger(this).Result (grid cells). MinRangeCells returns 0 for Personal range else Blueprint.MinRange. NOT currently read by RTAccess.
- **AbilityData.IsOnCooldown / Cooldown** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:799,823
    IsOnCooldown via Caster.GetAbilityCooldownsOptional(). Cooldown => GetAutonomousCooldown(Blueprint) rounds remaining. ActionBarSlotVM.IsOnCooldown/CooldownText already mirror these.
- **AbilityData.GetAvailableForCastCount() / GetResourceCost() / GetResourceAmount()** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:1723,1763,1774
    GetAvailableForCastCount = number of times castable right now (min of item charges, ammo/AmmoRequired, and resource_amount/resource_cost). This is the 'charges/uses left' number. GetResourceAmount = current pool of the ability's resource (e.g. psychic/valour), GetResourceCost = per-cast cost. Slot mirrors as ResourceCount/ResourceCost/ResourceAmount but RTAccess ignores them.
- **AbilityData.IsAvailable / HasEnoughActionPoint / HasEnoughAmmo / IsRestricted** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:758,786,825,851
    IsAvailable = GetAvailableForCastCount!=0 && HasEnoughActionPoint && HasEnoughAmmo && !IsRestricted && (cooldown handled). HasEnoughActionPoint compares combatState.ActionPointsYellow >= CalculateActionPointCost(). IsRestricted is the big composite (LoS-area, threatened area, forbidden, ultimate-used-this-round, etc.).
- **AbilityData.GetUnavailableReason(Vector3) + UnavailabilityReasonType** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:1882,76
    Returns a localized human string for the first blocking reason (NotEnoughAmmo, IsOnCooldown, TargetTooFar/Close, HasNoLosToTarget, CannotTargetSelf/Ally/Enemy, IsUltimateAbilityUsedThisRound, CannotUseInThreatenedArea, ...). GetUnavailabilityReasons() (l.1787) yields the enum sequence. Perfect for speaking WHY a slot is greyed.
- **AbilityData.TargetAnchor + CanTargetSelf/Friends/Enemies/Point** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:720,748-754
    TargetAnchor is Owner (self/personal/potion), Unit (needs a unit target), or Point (AoE/ground). Determines whether OnClick self-casts immediately vs enters targeting. CanTargetSelf/Friends/Enemies/Point tell who's a legal target.
- **AbilityData.CanTarget(TargetWrapper,out reason) / CanTargetFromNode(...out distance,out los,out reason)** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:1585,1591,1622
    Validity + reason for a concrete target; CanTargetFromNode also returns distance-in-cells and LosCalculations.CoverType. This is the bridge to an accessible targeting facet (per-candidate 'in range / LoS / cover / reason').
- **AbilityData.IsHeroicAct / IsDesperateMeasure / IsUltimate / IsFreeAction** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:558,560,562,974
    Blueprint.IsHeroicAct / IsDesperateMeasure / IsMomentum classify momentum abilities. Desperate measure blocked once per round via TurnController.IsUltimateAbilityUsedThisRound (checked in IsRestricted l.951). IsFreeAction -> 0 AP.
- **Game.Instance.SelectedAbilityHandler (ClickWithSelectedAbilityHandler)** (OK) @ decompiled/Kingmaker/Game.cs:600; decompiled/Kingmaker.Controllers.Clicks.Handlers/ClickWithSelectedAbilityHandler.cs:36
    SetAbility(AbilityData) (l.295) enters PointerMode.Ability + raises IAbilityTargetSelectionUIHandler.HandleAbilityTargetSelectionStart; DropAbility() (l.321) cancels + raises ...End; RootAbility/Ability/IsSelected props. Once a target is clicked it calls UnitCommandsRunner.TryUnitUseAbility(RootAbility, target) (l.269). This is the targeting-mode the blind player is dropped into after activating a targeted ability.
- **UnitCommandsRunner.TryUnitUseAbility(AbilityData,TargetWrapper,bool) / CancelMoveCommand()** (OK) @ decompiled/Kingmaker.Controllers.Units/UnitCommandsRunner.cs:188,71
    Direct cast dispatch: builds a use-ability command (optionally path-approach) and AddToQueue on the unit's PartUnitCommands. Used by self-cast path. This is the API an accessible targeting flow would call once the player picks a target.

### ALREADY BUILT (seams)
RTAccess builds the action-bar region in Screens/InGameScreen.cs (BarSlots() l.167 enumerates weapons->abilities->consumables->heroic acts->desperate measures->overdrive off SurfaceActionBarVM; Usable() filters IsFake/IsEmpty/IsBad; RebuildActions() l.190 rebuilds a ListContainer of ProxyActionBarSlot on ActionsSig() change). UI/Proxies/ProxyActionBarSlot.cs wraps one ActionBarSlotVM: label = MechanicActionBarSlot.GetTitle(); value (State()) = AP cost + ammo (CurrentAmmo/MaxAmmo when IsReload) + cooldown (IsOnCooldown/CooldownText) + 'targeting' (IsSelected) / 'active' (IsActive()); enabled = IsPossibleActive; Space reads the rich tooltip (slot.Tooltip.Value); Enter calls slot.OnMainClick() but ONLY when Enabled. The AP/MP pools are read in InGameScreen.StatusLine() (l.355) via unit.GetCombatStateOptional().ActionPointsYellow/ActionPointsBlue. WarningReader already speaks the game's refusal toasts (e.g. 'not enough action points') that MechanicActionBarSlot.TryShowWarning raises. Seams to extend: ProxyActionBarSlot.State()/GetFocusAnnouncements() for the per-slot read recipe; ProxyActionBarSlot.GetActions() for the activation flow.

### GAPS
- No RANGE read: a blind player can't know an ability reaches before committing. AbilityData.RangeCells (and MinRangeCells) are available but unused.
- No CHARGES/USES read: only weapon ammo is spoken. Ability/psychic/consumable uses (GetAvailableForCastCount, ResourceCount/ResourceAmount) are ignored, so e.g. '2 uses left', psychic resource, heroic-act availability are silent.
- No WHY-can't-use on disabled slots: ProxyActionBarSlot only offers Activate when Enabled, so a greyed slot just reads 'disabled' and Enter does nothing — AbilityData.GetUnavailableReason(pos) / MechanicActionBarSlot.WarningMessage(pos) are never surfaced. The player learns nothing about not-enough-AP / on-cooldown / out-of-LoS / desperate-measure-already-used.
- No self-vs-targeted signal: nothing announces AbilityData.TargetAnchor, so the player can't tell whether Enter fires immediately (Owner/self, toggle) or throws them into targeting (Unit/Point). No AoE/point indication either.
- Targeted-ability activation dead-ends: OnMainClick on a TargetAnchor!=Owner ability calls SelectedAbilityHandler.SetAbility -> mouse PointerMode.Ability with no accessible target picker; the blind player is stranded in targeting with no way to choose a target or cancel (needs the separate targeting facet; ProxyActionBarSlot must hand off cleanly and expose a cancel).
- Convert / variants sub-menus are invisible: OnMainClick can open ConvertedVm (spontaneous/memorized spell conversion, weapon-ability variants/arsenal via OnShowConvertRequest) — a visual popup the player can't navigate; HasConvert/HasAvailableConvert/IsCanConvert are unread.
- Toggle/activatable state is under-read: activable abilities' on/off (IsActive/IsOn) is folded into a generic 'active' but heroic-act/desperate-measure/overdrive category and momentum gating aren't distinguished (IsHeroicAct/IsDesperateMeasure on the VM are unused).
- MP (blue) cost of abilities not represented: charge/move-type abilities can consume MP; only yellow ActionPointCost is spoken (see open questions).

### RECOMMENDED APPROACH
**Extend UI/Proxies/ProxyActionBarSlot.cs** (per-slot read recipe) and its GetActions() (activation flow). No new proxy type needed for the read side; a new targeting screen is a separate facet.

Per-slot read recipe (build in State()/GetFocusAnnouncements(), pulling from `_slot.AbilityData` = `abil` and `_slot.MechanicActionBarSlot` = `mabs`):
- Title: mabs.GetTitle() (already).
- Cost: `_slot.ActionPointCost.Value` AP (already). Add free-action note when abil?.IsFreeAction.
- Range: if abil != null and abil.TargetAnchor != Owner, append `abil.RangeCells` cells (and `MinRangeCells` if >0). Guard in try/catch (RangeCells triggers a rule).
- Charges/uses: prefer `_slot.ResourceCount.Value` when >-1 (uses of the resource) OR `abil.GetAvailableForCastCount()` for 'N uses left'; keep the existing ammo read (IsReload path) distinct. For activatables, read on/off via mabs.IsActive().
- Cooldown: existing IsOnCooldown/CooldownText (keep).
- Category tag: 'heroic act' if `_slot.IsHeroicAct`, 'desperate measure' if `_slot.IsDesperateMeasure`, 'toggle' if mabs is MechanicActionBarSlotActivableAbility (with on/off).
- Target kind: from abil.TargetAnchor -> 'self' (Owner) / 'target a unit' (Unit) / 'area, target a point' (Point); combine with abil.IsAOE for 'area'.
- WHY-disabled: when !Enabled, append `abil?.GetUnavailableReason(caster.Position)` (or mabs.WarningMessage(pos) which already routes to it for the ability slot). Use Game.Instance.VirtualPositionController.GetDesiredPosition(unit) as the cast position (that is what MechanicActionBarSlot.OnClick uses, MechanicActionBarSlot.cs:143).

Activation flow (GetActions()):
- Always offer Activate (drop the current `if (Enabled)` gate) so disabled slots still act — but on activation of a disabled slot, first speak GetUnavailableReason instead of clicking, matching the game's warning (or let OnMainClick fire and rely on WarningReader). Recommendation: for disabled slots, speak the reason directly and do NOT click.
- For enabled slots, branch on abil.TargetAnchor:
  * Owner / activatable / IsFreeAction self-cast -> call `_slot.OnMainClick()` (immediate; ability queues via TryUnitUseAbility; toggles flip). Announce e.g. 'cast'.
  * Unit / Point -> call `_slot.OnMainClick()` which will set Game.Instance.SelectedAbilityHandler; THEN hand off to the accessible targeting facet (open a targeting screen that reads candidates via AbilityData.CanTargetFromNode -> distance/LoS/cover/reason and, on confirm, either lets ClickWithSelectedAbilityHandler resolve or calls UnitCommandsRunner.TryUnitUseAbility(handler.RootAbility, chosenTarget)). Provide a Cancel action that calls Game.Instance.SelectedAbilityHandler.DropAbility() so the player is never stranded.
- For slots with HasConvert/variants, detect `_slot.HasConvert.Value` and expose the conversions (mabs.GetConvertedAbilityData() / abil.GetConversions()) as sub-actions instead of silently opening ConvertedVm.

AP/MP status (already in InGameScreen.StatusLine) is adequate; optionally also announce remaining AP after each cast by subscribing to IUnitSpentActionPoints/IUnitGainActionPoints (PartUnitCombatState raises these) in Accessibility/CombatEvents.cs so the player hears the pool tick down without re-focusing the status line.

### RISKS
- RangeCells / CalculateActionPointCost / GetAvailableForCastCount trigger Rulebook rules (RuleCalculateAbilityRange, RuleCalculateAbilityActionPointCost) — cheap and cached (TryGetCachedOrTrigger) but must be called on the main thread and wrapped in try/catch; call them at focus time, not every frame.
- GetUnavailableReason takes a cast Vector3 — must match the game's desired position (VirtualPositionController.GetDesiredPosition) or the reason (esp. range/LoS/threatened-area) can differ from what the game shows.
- The targeted-ability hand-off depends on the separate targeting facet; until it exists, activating a Unit/Point ability puts the game in PointerMode.Ability. Must always expose DropAbility() cancel to avoid a soft-lock in mouse mode.
- OnMainClick has coop/net-role and convert/variant side-branches; on a variant/convertible ability it opens a sub-VM rather than casting — proxy must detect HasConvert first or the player triggers an invisible popup.
- Slot set is split across multiple part VMs (SurfaceActionBarVM.Weapons/Abilities/Consumables/SurfaceMomentumVM/OverdriveSlotVM) and rebuilt reactively; ActionsSig() must keep catching set changes (weapon swap, item consume) or per-slot reads go stale.

### OPEN QUESTIONS
- Do any surface abilities actually spend MP (ActionPointsBlue) rather than AP? Charge-type abilities move the unit — need live verification whether their cost shows only as AP (CalculateActionPointCost) or also debits blue via movement, and whether to announce an MP cost.
- For psychic powers specifically: is the veil/warp cost surfaced through GetResourceCost/GetResourceAmount or only via GetVeilThicknessPointsToAdd? Verify in-game which number is meaningful to speak (peril/veil).
- Does ActionBarSlotVM.UpdateResources() run often enough that the reactive mirrors (ActionPointCost, ResourceCount, cooldown) are current at focus time, or must the proxy re-pull from MechanicActionBarSlot directly? It calls UpdateResources on set + a 0.5s delayed invoke; confirm live refresh cadence during a turn.
- Confirm the exact hand-off contract with the targeting facet: does letting ClickWithSelectedAbilityHandler resolve a synthesized click work in mouse mode, or must we bypass it and call UnitCommandsRunner.TryUnitUseAbility(RootAbility, target) directly (given the known SurfaceMainInputLayer !IsControllerMouse gate)?


################################################################
## AREA: Ability/attack TARGETING (single-target + AoE/burst/scatter cell targeting and commit)   [effort: L]
################################################################

### SUMMARY
RT drives all targeting through one class: when a targeted ability is chosen, MechanicActionBarSlotAbility.OnClick calls Game.Instance.SelectedAbilityHandler.SetAbility(ability), which puts the game in an "Ability" pointer mode where a sighted player aims the mouse at a unit or ground cell; a click builds a TargetWrapper and commits via UnitCommandsRunner.TryUnitUseAbility. The mod's action bar already reaches SetAbility (so targeted abilities enter targeting) but has NO way to pick a cell/unit or confirm, because the mod owns the keyboard in mouse mode and the engine's aim/click input layer is dead. The core gap is a mod-owned targeting mode: enumerate valid targets, cycle a target list and/or the existing grid cursor for ground/AoE, preview affected cells + hit/damage via AbilityData.GetPattern + AbilityTargetUIData, and commit by building a TargetWrapper directly and calling TryUnitUseAbility (bypassing the dead click layer). Every needed API is present and verified in decompiled source.

### VERIFIED GAME APIs
- **Game.SelectedAbilityHandler (ClickWithSelectedAbilityHandler)** (OK) @ decompiled/Kingmaker/Game.cs:600 (assigned :1251); class in Kingmaker.Controllers.Clicks.Handlers/ClickWithSelectedAbilityHandler.cs
    The single targeting controller. .SetAbility(AbilityData) (:295) enters targeting mode (SetPointerMode(Ability), raises IAbilityTargetSelectionUIHandler.HandleAbilityTargetSelectionStart). .IsSelected (:68) true while targeting. .DropAbility() (:321) cancels. .OnClick(GameObject,Vector3,int) (:211) is the mouse commit. .GetTarget(GameObject,Vector3,AbilityData,Vector3 casterPos) (:162) builds a TargetWrapper from a world point per TargetAnchor. .MultiTargetHandler (:66) for multi-target abilities. We should mostly BYPASS OnClick (needs a GameObject) and build TargetWrapper + commit ourselves.
- **MechanicActionBarSlotAbility.OnClick** (OK) @ decompiled/Kingmaker.UI.Models.UnitSettings/MechanicActionBarSlotAbility.cs:137
    THE SEAM. If Ability.TargetAnchor != Owner -> Game.Instance.SelectedAbilityHandler.SetAbility(Ability) (enters targeting; this is where the mod currently lands and stalls). If == Owner -> UnitCommandsRunner.TryUnitUseAbility(Ability, base.Unit) (self-cast already works via the mod action bar). This is reached from the mod's ProxyActionBarSlot -> ActionBarSlotVM.OnMainClick -> MechanicActionBarSlot.OnClick.
- **AbilityData.TargetAnchor / CanTargetPoint / CanTargetEnemies / CanTargetFriends / CanTargetSelf** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:720 (anchor Owner/Unit/Point), :748/:750/:752/:754
    Anchor drives targeting UX: Owner=self (no picking), Unit=pick a unit, Point=pick a ground cell. CanTargetPoint means ground cell allowed; CanTargetEnemies/Friends/Self gate which units are valid. Use to decide whether to show the unit-list, the cell-cursor, or both.
- **AbilityData.CanTarget / CanTargetFromDesiredPosition / CanTargetFromNode** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:1585/:1591, :1596/:1602, :1616/:1622
    Validation. CanTargetFromDesiredPosition(TargetWrapper, out UnavailabilityReasonType?) is the one the click handler uses (accounts for pending virtual move). CanTargetFromNode(casterNode,targetNodeHint,target,out int distance,out LosCalculations.CoverType los,out reason,int? casterDir) also returns distance-in-cells and cover/LOS — perfect for a per-target readout. Reason enum at :76.
- **AbilityData.GetPattern(TargetWrapper target, Vector3 casterPosition) -> OrientedPatternData** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:2113 (GetPatternSettings :2126)
    THE AoE-cells API. Returns OrientedPatternData with .Nodes (NodeList of CustomGridNodeBase), .ApplicationNode (center), .NodesWithExtraData (per-cell PatternCellData incl. MainCell), .Contains(node). Iterate .Nodes -> node.GetUnit() to enumerate exactly who's caught. GetPatternSettings() returns null for non-pattern abilities (single-target).
- **OrientedPatternData** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities.Components.Patterns/OrientedPatternData.cs
    Struct. .Nodes (NodeList, foreach-able), .ApplicationNode, .NodesWithExtraData enumerator yields (node, PatternCellData) where PatternCellData.MainCell marks the primary vs splash cells (mirrors CombatHUDRenderer primary/secondary areas). .IsEmpty. This is what CombatHUDRenderer paints; we read it as text.
- **AbilityTargetUIData + AbilityTargetUIDataCache.Instance.GetOrCreate(ability,target,casterPosition)** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityTargetUIData.cs; cache in Kingmaker.UI.SurfaceCombatHUD/AbilityTargetUIDataCache.cs:77
    THE PREVIEW numbers a blind player needs before confirming: InitialHitChance, HitWithAvoidanceChance, MinDamage, MaxDamage, DodgeChance, ParryChance, CoverChance, BlockChance, BurstIndex (=BurstAttacksCount), BurstHitChances (per-shot list). Cache keyed on (ability,target,casterPos); recomputes on turn/position change. casterPos = Game.Instance.VirtualPositionController.GetDesiredPosition(caster).
- **AbilityData attack-shape flags: IsAOE/IsScatter/IsMelee/IsSingleShot/IsCharge/IsBurstAttack/BurstAttacksCount/RangeCells/MinRangeCells** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:468,:510,:527,:525,:542,:372,:344,:376,:378
    Classify the ability to pick the targeting UX and compose the announce (e.g. 'burst, 6 shots', 'blast pattern', 'scatter', range 12). RangeCells/MinRangeCells are the range ring the sighted HUD draws.
- **TargetWrapper (ctors + implicit ops)** (OK) @ decompiled/Kingmaker.Utility/TargetWrapper.cs:126-146,168-189
    new TargetWrapper(MechanicEntity) for unit targets; new TargetWrapper(Vector3 point) or (point, float? orientation, MechanicEntity) for ground/point targets. Implicit from MechanicEntity and Vector3. .NearestNode maps a point target to a CustomGridNodeBase. We build these directly from the grid cursor node (node.Vector3Position) or the selected unit.
- **UnitCommandsRunner.TryUnitUseAbility(AbilityData, TargetWrapper, bool shouldApproach=false)** (OK) @ decompiled/Kingmaker.Controllers.Units/UnitCommandsRunner.cs:188 (builds PlayerUseAbilityParams via CreateUseAbilityCommandParams :518)
    THE COMMIT. Builds PlayerUseAbilityParams(ability,target){IsSynchronized=true}, fills AllTargets from SelectedAbilityHandler.MultiTargetHandler, queues on caster.Commands. shouldApproach=true walks into range first (used when reason==TargetTooFar). This is exactly what ClickWithSelectedAbilityHandler.OnClick calls at :269 after validation. Preferred commit path for the mod.
- **UnitUseAbilityParams / PlayerUseAbilityParams** (OK) @ decompiled/Kingmaker.UnitLogic.Commands/UnitUseAbilityParams.cs
    Command payload: ctor (AbilityData ability, TargetWrapper target); AllTargets:List<TargetWrapper> for multi-target. If we ever need to bypass UnitCommandsRunner we can new PlayerUseAbilityParams(ability,target){IsSynchronized=true,AllTargets=...} and caster.Commands.Run(it) — but TryUnitUseAbility is the vetted wrapper.
- **IAbilityTargetSelectionUIHandler** (OK) @ decompiled/Kingmaker.PubSubSystem/IAbilityTargetSelectionUIHandler.cs
    EventBus interface: HandleAbilityTargetSelectionStart(AbilityData)/End(AbilityData). Subscribe to detect the exact moment the game enters/leaves targeting (raised by SetAbility/DropAbility). Cleaner trigger than polling SelectedAbilityHandler.IsSelected; lets the mod auto-open its targeting mode no matter how the ability was selected.
- **Game.Instance.State.AllBaseAwakeUnits + entity.IsEnemy(caster)/CanBeAttackedDirectly/LifeState** (OK) @ decompiled/Kingmaker/Game.cs (State); usage e.g. Kingmaker.AI.TargetSelectors/ScatterShotTargetSelector.cs:170; IsEnemy used in ClickWithSelectedAbilityHandler.cs:121; CanBeAttackedDirectly at :187
    Enumerate candidate target units: iterate AllBaseAwakeUnits, keep entity.CanBeAttackedDirectly && (IsEnemy(caster) ? CanTargetEnemies : CanTargetFriends), then test ability.CanTargetFromDesiredPosition(new TargetWrapper(u)) to split valid/invalid. Sort by distance (CanTargetFromNode out-distance) and/or enemy-first for the target list.
- **CustomGridNodeBaseExtensions.GetUnit()/GetAllUnits()/TryGetUnit()** (OK) @ decompiled/Kingmaker.Pathfinding/CustomGridNodeBaseExtensions.cs:24,30,18
    node.GetUnit() maps a pattern/cursor cell to the BaseUnitEntity standing on it. Used to turn GetPattern().Nodes into a spoken 'affects: X, Y, Z' list, and to resolve the grid cursor's cell into a unit target.
- **CombatHUDRenderer (reference, do not drive)** (OK) @ decompiled/Kingmaker.UI.SurfaceCombatHUD/CombatHUDRenderer.cs (PopulateAbilityPatternAreas :732; SetAbilityAreaHUD :431)
    Shows the canonical mapping: primary area = pattern cells where MainCell||==ApplicationNode, secondary = all other pattern cells; range rings from min/max/effective RangeCells. Confirms our text model. This is the sighted visual we replace with speech; we do not need to call it.

### ALREADY BUILT (seams)
RTAccess already covers the PREREQUISITES but not targeting itself:
- Ability SELECTION exists: RTAccess/UI/Proxies/ProxyActionBarSlot.cs activates a slot via ActionBarSlotVM.OnMainClick, which routes to MechanicActionBarSlot.OnClick. For self/Owner-anchor abilities this already casts (MechanicActionBarSlotAbility.OnClick -> TryUnitUseAbility). For Unit/Point-anchor abilities it already calls SelectedAbilityHandler.SetAbility — i.e. the game IS put into targeting mode — but then nothing in the mod can pick or confirm a target, so it silently stalls (targeting limbo). THIS is the seam to plug.
- A grid cell cursor already exists: RTAccess/Accessibility/TileExplorer.cs + Exploration/MapCursor.cs — an always-active virtual CustomGridGraph cursor with arrow-key stepping, node readout via InteractableDescriber, camera follow, and a two-step move-to confirm. This is directly reusable as the AoE/ground cell-cursor: MapCursor.Node gives the CustomGridNodeBase to build a point TargetWrapper.
- Combat context is read: RTAccess/Screens/InGameScreen.cs Status region (AP/MP/whose-turn) and Combat region (End turn + initiative list via SurfaceHUDVM.InitiativeTrackerVM.Units) — a natural home for a target list, and the initiative units are a ready enemy/ally roster.
- Warning/refusal plumbing exists: RTAccess/Accessibility/WarningReader.cs already speaks IWarningNotificationUIHandler refusals ('not enough action points', etc.), and ClickWithSelectedAbilityHandler raises exactly those on invalid targets — so validation failures already have a voice if we route through the game, or we can speak GetUnavailabilityReasonString ourselves.
- Input architecture exists: RTAccess/Input/InputManager.cs + InputCategory.cs give the mod its own keyboard with a priority/shadowing model; a new transient Targeting category (declared top while targeting is active) fits the existing pattern (like Windows/WorldMap).
- Live unit buffers (Buffers/UnitBuffer.cs) already read HP/AP/defenses/buffs — reusable to describe a highlighted target in depth.

### GAPS
- No way to CONFIRM a target: selecting a Unit/Point ability enters SetAbility targeting mode with no mod-side commit; the engine mouse/console aim+click path is dead in mouse mode, so the ability just hangs 'targeting'.
- No target ENUMERATION: a blind player cannot discover which units are in range/LOS/valid for the selected ability, nor cycle them by nearest/threat.
- No ground/AoE CELL selection wired to abilities: the grid cursor exists but is not connected to AbilityData targeting; point-anchor abilities (blasts, spawn-area, cones) can't be aimed.
- No AoE PREVIEW: no read of GetPattern() affected cells or who's caught before committing — critical for not friendly-firing the party.
- No hit/damage PREVIEW: AbilityTargetUIData (hit %, dodge/parry/cover/block, min-max damage, per-shot burst chances) is never surfaced, so the player commits blind to outcome odds.
- No range/LOS/cover feedback per candidate: CanTargetFromNode returns distance + CoverType + reason but nothing speaks it; 'too far'/'no line of sight' only appear as a refusal after a failed click.
- No multi-target flow: abilities with IAbilityMultiTarget (AbilityMultiTargetSelectionHandler) need N sequential target picks with 'target 2 of 3' prompts — unhandled.
- No cancel affordance tied to targeting: DropAbility() is never called from the mod, so an accidental selection can't be backed out cleanly.
- Scatter/burst semantics unspoken: IsScatter / IsBurstAttack / BurstAttacksCount and scatter deviation risk (TargetSelector.IsScatterShotRisky) are not announced, though they change the decision.

### RECOMMENDED APPROACH
Add a new mod-owned targeting mode, `RTAccess/Accessibility/TargetingMode.cs` (static, EventBus subscriber to IAbilityTargetSelectionUIHandler), plus a transient `InputCategory.Targeting` declared top-of-stack by InGameScreen while active. Do NOT drive the engine's aim/click layer (dead in mouse mode) and do NOT call ClickWithSelectedAbilityHandler.OnClick (needs a GameObject). Instead read the game's own selection state, then build a TargetWrapper and commit directly.

Data flow:
1. ENTER. Hook `IAbilityTargetSelectionUIHandler.HandleAbilityTargetSelectionStart(ability)` (raised by SetAbility from the mod's existing ProxyActionBarSlot activation). Stash `ability` and `caster = ability.Caster`, `casterPos = Game.Instance.VirtualPositionController.GetDesiredPosition(caster)`. Classify via TargetAnchor + IsAOE/IsScatter/IsMelee/IsBurstAttack/BurstAttacksCount/RangeCells to compose the opening announce ('Force Sword strike, melee, 1 target' / 'Frag grenade, blast, range 12, aim a cell'). Announce mode + controls.

2. TARGET LIST (anchor Unit, and Point abilities that can also snap to a unit). Build the candidate list once: iterate `Game.Instance.State.AllBaseAwakeUnits`, keep `u.CanBeAttackedDirectly && u.LifeState.IsConscious && (u.IsEnemy(caster) ? ability.CanTargetEnemies : ability.CanTargetFriends)`; for each call `ability.CanTargetFromNode(caster.CurrentUnwalkableNode, null, new TargetWrapper(u), out int dist, out LosCalculations.CoverType los, out var reason)`. Partition into valid (reason==None) and invalid; sort valid by dist (nearest) with an enemy-first secondary key; expose a 'threat' sort later. Cycle with e.g. Tab/Shift+Tab (or [ ]); each stop speaks name + dist-in-cells + cover/LOS + a one-line preview from `AbilityTargetUIDataCache.Instance.GetOrCreate(ability, u, casterPos)` (hit% (HitWithAvoidanceChance), MinDamage-MaxDamage, and for burst 'N shots' from BurstIndex). Invalid targets can be a second cycle group announcing the reason (GetUnavailabilityReasonString).

3. CELL CURSOR (anchor Point / AoE). Reuse TileExplorer/MapCursor as the aim cursor (arrows step; C recenter on caster). On each move, build `var target = new TargetWrapper(MapCursor.Node.Vector3Position)`, then `var pattern = ability.GetPattern(target, casterPos)` and speak: cell offset from caster, in/out of range (CanTargetFromDesiredPosition reason), pattern size, and the caught units by iterating `pattern.Nodes` -> `node.GetUnit()` (flag allies as 'friendly!'). Optionally split primary vs splash via NodesWithExtraData PatternCellData.MainCell. Let the same cursor also read the unit under the cell so list-mode and cursor-mode agree.

4. PREVIEW key (e.g. P) speaks the full AbilityTargetUIData for the current target/cell: InitialHitChance vs HitWithAvoidanceChance, DodgeChance/ParryChance/CoverChance/BlockChance, MinDamage-MaxDamage, and per-shot BurstHitChances for burst weapons.

5. CONFIRM (Enter). Validate with `ability.CanTargetFromDesiredPosition(target, out var reason)`. If invalid and reason==TargetTooFar out of combat, allow approach. Multi-target: if `ability.Blueprint.GetComponent<IAbilityMultiTarget>() != null`, drive `Game.Instance.SelectedAbilityHandler.MultiTargetHandler.AddTarget(target)` and loop with 'target k of n' until GetAbilityForNextTarget()==null, then commit. Single/final commit: `UnitCommandsRunner.TryUnitUseAbility(Game.Instance.SelectedAbilityHandler.RootAbility ?? ability, target, shouldApproach: reason==TargetTooFar)`. This reuses the game's PlayerUseAbilityParams build (including AllTargets) exactly like the mouse path. Then Speak confirmation; the game's own combat-log/damage events (already read by Accessibility/CombatEvents.cs) narrate the result.

6. CANCEL (Esc/Backspace) -> `Game.Instance.SelectedAbilityHandler.DropAbility()` and pop the Targeting category. Also handle HandleAbilityTargetSelectionEnd to tear down if the game exits targeting for any other reason.

Files: NEW `Accessibility/TargetingMode.cs` (state machine + announces + commit); NEW `Input/InputCategory.Targeting` + bindings in InputBindings; EXTEND `Screens/InGameScreen.cs` to declare the Targeting category while active and to route the ability-selected event; REUSE `Exploration/MapCursor.cs`/`Accessibility/TileExplorer.cs` for the cell cursor (add a 'targeting sub-mode' that swaps the readout to the pattern/who's-caught text and disarms move-to); REUSE `Buffers/UnitBuffer.cs` for a deep target inspect. Keep the ProxyActionBarSlot activation unchanged — it already enters targeting; TargetingMode just takes over from there.

### RISKS
- Preview cost: AbilityTargetUIData/GetDamagePrediction triggers rulebook rules and clones the ability (ability.Clone(isPreview:true)); computing it for every candidate on every cursor step could hitch. Mitigate by using AbilityTargetUIDataCache (already memoized per turn/position) and computing lazily only for the highlighted target, not the whole list.
- casterPosition consistency: the game consistently uses VirtualPositionController.GetDesiredPosition(caster) (pending-move aware). If we pass caster.Position instead, range/LOS/hit numbers will disagree with what a commit actually does. Must use the desired-position everywhere (validate, pattern, preview, commit).
- GetPatternSettings() returns null for non-pattern abilities -> GetPattern returns OrientedPatternData.Empty; must branch on IsAOE / anchor rather than assuming a pattern.
- Multi-target via MultiTargetHandler mutates shared engine state (SelectedAbilityHandler.MultiTargetHandler); if the player cancels mid-sequence we must DropAbility to reset, or a stale partial target set can leak into the next cast.
- Some point abilities snap to a unit when CanTargetPoint is false or IsCharge (see ClickWithSelectedAbilityHandler.GetTarget); our cell TargetWrapper must replicate that (attach the unit under the cell) or the commit target won't match the aim.
- Restricted firing arc / best-shooting-position abilities (RestrictedFiringArc, UseBestShootingPosition) affect validity in non-obvious ways; CanTargetFromNode already accounts for them, so always gate on it rather than a hand-rolled range check.
- Engine-gate assumption: relying on TryUnitUseAbility (command API) sidesteps the dead SurfaceMainInputLayer, but if a future ability requires the pointer-mode UI to have run first, direct commit could differ from the mouse path — needs live verification per ability archetype.
- Deployment/preparation phase and non-combat casts have different rules (IsInCombat branches in ShouldHandleAbilityCastFail); the mode must handle out-of-combat targeted abilities too.

### OPEN QUESTIONS
- Does UnitCommandsRunner.TryUnitUseAbility from a mod-owned keyboard (mouse-mode, no engine pointer mode active besides SetAbility) reliably fire for all archetypes — single melee, ranged burst, blast/AoE point, cone, scatter, multi-target, charge — or do some require the game's pointer-mode UI to have processed? Verify each in the dev harness.
- Is subscribing to IAbilityTargetSelectionUIHandler sufficient to catch EVERY entry into targeting (including item-thrown grenades, variant abilities, spontaneous-spell conversions), or are there paths that cast without raising it?
- For point/AoE aim, is snapping the cell cursor to the game's AoEPatternHelper.GetActualCastPosition needed for the pattern preview to match the committed pattern exactly (ClickWithSelectedAbilityHandler.GetTarget applies it), or is the raw node position close enough?
- Scatter: how to surface deviation risk (TargetSelector.IsScatterShotRisky / ScatterShotTargetSelector) as an accessible warning before confirm — is there a UI-facing 'risky' flag we can read directly?
- Multi-target abilities: confirm the AddTarget/GetAbilityForNextTarget loop and its cancellation semantics behave when driven headlessly (no view), and whether AllTargets is populated correctly by TryUnitUseAbility in that case.
- Best UX for the two aiming modes: should target-list and cell-cursor be one unified cursor (cursor lands on a unit => that unit is the target) or two explicit modes toggled by a key? Needs a blind-playtester decision.


################################################################
## AREA: Movement & pathfinding in combat   [effort: M]
################################################################

### SUMMARY
RT combat movement is a discrete grid A* over the CustomGridGraph, budgeted by a float "blue" action-point pool (PartUnitCombatState.ActionPointsBlue) spent at a per-cell rate; diagonals and enemy-threatened cells cost more. The game exposes clean, blocking, synchronous APIs to (a) compute all reachable tiles with their cumulative cost, (b) compute the path and per-cell AP cost to any tile, and (c) commit a grid move via unit.TryCreateMoveCommandTB(...) + unit.Commands.Run(...). RTAccess's TileExplorer already COMMITS moves through exactly this path, but its preview only speaks a Chebyshev straight-line tile count — the core gap is that a blind player gets no real path length, no MP/AP cost, no "reachable this turn?" verdict, and no attack-of-opportunity/overwatch warning before confirming.

### VERIFIED GAME APIs
- **PartUnitCombatState.ActionPointsBlue / ActionPointsBlueMax / ActionPointsBlueSpentThisTurn** (OK) @ decompiled/Kingmaker.Controllers.Combat/PartUnitCombatState.cs:93,99,102
    float MP pool. ActionPointsBlue = current remaining movement budget (in AP units, NOT cells). SpendActionPoints(null, blue) deducts it. cells = ActionPointsBlue / blueprint.WarhammerMovementApPerCell.
- **CurrentMovementPointsGetter.GetBaseValue** (OK) @ decompiled/Kingmaker.EntitySystem.Properties.Getters/CurrentMovementPointsGetter.cs:16
    Confirms MP = ActionPointsBlueMax - ActionPointsBlueSpentThisTurn. MaxMovementPointsGetter / MovementPointsSpentThisTurnGetter are siblings.
- **PathfindingService.FindAllReachableTiles_Blocking(agent, start, maxLength, ignoreThreateningAreaCost)** (OK) @ decompiled/Kingmaker.Pathfinding/PathfindingService.cs:633
    Returns Dictionary<GraphNode,WarhammerPathPlayerCell>: EVERY reachable tile within maxLength AP. Each cell has .Length (cumulative AP cost), .IsCanStand, .ParentNode, .DiagonalsCount. This is the reachable-set + cost oracle. Pass maxLength = unit.CombatState.ActionPointsBlue.
- **WarhammerPathPlayerCell (struct)** (OK) @ decompiled/Kingmaker.Pathfinding/WarhammerPathPlayerCell.cs:6
    readonly struct{Vector3 Position; int DiagonalsCount; float Length; GraphNode Node; GraphNode ParentNode; bool IsCanStand}. Length is the AP cost to reach; ParentNode lets you reconstruct the path.
- **PathfindingService.FindPathTB_Blocking(agent, destination, limitRangeByActionPoints)** (OK) @ decompiled/Kingmaker.Pathfinding/PathfindingService.cs:478
    Returns WarhammerPathPlayer (a Path). .CalculatedPath is WarhammerPathPlayerCell[] with cumulative .Length; .vectorPath is List<Vector3>; .path is List<GraphNode>. Pass limitRangeByActionPoints:false to get the FULL path even beyond budget (so you can say how far short you fall).
- **RuleCalculateMovementCost(initiator, path, calcFullPathApCost)** (OK) @ decompiled/Kingmaker.RuleSystem.Rules/RuleCalculateMovementCost.cs:14
    Rulebook.Trigger(new RuleCalculateMovementCost(unit, warhammerPath, calcFullPathApCost)). Outputs: ResultPointCount (# path points reachable within ActionPointsBlue), ResultAPCostPerPoint[] (per-step AP), ResultFullPathAPCost (total AP to end). If ResultPointCount < path.path.Count -> destination NOT reachable this turn. This is the exact reachability+cost API the game uses.
- **UnitHelper.TryCreateMoveCommandTB(this BaseUnitEntity, MoveCommandSettings, showMovePrediction, out MoveCommandStatus)** (OK) @ decompiled/Kingmaker.UnitLogic/UnitHelper.cs:879 (impl TryCreateMoveCommandTBUnit:893)
    THE commit API. Internally: FindPathTB_Blocking -> RuleCalculateMovementCost -> reachability/blocker checks -> ForcedPath.Construct -> returns UnitMoveToProperParams (does NOT auto-run). Caller does unit.Commands.Run(params). Statuses: NewCommandCreated, SamePath, NotEnoughPathPoints, NoReachableTile, NoForcedPath, NoStartingCell, CannotMove, NotEnoughMovementPoints, DestinationUnreachable (UnitHelper.cs:200).
- **MoveCommandSettings (struct)** (OK) @ decompiled/Kingmaker.UnitLogic/MoveCommandSettings.cs:6
    {Vector3 Destination; BaseUnitEntity FollowedUnit; bool IsControllerGamepad; bool DisableApproachRadius; bool LeaveFollowers}. For grid move use {Destination = node.Vector3Position, DisableApproachRadius = true}.
- **UnitMoveToProper / UnitMoveToProperParams** (OK) @ decompiled/Kingmaker.UnitLogic.Commands/UnitMoveToProper.cs:19
    The TURN-BASED grid move command. Carries ForcedPath, ApCostPerEveryCell[], ActionPointsPerCell, DisableAttackOfOpportunity, DisableApproachRadius. OnStart spends MovePointsSpent = min(pathCost, ActionPointsBlue). Distinct from UnitMoveTo (below).
- **UnitMoveTo / UnitMoveToParams** (OK) @ decompiled/Kingmaker.UnitLogic.Commands/UnitMoveTo.cs:16 ; UnitMoveToParams.cs:18
    The REAL-TIME / free-roam move (ForcedPath + approachRadius + WalkSpeedType Walk/Run/Sprint). Used out of combat and by TryApproachAndInteractRT. NOT used to spend TB movement budget. Confirms the RT-vs-WOTR type difference: WOTR CombatMode drove UnitMoveTo; RT combat drives UnitMoveToProper.
- **UnitCommandsRunner.MoveSelectedUnitToPointTB / MoveSelectedUnitsToPoint** (OK) @ decompiled/Kingmaker.Controllers.Units/UnitCommandsRunner.cs:248,236
    The game's canonical click-to-move. TB path calls unit.TryCreateMoveCommandTB(...,showMovePrediction:true) then, on a 2nd identical click (SamePath), commits the queued virtual move. Emits WarningNotification on NotEnoughMovementPoints/PathBlocked.
- **UnitMovementAgentBase.CacheThreateningAreaCells(entity)** (OK) @ decompiled/Kingmaker.View/UnitMovementAgentBase.cs:558
    HashSet<GraphNode> of every cell threatened by an enemy that CanMakeAttackOfOpportunity vs this unit. This is the AoO-provocation set — intersect with the chosen path nodes to warn 'crosses N threatened tiles'.
- **AttackOfOpportunityHelper.CanMakeAttackOfOpportunity / CollectThreateningArea** (OK) @ decompiled/Kingmaker.UnitLogic/AttackOfOpportunityHelper.cs:163,200
    Confirms RT HAS attacks of opportunity. CollectThreateningArea(unit, HashSet<GraphNode>, WeaponSlot) fills a single enemy's threatened cells — lets you ATTRIBUTE a threat to a named enemy.
- **WarhammerPathPlayerMetricCostProvider (threat cost)** (OK) @ decompiled/Kingmaker.Pathfinding/WarhammerPathPlayerMetricCostProvider.cs:69-86
    GetCellCost: Normal cell = blueprint.WarhammerMovementApPerCell; ThreateningArea cell = GetWarhammerMovementApPerCellThreateningArea(). Diagonals alternate x1/x2 (line 48-53). So moving through threatened cells is BOTH more expensive AND provokes — the extra cost is already baked into ResultAPCostPerPoint.
- **PartOverwatch.OverwatchArea** (OK) @ decompiled/Kingmaker.UnitLogic.Parts/PartOverwatch.cs:57
    IReadOnlyCollection<CustomGridNodeBase> — an enemy on Overwatch (40K ranged AoO analog) covers these tiles; .Contains(unit)/.TryTriggerAttack fire when a mover enters. Enumerate enemies' PartOverwatch to warn 'path enters Ork overwatch'. Provocation condition: WarhammerContextConditionProvokesOverwatch.
- **WarhammerPathHelper.ConstructPathTo(node, Dictionary<GraphNode,WarhammerPathPlayerCell>)** (OK) @ decompiled/Kingmaker.Pathfinding/WarhammerPathHelper.cs:15
    Rebuilds a ForcedPath to any node by walking ParentNode chain of a cached FindAllReachableTiles_Blocking dict — CHEAP per-cursor-cell path preview without re-running A* each keypress.
- **PathExtras.LengthInCells(path, startFromOddDiagonal) / DiagonalsCount(path)** (OK) @ decompiled/Kingmaker.Pathfinding/PathExtras.cs:25,36
    Path length in GRID CELLS accounting for the 1/2 diagonal alternation. Use for a 'N tiles' readout that matches the game's true routed path (not Chebyshev).
- **UnitMovableAreaController.GetMovableArea** (OK) @ decompiled/Kingmaker.Controllers.Units/UnitMovableAreaController.cs:237
    Shows the game builds the blue movable-area highlight via FindAllReachableTiles_Blocking(agent, pos, ActionPointsBlue). CurrentUnitMovableArea (List<GraphNode>) is the public reachable-node list per turn, but WITHOUT per-cell cost — recompute the dict yourself if you need Length.
- **UnitPathManager / UnitPredictionManager (path render)** (OK) @ decompiled/Kingmaker.UI.PathRenderer/UnitPathManager.cs ; UnitMoveToProper.cs:122,190
    RT's path preview renderer (AddPath(unit, forcedPath, apPerCell, movePointsSpent, oddDiagonal, apCostPerEveryCell)). There is NO PathVisualizer/CalculatePathForCommand (that is WOTR-only). No readable 'break marker' object — the break is implicit: cells whose cumulative cost exceeds ActionPointsBlue. Compute it from RuleCalculateMovementCost.ResultPointCount.

### ALREADY BUILT (seams)
RTAccess already has the grid cursor and already COMMITS combat moves through the correct engine API. In RTAccess/Accessibility/TileExplorer.cs: MoveToCursor() (lines 136-180) gates on TB + IsPlayerTurn + acting unit selected/controllable, then calls unit.TryCreateMoveCommandTB(new MoveCommandSettings{Destination=node.Vector3Position, DisableApproachRadius=true}, showMovePrediction:false, out status) and unit.Commands.Run(cmd) — exactly the verified commit path — with a 3s two-press confirm (_armedNode/_armTime) and MoveFailure(status) mapping the MoveCommandStatus enum to speech. Out of combat it routes through UnitCommandsRunner.MoveSelectedUnitsToPoint. The cursor itself (MapCursor) steps tile-by-tile on the CustomGridGraph and reads each tile via InteractableDescriber.DescribeTile (RTAccess/Accessibility/InteractableDescriber.cs:76) which already speaks occupant / walkable-or-wall / cover per edge / offset-from-anchor. Seams to plug into: (1) the arm-press branch in MoveToCursor (TileExplorer.cs:156-162) currently speaks TilesAway() = Chebyshev max(dx,dz) only; (2) DescribeTile could gain a combat cost/reach clause; (3) InGameScreen Status/Combat region + CombatMode-style status already report ActionPointsBlue-derived numbers elsewhere.

### GAPS
- No real path length or MP/AP cost to the cursor cell: TileExplorer.TilesAway (TileExplorer.cs:222) reports Chebyshev straight-line max(dx,dz), which ignores walls, detours, diagonal cost, and threatened-cell surcharge — it can be wildly wrong versus the routed path the move actually takes.
- No pre-commit reachability verdict: the arm press cannot say 'reachable this turn' vs 'out of range by N'; the player only learns it FAILED on the second (commit) press when TryCreateMoveCommandTB returns NotEnoughMovementPoints.
- No attack-of-opportunity warning: nothing tells a blind player the routed path crosses enemy-threatened tiles (provokes melee AoO) even though CacheThreateningAreaCells makes this trivially computable and named via CollectThreateningArea.
- No overwatch warning: entering an enemy PartOverwatch.OverwatchArea (the 40K ranged-AoO analog) is unannounced.
- No reachable-set overview: the player can't ask 'how far can I go / which enemies can I reach this turn', which the movable-area dict (FindAllReachableTiles_Blocking) provides directly.
- No cover-seeking destination: no way to find the nearest reachable tile that grants cover versus a chosen enemy (needs reachable-set + the cover query TileExplorer already uses).

### RECOMMENDED APPROACH
Add a small combat-movement helper (new file RTAccess/Accessibility/CombatMovement.cs) and wire two seams into the existing TileExplorer/InteractableDescriber — do NOT re-plumb the commit, which already works.

DATA FLOW (all verified APIs, all synchronous/blocking):
1. Per turn, cache the reachable set once: `var reach = PathfindingService.Instance.FindAllReachableTiles_Blocking(unit.View.MovementAgent, unit.Position, unit.CombatState.ActionPointsBlue);` (Dictionary<GraphNode,WarhammerPathPlayerCell>). Invalidate on turn change / after any move (subscribe like UnitMovableAreaController, or just recompute lazily keyed on unit+ActionPointsBlue).
2. On the cursor tile, produce a PathInfo(node):
   - reachable-this-turn = reach.ContainsKey(node) && reach[node].IsCanStand; if so, apCost = reach[node].Length, and path = WarhammerPathHelper.ConstructPathTo(node, reach) (cheap, no A*).
   - if NOT in reach, run the full path once to report how far short: `var full = PathfindingService.Instance.FindPathTB_Blocking(agent, node.Vector3Position, limitRangeByActionPoints:false);` then `var rule = Rulebook.Trigger(new RuleCalculateMovementCost(unit, full, calcFullPathApCost:true));` -> total AP = rule.ResultFullPathAPCost, reachable prefix = re-trigger with calcFullPathApCost:false -> ResultPointCount; deficit = ResultFullPathAPCost - ActionPointsBlue.
   - cells = PathExtras.LengthInCells(path) for a 'tiles' number that matches the routed path (replace Chebyshev TilesAway).
   - Convert AP->cells for display if desired: cells_budget = ActionPointsBlue / unit.Blueprint.WarhammerMovementApPerCell (verify display unit live; likely present AP directly since UI uses blue points).
3. Threats crossed: `var threat = UnitMovementAgentBase.CacheThreateningAreaCells(unit);` count path.path nodes in threat; for names, loop enemies and call CollectThreateningArea per enemy to attribute ('provokes attack of opportunity from X'). Overwatch: loop combat enemies with PartOverwatch, test path nodes against OverwatchArea.
4. Announce (spoken on the ARM press, replacing TileExplorer.cs:160): e.g. '4 tiles, 8 movement points, reachable, crosses 1 threatened tile — provokes attack of opportunity. Press again to move.' or 'out of range, 3 more movement points needed. Press again to path as far as possible.'

SEAMS:
- TileExplorer.MoveToCursor arm branch (lines 156-162): swap TilesAway(unit,node) for CombatMovement.PreviewLine(unit, node). Keep the existing commit (TryCreateMoveCommandTB + Commands.Run) unchanged — it already enforces the same reachability the preview reports.
- InteractableDescriber.DescribeTile (line 76): when Game.Instance.TurnController.TurnBasedModeActive and node is standable, append CombatMovement.CostClause(unit, node) so plain cursor stepping also hears reach/cost (optional, gate behind a setting to avoid chatter).
- Optionally add a dedicated key (e.g. a 'movement overview' key on InGameScreen) that reads the reachable-set summary from the cached dict: reachable tile count, nearest reachable enemy distance, whether any cover tile is reachable.

Do NOT use UnitMoveTo/UnitMoveToParams for the TB commit (that is the real-time move); UnitMoveToProperParams via TryCreateMoveCommandTB is correct and already used. No PathVisualizer exists in RT — ignore the WOTR CombatMode.ComputePath pattern; the RT equivalents are FindPathTB_Blocking + RuleCalculateMovementCost + FindAllReachableTiles_Blocking.

### RISKS
- Per-keypress A*: FindPathTB_Blocking + Rulebook.Trigger on every cursor step is a blocking A* and could hitch. Mitigate by caching FindAllReachableTiles_Blocking once per turn and using WarhammerPathHelper.ConstructPathTo for in-range cells (no A*); only run the full FindPathTB_Blocking for the rarer out-of-range case, or defer all cost speech to the arm press / a dedicated key.
- Rulebook.Trigger(RuleCalculateMovementCost) side effects: OnTrigger appears to be a pure calculation, but rulebook events can fire interceptors; needs a live check that repeated triggers don't mutate state or spam events.
- Display unit ambiguity: ActionPointsBlue is AP, not cells; WarhammerMovementApPerCell converts. Must verify live what the on-screen HUD shows (blue points vs cells) so the mod's numbers match what a sighted guide sees.
- Overwatch enumeration cost/availability: PartOverwatch area is configured lazily (TrySetupOverwatchArea on Start); reading it mid-enemy-turn should be fine but reading before an enemy has set overwatch yields nothing — verify timing. Enumerating all enemies each cursor move adds cost.
- Routed-path vs intuition: the cost provider may route AROUND threatened cells when cheaper, so the announced 'threatened tiles crossed' must be derived from the ACTUAL returned path nodes, not a straight line — and the committed move uses the same path, so they stay consistent.
- Pet/summon and non-standard movers (starships handled separately in TryCreateMoveCommandTBShip) — gate the helper to normal BaseUnitEntity player units.

### OPEN QUESTIONS
- Confirm live (dev harness /eval) that ActionPointsBlue is displayed to sighted players as 'movement points' and the exact WarhammerMovementApPerCell for a baseline character, so the readout unit (AP vs cells) matches the HUD.
- Verify Rulebook.Trigger(new RuleCalculateMovementCost(...)) is side-effect-free enough to call repeatedly for preview.
- Confirm CacheThreateningAreaCells + per-enemy CollectThreateningArea give correct AoO attribution from the ACTING unit's perspective when called by the mod (it takes the acting unit as the target of the enemies' AoO).
- Determine whether reading enemies' PartOverwatch.OverwatchArea before movement reliably reflects active overwatch, and the cheapest way to enumerate overwatching enemies (a global list vs iterating CombatGroup).
- Decide whether cost/threat speech belongs in DescribeTile (every step, possibly chatty) or only on the arm press / a dedicated 'path info' key — a UX call for the maintainer.
- Check whether the committed UnitMoveToProper ever provokes differently than previewed when DisableAttackOfOpportunity is set by some buff/ability (plain player move does not set it).


################################################################
## AREA: Battlefield spatial awareness (the tactical radar)   [effort: L]
################################################################

### SUMMARY
RT exposes everything a "tactical radar" needs through public, verified APIs: the combatant set (TurnController / Game.State.AllBaseAwakeUnits), faction predicates on MechanicEntity, cell-distance helpers, PartHealth/Buffs, and — crucially — LosCalculations, the single static cover+line-of-sight engine the game's own overtips use. RTAccess already has the per-tile TileExplorer, a shared MapCursor, and per-unit UnitBuffers, but none of these give a blind player a faction-segmented, nearest-first sweep of enemies with distance/bearing/HP/cover/threat, nor a whole-battlefield summary, nor a cover/LOS-to-a-chosen-target readout. The core gap is a combat-scoped tactical scanner that enumerates combatants, sorts by distance, and speaks a spatial+state line per target, backed by a review buffer for the full stat block. This is a new file that reuses MapCursor, Speaker, the InputCategory system, and BufferManager.

### VERIFIED GAME APIs
- **Kingmaker.Controllers.TurnBased.TurnController** (OK) @ decompiled/Kingmaker.Controllers.TurnBased/TurnController.cs:195-206
    Game.Instance.TurnController. Public: AllUnits (IEnumerable<MechanicEntity>), CurrentUnit ([CanBeNull] MechanicEntity), TurnBasedModeActive/InCombat/IsPlayerTurn (bool), UnitsAndSquadsByInitiativeForCurrentTurn/ForNextTurn (public). NOTE UnitsInCombat is INTERNAL (line 197) — not usable from the mod; use AllUnits.Where(u=>u.IsInCombat) or Game.State.AllBaseAwakeUnits instead.
- **Game.Instance.State.AllBaseAwakeUnits** (OK) @ decompiled/Kingmaker.Controllers.TurnBased/TurnController.cs:262,350 (usage); Game.State
    Public IEnumerable<BaseUnitEntity> iterated by EnumerateAllUnits and CanFinishDeploymentPhase. Best combatant source: .Where(u => u.IsInCombat && !u.IsDead && u.IsVisibleForPlayer).
- **MechanicEntity.IsPlayerEnemy / IsPlayerFaction / IsNeutral / IsInPlayerParty** (OK) @ decompiled/Kingmaker.EntitySystem.Entities/MechanicEntity.cs:106-114
    Faction predicates for segmentation. IsInPlayerParty via CombatGroup.IsPlayerParty; IsPlayerEnemy/IsPlayerFaction/IsNeutral via GetFactionOptional()->PartFaction.
- **MechanicEntity.IsEnemy / IsAlly / CanAttack** (OK) @ decompiled/Kingmaker.EntitySystem.Entities/MechanicEntity.cs:524-537
    Relative faction predicates via CombatGroup; useful to segment from the anchor's perspective.
- **PartFaction (unit.Faction)** (OK) @ decompiled/Kingmaker.UnitLogic.Parts/PartFaction.cs:34-84
    IsPlayer, IsHelpingPlayer, IsPlayerEnemy, Neutral, Peaceful, AlwaysEnemy. InteractableDescriber already uses unit.Faction.IsPlayerEnemy.
- **EntityHelper.DistanceToInCells(this MechanicEntity, MechanicEntity)** (OK) @ decompiled/Kingmaker.EntitySystem/EntityHelper.cs:53-56
    Size-aware tactical distance in grid cells (the WH40K cell distance). Also DistanceTo(->float metres), overloads to Vector3. Use for nearest-first sort + 'N tiles' readout.
- **MechanicEntity.IsVisibleForPlayer** (OK) @ decompiled/Kingmaker.EntitySystem.Entities.Base/Entity.cs:239 (virtual); MechanicEntityUIWrapper.cs:155
    Fog/visibility gate; InitiativeTrackerVM.CheckVisibiltyInTracker uses it. Gate the scan on this for sighted-parity.
- **LosCalculations (static)** (OK) @ decompiled/Kingmaker.View.Covers/LosCalculations.cs:13-508
    THE cover+LOS engine. GetWarhammerLos(MechanicEntity from, MechanicEntity to)->LosDescription (l.451); HasLos(MechanicEntity,MechanicEntity)->bool (l.228); GetBestShootingPosition(from,to or pos/size overloads) (l.173-191); GetCellCoverStatus(node,dir)->LosDescription (l.91, already used by InteractableDescriber); nested enum CoverType{None,Half,Full,Invisible} (l.21). LosDescription exposes .CoverType and .Obstacle.
- **OvertipCoverBlockVM.UpdateCover (pattern to replicate)** (OK) @ decompiled/Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts/OvertipCoverBlockVM.cs:89-125
    Authoritative 'cover the enemy has vs my current unit': GetBestShootingPosition(VirtualPositionController.GetDesiredPosition(currentUnit), currentUnit.SizeRect, Unit.Position, Unit.SizeRect) then GetWarhammerLos(bestPos, currentUnit.SizeRect, Unit). Only runs when IsPlayerTurn && Unit.IsPlayerEnemy — replicate the call, don't read the VM for arbitrary units.
- **OvertipHealthBlockVM / PartHealth (unit.Health)** (OK) @ decompiled/Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts/OvertipHealthBlockVM.cs:84-95
    unit.Health.HitPointsLeft / MaxHitPoints / TemporaryHitPoints (same path UnitBuffer uses). Respect MechanicsFeatureType.HideRealHealthInUI (l.54-64) as the VM does.
- **AttackOfOpportunityHelper.IsThreat (ext on BaseUnitEntity)** (OK) @ decompiled/Kingmaker.UnitLogic/AttackOfOpportunityHelper.cs:112,130,135
    attacker.IsThreat(target[, node]) -> bool: does attacker threaten target's cell (melee engagement / AoO). Use to speak 'threatening you'.
- **CombatEngagementHelper (ext on BaseUnitEntity)** (OK) @ decompiled/Kingmaker.UnitLogic/CombatEngagementHelper.cs:41-58
    GetEngagedUnits(), GetEngagedByUnits(), IsEngage(target), IsEngagedBy(attacker), IsEngagedInPosition(pos). Drives flanking/ganging: count GetEngagedByUnits() on a party member.
- **AbilityData.RangeCells / MinRangeCells / CanTargetFromNode** (OK) @ decompiled/Kingmaker.UnitLogic.Abilities/AbilityData.cs:376,378,1622-1668
    RangeCells (RuleCalculateAbilityRange), MinRangeCells; CanTargetFromNode(casterNode,targetNodeHint,target,out distance,out los,out UnavailabilityReasonType?) is the authoritative range+LOS+cover gate for 'in my weapon range'. Also CanTargetFromDesiredPosition(target)->bool.
- **UnitHelper.GetThreatHandMelee/Ranged/GetThreatHand (ext on MechanicEntity)** (OK) @ decompiled/Kingmaker.UnitLogic/UnitHelper.cs:440-561
    Return WeaponSlot for the unit's active melee/ranged threat hand; slot.Weapon.Blueprint.WeaponAbilities.Ability1.Ability gives the attack ability blueprint — a fallback for weapon reach until the AbilityData path is wired.
- **InitiativeTrackerVM (SurfaceHUDVM.InitiativeTrackerVM.Value)** (OK) @ decompiled/Kingmaker.Code.UI.MVVM.VM.SurfaceCombat/InitiativeTrackerVM.cs:24,141-233
    Public List<InitiativeTrackerUnitVM> Units (turn order, already surfaced by InGameScreen). CheckVisibiltyInTracker (l.218) shows the game's visibility gate. This is ORDER not POSITION — not a spatial radar, but the ready visible-combatant list.
- **BaseUnitEntity.CurrentUnwalkableNode + node grid coords** (OK) @ decompiled/RTAccess usage TileExplorer.cs:117,224; CustomGridNodeBase.XCoordinateInGrid/ZCoordinateInGrid
    Anchor/target node for MapCursor.Set and tile-offset bearing (RelativeTile already computes east/north from these). CameraRig.Instance.ScrollTo(node.position) follows.

### ALREADY BUILT (seams)
RTAccess already covers pieces of this facet but none of the combat sweep. TileExplorer.cs (E:/Games/modding/WH40KRTAccess/RTAccess/Accessibility/TileExplorer.cs) is an always-active per-tile cursor: arrows step one grid cell, and InteractableDescriber.DescribeTile reads occupant + walkability + per-edge cover + tile offset. It is faction-agnostic and per-tile — a blind player would have to sweep the whole grid cell by cell to find enemies. InteractableDescriber.cs already has the reusable seams the scanner needs: DirectionAndDistance(from,to) (metres + 8-way map compass), RelativeTile(node,anchor) (tiles east/north), AppendCover via LosCalculations.GetCellCoverStatus, and it already reads unit.Faction.IsPlayerEnemy for the "enemy"/"ally" tag. MapCursor.cs (RTAccess/Exploration) is the SHARED cursor the scanner should drive so camera + TileExplorer stay coupled. Buffers/UnitBuffer.cs already produces a full per-unit stat block (name, HP, AP/MP, absorption/deflection/dodge/parry, buffs) resolved live via a Func<BaseUnitEntity>; BufferManager.RegisterDefaults registers "Selected unit" and "Target" buffers — adding a third "Scanned combatant" buffer keyed to the scanner selection is a one-line change. InGameScreen.cs Combat region already lists initiative order (SurfaceHUDVM.InitiativeTrackerVM.Units) with a "current" tag, but that is turn ORDER, not battlefield POSITION. CombatEvents.cs streams damage/heal/death/downed via EventBus but has no spatial query. So the seams (cursor, buffer ring, distance/compass/cover helpers, faction tag) exist; the enumerate-and-sweep controller does not.

### GAPS
- No faction-segmented combatant scan: nothing cycles enemies (then allies/neutrals) nearest-first speaking name + faction + distance(cells)+bearing + HP + cover-vs-me + in-range/LOS + threat. The player can only step the faction-agnostic tile cursor cell by cell.
- No 'read the whole battlefield' summary key (e.g. counts per faction + nearest threat + who can hit me), so a blind player has no O(1) overview of the tactical situation.
- No cover/LOS-to-a-chosen-target readout: LosCalculations.GetWarhammerLos(from,to) gives Half/Full/Invisible cover and HasLos gives sight, but nothing surfaces it for an arbitrary scanned enemy relative to the active unit's shooting position (the game's OvertipCoverBlockVM only computes it for on-screen enemies during the player's turn).
- No threat / flanking / ganging readout: AttackOfOpportunityHelper.IsThreat and CombatEngagementHelper.GetEngagedByUnits/GetEngagedUnits are unused — a blind player cannot tell who threatens the active unit (melee engagement) or how many enemies are ganging a party member.
- No 'in my weapon range' cue: AbilityData.RangeCells + CanTargetFromNode exist but are not wired, so the player cannot tell whether a scanned enemy is reachable this turn.
- The scan is not coupled to the review buffer: UnitBuffer can already render a scanned unit's full stat block but there is no buffer whose resolver follows a scanner selection.

### RECOMMENDED APPROACH
## New file: `RTAccess/Accessibility/CombatScanner.cs` (combat-scoped, keyboard-driven)

A static controller modeled on the existing exploration `Scanner`, registered in a combat `InputCategory` so its keys go dead out of turn-based combat.

**1. Enumerate combatants (verified).** Use the public set and filter — do NOT use `TurnController.UnitsInCombat` (it is `internal`). Two good sources:
- `Game.Instance.State.AllBaseAwakeUnits` (public `IEnumerable<BaseUnitEntity>`), filtered `u.IsInCombat && !u.IsDead && u.IsVisibleForPlayer`. This mirrors `InitiativeTrackerVM.CheckVisibiltyInTracker` (which gates on `IsVisibleForPlayer`), keeping parity with what a sighted player sees (no fog cheating).
- or `Game.Instance.TurnController.UnitsAndSquadsByInitiativeForCurrentTurn` (public) if you want initiative-membership semantics; take `.OfType<BaseUnitEntity>()` to drop `UnitSquad` aggregates for spatial work.

**2. Segment by faction (verified, `MechanicEntity`):** enemies = `u.IsPlayerEnemy`; allies = `u.IsInPlayerParty` (party+pets) or `u.IsPlayerFaction`/`IsHelpingPlayerFaction`; neutral = `u.IsNeutral`. Relative predicates `anchor.IsEnemy(u)`/`IsAlly(u)`/`CanAttack(u)` are also available.

**3. Anchor + sort.** Anchor = `TurnController.CurrentUnit as BaseUnitEntity` on the player's turn, else `SelectionCharacter.SelectedUnit.Value` (same fallback chain BufferManager uses). Sort each segment nearest-first by `anchor.DistanceToInCells(u)` (int cells, size-aware — `EntityHelper.DistanceToInCells`).

**4. Per-target spoken line.** Compose from existing helpers:
- name + faction tag (reuse InteractableDescriber's `unit.CharacterName` + enemy/ally tag).
- distance + bearing: `anchor.DistanceToInCells(u)` for "N tiles", plus `InteractableDescriber.DirectionAndDistance(anchor.Position, u.Position)` for the 8-way compass (or `RelativeTile` for "3 east, 2 north").
- HP: `u.Health.HitPointsLeft`/`MaxHitPoints` (+`TemporaryHitPoints`), same path as UnitBuffer; respect `MechanicsFeatureType.HideRealHealthInUI` like OvertipHealthBlockVM does.
- cover-vs-me: replicate OvertipCoverBlockVM.UpdateCover — `LosCalculations.GetBestShootingPosition(anchor.Position, anchor.SizeRect, u.Position, u.SizeRect)` then `LosCalculations.GetWarhammerLos(bestPos, anchor.SizeRect, u)` → `CoverType` (None/Half/Full/Invisible→"no line of sight"). Do NOT read the VM (only exists for on-screen units on player turn).
- LOS: `LosCalculations.HasLos(anchor, u)`.
- threat: `((BaseUnitEntity)u).IsThreat(anchor)` (AttackOfOpportunityHelper) → "threatening you"; and `anchor.GetEngagedByUnits().Count()` for ganging.

**5. Drive the shared cursor.** On each scanner step, `MapCursor.Set(u.CurrentUnwalkableNode)` and `CameraRig.Instance.ScrollTo` so TileExplorer, the camera, and later cues all measure from the same point (exactly how the exploration Scanner couples to the cursor today).

**6. Whole-battlefield summary key.** Speak counts per segment + nearest enemy line + who threatens the anchor, e.g. "4 enemies, 2 allies. Nearest, Ork Boy, 3 tiles east, half cover, in range. Threatened by 1."

**7. Review buffer.** In `BufferManager.RegisterDefaults`, add `new UnitBuffer("Scanned combatant", () => CombatScanner.Current)` so Alt+arrows review the selected enemy's full stat block with zero new buffer code. Optionally extend UnitBuffer with spatial lines (distance/cover/threat) gated on combat.

**Key bindings (new combat InputCategory, live only when `TurnController.TurnBasedModeActive`):** cycle-nearest-enemy (e.g. `]`/`[` forward/back), Shift = allies, a summary key, and a "cover/LOS to current scan target" key. Register through Input/InputManager + InputCategory the same way TileExplorer keys are.

**"In my weapon range" (defer coordination):** use `AbilityData.RangeCells`/`MinRangeCells` and `AbilityData.CanTargetFromNode(casterNode, targetNodeHint, target, out distance, out los, out reason)` — the authoritative range+LOS+cover gate. Resolving the active weapon's AbilityData is shared with the attack/targeting facet; the scanner can start with distance-in-cells vs `GetThreatHandRanged()/GetThreatHandMelee()` weapon reach and adopt the AbilityData path once that facet lands.

### RISKS
- Visibility policy: gating on IsVisibleForPlayer keeps parity with sighted play (fogged enemies hidden), matching InitiativeTrackerVM; but a blind player may want fogged enemies too. Decide deliberately — revealing them diverges from the sighted UI and could be seen as cheating.
- Cover math must be replicated, not read from OvertipCoverBlockVM: that VM only populates CoverType when TurnController.IsPlayerTurn and Unit.IsPlayerEnemy, and only for units with a live overtip. Reading it for offscreen/ally/neutral units returns stale None. Call LosCalculations directly (GetBestShootingPosition + GetWarhammerLos).
- GetWarhammerLos/GetBestShootingPosition run linecasts over the grid; calling per-enemy on every scan step is fine for a handful of combatants but avoid recomputing for the whole battlefield every frame — compute on demand (on step / on summary key), not in an Update loop.
- UnitSquad appears in initiative enumerations (UnitsAndSquadsByInitiativeForCurrentTurn) and has no single grid node; filter to BaseUnitEntity for spatial lines or the distance/cover calls will misbehave.
- TurnController.UnitsInCombat is internal — reflection or the wrong overload will fail to compile/resolve; use Game.State.AllBaseAwakeUnits + IsInCombat or the public UnitsAndSquadsByInitiative* properties.
- Squad/lightweight/crowd units (LightweightUnitEntity) may lack full PartHealth or weapon slots; null-guard Health/GetThreatHand* as UnitBuffer already does.

### OPEN QUESTIONS
- Exact 'in my weapon range' computation: which AbilityData represents the active unit's default attack (primary vs secondary hand, melee vs ranged, selected ability), and whether to reuse the attack/targeting facet's resolver or compute from the equipped weapon's WeaponAbilities blueprint + RangeCells. Needs coordination with that facet.
- Should the scan include fogged/invisible enemies (IsVisibleForPlayer == false)? Requires a maintainer/design call on fairness vs completeness.
- How to represent multi-cell (large) enemies and squads in the distance/bearing line — nearest occupied cell vs entity Position centre; verify DistanceToInCells (size-aware) reads naturally when spoken.
- Whether cover-vs-me should assume the anchor stays put or uses VirtualPositionController.GetDesiredPosition(currentUnit) (as OvertipCoverBlockVM does) so the readout tracks a planned move — needs live in-game verification of how the desired position updates during aiming.
- Live check that GetWarhammerLos results match the on-screen cover pips for the same enemy (validate the replicated OvertipCoverBlockVM logic) via the dev harness.


################################################################
## AREA: Combat — Hit prediction & decision support   [effort: M]
################################################################

### SUMMARY
RT already computes everything a sighted player sees on hover — hit%, per-burst hit%, expected damage range, cover/dodge/parry/block/evasion mitigation, crit chance, in-range/LOS, and AoE who-gets-hit — through a self-contained, side-effect-free preview pipeline (RuleCalculateHitChances + AbilityTargetUIData + AbilityDataHelper.GetDamagePrediction). These can be invoked on demand for any (attacker, ability, target) WITHOUT the game's targeting mode being active; the overtip/LineOfSight VMs prove the exact call recipe. RTAccess currently reads ZERO of this: the tile cursor and action-bar proxies never surface a single prediction number. The core gap is a small "prediction readout" service wired to the enemy-tile cursor readout, keyed off the currently-selected ability.

### VERIFIED GAME APIs
- **RuleCalculateHitChances** (OK) @ decompiled\Kingmaker.RuleSystem.Rules\RuleCalculateHitChances.cs (ctor L126-151; ResultHitChance L50; ResultHitChanceNoUpperLimit L54; ResultRighteousFuryChance L72; ResultLos/ResultCoverEntity L58-64; ResultCoverHitChanceRule L64; DistanceFactor L88)
    The core dry-run hit-chance rule. new RuleCalculateHitChances(MechanicEntity initiator, target, AbilityData ability, int burstIndex[, casterPos, targetPos]) then Rulebook.Trigger / GameHelper.TriggerRule. ResultHitChance = raw hit% before defender avoidance (clamped to overkill border). ResultRighteousFuryChance = CRIT chance (RighteousFury == crit in RT); NOT surfaced by AbilityTargetUIData, so trigger this rule directly for crit%. Pure calculation, no state mutation.
- **AbilityTargetUIData (struct)** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityTargetUIData.cs (fields L33-61; ctor L85 with ref OverpenetrationUIData; UpdateWithWeapon L205-253; DisableStatefulRandomContext L87)
    THE preview aggregate the HUD shows. Constructing it runs the whole prediction: InitialHitChance (avg base hit%), HitWithAvoidanceChance (final hit% after dodge+parry+cover+block), MinDamage/MaxDamage, DodgeChance, ParryChance, CoverChance, BlockChance, EvasionChance, BurstIndex, BurstHitChances (List<float> per shot), HitAlways, CanPush. Clones ability as preview + wraps in ContextData<DisableStatefulRandomContext> => side-effect-free. Does NOT include crit chance.
- **AbilityTargetUIDataCache.GetOrCreate** (OK) @ decompiled\Kingmaker.UI.SurfaceCombatHUD\AbilityTargetUIDataCache.cs:77
    GetOrCreate(AbilityData, MechanicEntity target, Vector3 casterPosition) -> AbilityTargetUIData. Singleton .Instance (MonoBehaviour on the combat HUD). Caches per (ability,target,pos); auto-clears on turn/equipment/virtual-position change. Convenience wrapper over the AbilityTargetUIData ctor; can also bypass and construct the struct directly to avoid cache lifetime coupling.
- **AbilityDataHelper.GetDamagePrediction** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityDataHelper.cs:275
    ability.GetDamagePrediction(MechanicEntity target, Vector3 casterPosition, AbilityExecutionContext ctx=null) -> DamagePredictionData{MinDamage,MaxDamage,Penetration}. Wrapped in DisableStatefulRandomContext. Handles weapon attacks, melee burst (x RateOfFire), and AbilityEffectRunAction damage (incl. saving-throw min/max merge). Companion GetHealPrediction (L211) for heals.
- **AbilityDataHelper.GatherAffectedTargetsData** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityDataHelper.cs:567 (CheckAffectedEntity L639)
    ability.GatherAffectedTargetsData(OrientedPatternData pattern, Vector3 casterPos, TargetWrapper clickedTarget, in List<AbilityTargetUIData> listToFill, MechanicEntity targetEntity=null). AoE 'who-gets-hit': fills one AbilityTargetUIData per affected unit/destructible in the pattern (hit%, damage, cover per target). Pair with ability.GetPattern / GetPatternSettings().GetOrientedPattern to build the pattern nodes.
- **AbilityData.CanTargetFromNode / CanTargetFromDesiredPosition** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityData.cs:1616/1622 (CanTargetFromDesiredPosition L1596/1602; range check L1639-1651)
    CanTargetFromNode(casterNode, targetNodeHint, TargetWrapper target, out int distance, out LosCalculations.CoverType los, out UnavailabilityReasonType? reason). Returns is-target-legal (range + LOS + validity + pattern) AND the cell distance + cover. Use out-distance vs RangeCells for MP-to-close. CanTargetFromDesiredPosition(target) is the caster-current-position convenience overload used by the overtip.
- **AbilityData.RangeCells / MinRangeCells** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityData.cs:376/378
    RangeCells => RuleCalculateAbilityRange.TryGetCachedOrTrigger(this).Result (max range in grid cells). MinRangeCells for too-close. Subtract from CanTargetFromNode out-distance to get cell shortfall for 'X cells / MP to close'.
- **AbilityData.UnavailabilityReasonType (enum)** (OK) @ decompiled\Kingmaker.UnitLogic.Abilities\AbilityData.cs:76 (TargetTooFar 94, TargetTooClose 95, HasNoLosToTarget 93, NotEnoughAmmo 85); localized text map L1977-1992
    Reason a target is unavailable; maps to LocalizedTexts.Instance.Reasons.* for ready-made human strings (TargetTooFar/TargetTooClose/HasNoLosToTarget). Directly announceable when prediction can't be computed.
- **ActionBarSlotVM.AbilityData** (OK) @ decompiled\Kingmaker.Code.UI.MVVM.VM.ActionBar\ActionBarSlotVM.cs:118
    public AbilityData AbilityData { get { if (MechanicActionBarSlot is MechanicActionBarSlotAbility a) return a.Ability... } }. This is the AbilityData behind an action-bar slot — the mod's ProxyActionBarSlot already holds the ActionBarSlotVM, so _slot.AbilityData is the 'selected attack' source without any game targeting mode.
- **ClickWithSelectedAbilityHandler.Ability** (OK) @ decompiled\Kingmaker.Controllers.Clicks.Handlers\ClickWithSelectedAbilityHandler.cs:64
    Game.Instance.SelectedAbilityHandler.Ability = the ability currently in targeting mode (set when a slot is activated). Primary 'what is the player aiming' source; LineOfSightVM reads exactly this (L66).
- **LineOfSightVM.UpdateHitChance (reference recipe)** (OK) @ decompiled\Kingmaker.Code.UI.MVVM.VM.InGameCombat\LineOfSightVM.cs:178 (ability resolve L182; CanTargetFromNode L186; GetBestShootingPositionForDesiredPosition L188; GetOrCreate L203; HitWithAvoidanceChance L205)
    Authoritative end-to-end example: ability = SelectedAbilityHandler.Ability ?? currentWeapon.Abilities.First().Data; if CanTargetFromNode(node,...) then GetOrCreate(ability, target, bestShootingPos).HitWithAvoidanceChance. Also shows scatter/AoE branch via GatherAffectedTargetsData. Copy this pattern verbatim.
- **RuleCalculateHitChanceBorder** (OK) @ decompiled\Kingmaker.RuleSystem.Rules\RuleCalculateHitChanceBorder.cs (Result L26; ResultOverkillBorder L24)
    Caps ranged hit% at CombatRoot.HitChanceOverkillBorder (the reason ranged 'max hit%' isn't 100). Explains InitialHitChance vs the clamped ResultHitChance; useful context if announcing why a shot caps below 100.

### ALREADY BUILT (seams)
RTAccess has NO prediction reads today — this facet is greenfield. The seams it plugs into already exist: (1) RTAccess/UI/Proxies/ProxyActionBarSlot.cs holds the ActionBarSlotVM (_slot), whose _slot.AbilityData (ActionBarSlotVM.cs:118) is the selected attack — the proxy currently only reads title/AP/ammo/cooldown, never predictions. (2) RTAccess/Accessibility/TileExplorer.cs steps an always-active grid cursor; node.GetUnit() already yields the occupant and the readout is composed by InteractableDescriber.DescribeTile (Accessibility/InteractableDescriber.cs:76, occupant at :86) — the natural place to append a prediction line when the occupant is an enemy and an attack is selected. (3) The anchor/active unit is resolved via game.SelectionCharacter?.SelectedUnit?.Value (TileExplorer.cs:258) — that is the attacker (caster). (4) Buffers/UnitBuffer.cs already surfaces live AP/MP so MP-to-close can reuse its MP read. (5) Speech/Speaker is the announce facade. Nothing reads RuleCalculateHitChances, AbilityTargetUIData, GetDamagePrediction, RangeCells, or CanTargetFromNode anywhere in RTAccess.

### GAPS
- No hit% is ever spoken: a blind player selecting an attack and moving the cursor onto an enemy hears the occupant name but not the final hit chance (HitWithAvoidanceChance) a sighted player sees on the overtip.
- No expected-damage preview: MinDamage-MaxDamage (and penetration) from GetDamagePrediction is never surfaced, so the player cannot judge whether a shot can kill (the game even computes CanDie = MaxDamage >= HP; unused).
- No mitigation breakdown: cover% (CoverChance), dodge/parry/block/evasion are invisible, so the player can't reason about why a shot is bad or whether to reposition to strip cover.
- No crit chance: RighteousFury (crit) chance is computable (RuleCalculateHitChances.ResultRighteousFuryChance) but never spoken.
- No in-range / LOS feedback before committing: the player cannot tell an enemy is out of range or blocked without firing and hitting the WarningReader refusal; there is no proactive 'in range' / 'too far, N cells to close' cue.
- No burst breakdown: multi-shot weapons (BurstHitChances per shot) collapse to nothing; player can't tell a burst from a single shot.
- No AoE who-gets-hit: for pattern/AoE abilities GatherAffectedTargetsData enumerates every affected unit with per-target hit/damage, but the player gets no list of who is caught in a blast, incl. friendly-fire.
- No MP-to-close guidance: when out of range there is no 'move N cells / M MP to be in range' hint, even though CanTargetFromNode out-distance minus RangeCells yields the shortfall.

### RECOMMENDED APPROACH
Add a new static service `RTAccess/Accessibility/HitPredictor.cs` that produces a spoken readout for a (caster, ability, target) triple, then wire it into the existing enemy-tile readout and the action-bar. Follow LineOfSightVM.UpdateHitChance (LineOfSightVM.cs:178) verbatim — it is the maintained reference.

RESOLVING THE THREE INPUTS
- caster: game.SelectionCharacter?.SelectedUnit?.Value as MechanicEntity (already used in TileExplorer.cs:258).
- ability (the 'selected attack'): prefer `Game.Instance.SelectedAbilityHandler?.Ability` (ClickWithSelectedAbilityHandler.cs:64) when the player has activated a slot; else the currently-focused ProxyActionBarSlot's `_slot.AbilityData` (ActionBarSlotVM.cs:118); else fall back to the caster's default weapon attack `weapon.Abilities.FirstOrDefault()?.Data` (exactly LineOfSightVM.cs:182). This means prediction works WITHOUT entering the game's targeting mode — the rules are pure.
- target: node.GetUnit() (the enemy under the tile/target cursor), as MechanicEntity.

COMPUTING THE READOUT (all side-effect-free, wrap defensively in ContextData<DisableStatefulRandomContext> though the ctors already do):
1. Range/LOS: `ability.CanTargetFromNode(casterNode, null, targetWrapper, out int distance, out CoverType los, out UnavailabilityReasonType? reason)` (AbilityData.cs:1622). If false -> announce the localized reason (LocalizedTexts.Instance.Reasons.*); if reason==TargetTooFar also announce cells-to-close = distance - ability.RangeCells (AbilityData.cs:376).
2. Core preview: `var d = AbilityTargetUIDataCache.Instance.GetOrCreate(ability, target, bestShootingPos)` where bestShootingPos = `ability.GetBestShootingPositionForDesiredPosition(target.Position).Vector3Position` (LineOfSightVM.cs:188/203). Or bypass the cache and `new AbilityTargetUIData(ability, target, casterPos, ref overpen)` to avoid cache-lifetime coupling. Read: d.HitWithAvoidanceChance (final hit%), d.InitialHitChance (pre-avoidance), d.MinDamage/d.MaxDamage, d.CoverChance, d.DodgeChance, d.ParryChance, d.BlockChance, d.EvasionChance, d.BurstIndex, d.BurstHitChances, d.HitAlways.
3. Crit: `Rulebook.Trigger(new RuleCalculateHitChances(caster, target, ability, 0)).ResultRighteousFuryChance` (RuleCalculateHitChances.cs:72) — not on the struct.
4. AoE: if ability.IsAOE/IsScatter, build the pattern (`ability.GetPatternSettings().GetOrientedPattern(...)`) and call `ability.GatherAffectedTargetsData(pattern, casterPos, targetWrapper, in list)` (AbilityDataHelper.cs:567); announce count + each affected unit name with per-target hit%/damage, flagging allies for friendly-fire.

Compose one terse line, e.g.: "Ork Boy: 72% hit, 18-34 damage, crit 15%, half cover 25%, dodge 10%; burst 4." and a fuller breakdown on a modifier key.

WIRING / KEYS
- Passive: extend InteractableDescriber.DescribeTile (InteractableDescriber.cs:76) so that when the occupant is a player-enemy AND an attack ability is resolvable, it appends the short prediction line — gated so it only runs when an attack is actually selected (don't trigger the rule chain on every cursor step for non-combat).
- Explicit: add one dedicated key in InputCategory (e.g. a free key per rt-keyboard-usage) that speaks the FULL breakdown for the cursor's current enemy, so prediction is available on demand regardless of passive gating.
- Also consider surfacing hit% inside ProxyActionBarSlot.State() when a target is under the cursor, so scanning attacks reads their odds against the current mark.

### RISKS
- Side-effect safety: the preview ctors clone the ability as isPreview and wrap in DisableStatefulRandomContext, and overtip/LOS VMs call GetOrCreate every hover, so on-demand calls should be inert — but this must be confirmed live (/eval) that repeated HitPredictor calls don't perturb cooldowns/ammo/RNG.
- casterPosition parity: the game uses GetBestShootingPositionForDesiredPosition and VirtualPositionController.GetDesiredPosition, not raw caster.Position. Using the wrong position yields hit% that disagrees with the sighted overtip. Mirror LineOfSightVM's bestShootingPos exactly.
- Crit not on the struct: crit% must come from a second rule trigger (RuleCalculateHitChances.ResultRighteousFuryChance); need to confirm this equals the number the tooltip labels 'Righteous Fury'.
- Performance: constructing AbilityTargetUIData triggers many nested rules (hit, cover, dodge, parry, block, damage). Fine per keypress, but must NOT run automatically on every silent cursor step — gate behind an active-attack check or an explicit key.
- AbilityTargetUIDataCache is a HUD MonoBehaviour that may be null/cleared outside combat or between turns; guard Instance and prefer direct struct construction if it is unreliable.
- MP-to-close is only a straight-cell estimate from CanTargetFromNode out-distance; true reachability is pathfinding-dependent (obstacles/diagonals) and MP-per-cell cost must be verified.
- Mouse-mode engine gate [rt-mouse-mode-engine-gate] does not affect these rules (pure mechanics/VM reads), but confirm SelectedAbilityHandler.Ability is actually populated in mouse mode when the mod activates a slot.

### OPEN QUESTIONS
- Live via /eval: does RuleCalculateHitChances.ResultRighteousFuryChance match the crit% the game tooltip shows, across melee vs ranged?
- Live: exact MP-per-cell cost (and diagonal cost) so 'cells to close' can be converted to MP; and whether the active unit's remaining MP is the right budget (reuse UnitBuffer's MP read).
- Live: confirm AbilityTargetUIData.HitWithAvoidanceChance for a hovered enemy equals the on-screen overtip value for the same attacker/weapon/target (validates the position + cache recipe).
- Which casterPosition should the readout assume — the unit's current tile, or the tile the mod's move-to cursor is armed on? (Sighted play previews from the desired/virtual position.)
- Is Game.Instance.SelectedAbilityHandler.Ability reliably set in mouse mode after the mod's ProxyActionBarSlot.OnMainClick, or should HitPredictor always derive the ability from _slot.AbilityData instead?
- For AoE, confirm GatherAffectedTargetsData returns the same membership the sighted red-highlight shows, including destructibles and friendly units.


################################################################
## AREA: Combat-log narration (rolls, hits, misses, effects)   [effort: M]
################################################################

### SUMMARY
RT funnels every combat-log line through one shared base: LogThreadBase.AddMessage(CombatLogMessage), where CombatLogMessage.Message is the already-localized sentence a sighted player reads. There is a single, universal, channel-agnostic chokepoint to tap all rolls/hits/misses/dodges/parries/blocks/crits/damage/saves/momentum/ability results. The core gap: RTAccess's CombatEvents.cs voices outcomes (damage/heal/death/buff) off rule handlers but is completely blind to attack RESOLUTION (to-hit, miss, dodge/parry/block/deflect, crit) and to saves/skill-checks/momentum/casts — exactly the content only the log carries. The main design work is a one-Harmony-tap log narrator plus reconciling it with CombatEvents so damage/heal/death aren't double-spoken.

### VERIFIED GAME APIs
- **LogThreadBase.AddMessage(CombatLogMessage)** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem/LogThreadBase.cs:49
    THE universal chokepoint. protected instance method on the abstract base of EVERY log thread; all threads (PerformAttack, DealDamage, Healing, SavingThrow, Momentum, etc.) call it to publish a line. Guarded by !ContextData<GameLogDisabled>.Current && newMessage!=null (line 52) — replicate that guard in a postfix to skip preview/precalc messages. Harmony postfix target: static void Postfix(LogThreadBase __instance, CombatLogMessage newMessage).
- **LogThreadBase.ObserveAdd()** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem/LogThreadBase.cs:39
    Public IObservable<CollectionAddEvent<CombatLogMessage>> over the thread's ReactiveCollection. Non-Harmony alternative to the postfix: fires ONLY after the GameLogDisabled guard passed (inherently correct). Downside: must re-subscribe per game load and enumerate threads via LogThreadService.
- **CombatLogMessage.Message** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem/CombatLogMessage.cs:29
    The already-localized display string (e.g. 'X misses Y.', 'Y dodges.', 'X deals 12 damage to Y.'). Also: .Unit (MechanicEntity, line 33) for a visibility gate; .IsSeparator (line 21) — skip these; .ShotNumber (burst index); .Tooltip (TooltipBaseTemplate, the roll breakdown). Message may contain TMP rich-text (<b>…</b>, color) → run through TextUtil.StripRichText.
- **LogThreadService (thread taxonomy + channels)** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem/LogThreadService.cs:15-123
    Maps LogChannelType→List<LogThreadBase>. AnyCombat={RulebookDealDamage,UnitInitiative,UnitMissedTurn,InterruptCurrentTurn,Healing,RollSkillCheck,RulebookCastSpell,PartyUseAbility,AbilityImmunity,RulebookSavingThrow,RulePerformMomentumChange,AddSeparator,MergeRulePerformSavingThrow,PsychicPhenomenaAvoided}. InGameCombat={UnitLifeStateChanged,PerformAttack,GrenadeDealDamage,PerformScatterAttack,RuleRollScatterShotHitDirection,...,ContextActionKill}. Use this to build the type→category allow/deny filter (LogThreadService.Instance.AllThreads / GetThreadsByChannelType).
- **PerformAttackLogThread (attack resolution)** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat/PerformAttackLogThread.cs:58-128,689-784
    Produces the one-line attack Message via GetMessage(): WarhammerMiss/WarhammerDodge/WarhammerBlock/WarhammerParry/WarhammerDodgeAndParry/WarhammerRFHit(crit)/WarhammerDealDamage/WarhammerHitNoDamage/WarhammerDamageNegated + Superiority variants. Detailed to-hit%/d100/dodge%/crit breakdown lives in the message.Tooltip (TooltipTemplateCombatLogMessage.ExtraInfoBricks, set line 118) — NOT in Message. Weapon-attack damage is bundled into THIS message, not a separate DealDamage line (see RulebookDealDamageLogThread routing).
- **RulebookDealDamageLogThread (non-attack damage) + internal dedupe** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat/RulebookDealDamageLogThread.cs:40-45
    CreateMessage: if rule.SourceAttackRule != null it DEFERS to PerformAttackLogThread.CreateMessage (so weapon hits emit ONE bundled attack line, no double damage line). Standalone RuleDealDamage (DoT/environmental/ability) emits its own 'deals N damage' line. This is the game's own no-double-line behavior; the mod's reconciliation must mirror it.
- **CombatLogVM.AddNewMessage / Items / channels** (OK) @ decompiled/Kingmaker.UI.MVVM.VM.CombatLog/CombatLogVM.cs:39-101,162-175
    VM-level alternative tap. Per-channel: only the CURRENTLY-SELECTED channel's messages flow here, and only while the CombatLog window VM exists — so it MISSES lines and depends on UI state. Inferior to the LogThreadBase tap for narration; but Items (ReactiveCollection<CombatLogBaseVM>) is a ready-made backing store if a review buffer wants to mirror the on-screen log.
- **CombatLogItemVM.MessageText / TooltipTemplate** (OK) @ decompiled/Kingmaker.UI.MVVM.VM.CombatLog/CombatLogItemVM.cs:12,22,29-34
    Confirms MessageText = message.Message and TooltipTemplate = message.Tooltip; the roll-breakdown tooltip is a TooltipTemplateCombatLogMessage — walkable via the mod's existing Accessibility/TooltipReader.cs for a verbose 'read the roll math' mode.
- **RulePerformMomentumChangeLogThread / RulebookSavingThrowLogThread** (OK) @ decompiled/Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat/RulePerformMomentumChangeLogThread.cs:13-38
    Momentum: party-only, reasons StartTurn/KillEnemy/Wound/Trauma/AbilityCost via UIStrings.CombatLog.MomentumType*. Saving throws handled by RulebookSavingThrowLogThread + MergeRulePerformSavingThrowLogThread (files present in taxonomy). All emit through AddMessage → covered by the single tap for free.
- **GameLogController (event→thread routing, Tick)** (OK) @ decompiled/Kingmaker.UI.Models.Log/GameLogController.cs:113-143,196-211
    Runs on main-thread Tick; applies merge/swallow patterns (saving-throw merge, scatter/aoe conversion, attack-children sort) at the GameLogEvent level BEFORE threads call AddMessage. So by the time AddMessage fires, merging is already resolved — tapping AddMessage yields final, deduped per-line text. No need to reimplement any of it.

### ALREADY BUILT (seams)
RTAccess/Accessibility/CombatEvents.cs is an EventBus subscriber (IDamageHandler/IHealingHandler/IUnitDeathHandler/IUnitBuffHandler) that voices OUTCOMES off rule handlers, NOT off the log: damage dealt (RuleDealDamage.Result), heal (RuleHealDamage.Value), death/downed (IUnitDeathHandler + LifeState.IsDead), and buff gained/lost via a per-frame add/remove reconciler. It has a frame-flushed non-interrupt queue (_pending, flushed in Tick, pumped from Main.OnUpdate line 92), faction/visibility gating (ShouldRead: party always, others only IsVisibleForPlayer), and localized templates in ui.json. WarningReader.cs voices refusal toasts (IWarningNotificationUIHandler). BarkEvents.cs is the proven parallel-subscriber pattern (rich-text strip + short-window dedupe) — the direct template for a log narrator. TextUtil.StripRichText handles TMP tags. Buffers/{Buffer,BufferManager,UnitBuffer}.cs provide the Alt+arrow review-buffer ring with FollowLatest support (BufferManager.cs:53 already jumps to the last line for append-only streams) — ready to host a log scrollback buffer. Accessibility/TooltipReader.cs can render tooltip templates (path to verbose roll math). Main.cs wires subscribers at load and pumps CombatEvents.Instance.Tick() per frame — the exact seam to add a narrator tick. Screens/InGameScreen.cs Combat region (End-turn + initiative list) is where a log-review container / 'read last N lines' hotkey would surface.

### GAPS
- Attack RESOLUTION is entirely unspoken: hit vs miss, dodge, parry, block, deflect, dodge+parry, and Righteous Fury (crit). A blind player hears 'Enemy takes 12 damage' (CombatEvents) but never hears a MISS, a DODGE, a PARRY, or a CRIT — the single most important combat feedback. Only the log (PerformAttackLogThread) carries these.
- To-hit context is missing: which attacker, which weapon/ability, burst shot number, the hit-chance% and the d100 roll. The one-line Message gives hit/miss; the % and roll live in the message.Tooltip bricks (verbose mode).
- Saving throws (success/resisted), skill checks, ability/spell casts, immunity ('immune'), psychic phenomena, interrupt/missed-turn are never spoken — all log-only.
- Momentum/Desperate-measures & Heroic-act economy: momentum gained/lost with reason (KillEnemy/Wound/StartTurn) is unspoken; blind players can't track the party momentum bar.
- Damage BREAKDOWN by type + armor absorption/deflection/penetration/overpenetration/reflection is invisible: CombatEvents says only a bare number; the log line adds damage type and 'negated'/'deflected', and the tooltip has the full math.
- No scrollback: a burst of combat lines flushes to speech once and is gone; there is no way to review the last exchange (the game's on-screen log is inaccessible). BufferManager exists but has no log buffer.
- Double-speak hazard: if a naive log narrator is added, weapon hits would be voiced twice (log 'X hits for N' + CombatEvents 'Y takes N'); heals/deaths likewise. Requires explicit reconciliation.

### RECOMMENDED APPROACH
## New file: RTAccess/Accessibility/CombatLogNarrator.cs (one Harmony tap + queue + filter + scrollback)

**The tap (single, universal).** Harmony postfix on `LogThreadBase.AddMessage(CombatLogMessage newMessage)` (LogThreadBase.cs:49). Signature:
`static void Postfix(LogThreadBase __instance, CombatLogMessage newMessage)`.
In the postfix, replicate AddMessage's own guard and skip: `newMessage == null`, `ContextData<GameLogDisabled>.Current` (Kingmaker.UI.Models.Log.ContextFlag), `newMessage.IsSeparator`, and empty `Message`. Patch once at load (survives game reloads — unlike ObserveAdd, which recreates per-game). Register via the existing `HarmonyInstance.PatchAll` (Main.cs:38) using an annotated patch class, OR AccessTools since AddMessage is protected.

**Category filter (which threads to speak).** `CombatLogMessage` does not store its channel, but the postfix has `__instance`, so filter by thread type. Build the map once from `LogThreadService.Instance.GetThreadsByChannelType(...)` (LogThreadService.cs:151) → a `HashSet<Type>` of thread types to speak. v1 allow-set = the combat resolution/effect threads: PerformAttackLogThread, PerformScatterAttackLogThread, GrenadeDealDamageLogThread, RulebookDealDamageLogThread, HealingLogThread, RulebookSavingThrowLogThread + MergeRulePerformSavingThrowLogThread, RollSkillCheckLogThread, RulebookCastSpellLogThread, PartyUseAbilityLogThread, AbilityImmunityLogThread, RulePerformMomentumChangeLogThread, ContextActionKillLogThread, UnitLifeStateChangedLogThread, InterruptCurrentTurnLogThread, UnitMissedTurnLogThread, PsychicPhenomenaAvoidedLogThread. Deny: Dialog threads (BarkEvents owns those), LifeEvents loot/XP, Common colony/navigator noise, WarningNotification (WarningReader owns it). Expose as a settings category later (Settings/ infra exists).

**Text + queue + gate.** `line = TextUtil.StripRichText(newMessage.Message)`; optional visibility gate `newMessage.Unit is BaseUnitEntity u ? party||u.IsVisibleForPlayer : true` (mirror CombatEvents.ShouldRead). Enqueue to a frame-flushed `List<string>` and flush non-interrupt via `Speaker.Speak(line, interrupt:false)` in a new `CombatLogNarrator.Instance.Tick()` added next to `CombatEvents.Instance.Tick()` (Main.cs:92). This matches [[rt-interrupt-speech-rule]] (combat reads are passive). Add a short same-text dedupe window (copy BarkEvents.cs:72-76).

## Reconciliation with CombatEvents (avoid double-speak) — RECOMMENDED: log-authoritative

Make the combat log the single authority for in-combat resolution+damage and RETIRE the overlapping CombatEvents paths. The log line is the exact sentence a sighted player reads, is richer (attacker name, damage type, 'negated'/'deflected', crit), and the game already prevents double damage lines (RulebookDealDamageLogThread defers weapon-attack damage to the bundled attack message, RulebookDealDamageLogThread.cs:42). Concretely:
- Disable/remove `CombatEvents.HandleDamageDealt`, `HandleHealing`, and `HandleUnitDeath` speech (the log's RulebookDealDamage/Healing/UnitLifeStateChanged/ContextActionKill threads cover them). Keep CombatEvents ONLY as the per-frame buff gain/loss reconciler (the log's buff handling — RulebookCanApplyBuff/MergeRuleCalculateCanApplyBuff — reads as save-vs-buff, not a clean 'gained X', so the existing reconciler is still the better UX for buffs), OR gate all three behind a setting so users can pick.
- Note the log narrator will now voice enemy-vs-enemy exchanges the rule-handlers also saw; keep the visibility gate on the narrator (message.Unit → IsVisibleForPlayer) to preserve current behavior.

**Lighter-touch alternative (Option 2):** keep CombatEvents for damage/heal/death, and in the narrator SUPPRESS PerformAttack/DealDamage lines that carry damage (speak only miss/dodge/parry/block/negated resolutions where the attack dealt no damage). Result: hit → CombatEvents 'Enemy takes 12'; miss → narrator 'Dodged'. Zero double-speak, minimal edits, but loses attacker name + damage type on hits. Detect via GetMessage's result branch (rule.ResultIsHit / ResultDodgeRule.Result etc.) — but that requires reading the RulePerformAttack, so it's simpler to just diff against a set of 'damage-bearing' string templates. Option 1 is cleaner; recommend it.

## Scrollback review buffer (reuse Buffers/)

Add `RTAccess/Buffers/LogBuffer.cs : Buffer` with `FollowLatest = true`, a fixed cap (e.g. last 100 lines). The narrator's tap appends each spoken line via `LogBuffer.Instance.Append(line)` (Buffer.Add exists; add a trim-to-cap). Register it in `BufferManager.RegisterDefaults()` (BufferManager.cs:65). Alt+Left/Right selects the 'Combat log' buffer, Alt+Up/Down scrubs prior lines — BufferManager.cs:53 already jumps to the latest on entry. This gives review with no new keybinding surface.

## Verbose 'read the roll math' (optional, phase 2)

The hit-chance%, d100 roll, dodge/parry/block chances, and full damage breakdown are in `message.Tooltip` (TooltipTemplateCombatLogMessage, ExtraInfoBricks set at PerformAttackLogThread.cs:118). A hotkey on the LogBuffer's current line can feed message.Tooltip to the existing Accessibility/TooltipReader.cs to speak the brick breakdown. Store the CombatLogMessage (not just the string) in LogBuffer entries to enable this.

### RISKS
- Double-speak if reconciliation is skipped: weapon hits/heals/deaths are carried by BOTH the log and CombatEvents' rule handlers. Must disable the overlapping CombatEvents paths (Option 1) or damage-filter the narrator (Option 2).
- GameLogDisabled context: AddMessage early-returns without adding when ContextData<GameLogDisabled>.Current is set (preview/precalc). A blind postfix would speak phantom preview lines — must replicate the guard (or use ObserveAdd, which is inherently correct).
- Verbosity/flooding: a full burst attack + damage + momentum + saves can be many lines/turn. Non-interrupt queue helps, but needs a category allow-set and likely a per-category settings toggle to keep it usable.
- Rich text + placeholder residue: Message is TMP rich text (<b>, color); StripRichText handles tags but verify no leftover template placeholders ({source}/{target} are pre-resolved by TextTemplateEngine before AddMessage, so they should be filled — confirm in-game).
- Separators & merge artifacts: IsSeparator messages and merged saving-throw/scatter lines flow through AddMessage; filter separators and verify merged lines read as one clean sentence.
- Thread lifetime for the ObserveAdd alternative: LogThreadService is Game-lifetime, threads recreate on load — if chosen over Harmony, must re-subscribe each game start and dispose. Recommend the Harmony tap to avoid this.
- message.Unit is null for non-unit/environmental lines and MapObjectEntity is excluded (CombatLogMessage.cs:49); the visibility gate must fail-open on null Unit or those lines go silent.

### OPEN QUESTIONS
- Live verify the actual one-line phrasing for miss/dodge/parry/block/crit/negated (WarhammerMiss etc.) and that placeholders are fully resolved at AddMessage time — capture real strings via the dev harness during a fight.
- Decide the reconciliation policy with the maintainer: fully retire CombatEvents damage/heal/death (Option 1) vs damage-filter the narrator (Option 2) — affects whether attacker-name/damage-type is spoken on hits.
- Confirm enemy-vs-enemy log lines should be gated by IsVisibleForPlayer (matches CombatEvents) or spoken always (a blind player may want full battlefield awareness) — a settings choice.
- Confirm TooltipReader can render TooltipTemplateCombatLogMessage's ExtraInfoBricks (TooltipBrickChance/TooltipBrickDamageRange/etc.) into readable speech for the verbose mode — may need brick-type formatting.
- Which categories default ON: is momentum/saves/skill-checks wanted in v1 or is hit/miss/damage the minimal set? Needs maintainer call on default verbosity.


################################################################
## AREA: Command dispatch mechanism & engine gates   [effort: M]
################################################################

### SUMMARY
RT issues every combat action through one funnel: build a `UnitCommandParams` subclass and call `unit.Commands.Run(...)` (`PartUnitCommands`). Abilities AND weapon attacks are the same thing — there is no `UnitAttack` command; both are `PlayerUseAbilityParams` dispatched by the static helper `UnitCommandsRunner.TryUnitUseAbility(AbilityData, TargetWrapper, shouldApproach)`. The full validation+warnings+targeting path lives one layer up in the pointer-controller click handler `ClickWithSelectedAbilityHandler` (`Game.Instance.SelectedAbilityHandler`). All of these are mechanics/pointer-layer calls that survive mouse mode; the `!IsControllerMouse` gate only kills the gamepad `SurfaceMainInputLayer` interactable-navigation loop, not the command API or the click handlers. The core gap is that the mod has no combat command service yet — only a proven move-to-cursor exists in TileExplorer.

### VERIFIED GAME APIs
- **PartUnitCommands.Run(UnitCommandParams)** (OK) @ Kingmaker.UnitLogic.Commands/PartUnitCommands.cs:106
    unit.Commands.Run(params) -> UnitCommandHandle (nullable). Clears queue then runs; may route through Game.Instance.UnitCommandBuffer.TryAdd for netcode (single-player returns false -> RunImmediate). Also AddToQueue(:230), AddToQueueFirst(:244), ForceAddToQueue(:239), RunImmediate(:121).
- **PartUnitCommands.CanRun** (OK) @ Kingmaker.UnitLogic.Commands/PartUnitCommands.cs:130
    Gate: returns Owner.LifeState.IsConscious && cmdParams.IsDirectionCorrect. If false, Run() releases the path and returns null (SILENT failure). IsDirectionCorrect for abilities = firing-arc check (UnitUseAbilityParams.cs:108).
- **PartUnitCommands.RunInternal input lock** (OK) @ Kingmaker.UnitLogic.Commands/PartUnitCommands.cs:169
    When Owner.IsInCombat, running a command calls Game.Instance.PlayerInputInCombatController.RequestLockPlayerInputWithSource(this) until the command finishes. Affects the game's own input; the mod owns its keyboard separately.
- **UnitCommandsRunner.TryUnitUseAbility** (OK) @ Kingmaker.Controllers.Units/UnitCommandsRunner.cs:188
    Static. TryUnitUseAbility(AbilityData abilityData, TargetWrapper target, bool shouldApproach=false). Builds PlayerUseAbilityParams via CreateUseAbilityCommandParams (:518), pulls multi-targets from Game.Instance.SelectedAbilityHandler.MultiTargetHandler, raises IClickActionHandler.OnCastRequested/OnItemUseRequested, then commands.AddToQueue(cmd) (optionally after an approach UnitMoveToParams). THE canonical ability/attack dispatch used by the action bar.
- **UnitCommandsRunner.MoveSelectedUnitsToPoint / MoveSelectedUnitToPointTB** (OK) @ Kingmaker.Controllers.Units/UnitCommandsRunner.cs:236,248
    Static. Routes TB vs RT. TB path validates single selected == CurrentUnit, calls baseUnitEntity.TryCreateMoveCommandTB(MoveCommandSettings{Destination,DisableApproachRadius}, showMovePrediction:true, out status) and speaks status. RT path is formation-aware MoveSelectedUnitsToPointRT (:331).
- **UnitHelper.TryCreateMoveCommandTB (ext)** (OK) @ Kingmaker.UnitLogic/UnitHelper.cs:873,879
    BaseUnitEntity.TryCreateMoveCommandTB(MoveCommandSettings, bool showMovePrediction, out MoveCommandStatus) -> UnitMoveToProperParams. Runs FindPathTB_Blocking + RuleCalculateMovementCost, returns null with a status enum (NotEnoughMovementPoints/DestinationUnreachable/SamePath/... :200). Feed result to Commands.Run. Already used by the mod.
- **PlayerUseAbilityParams / UnitUseAbilityParams** (OK) @ Kingmaker.UnitLogic.Commands/PlayerUseAbilityParams.cs:27, UnitUseAbilityParams.cs:25
    PlayerUseAbilityParams(AbilityData, TargetWrapper) is the player-issued ability command (serializes ability by UniqueId for netcode; sets AllTargets, flip-pattern). UnitUseAbilityParams(AbilityData, TargetWrapper) is the base. This covers weapon attacks too — no separate UnitAttack command exists.
- **UnitMoveToParams / UnitMoveToProperParams** (OK) @ Kingmaker.UnitLogic.Commands/UnitMoveToParams.cs:18, UnitMoveToProperParams.cs:21
    UnitMoveToParams(ForcedPath, TargetWrapper, approachRadius=0.3, leaveFollowers) = RT/continuous move. UnitMoveToProperParams(ForcedPath, apPerCell, ...) = TB grid move (AP-costed, DefaultMovementType Run). Both need a pre-built ForcedPath; use the helper rather than constructing by hand.
- **ClickWithSelectedAbilityHandler (Game.Instance.SelectedAbilityHandler)** (OK) @ Kingmaker.Controllers.Clicks.Handlers/ClickWithSelectedAbilityHandler.cs:211,269,295; Kingmaker/Game.cs:600
    The targeting handler. SetAbility(AbilityData) (:295) enters PointerMode.Ability + fires IAbilityTargetSelectionUIHandler. OnClick(GameObject,Vector3 worldPos,int button,simulate=false,muteEvents=false) (:211) resolves TargetWrapper via GetTarget (:162), runs ShouldHandleAbilityCastFail -> range/LoS/TargetRestrictions with IWarningNotificationUIHandler warnings + fail sound, accumulates multi-targets, then calls UnitCommandsRunner.TryUnitUseAbility and ClearPointerMode. FULL targeting+validation+dispatch.
- **MechanicActionBarSlotAbility.OnClick** (OK) @ Kingmaker.UI.Models.UnitSettings/MechanicActionBarSlotAbility.cs:137
    For TargetAnchor!=Owner: Game.Instance.SelectedAbilityHandler.SetAbility(Ability) (arms targeting, waits for a target click). For Owner-anchored (self-buff): UnitCommandsRunner.TryUnitUseAbility(Ability, Unit) immediately. This is what ProxyActionBarSlot.OnMainClick already reaches.
- **ClickMapObjectHandler.Interact / HasAvailableInteractions** (OK) @ Kingmaker.Controllers.Clicks.Handlers/ClickMapObjectHandler.cs:127,117
    Static. Interact(GameObject, List<BaseUnitEntity> units, forceOvertipInteractions=false, muteEvents=false). Line 131 has the mouse-mode branch: (!IsControllerMouse || !item.Settings.ShowOvertip || forceOvertipInteractions). Already used by the mod for world objects; the interactions route to UnitCommandsRunner.DirectInteract / UnitInteractWithObject.
- **ClickUnitHandler / IUnitClickUIHandler** (OK) @ Kingmaker.Controllers.Clicks.Handlers/ClickUnitHandler.cs:97,107
    OnClick handles unit clicks (attack/interact/select). Right-click raises IUnitClickUIHandler.HandleUnitRightClick for inspect; the mod's Inspect.cs already uses IUnitClickUIHandler.HandleUnitConsoleInvoke for the inspect panel.
- **SurfaceMainInputLayer.OnUpdate mouse gate** (OK) @ Kingmaker.Code.UI.MVVM.View.Surface.InputLayers/SurfaceMainInputLayer.cs:107
    if (!Game.Instance.IsControllerMouse && LayerBinded.Value) { ... UpdateInteractions(); }. The interactable-navigation loop (m_InteractableObjects, :484) is DEAD in mouse mode. This is [rt-mouse-mode-engine-gate]. It gates only this gamepad/console input layer — NOT PartUnitCommands.Run, UnitCommandsRunner, or the pointer-controller click handlers.
- **Game.DefaultPointerController / TurnController guards** (OK) @ Kingmaker/Game.cs:345; Kingmaker.Controllers.TurnBased/TurnController.cs:95,109,206,189
    DefaultPointerController holds the click handlers. TurnController.TurnBasedModeActive(:95), IsPlayerTurn(:109), CurrentUnit(:206), IsPreparationTurn(:189) are the turn/controllability gates a dispatch service must check before hand-rolled commands.

### ALREADY BUILT (seams)
The mod already dispatches through the game's own paths for world/exploration, establishing the exact pattern to reuse for combat: (1) RTAccess/Accessibility/TileExplorer.cs:147-176 has a WORKING combat move — in TB it calls `unit.TryCreateMoveCommandTB(new MoveCommandSettings{Destination=node.Vector3Position,DisableApproachRadius=true}, false, out status)` then `unit.Commands.Run(cmd)` guarded by IsPlayerTurn + unit==CurrentUnit + IsDirectlyControllable + a two-press confirm; out of combat it calls `UnitCommandsRunner.MoveSelectedUnitsToPoint`. This is the canonical move dispatch already proven live. (2) RTAccess/Exploration/ProxyMapObject.cs:119-139 drives `ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions:true)` — the object-interaction dispatch, already handling the mouse-mode overtip gate. (3) RTAccess/Exploration/Inspect.cs raises `IUnitClickUIHandler.HandleUnitConsoleInvoke`. (4) RTAccess/UI/Proxies/ProxyActionBarSlot.cs activates via `ActionBarSlotVM.OnMainClick()`, which already correctly self-casts Owner-anchored abilities immediately and arms `SelectedAbilityHandler.SetAbility` for targeted ones. Seam: there is NO ability/attack target-delivery, no point-target cast, and no unified combat command service — grep confirms Commands.Run appears only in TileExplorer.

### GAPS
- No ability/attack dispatch to a chosen target: ProxyActionBarSlot arms SelectedAbilityHandler.SetAbility for targeted abilities but nothing then delivers the target 'click' — a blind player selects an ability and nothing happens.
- No point/AoE target cast path (ground-targeted abilities, grenades, movement-abilities like Charge that end on a tile).
- No unified combat command service — each facet would otherwise re-implement guards, netcode buffering, and refusal speech independently.
- No spoken refusal when a command silently drops: Commands.Run returns null on !IsConscious or wrong firing arc, and ClickWithSelectedAbilityHandler.OnClick swallows range/LoS failures into on-screen warnings a blind player can't see (must be re-surfaced via WarningReader).
- No multi-target (chain) handling: TryUnitUseAbility reads SelectedAbilityHandler.MultiTargetHandler.Targets, which is only populated by going through SetAbility+OnClick per target; a direct TryUnitUseAbility call bypasses it.
- No End-Turn / turn-flow command wiring in the dispatch layer (InGameScreen has an End-turn button but no shared command entry point).

### RECOMMENDED APPROACH
## Add one service: `RTAccess/Combat/CommandDispatch.cs` (static) that ALL combat facets call through.

**Prefer the game's own click/confirm path (option a) over hand-building params (option b)** for abilities/attacks, because it runs full targeting+validation+warnings+multi-target+approach and is exactly what the game does. Hand-built `Commands.Run` (option b) is correct only for movement (no pointer/targeting semantics) and self-cast.

### Ability / weapon attack on a UNIT target
```
// arm targeting on the game's real handler, then synthesize the confirm click
var h = Game.Instance.SelectedAbilityHandler;              // ClickWithSelectedAbilityHandler
h.SetAbility(abilityData);                                  // enters PointerMode.Ability
var view = targetUnit.View;                                // AbstractUnitEntityView
bool ok = h.OnClick(view.gameObject, view.transform.position, 0 /*LMB*/);
```
`OnClick` resolves the TargetWrapper, runs `ShouldHandleAbilityCastFail` (range/LoS/TargetRestrictions -> IWarningNotificationUIHandler), handles multi-target accumulation, calls `UnitCommandsRunner.TryUnitUseAbility`, and clears pointer mode — identical to a mouse click, and it is a pointer-controller call so the `!IsControllerMouse` gate does NOT touch it. This mirrors the mod's existing ClickMapObjectHandler.Interact/Inspect approach. The mod should subscribe to `IWarningNotificationUIHandler` (WarningReader already does) so the swallowed refusals are spoken.

### Ability on a POINT/tile (AoE, grenades, charge)
Same pattern but click a walkable GameObject at the tile, or (if no GameObject) call `UnitCommandsRunner.TryUnitUseAbility(ability, new TargetWrapper(node.Vector3Position, orientation, null), shouldApproach)` directly. `TargetWrapper` ctors verified: (MechanicEntity) / (Vector3) / (Vector3, orientation) / (Vector3, orientation, MechanicEntity) at TargetWrapper.cs:126-141.

### Self / Owner-anchored ability
`UnitCommandsRunner.TryUnitUseAbility(abilityData, abilityData.Caster)` (or just keep `ActionBarSlotVM.OnMainClick`, which already does this).

### Movement (reuse the proven TileExplorer path)
- TB: `var cmd = unit.TryCreateMoveCommandTB(new MoveCommandSettings{Destination=node.Vector3Position, DisableApproachRadius=true}, false, out var status); if (cmd!=null) unit.Commands.Run(cmd); else speak(status);`
- RT/out of combat: `UnitCommandsRunner.MoveSelectedUnitsToPoint(node.Vector3Position);`

### Central guard (do this once in the service, before any hand-rolled command)
`Game.Instance.TurnController.TurnBasedModeActive && IsPlayerTurn && SelectedUnit==CurrentUnit && unit.IsDirectlyControllable()` — the hand-rolled TB command bypasses the engine guards UnitCommandsRunner enforces, so the service must enforce them itself (TileExplorer already documents this).

### API the service should expose (so targeting/movement/action-bar facets share it)
`UseAbilityOnUnit(AbilityData, BaseUnitEntity)`, `UseAbilityOnPoint(AbilityData, CustomGridNodeBase, approach)`, `UseSelfAbility(AbilityData)`, `MoveTo(CustomGridNodeBase)`, `Interact(MapObjectEntity)` (existing), `EndTurn()`. Each returns success + speaks refusals. The targeting facet's job is only to produce the correct AbilityData + target; dispatch is centralized here.

### RISKS


### OPEN QUESTIONS
- Does ClickWithSelectedAbilityHandler.OnClick reliably fire in mouse mode when invoked programmatically (it should, as a pointer-controller call), and does SetAbility require any camera/selection state the blind flow lacks?
- For point-target abilities without a clickable GameObject, is TryUnitUseAbility with a Vector3 TargetWrapper sufficient, or does the game rely on SelectedAbilityHandler state (pattern/rotation) set during OnClick? Verify AoE orientation is correct.
- After a queued attack finishes, does PlayerInputInCombatController auto-unlock so the next mod command runs, or must the service wait on the UnitCommandHandle?
- Do weapon-attack action-bar slots (burst/single/aimed) all go through MechanicActionBarSlotAbility.OnClick -> SetAbility, i.e. is 'attack' fully covered by the ability path with no special-case? (Grep found no plain UnitAttack command, but confirm live.)
- Confirm the two-press-confirm UX used for movement should also gate attacks (irreversible AP spend) or whether inspect-before-confirm is enough.
