using Kingmaker;
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity, MechanicEntity, UnitEntity
using Kingmaker.Items;                                    // ItemEntityWeapon (the LOS line's ranged-preferred pick)
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using Kingmaker.UI.SurfaceCombatHUD;                      // AbilityTargetUIDataCache (the reticle/LOS-line hit cache)
using Kingmaker.UnitLogic;                                // IsThreat (AttackOfOpportunityHelper ext)
using Kingmaker.UnitLogic.Abilities;                      // AbilityData, AbilityTargetUIData
using Kingmaker.UnitLogic.Abilities.Components.Patterns;  // AoEPatternHelper.GetGridNode
using Kingmaker.Utility;                                  // TargetWrapper
using Kingmaker.View.Covers;                              // LosCalculations
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Side-effect-free combat reads shared by the battlefield scanner's per-enemy suffix (C5) and its summary key —
/// the passive counterpart of the aiming-time <see cref="HitPredictor"/>. These are the same facts a sighted player
/// reads off the on-screen cover overtip and threatened-area FX (do NOT read the overtip VM — it is stale for
/// off-screen / non-current units, so the cover is recomputed here exactly as the game's own <c>UpdateCover</c>
/// does: best shooting position from the acting unit's desired/move-preview tile → LOS/cover to the target). No
/// state is mutated; the numbers agree with the reticle after a partial move plan because both use
/// <c>VirtualPositionController.GetDesiredPosition</c>.
/// </summary>
internal static class CombatReads
{
    /// <summary>The observer's primary-weapon attack ability (null if unarmed) — used only for a dry "in range?" +
    /// cover read via <see cref="AbilityData.CanTargetFromNode"/>. NOT the attack-of-opportunity ability (that
    /// returns a <c>BlueprintAbility</c>, which <c>CanTargetFromNode</c> cannot take).</summary>
    public static AbilityData DefaultAttack(BaseUnitEntity u)
    {
        var abilities = u?.GetFirstWeapon()?.Abilities;
        return (abilities != null && abilities.Count > 0) ? abilities[0].Data : null;
    }

    /// <summary>The node the unit would SHOOT from — its desired (move-preview) position, matching the reticle/overtip.</summary>
    public static CustomGridNodeBase ShootNode(BaseUnitEntity u)
    {
        if (u == null) return null;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 p = vpc != null ? vpc.GetDesiredPosition(u) : u.Position;
        return AoEPatternHelper.GetGridNode(p);
    }

    /// <summary>Is <paramref name="target"/> in range of <paramref name="me"/>'s default weapon attack right now
    /// (from where I'd shoot)? False when unarmed or not targetable. A pure read — the same check the reticle runs.</summary>
    public static bool InRange(BaseUnitEntity me, BaseUnitEntity target)
    {
        var atk = DefaultAttack(me);
        var node = ShootNode(me);
        if (atk == null || node == null || target == null) return false;
        return atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
    }

    /// <summary>The cover the target has against me — computed exactly as the game's on-screen cover overtip does
    /// (<c>OvertipCoverBlockVM.UpdateCover</c>): the best shooting position from my desired/move-preview tile, then
    /// the Warhammer LOS/cover to the target. Returns <c>Invisible</c> when I have no line of sight. NOTE:
    /// <c>AbilityData.CanTargetFromNode</c>'s out-cover is unusable — the game hardcodes it to <c>None</c> and tests
    /// LOS with a separate bool — so cover MUST be computed here, not read from that call.</summary>
    public static LosCalculations.CoverType CoverTo(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null) return LosCalculations.CoverType.None;
        var vpc = Game.Instance?.VirtualPositionController;
        Vector3 from = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;
        return CoverTo(from, me, target);
    }

    /// <summary>The same cover read, but if I shot from an explicit cell <paramref name="from"/> — the holographic
    /// "if I stood here" preview a sighted player reads off the move/deploy ghost. Every primitive takes an explicit
    /// position, so this needs NO <c>VirtualPositionController</c> mutation: the desired position is simply swapped
    /// for the candidate cell. (Cover/LOS transfer cleanly by moving the origin.)</summary>
    public static LosCalculations.CoverType CoverTo(Vector3 from, BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null) return LosCalculations.CoverType.None;
        Vector3 best = LosCalculations.GetBestShootingPosition(from, me.SizeRect, target.Position, target.SizeRect);
        return LosCalculations.GetWarhammerLos(best, me.SizeRect, target).CoverType;
    }

    /// <summary>The passive tactical tail for ME considering TARGET — "half cover, in range, threatening you" — the
    /// not-aiming counterpart of the <see cref="HitPredictor"/> line. Returns null when nothing applies.</summary>
    public static string CoverRangeThreat(BaseUnitEntity me, BaseUnitEntity target)
    {
        if (me == null || target == null || me == target) return null;
        var bits = new List<string>();

        var cover = CoverTo(me, target);
        if (cover == LosCalculations.CoverType.Invisible)
        {
            bits.Add(Loc.T("combat.no_los"));
        }
        else
        {
            if (cover == LosCalculations.CoverType.Half) bits.Add(Loc.T("cover.half"));
            else if (cover == LosCalculations.CoverType.Full) bits.Add(Loc.T("cover.full"));

            // With LOS established, whether my default weapon can actually reach the target (range/targetability).
            // Only spoken when I'm armed — an unarmed observer has no weapon range to report.
            var atk = DefaultAttack(me);
            var node = ShootNode(me);
            if (atk != null && node != null)
            {
                bool targetable = atk.CanTargetFromNode(node, null, new TargetWrapper(target), out int _, out var _, out var _);
                bits.Add(targetable ? Loc.T("combat.in_range") : Loc.T("combat.out_of_range"));
            }

            // The LOS-line number: the hit% a sighted player reads off the line to this enemy while hovering it
            // (browsing the enemy IS our hover). The line hides at zero and for melee weapons — mirror that.
            int pct = LosHitChance(me, target);
            if (pct > 0) bits.Add(Loc.T("predict.to_hit", new { hit = pct }));
        }
        if (target.IsThreat(me)) bits.Add(Loc.T("combat.threatening"));   // does this enemy threaten my acting unit (AoO reach)

        return bits.Count > 0 ? string.Join(", ", bits) : null;
    }

    /// <summary>
    /// The number on the game's LOS line from ME to TARGET — a faithful port of
    /// <c>LineOfSightVM.UpdateHitChance</c>, answered from my DESIRED position (so it tracks the hover-sim and the
    /// Backspace-planted holo unit exactly as the on-screen lines do): the real hit-with-avoidance chance from the
    /// current weapon's ability (best-shooting-position + the reticle cache; scatter weapons via the oriented
    /// pattern, as the VM does), or with no weapon ability the line's flat cover mapping (none 80 / half 50 /
    /// full 10 / no-LOS 0). Returns −1 when the game would draw NO line at all (melee weapon, charge ability) and
    /// 0 when the line exists but hides (no LOS / can't target from here) — callers speak only positive numbers.
    /// </summary>
    public static int LosHitChance(BaseUnitEntity me, BaseUnitEntity target)
    {
        try
        {
            if (me == null || target == null || me == target) return -1;
            var weapon = RangedPreferredWeapon(me);
            var ability = Game.Instance?.SelectedAbilityHandler?.Ability ?? weapon?.Abilities.FirstOrDefault()?.Data;
            if (weapon?.Blueprint.IsMelee ?? false) return -1;   // the game draws no LOS line for a melee weapon
            if (ability != null && ability.IsCharge) return -1;  // ...nor for a charge ability
            var vpc = Game.Instance?.VirtualPositionController;
            Vector3 from = vpc != null ? vpc.GetDesiredPosition(me) : me.Position;

            if (ability == null)
            {
                // No weapon ability: the line's flat cover→chance mapping (LineOfSightVM.UpdateHitChance).
                switch (LosCalculations.GetWarhammerLos(from, me.SizeRect, target).CoverType)
                {
                    case LosCalculations.CoverType.None: return 80;
                    case LosCalculations.CoverType.Half: return 50;
                    case LosCalculations.CoverType.Full: return 10;
                    default: return 0;
                }
            }

            var fromNode = AoEPatternHelper.GetGridNode(from);
            var tw = new TargetWrapper(target);
            if (fromNode == null || !ability.CanTargetFromNode(fromNode, null, tw, out int _, out var _, out var _))
                return 0;
            var best = ability.GetBestShootingPositionForDesiredPosition(tw) ?? fromNode;
            return HitChanceAt(ability, target, best);
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.LosHitChance failed: " + e); return -1; }
    }

    /// <summary>The reticle's own number for <paramref name="ability"/> on <paramref name="target"/> fired from
    /// <paramref name="shootNode"/> (already the engine's lean-around-cover cell): the pair cache for an ordinary
    /// shot, the oriented pattern's per-target entry for a scatter weapon (LineOfSightVM's own split). −1 when the
    /// cache has nothing for the pair.</summary>
    private static int HitChanceAt(AbilityData ability, BaseUnitEntity target, CustomGridNodeBase shootNode)
    {
        var tw = new TargetWrapper(target);
        AbilityTargetUIData ui;
        if (ability.IsScatter && !ability.IsMelee)
        {
            // Scatter sprays a pattern — per-target chance comes from the oriented pattern, not the pair cache.
            var targetNode = AoEPatternHelper.GetGridNode(target.Position);
            var pattern = ability.GetPatternSettings().GetOrientedPattern(ability, shootNode, targetNode);
            var list = new List<AbilityTargetUIData>();
            ability.GatherAffectedTargetsData(pattern, shootNode.Vector3Position, tw, in list, target);
            ui = list.FirstOrDefault(t => t.Target == target);
        }
        else
        {
            ui = AbilityTargetUIDataCache.Instance.GetOrCreate(ability, target, shootNode.Vector3Position);
        }
        return Mathf.RoundToInt(ui.HitWithAvoidanceChance);
    }

    // ---- the hands, and who each can reach (September 2026 tester item 6) ----

    /// <summary>One live weapon hand: the attack the game would fire with it, the weapon's name, and whether it
    /// is a melee hand — the split the tester asked for ("melee list, ranged list, both when dual-wielding").</summary>
    internal readonly struct HandAttack
    {
        public readonly AbilityData Ability;
        public readonly string Weapon;
        public readonly bool Melee;
        public HandAttack(AbilityData ability, string weapon, bool melee) { Ability = ability; Weapon = weapon; Melee = melee; }
    }

    /// <summary>The unit's live weapon hands — the current weapon set's two hands plus any additional limbs —
    /// each as its first (basic) attack ability, classed melee / ranged by the weapon blueprint. A two-handed
    /// weapon appears once. Empty when unarmed.</summary>
    public static List<HandAttack> HandAttacks(BaseUnitEntity me)
    {
        var hands = new List<HandAttack>();
        try
        {
            var body = (me as UnitEntity)?.Body;
            if (body == null) return hands;
            var seen = new HashSet<ItemEntityWeapon>();
            void Add(Kingmaker.Items.Slots.WeaponSlot slot)
            {
                var w = slot?.MaybeWeapon;
                if (w == null || !seen.Add(w)) return;
                var abilities = w.Abilities;
                if (abilities == null || abilities.Count == 0) return;
                var data = abilities[0].Data;
                if (data == null) return;
                hands.Add(new HandAttack(data, w.Name, w.Blueprint.IsMelee));
            }
            var set = body.CurrentHandsEquipmentSet;
            Add(set?.PrimaryHand);
            Add(set?.SecondaryHand);
            foreach (var limb in body.AdditionalLimbs) Add(limb);
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.HandAttacks failed: " + e); }
        return hands;
    }

    /// <summary>Every living, in-combat, VISIBLE enemy of <paramref name="me"/>, nearest to <paramref name="from"/>
    /// first — the one enemy set every read in this file runs over (parity lens: never an unseen enemy).</summary>
    public static List<BaseUnitEntity> VisibleEnemies(BaseUnitEntity me, Vector3 from)
    {
        var foes = new List<BaseUnitEntity>();
        var state = Game.Instance?.State;
        if (me == null || state == null) return foes;
        foreach (var o in (System.Collections.IEnumerable)state.AllBaseAwakeUnits)
        {
            if (!(o is BaseUnitEntity u) || u == me) continue;
            if (!u.IsInCombat || u.LifeState.IsDead || !u.IsPlayerEnemy || !u.IsVisibleForPlayer) continue;
            foes.Add(u);
        }
        foes.Sort((a, b) => (a.Position - from).sqrMagnitude.CompareTo((b.Position - from).sqrMagnitude));
        return foes;
    }

    /// <summary>
    /// "Who can I attack from here", in full: the cover the nearest visible enemy gives me and how many threaten
    /// the cell, then one named list per weapon hand — "Melee, chainsword: Cultist 1, 78 percent; Cultist 2, 65
    /// percent. Ranged, laspistol: Cultist 3, 54 percent, half cover" — each enemy tested with the game's own
    /// pointer-decal predicate (<c>CanTargetFromNode</c>) from <paramref name="from"/>, with the reticle's hit
    /// chance and, for a ranged hand, the target's cover; a hand that reaches nobody is silent, and visible
    /// enemies no hand reaches are counted as "out of reach". Every half of the line is answered from the ONE
    /// cell passed in (the old key mixed the cursor cell with the desired position — the reported anchor bug).
    /// Known engine caveat: abilities carrying <c>IAbilityOverrideCasterForRange</c> measure range from the
    /// unit's real tile whatever cell is passed. A "no enemies" line when the field is clear; null on bad input.
    /// </summary>
    public static string AttackReadout(Vector3 from, BaseUnitEntity me, int maxPerHand = 8)
    {
        try
        {
            if (me == null) return null;
            var node = AoEPatternHelper.GetGridNode(from);
            if (node == null) return null;
            var foes = VisibleEnemies(me, from);
            if (foes.Count == 0) return Loc.T("vantage.no_enemies");

            var sb = new System.Text.StringBuilder();
            var cover = CoverTo(from, me, foes[0]);
            sb.Append(Loc.T("vantage.cover_from", new { cover = CoverWord(cover), name = UnitNames.Of(foes[0]) }));
            int threats = 0;
            foreach (var u in foes) if (u.IsThreat(node, me.SizeRect)) threats++;
            if (threats > 0) sb.Append(", ").Append(Loc.T("vantage.threatened", new { count = threats }));

            var hands = HandAttacks(me);
            var reached = new HashSet<BaseUnitEntity>();
            var lists = new List<string>();
            foreach (var hand in hands)
            {
                var rows = new List<string>();
                int more = 0;
                foreach (var u in foes)
                {
                    if (!hand.Ability.CanTargetFromNode(node, null, new TargetWrapper(u), out int _, out var _, out var _)) continue;
                    reached.Add(u);
                    if (rows.Count >= maxPerHand) { more++; continue; }
                    rows.Add(TargetRow(hand, me, u, node, from));
                }
                if (rows.Count == 0) continue;
                string list = string.Join("; ", rows);
                if (more > 0) list += "; " + Loc.T("vantage.more", new { count = more });
                lists.Add(Loc.T(hand.Melee ? "vantage.melee_list" : "vantage.ranged_list", new { weapon = hand.Weapon, list }));
            }
            if (lists.Count == 0) sb.Append(", ").Append(Loc.T(hands.Count == 0 ? "vantage.unarmed" : "vantage.no_targets"));
            else sb.Append(". ").Append(string.Join(". ", lists));
            int unreached = foes.Count - reached.Count;
            if (lists.Count > 0 && unreached > 0) sb.Append(", ").Append(Loc.T("vantage.out_of_reach", new { count = unreached }));
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.AttackReadout failed: " + e); return null; }
    }

    // One list entry: name + the reticle's odds from the engine's lean cell, plus the target's cover for a ranged hand
    // (cover is a to-hit input; for melee it is meaningless and the game shows none).
    private static string TargetRow(HandAttack hand, BaseUnitEntity me, BaseUnitEntity target, CustomGridNodeBase node, Vector3 from)
    {
        int pct = -1;
        try
        {
            var shootNode = hand.Ability.GetBestShootingPosition(node, new TargetWrapper(target)) ?? node;
            pct = HitChanceAt(hand.Ability, target, shootNode);
        }
        catch (Exception e) { Main.Log?.Log("CombatReads.TargetRow odds failed: " + e.Message); }
        string name = UnitNames.Of(target);
        if (pct < 0) return Loc.T("vantage.target_nopct", new { name });
        if (hand.Melee) return Loc.T("vantage.target", new { name, pct });
        return Loc.T("vantage.target_cover", new { name, pct, cover = CoverWord(CoverTo(from, me, target)) });
    }

    private static string CoverWord(LosCalculations.CoverType cover)
        => cover == LosCalculations.CoverType.Invisible ? Loc.T("vantage.hidden")
         : cover == LosCalculations.CoverType.Half ? Loc.T("cover.half")
         : cover == LosCalculations.CoverType.Full ? Loc.T("cover.full")
         : Loc.T("cover.none");

    /// <summary>The weapon whose ability prices the LOS line — the game prefers a RANGED hand over the primary
    /// (LineOfSightVM.TryGetCurrentWeapon), so a sword-and-pistol unit reads the pistol's numbers.</summary>
    private static ItemEntityWeapon RangedPreferredWeapon(BaseUnitEntity u)
    {
        var set = (u as UnitEntity)?.Body.CurrentHandsEquipmentSet;
        var w1 = set?.PrimaryHand.MaybeWeapon;
        var w2 = set?.SecondaryHand.MaybeWeapon;
        if (w1?.Blueprint.IsRanged ?? false) return w1;
        if (w2?.Blueprint.IsRanged ?? false) return w2;
        return w1;
    }

    /// <summary>The SHORT "if I stood on this cell" read for <paramref name="me"/> — the per-step deployment tail and
    /// the cover cycle's suffix, where the full per-hand lists of <see cref="AttackReadout"/> would drown the step:
    /// cover vs the nearest visible enemy, how many enemies ANY weapon hand could reach from there, and how many
    /// would threaten that cell. All pure reads from <paramref name="from"/>. Returns a "no enemies" line when the
    /// field is clear (or the caller's fallback), null on bad input. NOTE the in-range count uses
    /// <c>CanTargetFromNode(candidate cell)</c>, which for some abilities measures from the unit's ACTUAL tile
    /// (<c>TryGetCasterForDistanceCalculation</c>) — cover and threat transfer exactly, in-range is best-effort.</summary>
    public static string VantageFrom(Vector3 from, BaseUnitEntity me)
    {
        try
        {
            if (me == null) return null;
            var node = AoEPatternHelper.GetGridNode(from);
            if (node == null) return null;
            var foes = VisibleEnemies(me, from);
            if (foes.Count == 0) return Loc.T("vantage.no_enemies");

            var hands = HandAttacks(me);
            int threats = 0, inRange = 0;
            foreach (var u in foes)
            {
                if (u.IsThreat(node, me.SizeRect)) threats++;   // does this enemy threaten the CANDIDATE cell (AoO reach)
                var tw = new TargetWrapper(u);
                foreach (var hand in hands)
                    if (hand.Ability.CanTargetFromNode(node, null, tw, out int _, out var _, out var _)) { inRange++; break; }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.T("vantage.cover_from", new { cover = CoverWord(CoverTo(from, me, foes[0])), name = UnitNames.Of(foes[0]) }));
            sb.Append(", ").Append(Loc.T("vantage.in_range", new { count = inRange }));
            if (threats > 0) sb.Append(", ").Append(Loc.T("vantage.threatened", new { count = threats }));
            return sb.ToString();
        }
        catch (Exception e) { Main.Log?.Error("CombatReads.VantageFrom failed: " + e); return null; }
    }
}
