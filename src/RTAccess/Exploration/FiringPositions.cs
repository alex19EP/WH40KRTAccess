using Kingmaker;                                          // Game
using Kingmaker.EntitySystem.Entities;                    // BaseUnitEntity
using Kingmaker.Pathfinding;                              // CustomGridNodeBase, PathfindingService, GraphParamsMechanicsCache
using Kingmaker.UI.SurfaceCombatHUD;                      // AbilityTargetUIDataCache (the reticle's own numbers)
using Kingmaker.UnitLogic.Abilities;                      // AbilityData
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.View.Covers;                              // LosCalculations
using RTAccess.Accessibility;                             // CombatReads
using UnityEngine;                                        // Vector3, Mathf

namespace RTAccess.Exploration;

/// <summary>
/// "Where can I stand so I can shoot that?" — the ranked list of reachable cells from which the acting unit's
/// attack can actually reach a chosen enemy this turn.
///
/// <b>Why a search is the right answer here.</b> The game gives a sighted player no such list: its combat-HUD
/// grid paints the movement area and the ability's range bands around the CASTER
/// (<c>CombatHudAreas</c> has no "cells that can hit target X" layer), and the range overlay simply recentres on
/// <c>VirtualPositionController.GetDesiredPosition</c> every frame as the mouse hovers
/// (<c>AbilityRange.SetRangeToCasterPosition</c>), while the pointer decal flips Attack/Unable off
/// <c>CanTargetFromDesiredPosition</c> for whichever cell is under the cursor. The sighted player is sweeping
/// dozens of cells a second and reading a binary off each; the advantage is scan SPEED, not a different
/// mechanism. So automating the sweep is the faithful accessible equivalent — and every judgement inside it is
/// still the game's own: candidate set from <c>UnitMovableAreaController</c>, legality from
/// <c>CanTargetFromNode</c> (the decal's predicate), the firing cell from <c>GetBestShootingPosition</c> (the
/// engine's own lean around cover), the percentage from <c>AbilityTargetUIDataCache</c> (the reticle's cache),
/// and the cover from the same <c>LosCalculations</c> the overtip uses. Only the sweep is ours.
/// See docs/feedback/2026-08-discord-triage-2.md §4 for the full research.
///
/// <b>The list is trade-offs, not tiles.</b> A movement area can hold forty cells that all have range and line
/// of sight; cycling forty is worse than useless. Candidates are therefore collapsed by (cover class × hit-chance
/// band), keeping the cheapest cell of each — which leaves the three to six answers that actually differ:
/// "full cover, 45 percent", "half cover, 61 percent", "in the open, 78 percent". That is the decision.
///
/// Ordered SAFEST FIRST (cover descending, then odds, then movement cost): the field report that prompted this
/// describes being killed while positioning, so the option that keeps the unit alive leads and the cheap exposed
/// one is still two presses away.
/// </summary>
internal static class FiringPositions
{
    /// <summary>One candidate stance: where to stand, what it costs, what it buys.</summary>
    internal readonly struct Spot
    {
        public readonly CustomGridNodeBase Node;             // the cell to move to
        public readonly LosCalculations.CoverType Cover;     // the cover I WOULD HAVE there against this enemy
        public readonly int HitChance;                       // per cent on the target, from that cell
        public readonly int Cost;                            // movement points to get there

        public Spot(CustomGridNodeBase node, LosCalculations.CoverType cover, int hit, int cost)
        {
            Node = node; Cover = cover; HitChance = hit; Cost = cost;
        }

        public Vector3 Position => Node.Vector3Position;
    }

    /// <summary>
    /// The attack the search plans around: the ARMED ability when the player has one on the pointer, else the
    /// unit's primary-weapon attack. This is why the same key answers differently with a grenade armed than with
    /// a rifle — range, pattern and odds are all the armed ability's.
    /// </summary>
    public static AbilityData Attack(BaseUnitEntity me)
        => Game.Instance?.SelectedAbilityHandler?.Ability ?? CombatReads.DefaultAttack(me);

    /// <summary>
    /// Can the target already be hit from the cell the unit ACTUALLY STANDS ON? Then there is nothing to search
    /// for and the caller should say so rather than marching the player somewhere.
    /// <paramref name="hit"/> / <paramref name="cover"/> describe the shot from there.
    ///
    /// Deliberately NOT the desired (move-preview) position that every READ in this file's neighbourhood uses.
    /// The desired position is writable by us — the approach key's own planted stance sets it, and so does the
    /// tile cursor's hover-sim — so anchoring here on it made the question self-referential: press one planted a
    /// stance, press two asked "am I in range?", the engine answered about the plant, and the two-step said
    /// "already in range" forever instead of committing the move (field log 2026-08-28, three attempts, unit
    /// never moved). "Should I move?" is a question about where I stand; "what would I hit?" is the one that
    /// belongs to the plan.
    /// </summary>
    public static bool InRangeNow(BaseUnitEntity me, BaseUnitEntity target, out int hit, out LosCalculations.CoverType cover)
    {
        hit = 0;
        cover = LosCalculations.CoverType.None;
        try
        {
            if (me == null) return false;
            var atk = Attack(me);
            var node = NavmeshProbe.NodeAt(me.Position);   // where the legs are, NOT the move preview
            if (atk == null || node == null || target == null) return false;
            if (!atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _)) return false;
            Evaluate(atk, me, target, node, out cover, out hit);
            return true;
        }
        catch (Exception e) { Main.Log?.Error("FiringPositions.InRangeNow failed: " + e); return false; }
    }

    /// <summary>
    /// The ranked, collapsed stances for shooting <paramref name="target"/> this turn. Empty when the unit has no
    /// movement, no attack, or no reachable cell can reach the target at all — the caller then falls back to the
    /// plain approach ("no firing position this turn, closest approach is still N tiles short").
    /// </summary>
    public static List<Spot> Find(BaseUnitEntity me, BaseUnitEntity target)
    {
        var result = new List<Spot>();
        try
        {
            if (me == null || target == null || me.View == null) return result;
            var atk = Attack(me);
            if (atk == null) return result;

            // The game's authoritative reachable set — the blue highlight's own extent.
            var area = Game.Instance?.UnitMovableAreaController?.CurrentUnitMovableArea;
            if (area == null || area.Count == 0) return result;

            // Priced for standability + movement cost, exactly as PathInfo does (the controller keeps only node
            // keys and throws the per-cell costs away).
            var dict = PathfindingService.Instance?.FindAllReachableTiles_Blocking(
                me.View.MovementAgent, me.Position, me.CombatState.ActionPointsBlue);

            var tw = new TargetWrapper(target);
            Vector3 targetPos = target.Position;
            // Cheap geometric prefilter before the expensive per-node legality call: a cell further from the
            // target than the ability can reach can never be an answer. Two cells of slack absorbs the difference
            // between this flat centre-to-centre metric and the engine's footprint-aware cell distance, so the
            // filter can only ever be too generous, never wrong.
            float cell = GraphParamsMechanicsCache.GridCellSize;
            float maxDist = (atk.RangeCells + 2) * cell;

            var best = new Dictionary<int, Spot>();   // (cover, hit band) -> cheapest cell in that class
            foreach (var n in area)
            {
                if (!(n is CustomGridNodeBase node)) continue;
                if (Geo.Distance(node.Vector3Position, targetPos) > maxDist) continue;

                int cost = 0;
                if (dict != null)
                {
                    // Standability matters as much as reachability: a cell the fan crosses but cannot stop on is
                    // not a stance. A node the game lists but the pricing misses is an anomaly — skip it.
                    if (!dict.TryGetValue(node, out var priced) || !priced.IsCanStand) continue;
                    cost = Mathf.RoundToInt(priced.Length);
                }

                // The pointer decal's own predicate: false for range, line of sight, firing arc, area overlap and
                // the rest alike — every one of which means "you cannot shoot from there".
                if (!atk.CanTargetFromNode(node, null, tw, out int _, out var _, out var _)) continue;

                Evaluate(atk, me, target, node, out var cover, out int hit);

                // Collapse to the decision space: one representative per (cover class, 10-point odds band), the
                // cheapest to reach. Forty cells become the handful that actually differ.
                int key = ((int)cover * 100) + (hit / 10);
                if (!best.TryGetValue(key, out var held) || cost < held.Cost)
                    best[key] = new Spot(node, cover, hit, cost);
            }

            result.AddRange(best.Values);
            result.Sort((a, b) =>
            {
                int byCover = CoverRank(b.Cover).CompareTo(CoverRank(a.Cover));   // safest first
                if (byCover != 0) return byCover;
                int byHit = b.HitChance.CompareTo(a.HitChance);                   // then the better shot
                if (byHit != 0) return byHit;
                return a.Cost.CompareTo(b.Cost);                                  // then the cheaper walk
            });
        }
        catch (Exception e) { Main.Log?.Error("FiringPositions.Find failed: " + e); }
        return result;
    }

    /// <summary>
    /// What standing on <paramref name="node"/> would buy against <paramref name="target"/>: the cover I would
    /// have, and the odds I would have.
    ///
    /// The two are deliberately measured in OPPOSITE directions and must not be confused.
    /// <see cref="CombatReads.CoverTo"/> answers "how protected is the TARGET from me" — that is a to-hit input,
    /// and it is already folded into the percentage below. What ranks a stance is the mirror: line of sight from
    /// the enemy to the cell I would occupy, i.e. how protected *I* am when they shoot back. That is the number
    /// the field report was missing when it died in the open.
    ///
    /// The odds come from the reticle's own cache, keyed on the firing cell the engine would actually use —
    /// <c>GetBestShootingPosition</c> applies the lean-around-cover step (<c>LosCalculations.GetBestShootingNode</c>)
    /// before the shot, so pricing the raw candidate cell would quote a number the game never shows.
    /// </summary>
    private static void Evaluate(AbilityData atk, BaseUnitEntity me, BaseUnitEntity target,
        CustomGridNodeBase node, out LosCalculations.CoverType cover, out int hit)
    {
        cover = LosCalculations.CoverType.None;
        hit = 0;
        try
        {
            cover = LosCalculations.GetWarhammerLos(target, node, me.SizeRect).CoverType;
        }
        catch (Exception e) { Main.Log?.Log("FiringPositions cover read failed: " + e.Message); }
        try
        {
            var tw = new TargetWrapper(target);
            var shootNode = atk.GetBestShootingPosition(node, tw) ?? node;
            var ui = AbilityTargetUIDataCache.Instance?.GetOrCreate(atk, target, shootNode.Vector3Position);
            if (ui != null) hit = Mathf.RoundToInt(ui.Value.HitWithAvoidanceChance);
        }
        catch (Exception e) { Main.Log?.Log("FiringPositions odds read failed: " + e.Message); }
    }

    /// <summary>Safety order for the sort: full cover beats half beats open. <c>Invisible</c> means no line of
    /// sight at all and cannot occur here (the legality gate rejects it), but ranks lowest so a stray one can
    /// never lead the list.</summary>
    private static int CoverRank(LosCalculations.CoverType c)
        => c == LosCalculations.CoverType.Full ? 3
         : c == LosCalculations.CoverType.Half ? 2
         : c == LosCalculations.CoverType.None ? 1
         : 0;

    /// <summary>The spoken cover word, shared by every caller so the firing cycle and the vantage read agree.</summary>
    public static string CoverWord(LosCalculations.CoverType c)
        => c == LosCalculations.CoverType.Full ? Loc.T("cover.full")
         : c == LosCalculations.CoverType.Half ? Loc.T("cover.half")
         : Loc.T("cover.none");
}
