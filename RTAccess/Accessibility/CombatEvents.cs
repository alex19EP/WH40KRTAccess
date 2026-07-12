using System;
using System.Collections.Generic;
using Kingmaker;                         // Game
using Kingmaker.AreaLogic.TimeSurvival;  // TimeSurvival ("survive N rounds" areas)
using Kingmaker.Blueprints;              // BlueprintScriptableObject.GetComponent<T>()
using Kingmaker.Controllers.Combat;      // GetCombatStateOptional extension
using Kingmaker.EntitySystem.Entities;   // BaseUnitEntity, MechanicEntity
using Kingmaker.Mechanics.Entities;      // AbstractUnitEntity
using RTAccess.Speech;
using UnityEngine;                       // Mathf

namespace RTAccess.Accessibility;

/// <summary>
/// The combat announcement pipeline: owns the shared per-frame speech queue that <see cref="LogTap"/> feeds
/// (every non-owned game-log line flows in via <see cref="EnqueueLogLine"/>), plus the turn-lifecycle and
/// HUD-threshold polls voicing the cues the game does NOT write to its log. Pumped once per frame from
/// <c>Main.OnUpdate</c> via <see cref="Tick"/> (no EventBus subscription — everything here is poll- or tap-driven).
///
/// Attack resolution (hit/miss/dodge/parry/crit), damage, cover, healing, deaths AND buff/debuff gains are all
/// read from the game's combat log — authoritative, correctly grouped, richer — tapped by <see cref="LogTap"/>
/// and enqueued here so they interleave with the lifecycle/threshold cues in arrival order. Buffs used to be
/// reconciled here off the EventBus, but the game's own buff-application log threads
/// (Rulebook/MergeRuleCalculateCanApplyBuff) already group a multi-target application into one line and honour
/// every hidden/own-self filter, so they own gains directly now. The log has no buff-removal thread, so buff
/// EXPIRY is no longer announced — a deliberate sighted-parity call (the log never shows expiry either).
///
/// <see cref="Tick"/> POLLS the turn machinery (<see cref="PollLifecycle"/>) for the legibility cues a sighted
/// player reads off the HUD chrome — combat start/end, whose-turn, and the deployment phase —
/// and the HUD pressure gauges (<see cref="PollThresholds"/>). (Round advance is NOT ours: the game's own
/// "Round N" banner toast — <c>InitiativeTrackerView.RoundChanged</c> — reaches speech via
/// <see cref="WarningReader"/> already localized in the game's language.) These are polled rather than driven off the
/// turn-start/round EventBus interfaces on purpose: those are entity-targeted and unreliable for a global
/// subscriber (WrathAccess's turn-ended handler is famously never raised), whereas <c>TurnController</c> state
/// is authoritative every frame. Cues share the same <see cref="_pending"/> queue so a burst ("Ork takes 5
/// damage… Your turn") reads in arrival order.
///
/// The frame-flushed queue (<see cref="_pending"/>) is ported from WrathAccess: taps/polls fire mid-game-frame,
/// so we collect lines and speak them once per frame in arrival order — a burst reads as a clean sequence,
/// never mid-frame, and never interrupts (combat reads are passive; see [[rt-interrupt-speech-rule]]).
/// </summary>
internal sealed class CombatEvents
{
    internal static readonly CombatEvents Instance = new CombatEvents();

    // Lines collected this frame (in arrival order), flushed in Tick.
    private readonly List<string> _pending = new List<string>();

    // ---- Lifecycle poll state (see PollLifecycle) — last observed values, announced on transition. ----
    // Turn identity is tracked by UniqueId, not object reference: CurrentUnit can return a fresh entity
    // instance for the same logical unit across frames, so a reference compare would re-announce mid-turn.
    private string _lastTurnUnitId;
    private int _survivalRound = -1;   // last round the TimeSurvival rounds-left tail spoke for
    private bool _wasInCombat;
    private bool _wasInPrep;

    // ---- Threshold-cue state (S7; see PollThresholds) — last observed band, cue fires on the crossing. ----
    private bool _turnTimerLow;
    private int _bossHpBucket = -1;   // last 25% boss-HP band (floor(progress*4)); -1 = boss bar not shown
    private bool _etudeFailed;        // last-seen etude-counter FAIL/SUCCESS flip state (rising-edge cues)
    private bool _etudeSucceeded;
    private bool _etudeSeen;          // baseline taken — the first sight of an already-flipped state never cues

    /// <summary>Pumped once per frame from <c>Main.OnUpdate</c>: run the lifecycle/threshold polls, then flush
    /// every queued line (log taps + poll cues, in arrival order) as passive speech.</summary>
    public void Tick()
    {
        try { PollLifecycle(); }
        catch (Exception e) { Main.Log?.Log("combat lifecycle poll failed: " + e.Message); }

        try { PollThresholds(); }
        catch (Exception e) { Main.Log?.Log("threshold poll failed: " + e.Message); }

        if (_pending.Count == 0) return;
        // Snapshot count: speaking won't re-enter and grow this, but be defensive — only flush what's
        // present now, keep anything queued during the flush.
        int n = _pending.Count;
        for (int i = 0; i < n; i++) Speaker.Speak(_pending[i], interrupt: false);
        _pending.RemoveRange(0, n);
    }

    private void Enqueue(string line)
    {
        if (!string.IsNullOrEmpty(line)) _pending.Add(line);
    }

    /// <summary>Enqueue a line captured from the game's combat log (see <see cref="LogTap"/>) into the
    /// shared per-frame queue, so log resolution lines interleave with lifecycle/buff cues in arrival order.</summary>
    internal void EnqueueLogLine(string line) => Enqueue(line);

    // ---- Turn/combat lifecycle cues (polled once per frame) ----
    // Reads authoritative TurnController/Player state and enqueues a cue whenever a tracked value changes:
    // combat start/end, deployment (preparation) phase, round advance, and whose-turn. See the class summary
    // for why these are polled rather than EventBus-driven. Enqueued into the shared _pending queue so they
    // interleave with damage/buff lines in arrival order.
    private void PollLifecycle()
    {
        var game = Game.Instance;
        var tc = game?.TurnController;
        if (tc == null) return;

        // Combat start / end — party-level truth. Fires regardless of turn-based vs real-time combat.
        bool inCombat = game.Player?.IsInCombat ?? false;
        if (inCombat != _wasInCombat)
        {
            _wasInCombat = inCombat;
            Enqueue(Message.Localized("ui", inCombat ? "combat.started" : "combat.ended").Resolve());
            if (!inCombat) { _lastTurnUnitId = null; _survivalRound = -1; _wasInPrep = false; }
        }

        // The remaining cues (whose-turn, deployment) are turn-based-only chrome. Reset the turn poll
        // when out of TB so re-entering combat re-announces the first unit rather than staying silent.
        if (!inCombat || !tc.TurnBasedModeActive) { _lastTurnUnitId = null; return; }

        // Deployment (preparation) phase — CurrentUnit is null during it, so the turn poll below won't catch it.
        // "Deployment phase" on entry (RTAccess.Exploration.DeploymentMode.Tick follows it with the controls line);
        // "Battle begins" on exit. The exit cue is emitted HERE, not from DeploymentMode, so it shares this ordered
        // queue and reliably precedes the round / whose-turn cues below even on a battle-start frame where prep-end
        // and the first turn-start collapse into one poll.
        bool prep = tc.IsPreparationTurn;
        if (prep && !_wasInPrep)
            Enqueue(Message.Localized("ui", "combat.deployment").Resolve());
        else if (!prep && _wasInPrep)
            Enqueue(Message.Localized("ui", "deploy.battle_begins").Resolve());
        _wasInPrep = prep;

        // Round advance is deliberately NOT cued here: the game's own "Round N" banner toast
        // (InitiativeTrackerView.RoundChanged, round > 1) is voiced by WarningReader in the game's
        // language. Round 1 has no toast, but it coincides with "Battle begins" / combat start, and the
        // battle status line carries the labelled round for review.
        //
        // TimeSurvival areas ("survive N rounds") are the exception (Phase 6): their sighted counter
        // ticks down beside the tracker and the game's round toast there is a bare NUMBER, so follow
        // each round advance with the labelled count. Silent everywhere else.
        int survRound = tc.CombatRound;
        if (survRound != _survivalRound)
        {
            _survivalRound = survRound;
            if (survRound > 1)
            {
                var ts = game.CurrentlyLoadedArea?.GetComponent<TimeSurvival>();
                if (ts != null && !ts.UnlimitedTime)
                    Enqueue(Message.Localized("ui", "spacecombat.rounds_left", new { n = ts.RoundsLeft }).Resolve());
            }
        }

        // Whose turn — announce on change of the acting unit, keyed by stable UniqueId. Ignoring null ids
        // (rather than tracking them) keeps _lastTurnUnitId pinned to the last real actor, so neither a
        // between-turns null nor a fresh entity instance for the same unit re-announces the same turn.
        var cur = tc.CurrentUnit;
        var curId = cur?.UniqueId;
        if (curId != null && curId != _lastTurnUnitId)
        {
            _lastTurnUnitId = curId;
            Enqueue(TurnCue(tc, cur));
        }
    }

    // The whose-turn line: player turns read "Your turn, X, 2 AP, 6 MP" (the acting unit's economy, matching
    // the HUD status line); non-player turns read "X's turn" with an ", enemy" tag for hostiles.
    // Starships have no yellow-AP economy in play (weapons fire on charges), so a player ship — or a
    // commandable torpedo salvo, also a StarshipEntity — reads movement + speed mode instead
    // ("Your turn, X, movement 11, normal speed"); the Phase-0 transcript's "0 AP" was pure noise.
    private static string TurnCue(Kingmaker.Controllers.TurnBased.TurnController tc, MechanicEntity cur)
    {
        string name = (cur as AbstractUnitEntity)?.CharacterName ?? cur.Name;
        if (tc.IsPlayerTurn)
        {
            var cs = (cur as BaseUnitEntity)?.GetCombatStateOptional();
            if (cur is StarshipEntity ship && cs != null)
                return Message.Localized("ui", "combat.your_turn_ship", new
                {
                    name,
                    mp = Mathf.RoundToInt(cs.ActionPointsBlue),
                    speed = RTAccess.Screens.SpaceCombatScreen.SpeedModeWord(
                        ship.Navigation?.SpeedMode ?? Kingmaker.SpaceCombat.StarshipLogic.Parts.PartStarshipNavigation.SpeedModeType.Normal),
                }).Resolve();
            if (cs != null)
                return Message.Localized("ui", "combat.your_turn",
                    new { name, ap = cs.ActionPointsYellow, mp = Mathf.RoundToInt(cs.ActionPointsBlue) }).Resolve();
            return Message.Localized("ui", "combat.your_turn_simple", new { name }).Resolve();
        }
        bool enemy = (cur as BaseUnitEntity)?.IsPlayerEnemy ?? false;
        return Message.Localized("ui", enemy ? "combat.turn_enemy" : "combat.turn", new { name }).Resolve();
    }

    // ---- HUD gauge threshold cues (S7), polled once per frame ----
    // Passive one-shots for the pressure gauges a sighted player watches on the HUD that the game does NOT log:
    // the turn timer's last five seconds, and each 25% the boss loses. Edge-detected like PollLifecycle so each
    // fires once per crossing, enqueued into the shared passive queue, self-gated on its VM's visibility.
    // (Momentum and veil ARE logged by the game, so they come from LogTap instead — see below.)
    private void PollThresholds()
    {
        var sp = Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM;
        if (sp == null)
        {
            _turnTimerLow = false;
            _bossHpBucket = -1;
            _etudeFailed = _etudeSucceeded = _etudeSeen = false;
            return;
        }

        // Momentum (every change, with reason) and veil thickness (every change, with value) are voiced by LogTap
        // — the game logs both (RulePerformMomentumChange / VeilThicknessLogThread) — so no threshold cue is
        // emitted here. The on-demand K gauge (HudGauges) still reads momentum readiness + the veil critical band.

        // Turn timer entering its last five seconds (only while the timer is on-screen).
        var timer = sp.TurnTimerVM;
        bool low = timer != null && timer.IsShowing.Value && timer.IsFiveSecsLeft.Value;
        if (low && !_turnTimerLow) Enqueue(Loc.T("cue.turn_timer_low"));
        _turnTimerLow = low;

        // Boss HP: announce each 25% band as it drops (75 / 50 / 25). Bucket = floor(progress*4) in 0..3; a
        // decrease into band b (<=2) means HP just fell below (b+1)*25 percent. No cue on first sight or a heal.
        var boss = sp.BossHPBarVM;
        if (boss != null && boss.IsShowing.Value)
        {
            int bucket = Mathf.Clamp(Mathf.FloorToInt(boss.Progress.Value * 4f), 0, 3);
            if (_bossHpBucket >= 0 && bucket < _bossHpBucket && bucket <= 2)
                Enqueue(Loc.T("cue.boss_hp", new { percent = (bucket + 1) * 25 }));
            _bossHpBucket = bucket;
        }
        else _bossHpBucket = -1;

        // Etude counter FAIL/SUCCESS flip (main-HUD audit #6): raised over the EventBus with no game-log
        // line, so LogTap structurally can't carry it; the sighted cue is a red label / success icon swap
        // plus a semantics-free stinger. Rising-edge only, with a first-sight baseline (like the boss-HP
        // poll) so loading a save / re-entering an area with an already-flipped counter narrates nothing —
        // that state wasn't an event (review finding); the K gauge readout carries the persistent state.
        var etude = sp.EtudeCounterVM;
        if (etude != null && etude.IsShowing.Value)
        {
            bool ef = etude.IsSystemFailEnabled.Value, es = etude.IsSystemSuccessEnabled.Value;
            if (_etudeSeen)
            {
                if (ef && !_etudeFailed) Enqueue(Loc.T("cue.objective_failed", new { label = etude.Label.Value }));
                else if (es && !_etudeSucceeded) Enqueue(Loc.T("cue.objective_succeeded", new { label = etude.Label.Value }));
            }
            _etudeFailed = ef;
            _etudeSucceeded = es;
            _etudeSeen = true;
        }
        else { _etudeFailed = _etudeSucceeded = false; _etudeSeen = false; }
    }

    // Damage, healing, deaths AND buff/debuff gains were read here off the EventBus; they now all come from the
    // combat log (authoritative, correctly grouped, richer) via LogTap. Buff expiry has no log thread and is no
    // longer announced (sighted parity — the log never shows expiry either). See the class summary.
}
