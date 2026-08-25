using Kingmaker;                                          // Game
using Kingmaker.EntitySystem.Entities;                    // BaseUnitEntity
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, GetPattern / GetPatternSettings
using Kingmaker.UnitLogic.Abilities.Components.Patterns;  // AoEPatternHelper, OrientedPatternData
using Kingmaker.Utility;                                  // TargetWrapper
using UnityEngine;                                        // Vector3

namespace RTAccess.Exploration;

/// <summary>
/// Ranked BLAST POSITIONS for the armed area ability — "where do I drop this to catch the most of them", cycled
/// best-first by the scanner's enemy key while a pattern is armed (see <c>Scanner.CycleBlast</c>).
///
/// <b>Why this and not enemy clustering.</b> The August field report asked for nearby enemies to be grouped
/// ("5 cultists south-west within 3 tiles") so a blast could be dropped on the bunch. Geometric clustering
/// answers that question only for a circular template: RT's patterns are cones, rays, sectors and scatter
/// cones as well, they orient from the CASTER through the aim point, and they are clipped by cover and by the
/// firing arc — so a join-radius blob would confidently name a "group" that the actual template misses. The
/// game already computes the honest answer (<see cref="AbilityData.GetPattern"/> → the oriented node set the
/// commit itself uses), so this ranks candidate aim points by what that template really catches. It is the same
/// list a sighted player builds by dragging the red pattern preview around the screen — the mechanism the
/// research into the sighted surface turned up (docs/feedback/2026-08-discord-triage-2.md §4).
///
/// <b>Candidates are the enemies themselves.</b> Sweeping every cell in range would be (2R+1)² pattern builds
/// per keypress for no gain: a template is placed ON something, and the cell a player clicks is an enemy. Each
/// visible enemy's cell is therefore one candidate, deduped, and a candidate that catches nobody is dropped.
/// (A refinement worth measuring later: also seed the midpoints between close enemy pairs, which can beat both
/// of their own cells for a wide blast.)
///
/// Everything load-bearing is the game's own: the caster anchor is
/// <c>VirtualPositionController.GetDesiredPosition</c> — the expression the commit resolves, so a planted move
/// is accounted for (mirrors <see cref="AoEPreview"/> §6); legality is <c>CanTargetFromNode</c>, the same
/// predicate the sighted pointer decal flips Attack/Unable on; and <c>GetPattern</c> internally routes through
/// <c>GetBestShootingPosition</c>, so the engine's own lean-around-cover is already applied. Pure read.
/// </summary>
internal static class BlastPlan
{
    /// <summary>One candidate aim point: the cell, the enemy that seeded it, and what the template catches there.</summary>
    internal readonly struct Cell
    {
        public readonly CustomGridNodeBase Node;
        public readonly BaseUnitEntity Seed;      // the enemy whose cell this is — what the line is named after
        public readonly int Enemies;
        public readonly int Allies;               // friendly fire: the number that decides whether to fire at all

        public Cell(CustomGridNodeBase node, BaseUnitEntity seed, int enemies, int allies)
        {
            Node = node; Seed = seed; Enemies = enemies; Allies = allies;
        }

        public Vector3 Position => Node.Vector3Position;
    }

    private static AbilityData Armed => Game.Instance?.SelectedAbilityHandler?.Ability;

    /// <summary>
    /// True while an armed ability carries a real AoE template — the only state in which ranking blast cells means
    /// anything. A single-target ability has no pattern provider; a whole-area effect has a provider but a null
    /// <c>Pattern</c> (it hits everything, so there is nothing to place). Both fall through to the normal
    /// enemy-by-enemy cycle.
    /// </summary>
    public static bool Active
    {
        get
        {
            try
            {
                var ability = Armed;
                if (ability == null) return false;
                var prov = ability.GetPatternSettings();
                return prov?.Pattern != null;
            }
            catch (Exception e) { Main.Log?.Error("BlastPlan.Active failed: " + e); return false; }
        }
    }

    /// <summary>
    /// Candidate aim points, best first: most enemies caught, then fewest allies caught, then nearest. Empty when
    /// nothing is armed, no enemy is visible, or no legal aim point catches anyone.
    /// </summary>
    public static List<Cell> Rank()
    {
        var list = new List<Cell>();
        try
        {
            var ability = Armed;
            if (ability == null || ability.GetPatternSettings()?.Pattern == null) return list;
            var caster = ability.Caster as BaseUnitEntity;
            if (caster == null) return list;

            // The commit's own caster anchor, not Caster.Position: with a move planted, the template orients from
            // the cell the unit is going to stand on, exactly as the sighted overlay does.
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 casterPos = vpc != null ? vpc.GetDesiredPosition(caster) : caster.Position;
            var casterNode = AoEPatternHelper.GetGridNode(casterPos);
            if (casterNode == null) return list;

            var seen = new HashSet<CustomGridNodeBase>();
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible || !it.IsUnit || !it.CurrentlySeen) continue;
                if (it.Primary != ScanTaxonomy.UnitsEnemies) continue;
                var enemy = it.TargetUnit;
                if (enemy == null || enemy.LifeState.IsDead) continue;
                var node = enemy.CurrentUnwalkableNode;
                if (node == null || !seen.Add(node)) continue;

                // The pointer decal's own predicate. False for range, line of sight, firing arc, area overlap and
                // the rest alike — every one of which means the player cannot aim here, so all are simply skipped.
                var tw = new TargetWrapper(node.Vector3Position);
                if (!ability.CanTargetFromNode(casterNode, null, tw, out int _, out var _, out var _)) continue;

                Count(ability, tw, casterPos, out int enemies, out int allies);
                if (enemies <= 0) continue;                     // an aim point that catches nobody is not a candidate
                list.Add(new Cell(node, enemy, enemies, allies));
            }

            list.Sort((a, b) =>
            {
                int byEnemies = b.Enemies.CompareTo(a.Enemies);            // more caught first
                if (byEnemies != 0) return byEnemies;
                int byAllies = a.Allies.CompareTo(b.Allies);               // then the one that spares our own
                if (byAllies != 0) return byAllies;
                float da = Geo.Distance(casterPos, a.Position);            // then the nearest
                float db = Geo.Distance(casterPos, b.Position);
                return da.CompareTo(db);
            });
        }
        catch (Exception e) { Main.Log?.Error("BlastPlan.Rank failed: " + e); }
        return list;
    }

    /// <summary>
    /// Who the template catches at this aim point. Reads the game's oriented pattern and asks each covered cell for
    /// its occupant — deliberately NOT <c>GatherAffectedTargetsData</c>, which walks every unit in the level and
    /// builds a full damage prediction per target: correct for the ONE cell the player settles on (the aim readout
    /// already does exactly that through <c>AimReadTap</c>) but far too heavy to run once per candidate on a
    /// keypress. Counting occupants of the template's own cells is the same set, without the damage maths.
    ///
    /// Units are deduped: a multi-cell unit stands on several pattern cells at once. Visual parity is enforced with
    /// the usual lens — a stealth-unspotted or invisible enemy standing in the blast is not counted, because a
    /// sighted player dragging the preview over that cell would not see it counted either.
    /// </summary>
    private static void Count(AbilityData ability, TargetWrapper aim, Vector3 casterPos, out int enemies, out int allies)
    {
        enemies = 0;
        allies = 0;
        var pattern = ability.GetPattern(aim, casterPos);
        if (pattern.IsEmpty) return;
        var counted = new HashSet<BaseUnitEntity>();
        foreach (var node in pattern.Nodes)
        {
            var u = node?.GetUnit();
            if (u == null || u.LifeState.IsDead || !counted.Add(u)) continue;
            if (!(u.IsPlayerFaction || u.IsVisibleForPlayer)) continue;   // never count what the sighted player can't see
            if (u.IsPlayerEnemy) enemies++;
            else if (u.IsPlayerFaction) allies++;
        }
    }
}
