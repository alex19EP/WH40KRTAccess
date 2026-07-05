using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingmaker;                                         // Game
using Kingmaker.EntitySystem.Entities;                  // BaseUnitEntity
using Kingmaker.UI.Pointer.AbilityTarget;               // AbilityRange, AbilitySingleTargetRange, AbilityPatternRange
using Kingmaker.UnitLogic.Abilities;                    // AbilityData, AbilityTargetUIData
using RTAccess.Exploration;                             // MapCursor, CursorTarget, Targeting
using UnityEngine;                                      // Vector3, Mathf, FindObjectsByType

namespace RTAccess.Combat;

/// <summary>
/// Voices the aim result on each tile-cursor step, computed at our keyboard cursor by <see cref="AimPointerDriver"/> and
/// captured by <see cref="AimReadTap"/>. The SIGHTED-PARITY decision — which units are voiced and with what numbers —
/// is delegated entirely to <see cref="AimParity"/>, which sources the game's own overtip gate + values (no re-derived
/// rule). This file only shapes the spoken sentence: the aimed unit's headline (hit% / damage / kill / knockback), the
/// overpenetration pierce chain, AoE membership, the friendly-fire warning, and — on demand (verbose) — the avoidance
/// breakdown the sighted overtip shows at a glance. See docs/plans/piloted-aiming-lamport.md.
///
/// Timing: on a cursor STEP the arrow is processed after Tick's postfix ran, so the tap would be one step stale.
/// <see cref="RefreshAtCursor"/> drives the pointer to the new cursor and forces ONE synchronous recompute so the tap —
/// and the overtip gate we call — are current before we read them.
/// </summary>
internal static class AimRead
{
    private static AbilityData Armed => Game.Instance?.SelectedAbilityHandler?.Ability;

    /// <summary>Drive the game's pointer to the movement cursor and force one synchronous recompute so the captured aim
    /// data (and <see cref="AimParity"/>'s gate) are current for this frame. No-op when not aiming / nothing armed.</summary>
    public static void RefreshAtCursor() => RefreshAt(MapCursor.Position, CursorTarget.Inside()?.TargetUnit);

    /// <summary>As <see cref="RefreshAtCursor"/> but at an arbitrary world point / unit (e.g. a scanned target).</summary>
    public static void RefreshAt(Vector3 worldPos, BaseUnitEntity unit)
    {
        try
        {
            var cec = Game.Instance?.ClickEventsController;
            var ability = Armed;
            if (cec == null || ability == null) return;
            cec.WorldPosition = worldPos;
            cec.PointerOn = unit != null && unit.View != null ? unit.View.gameObject : null;
            var range = ActiveRange();
            var caster = ability.Caster ?? Game.Instance.TurnController?.CurrentUnit;
            if (range == null || caster == null) return;
            // Force the recompute NOW (the override ignores its cache when the target position actually changed, which a
            // cursor step guarantees) so HandleCellAbility fires synchronously and the captured data is up to date.
            range.SetRangeToWorldPosition(Game.Instance.VirtualPositionController.GetDesiredPosition(caster), true);
        }
        catch (Exception e) { Main.Log?.Log("AimRead.RefreshAt failed: " + e.Message); }
    }

    // The single armed aim-range component that is currently live (single-target OR pattern — never the always-on
    // counter-attack range). Scene search, but only on a user-paced cursor step, over ~12 components.
    private static AbilityRange ActiveRange()
    {
        foreach (var r in UnityEngine.Object.FindObjectsByType<AbilityRange>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((r is AbilitySingleTargetRange || r is AbilityPatternRange) && r.m_IsActive) return r;
        return null;
    }

    private readonly struct Entry
    {
        public readonly BaseUnitEntity Unit;
        public readonly AimParity.Shot S;
        public Entry(BaseUnitEntity u, AimParity.Shot s) { Unit = u; S = s; }
    }

    /// <summary>The per-move aim readout for the tile cursor. Null when nothing readable (the AoE geometry / refusal
    /// already spoke, or single-target aim with no shown target under the cursor).</summary>
    public static string CursorReadout(bool verbose)
    {
        try
        {
            RefreshAtCursor();
            var ability = Armed;
            if (ability == null) return null;
            var caster = ability.Caster as BaseUnitEntity;

            // The game's own SHOWN set: every captured target whose overtip the game would display (fog + faction +
            // range + LOS gate) — no more, no less. Values are the overtip's own projected numbers.
            var enemies = new List<Entry>();
            var allies = new List<Entry>();
            foreach (var d in AimReadTap.Instance.Last)
            {
                if (!(d.Target is BaseUnitEntity u) || u == caster) continue;
                if (!AimParity.Shown(u, ability)) continue;
                var e = new Entry(u, AimParity.Project(d, u));
                if (u.IsPlayerEnemy) enemies.Add(e);
                else if (u.IsPlayerFaction) allies.Add(e);
            }

            bool aoe = ability.GetPatternSettings() != null;
            if (enemies.Count == 0 && allies.Count == 0)
                return aoe ? Loc.T("aim.no_targets") : null;

            var sb = new StringBuilder();
            if (!aoe)
            {
                // Single-target: the aimed unit leads; the rest are overpenetration-pierced behind it.
                var cursorUnit = CursorTarget.Inside()?.TargetUnit;
                int primary = enemies.FindIndex(e => e.Unit == cursorUnit);
                if (primary < 0) primary = 0;
                AppendPrimary(sb, enemies[primary], verbose);
                var rest = enemies.Where((_, i) => i != primary).ToList();
                if (rest.Count > 0) AppendPierces(sb, rest);
            }
            else if (enemies.Count > 0)
            {
                sb.Append(enemies.Count == 1 ? Loc.T("aim.catches_enemy_one") : Loc.T("aim.catches_enemies", new { count = enemies.Count }));
                if (verbose) foreach (var e in enemies) AppendOdds(sb, e);
            }

            // Friendly fire — the headline. Allies the ability WILL actually hit (passed the same gate).
            if (allies.Count > 0)
            {
                var names = allies.Select(e => e.Unit.CharacterName).Where(n => !string.IsNullOrEmpty(n));
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(Loc.T("aim.ff_warning", new { names = string.Join(", ", names) }));
            }
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("AimRead.CursorReadout failed: " + e); return null; }
    }

    private static void AppendPrimary(StringBuilder sb, Entry e, bool verbose)
    {
        var s = e.S;
        sb.Append(e.Unit.CharacterName).Append(", ");
        sb.Append(s.HitAlways ? Loc.T("aim.odds_sure") : Loc.T("predict.to_hit", new { hit = Round(s.HitChance) }));
        if (s.MaxDamage > 0) sb.Append(", ").Append(Loc.T("predict.damage", new { min = s.MinDamage, max = s.MaxDamage }));
        else if (s.Ricochet) sb.Append(", ").Append(Loc.T("aim.ricochet_unknown"));
        if (s.CanDie) sb.Append(", ").Append(Loc.T("predict.can_kill"));
        if (s.CanPush) sb.Append(", ").Append(Loc.T("predict.can_push"));
        if (verbose) AppendAvoidance(sb, s);
    }

    private static void AppendPierces(StringBuilder sb, List<Entry> list)
    {
        var parts = new List<string>();
        foreach (var e in list)
        {
            var s = e.S;
            parts.Add(Loc.T(s.CanDie ? "predict.pierce_kill" : "predict.pierce",
                new { name = e.Unit.CharacterName, hit = Round(s.HitChance), min = s.MinDamage, max = s.MaxDamage }));
        }
        sb.Append(". ").Append(Loc.T("predict.pierces", new { count = parts.Count, list = string.Join("; ", parts) }));
    }

    private static void AppendOdds(StringBuilder sb, Entry e)
    {
        var s = e.S;
        string chance = s.HitAlways ? Loc.T("aim.odds_sure") : Loc.T("aim.odds_pct", new { chance = Round(s.HitChance) });
        sb.Append(". ").Append(Loc.T(s.CanDie ? "aim.odds_lethal" : "aim.odds_entry", new { name = e.Unit.CharacterName, chance }));
    }

    // The avoidance breakdown the sighted overtip shows at a glance (dodge/parry/cover/block/evasion) plus per-shot
    // burst chances — voiced only on demand (verbose), paced for audio. Each is the game's own AbilityTargetUIData value.
    private static void AppendAvoidance(StringBuilder sb, AimParity.Shot s)
    {
        var parts = new List<string>();
        if (s.Dodge > 0) parts.Add(Loc.T("aim.avoid_dodge", new { pct = Round(s.Dodge) }));
        if (s.Parry > 0) parts.Add(Loc.T("aim.avoid_parry", new { pct = Round(s.Parry) }));
        if (s.Cover > 0) parts.Add(Loc.T("aim.avoid_cover", new { pct = Round(s.Cover) }));
        if (s.Block > 0) parts.Add(Loc.T("aim.avoid_block", new { pct = Round(s.Block) }));
        if (s.Evasion > 0) parts.Add(Loc.T("aim.avoid_evasion", new { pct = Round(s.Evasion) }));
        if (parts.Count > 0) sb.Append(". ").Append(Loc.T("aim.avoidance", new { list = string.Join(", ", parts) }));
        if (s.Burst != null && s.Burst.Count > 1)
            sb.Append(". ").Append(Loc.T("aim.burst", new { count = s.Burst.Count, list = string.Join(", ", s.Burst.Select(b => Round(b) + "%")) }));
    }

    private static int Round(float v) => Mathf.RoundToInt(v);
}
