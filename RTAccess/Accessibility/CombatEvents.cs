using System;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;            // IDamageHandler, IHealingHandler, IUnitDeathHandler, IUnitBuffHandler
using Kingmaker.RuleSystem.Rules.Damage; // RuleDealDamage, RuleHealDamage
using Kingmaker.EntitySystem.Entities;   // BaseUnitEntity
using Kingmaker.Mechanics.Entities;      // AbstractUnitEntity
using Kingmaker.UnitLogic.Buffs;         // Buff
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// The combat/event announcement pipeline: one persistent <see cref="Kingmaker.PubSubSystem.Core.EventBus"/>
/// subscriber (mirroring <see cref="BarkEvents"/>) that voices what a sighted player SEES in combat —
/// damage dealt/taken, heals, unit deaths/downs, and buff/debuff gains/losses. Subscribed once at mod load
/// and unsubscribed at unload (see <see cref="Main"/>); <see cref="Tick"/> is pumped once per frame from
/// <c>OnUpdate</c>.
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
internal sealed class CombatEvents : IDamageHandler, IHealingHandler, IUnitDeathHandler, IUnitBuffHandler
{
    internal static readonly CombatEvents Instance = new CombatEvents();

    // Lines collected this frame (in arrival order), flushed in Tick.
    private readonly List<string> _pending = new List<string>();

    // Buffs currently announced as active, keyed by (unit, blueprint).
    private readonly HashSet<BuffKey> _active = new HashSet<BuffKey>();
    // This frame's raw adds/removes (last Buff for a key wins), reconciled in Tick.
    private readonly Dictionary<BuffKey, Buff> _frameAdds = new Dictionary<BuffKey, Buff>();
    private readonly Dictionary<BuffKey, Buff> _frameRemoves = new Dictionary<BuffKey, Buff>();

    // Death fires for BOTH unconscious and dead and can re-fire as state settles; announce each
    // (unit, state) once.
    private readonly HashSet<DeathKey> _announcedDeaths = new HashSet<DeathKey>();

    /// <summary>Pumped once per frame from <c>Main.OnUpdate</c>: reconcile the frame's buff churn into
    /// genuine gain/loss lines, then flush every queued line (in arrival order) as passive speech.</summary>
    public void Tick()
    {
        try { Reconcile(); }
        catch (Exception e) { Main.Log?.Log("combat reconcile failed: " + e.Message); }

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

    // ---- IDamageHandler ----
    // Fires after HP is applied. Gate on a real, non-fake hit to a unit; FxOnly/FakeDamage also raises this.
    public void HandleDamageDealt(RuleDealDamage dealDamage)
    {
        try
        {
            if (dealDamage == null || dealDamage.IsFake || dealDamage.Result <= 0) return;
            var u = dealDamage.TargetUnit; // null for non-unit targets (destructibles)
            if (u == null || !ShouldRead(u)) return;
            Enqueue(Message.Localized("ui", "event.damage",
                new { name = u.CharacterName, amount = dealDamage.Result }).Resolve());
        }
        catch (Exception e) { Main.Log?.Log("damage announce failed: " + e.Message); }
    }

    // ---- IHealingHandler ----
    // Value is the actual HP restored (already clamped); a no-op heal (full HP, fake, interrupted) is 0.
    public void HandleHealing(RuleHealDamage healDamage)
    {
        try
        {
            if (healDamage == null || healDamage.Value <= 0) return;
            var u = healDamage.TargetUnit;
            if (u == null || !ShouldRead(u)) return;
            Enqueue(Message.Localized("ui", "event.heal",
                new { name = u.CharacterName, amount = healDamage.Value }).Resolve());
        }
        catch (Exception e) { Main.Log?.Log("heal announce failed: " + e.Message); }
    }

    // ---- IUnitDeathHandler ----
    // Global, carries the unit; raised for Unconscious OR Dead. Branch on LifeState.IsDead (died vs downed).
    public void HandleUnitDeath(AbstractUnitEntity unitEntity)
    {
        try
        {
            var u = unitEntity as BaseUnitEntity;
            if (u == null || !ShouldRead(u)) return;
            bool dead = unitEntity.LifeState.IsDead;
            if (!_announcedDeaths.Add(new DeathKey(u, dead))) return; // per-(unit, state) de-dupe
            Enqueue(Message.Localized("ui", dead ? "event.death" : "event.downed",
                new { name = u.CharacterName }).Resolve());
        }
        catch (Exception e) { Main.Log?.Log("death announce failed: " + e.Message); }
    }

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

    private readonly struct DeathKey : IEquatable<DeathKey>
    {
        private readonly BaseUnitEntity _unit;
        private readonly bool _dead;
        public DeathKey(BaseUnitEntity unit, bool dead) { _unit = unit; _dead = dead; }
        public bool Equals(DeathKey o) => ReferenceEquals(_unit, o._unit) && _dead == o._dead;
        public override bool Equals(object o) => o is DeathKey k && Equals(k);
        public override int GetHashCode() => ((_unit?.GetHashCode() ?? 0) * 397) ^ (_dead ? 1 : 0);
    }
}
