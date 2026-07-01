using System;
using System.Collections.Generic;
using Kingmaker;                         // Game
using Kingmaker.PubSubSystem;            // IUnitBuffHandler
using Kingmaker.Controllers.Combat;      // GetCombatStateOptional extension
using Kingmaker.EntitySystem.Entities;   // BaseUnitEntity, MechanicEntity
using Kingmaker.Mechanics.Entities;      // AbstractUnitEntity
using Kingmaker.UnitLogic.Buffs;         // Buff
using RTAccess.Speech;
using UnityEngine;                       // Mathf

namespace RTAccess.Accessibility;

/// <summary>
/// The combat announcement pipeline: one persistent <see cref="Kingmaker.PubSubSystem.Core.EventBus"/>
/// subscriber (mirroring <see cref="BarkEvents"/>) that voices buff/debuff gains and losses, and owns the
/// shared per-frame speech queue plus the turn-lifecycle poll. Subscribed once at mod load and unsubscribed
/// at unload (see <see cref="Main"/>); <see cref="Tick"/> is pumped once per frame from <c>OnUpdate</c>.
///
/// Attack resolution (hit/miss/dodge/parry/crit), damage, cover, healing and deaths are NOT read here — the
/// game's combat log is authoritative for those and is tapped by <see cref="CombatLogReader"/>, whose lines
/// flow into this class's <see cref="_pending"/> queue via <see cref="EnqueueLogLine"/>. Buffs stay here
/// because the log records buff APPLICATION but has no removal thread, so only this reconciler can announce
/// expiry — and keeping both here yields one consistent gain/loss voice.
///
/// <see cref="Tick"/> also POLLS the turn machinery (<see cref="PollLifecycle"/>) for the legibility cues a
/// sighted player reads off the HUD chrome — combat start/end, round advance, whose-turn, and the deployment
/// phase. These are polled rather than driven off the turn-start/round EventBus interfaces on purpose: those
/// are entity-targeted and unreliable for a global subscriber (WrathAccess's turn-ended handler is famously
/// never raised), whereas <c>TurnController</c> state is authoritative every frame. Cues share the same
/// <see cref="_pending"/> queue so a burst ("Ork takes 5 damage… Your turn") reads in arrival order.
///
/// Two load-bearing ideas are ported from WrathAccess (the WOTR sibling mod), the rest of its settings
/// machinery dropped for a lean v1:
/// <list type="bullet">
/// <item>A frame-flushed queue (<see cref="_pending"/>): handlers fire mid-game-frame, so we collect lines
/// and speak them once per frame in arrival order — a burst reads as a clean sequence, never mid-frame, and
/// never interrupts (combat reads are passive; see [[rt-interrupt-speech-rule]]).</item>
/// <item>A per-frame buff reconciler: the game raises add/remove on every refresh/re-apply and for hidden
/// system buffs, so we mirror its combat-log filter (skip <c>Buff.Hidden</c> — already folds in
/// <c>Blueprint.IsHiddenInUI</c> + suppressed — and empty-name buffs) and reconcile each frame's churn
/// against an active set keyed by (owner, blueprint): only a genuine gain (newly active) or loss (was
/// active and NOT re-added this frame — a re-add is a refresh) is announced.</item>
/// </list>
///
/// Faction classification drives VISIBILITY gating, not wording: party units always read (you can check
/// portraits/conditions any time), while enemy/neutral units read only while perceptible
/// (<see cref="Kingmaker.EntitySystem.Entities.Base.Entity.IsVisibleForPlayer"/>, which already folds in
/// in-game + fog-of-war state). Spoken lines use the existing localized templates in <c>ui.json</c>.
/// </summary>
internal sealed class CombatEvents : IUnitBuffHandler
{
    internal static readonly CombatEvents Instance = new CombatEvents();

    // Lines collected this frame (in arrival order), flushed in Tick.
    private readonly List<string> _pending = new List<string>();

    // Buffs currently announced as active, keyed by (unit, blueprint).
    private readonly HashSet<BuffKey> _active = new HashSet<BuffKey>();
    // This frame's raw adds/removes (last Buff for a key wins), reconciled in Tick.
    private readonly Dictionary<BuffKey, Buff> _frameAdds = new Dictionary<BuffKey, Buff>();
    private readonly Dictionary<BuffKey, Buff> _frameRemoves = new Dictionary<BuffKey, Buff>();

    // ---- Lifecycle poll state (see PollLifecycle) — last observed values, announced on transition. ----
    // Turn identity is tracked by UniqueId, not object reference: CurrentUnit can return a fresh entity
    // instance for the same logical unit across frames, so a reference compare would re-announce mid-turn.
    private string _lastTurnUnitId;
    private int _lastRound = -1;
    private bool _wasInCombat;
    private bool _wasInPrep;

    /// <summary>Pumped once per frame from <c>Main.OnUpdate</c>: reconcile the frame's buff churn into
    /// genuine gain/loss lines, then flush every queued line (in arrival order) as passive speech.</summary>
    public void Tick()
    {
        try { Reconcile(); }
        catch (Exception e) { Main.Log?.Log("combat reconcile failed: " + e.Message); }

        try { PollLifecycle(); }
        catch (Exception e) { Main.Log?.Log("combat lifecycle poll failed: " + e.Message); }

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

    /// <summary>Enqueue a line captured from the game's combat log (see <see cref="CombatLogReader"/>) into the
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
            if (!inCombat) { _lastTurnUnitId = null; _lastRound = -1; _wasInPrep = false; }
        }

        // The remaining cues (round, whose-turn, deployment) are turn-based-only chrome. Reset the turn poll
        // when out of TB so re-entering combat re-announces the first unit rather than staying silent.
        if (!inCombat || !tc.TurnBasedModeActive) { _lastTurnUnitId = null; return; }

        // Deployment (preparation) phase — CurrentUnit is null during it, so the turn poll below won't catch it.
        bool prep = tc.IsPreparationTurn;
        if (prep && !_wasInPrep)
            Enqueue(Message.Localized("ui", "combat.deployment").Resolve());
        _wasInPrep = prep;

        // Round advance (CombatRound is 0 out of combat, 1 = first round). Announce before the turn cue so a
        // fresh round reads "Round 2 … Your turn, X".
        int round = tc.CombatRound;
        if (round != _lastRound && round > 0)
        {
            _lastRound = round;
            Enqueue(Message.Localized("ui", "combat.round", new { round }).Resolve());
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
    private static string TurnCue(Kingmaker.Controllers.TurnBased.TurnController tc, MechanicEntity cur)
    {
        string name = (cur as AbstractUnitEntity)?.CharacterName ?? cur.Name;
        if (tc.IsPlayerTurn)
        {
            var cs = (cur as BaseUnitEntity)?.GetCombatStateOptional();
            if (cs != null)
                return Message.Localized("ui", "combat.your_turn",
                    new { name, ap = cs.ActionPointsYellow, mp = Mathf.RoundToInt(cs.ActionPointsBlue) }).Resolve();
            return Message.Localized("ui", "combat.your_turn_simple", new { name }).Resolve();
        }
        bool enemy = (cur as BaseUnitEntity)?.IsPlayerEnemy ?? false;
        return Message.Localized("ui", enemy ? "combat.turn_enemy" : "combat.turn", new { name }).Resolve();
    }

    // Damage, healing and deaths were read here off the EventBus; they now come from the combat log
    // (authoritative, richer — damage type, misses, crits) via CombatLogReader. See the class summary.

    // ---- IUnitBuffHandler (five members; only add/remove are used, the rest are required no-ops) ----
    public void HandleBuffDidAdded(Buff buff) { var k = KeyOf(buff); if (k != null) _frameAdds[k.Value] = buff; }
    public void HandleBuffDidRemoved(Buff buff) { var k = KeyOf(buff); if (k != null) _frameRemoves[k.Value] = buff; }
    public void HandleBuffRankIncreased(Buff buff) { }
    public void HandleBuffRankDecreased(Buff buff) { }
    public void HandleBuffIsSuppressedChanged(Buff buff) { }

    // Reconcile the frame's buff churn into genuine gains/losses. The active set is updated for every
    // (even non-read) unit so it stays accurate; visibility only gates whether a line is spoken.
    private void Reconcile()
    {
        if (_frameAdds.Count == 0 && _frameRemoves.Count == 0) return;

        // Gains: added this frame and not already active (HashSet.Add is false for a dup/refresh).
        foreach (var kv in _frameAdds)
            if (_active.Add(kv.Key))
            {
                var u = kv.Value.Owner as BaseUnitEntity;
                if (u != null && ShouldRead(u))
                    Enqueue(Message.Localized("ui", "event.buff_gained",
                        new { name = u.CharacterName, buff = kv.Value.Name }).Resolve());
            }

        // Losses: removed this frame, was active, and NOT re-added this frame (a re-add = refresh).
        foreach (var kv in _frameRemoves)
            if (!_frameAdds.ContainsKey(kv.Key) && _active.Remove(kv.Key))
            {
                var u = kv.Value.Owner as BaseUnitEntity;
                if (u != null && ShouldRead(u))
                    Enqueue(Message.Localized("ui", "event.buff_lost",
                        new { name = u.CharacterName, buff = kv.Value.Name }).Resolve());
            }

        _frameAdds.Clear();
        _frameRemoves.Clear();
    }

    // Party always reads; enemy/neutral read only while perceptible (IsVisibleForPlayer folds in
    // in-game + fog-of-war). On any error, don't suppress (fail open).
    private static bool ShouldRead(BaseUnitEntity u)
    {
        try
        {
            var faction = u.Faction;
            if (faction != null && faction.IsPlayer) return true;
            return u.IsVisibleForPlayer;
        }
        catch { return true; }
    }

    // (unit, blueprint) identity, or null to ignore the buff (no unit owner, or hidden/empty per the game's
    // own combat-log filter — Buff.Hidden already covers Blueprint.IsHiddenInUI + suppressed).
    private static BuffKey? KeyOf(Buff buff)
    {
        var owner = buff?.Owner as BaseUnitEntity;
        object bp = buff?.Blueprint;
        if (owner == null || bp == null) return null;
        if (buff.Hidden || string.IsNullOrEmpty(buff.Name)) return null;
        return new BuffKey(owner, bp);
    }

    private readonly struct BuffKey : IEquatable<BuffKey>
    {
        private readonly BaseUnitEntity _unit;
        private readonly object _bp;
        public BuffKey(BaseUnitEntity unit, object bp) { _unit = unit; _bp = bp; }
        public bool Equals(BuffKey o) => ReferenceEquals(_unit, o._unit) && ReferenceEquals(_bp, o._bp);
        public override bool Equals(object o) => o is BuffKey k && Equals(k);
        public override int GetHashCode() => ((_unit?.GetHashCode() ?? 0) * 397) ^ (_bp?.GetHashCode() ?? 0);
    }
}
