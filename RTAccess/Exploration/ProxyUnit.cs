using Kingmaker;                                 // Game
using Kingmaker.Controllers.Clicks.Handlers;     // ClickUnitHandler (loot a corpse)
using Kingmaker.EntitySystem.Entities;           // BaseUnitEntity
using Kingmaker.UI.Common;                        // UIUtilityUnit.GetSurfaceEnemyDifficulty (enemy threat tier)
using Kingmaker.UnitLogic;                        // HasMechanicFeature (ext)
using Kingmaker.UnitLogic.Enums;                  // MechanicsFeatureType (HideRealHealthInUI), UnitCondition (Stunned)
using Kingmaker.UnitLogic.Parts;                  // UnitPartInteractions (HasDialogInteractions)
using Kingmaker.UnitLogic.Squads;                 // GetSquadOptional (squad grouping)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A scannable unit (party member, NPC, enemy). While alive, faction decides its single primary node and the review
/// group it cycles under; the spoken detail is faction word + condition (dead/unconscious) or HP (+ in combat).
/// Living units aren't interactable (no talk/attack) — interaction is map-objects-only. A DEAD unit that still has
/// loot is the one exception: it flips its primary node to <see cref="ScanTaxonomy.Corpses"/> (leaving the faction
/// cycles) and becomes interactable, so <c>I</c> loots it via the game's own unit click — a blind player reaches a
/// body exactly like a chest (mirrors WrathAccess; no dedicated corpse key). An emptied/lootless corpse carries no
/// node and drops out of the scanner entirely.
/// </summary>
internal sealed class ProxyUnit : ScanItem
{
    private readonly BaseUnitEntity _unit;

    public ProxyUnit(BaseUnitEntity unit) { _unit = unit; }

    public override object Key => _unit;

    public override string Name => _unit.CharacterName;

    public override Vector3 Position => _unit.Position;

    // This item IS a unit — the target the game's unit-targeted ability click wants.
    public override bool IsUnit => true;
    public override BaseUnitEntity TargetUnit => _unit;

    // Real footprint radius (metres) — a large creature reads distance/bearing to its nearest edge, not its centre,
    // and sorts by that edge, so an adjacent ogryn reads "here"/"1 tile" rather than its centre's ~2 tiles.
    public override float Footprint => _unit.Corpulence;

    // The player's OWN party is always known (the game always shows your party on the map), even though the engine's
    // IsVisibleForPlayer flag reports false for owned units when they aren't the current "spotlight" (out of combat /
    // not the acting unit). So player-faction units are always listed and always "seen"; everyone else is
    // reveal-latched on IsVisibleForPlayer and fog-gated for the review cycles.
    public override bool IsVisible => _unit.IsPlayerFaction || _unit.IsVisibleForPlayer;

    public override bool CurrentlySeen => _unit.IsPlayerFaction || (_unit.IsVisibleForPlayer && !_unit.IsInFogOfWar);

    // A corpse — dropped from the party/enemy/neutral review cycles and the unit category browse (Scanner), matching
    // the game's own enemy navigation (SurfaceCombatInputLayer.IsValidEnemy gates the same !LifeState.IsDead). Death
    // is State==Dead only, so downed-but-unconscious (revivable) companions are NOT corpses and stay listed/inspectable.
    public override bool IsDead => _unit.LifeState.IsDead;

    // A dead unit that still has loot and can be looted now (out of combat) — the game's own gate: looting a body
    // is blocked in combat (ClickUnitHandler), and a dead unit isn't even clickable then. So in combat a corpse is
    // simply absent from the scanner; out of combat, a lootable body appears (and an emptied one never does).
    public override bool LootableCorpse
        => _unit.IsDeadAndHasLoot && Game.Instance?.Player?.IsInCombat != true;

    // A lootable corpse classifies as a CORPSE (a container-like thing), not by faction — that pulls it out of the
    // party/enemy/neutral cycles (which key on Primary) and into the Corpses category / object cycle. Living units
    // (and dead-but-lootless ones, which the scanner filters out anyway) keep their faction node.
    public override string Primary => LootableCorpse ? ScanTaxonomy.Corpses : FactionNode();

    public override IEnumerable<string> Nodes
    {
        get { yield return Primary; }
    }

    // Actionable via the scanner's generic I: a lootable corpse (loots like a chest), OR a living unit the game itself
    // offers a click interaction on — a talkable NPC / merchant / dialog spawner. We don't hand-gate the living case:
    // we ask the game the same question its own click asks (SelectClickInteraction != null), so I offers a talk exactly
    // where a sighted click would. Interact() already routes both cases through ClickUnitHandler.OnClick.
    public override bool CanInteract => LootableCorpse || HasClickInteraction;

    // Does the game offer a click interaction (dialogue / merchant / spawner) on this LIVING unit right now? Mirrors
    // ClickUnitHandler.HandleClickUnit, whose interaction path only fires out of combat — in combat a unit click
    // targets, it never talks — so we advertise it only then. The initiator is the selected party member nearest the
    // unit (else the main character), the one GetNearestSelectedUnit would pick, so SelectClickInteraction's
    // per-initiator availability matches the real click.
    private bool HasClickInteraction
    {
        get
        {
            if (_unit.LifeState.IsDead || _unit.IsInCombat || Game.Instance?.Player?.IsInCombat == true) return false;
            try
            {
                var initiator = Initiator();
                return initiator != null && _unit.SelectClickInteraction(initiator) != null;
            }
            catch { return false; }
        }
    }

    // True when at least one of this unit's click interactions is a conversation (DialogOnClick / dialog spawner), so
    // the browse-label can say "talk" instead of the generic "interact".
    private bool HasDialogInteraction
    {
        get { try { return _unit.GetOptional<UnitPartInteractions>()?.HasDialogInteractions == true; } catch { return false; } }
    }

    // The party member the game would use to start a click interaction with this unit: the selected unit nearest it,
    // else the main character. Mirrors ClickUnitHandler's GetNearestSelectedUnit so our actionable answer tracks OnClick.
    private BaseUnitEntity Initiator()
    {
        BaseUnitEntity best = null;
        float bestSqr = float.MaxValue;
        var selected = Game.Instance?.SelectionCharacter?.SelectedUnits;
        if (selected != null)
        {
            foreach (var u in selected)
            {
                if (u == null) continue;
                float d = (u.Position - _unit.Position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = u; }
            }
        }
        return best ?? Game.Instance?.Player?.MainCharacterEntity;
    }

    // Loot the body through the game's OWN unit-click dispatch — the same context-sensitive handler a mouse click
    // hits, which for a dead-and-has-loot unit out of combat walks the nearest selected party member over and opens
    // its loot window (the ShortUnit LootScreen). Reused verbatim (as ProxyMapObject reuses ClickMapObjectHandler),
    // so approach, coop, and acting-unit selection all match the sighted click. Mirrors WrathAccess's ProxyUnit.
    public override bool Interact()
    {
        var view = _unit.View;
        if (view == null) return false;
        return new ClickUnitHandler().OnClick(view.gameObject, view.transform.position, 0);
    }

    public override string Detail
    {
        get
        {
            try
            {
                var bits = new List<string> { FactionWord() };
                var life = _unit.LifeState;
                if (life.IsDead)
                {
                    bits.Add(Loc.T("unit.dead"));
                    // The game marks an opened-but-not-emptied body by highlight color only (VisitedLootColor
                    // in AbstractUnitEntityView.GetHighlightColor) — color-only on screen, so voice it. Session-
                    // scoped like the game's own flag (BaseUnitEntity.m_LootViewed is not persisted to the save).
                    if (LootableCorpse && _unit.LootViewed) bits.Add(Loc.T("scan.already_opened"));
                }
                else if (!life.IsConscious)
                {
                    bits.Add(Loc.T("unit.unconscious"));
                }
                else
                {
                    var health = _unit.Health;
                    if (health != null)
                        // Honor the game's HideRealHealthInUI mask (fog-independent — the "???" concealed-HP units;
                        // audit L2). Our IsVisible/CurrentlySeen fog gate does NOT cover it, so guard HP directly.
                        bits.Add(_unit.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                            ? Loc.T("scan.unit_hp_hidden")
                            : Loc.T("scan.unit_hp", new { current = health.HitPointsLeft, max = health.MaxHitPoints }));
                    // #15 Enemy difficulty tier — the roman-numeral threat rating the game shows on an enemy
                    // (SurfaceCombatUnitVM.ShowDifficulty → UIUtilityUnit difficulty = DifficultyType+1). Enemy-only
                    // (party/neutral carry no tier), gated on the enemy being visible so it never leaks pre-reveal;
                    // spoken as a plain integer for TTS rather than the on-screen roman numeral.
                    if (_unit.IsPlayerEnemy && _unit.IsVisibleForPlayer)
                    {
                        int tier = UIUtilityUnit.GetSurfaceEnemyDifficulty(_unit);
                        if (tier > 0) bits.Add(Loc.T("unit.threat_tier", new { n = tier }));
                    }
                    // #17 squad grouping — the tracker collapses a squad to its leader's card with an
                    // alive-count badge; on the battlefield the members are ordinary visible units with no
                    // cue that they act as one initiative slot. Tag each: the leader carries the strength,
                    // members their membership. Leader mirrors SurfaceCombatUnitVM's formula — the flagged
                    // leader, falling back to the first LIVING member once that leader is dead.
                    if (_unit.IsPlayerFaction || _unit.IsVisibleForPlayer)
                    {
                        var squad = _unit.GetSquadOptional()?.Squad;
                        if (squad != null)
                        {
                            int alive = 0;
                            object firstAlive = null;
                            foreach (var r in squad.Units)
                            {
                                var e = r.Entity;
                                if (e == null || e.IsDead) continue;
                                alive++;
                                firstAlive ??= e;
                            }
                            if (alive > 1)
                                bits.Add(_unit.IsSquadLeader || ReferenceEquals(firstAlive, _unit)
                                    ? Loc.T("unit.squad_leader", new { count = alive })
                                    : Loc.T("unit.squad_member"));
                        }
                    }
                    if (_unit.IsInCombat) bits.Add(Loc.T("unit.in_combat"));
                    // #7 Turn-status marker — mirrors SurfaceCombatUnitVM's own priority (will-lose-turn folds
                    // Stunned/Helpless/Prone; then control-loss; then generic unable-to-act). A combat-tracker
                    // concept, so only while in combat; both allies and enemies, but gated so a not-yet-seen enemy's
                    // incapacitation never leaks (party units are always known).
                    if (_unit.IsInCombat && (_unit.IsPlayerFaction || _unit.IsVisibleForPlayer))
                    {
                        var marker = StatusMarker();
                        if (marker != null) bits.Add(marker);
                    }
                }
                // Advertise the game's own click interaction so the player knows I does something on this unit — "talk"
                // for a conversation, "interact" otherwise (merchant / spawner). Mirrors the interaction highlight a
                // sighted player sees on the card.
                if (HasClickInteraction)
                    bits.Add(Loc.T(HasDialogInteraction ? "scan.unit_talk" : "scan.unit_interact"));
                return string.Join(", ", bits);
            }
            catch
            {
                return FactionWord();
            }
        }
    }

    // The passive tactical tail for an enemy, relative to the acting unit (whose turn it is, else the selected
    // unit). Enemies only; dead enemies carry nothing tactical. The heavy read lives in CombatReads (shared with
    // the battlefield summary); this just resolves the observer and the enemy/dead gate.
    protected override string CombatSuffix()
    {
        try
        {
            if (!_unit.IsPlayerEnemy || _unit.LifeState.IsDead) return null;
            var me = Game.Instance?.TurnController?.CurrentUnit as BaseUnitEntity
                     ?? Game.Instance?.SelectionCharacter?.SelectedUnit?.Value;
            var bits = new List<string>();
            var tail = RTAccess.Accessibility.CombatReads.CoverRangeThreat(me, _unit);
            if (!string.IsNullOrWhiteSpace(tail)) bits.Add(tail);

            // #24 Enemy starship void shields — the current/max total a sighted player reads off the overtip shield
            // block (OvertipHealthBlockVM.UpdateEnemyShields, itself gated IsPlayerEnemy). Enemy is already gated
            // above; require visibility for the reveal. We sum all four sector shields (ShieldsSum/ShieldsMaxSum) —
            // the whole-ship strength — rather than a per-hit sector. Predicted shield damage is aim-time only: it
            // needs the armed ability + its StarshipWeapon, and this passive tail is suppressed while aiming (the
            // aiming hit-line covers it), so it belongs to the aim-announce path, not here (see #24 gap).
            if (_unit.IsVisibleForPlayer && _unit is StarshipEntity ship)
            {
                var shields = ship.Shields;
                if (shields != null && shields.ShieldsMaxSum > 0)
                    bits.Add(Loc.T("unit.shields", new { current = shields.ShieldsSum, max = shields.ShieldsMaxSum }));
            }
            return bits.Count > 0 ? string.Join(", ", bits) : null;
        }
        catch { return null; }
    }

    // #7 The turn-status marker for a unit, mirroring SurfaceCombatUnitVM.UpdateCanActStates' own priority:
    // will-lose-turn (Stunned || Helpless || Prone) outranks a control-loss effect, which outranks a generic
    // can't-act-mechanically. Null when the unit is fine. A pure read of the unit's own State part — the same
    // fields the combat tracker binds. Caller supplies the in-combat + visibility gate.
    private string StatusMarker()
    {
        var st = _unit.State;
        if (st == null) return null;
        if (st.HasCondition(UnitCondition.Stunned) || st.IsHelpless || st.IsProne)
            return Loc.T("unit.will_lose_turn");
        if (_unit.HasControlLossEffects())
            return Loc.T("unit.control_loss");
        if (!st.CanActMechanically)
            return Loc.T("unit.unable_to_act");
        return null;
    }

    private string FactionNode()
        => _unit.IsPlayerFaction ? ScanTaxonomy.UnitsParty
         : _unit.IsPlayerEnemy ? ScanTaxonomy.UnitsEnemies
         : ScanTaxonomy.UnitsNeutrals;

    private string FactionWord()
        => Loc.T(_unit.IsPlayerFaction ? "scan.faction.party"
              : _unit.IsPlayerEnemy ? "scan.faction.enemy"
              : "scan.faction.neutral");
}
