using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Surface;   // SurfaceStaticPartVM
using RTAccess.Speech;
using UnityEngine;                          // Mathf

namespace RTAccess.Accessibility
{
    /// <summary>
    /// K — one-press readout of the surface HUD's Rogue-Trader resource / pressure gauges that the
    /// mod's Tab tree (<see cref="RTAccess.Screens.InGameScreen"/>) doesn't carry: momentum (with
    /// heroic-act / desperate-measure readiness), veil thickness, profit factor, a boss HP bar, the
    /// turn and Necron countdowns, and the on-HUD etude objective counter.
    ///
    /// <para>Read-only — it never dispatches — and self-filtering: each gauge speaks only while its
    /// VM reports it is showing / relevant, so out of combat this is just profit factor (plus any
    /// active objective or Necron countdown) and in a fight it adds momentum / veil / boss HP /
    /// turn timer. Registered <see cref="RTAccess.Input.InputCategory.Exploration"/> (live while the
    /// in-game screen owns the world), like the party hotkeys. All VM field paths are verified
    /// against the decompiled source — see docs/plans/tiered-gauging-hollerith.md §0.</para>
    /// </summary>
    internal static class HudGauges
    {
        /// <summary>Speak every currently-relevant gauge as one comma-joined line (interrupts, since
        /// it's a key-driven read); says <c>gauge.none</c> if nothing applies.</summary>
        public static void ReadAll()
        {
            try
            {
                var parts = new List<string>();
                AppendMomentum(parts);
                AppendMoveArea(parts);
                AppendVeil(parts);
                AppendProfitFactor(parts);
                AppendBoss(parts);
                AppendTurnTimer(parts);
                AppendNecronTimer(parts);
                AppendObjective(parts);
                Speaker.Speak(parts.Count == 0 ? Loc.T("gauge.none") : string.Join(", ", parts), interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("HudGauges.ReadAll: " + e); }
        }

        private static SurfaceStaticPartVM StaticPart() => Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM;

        // Momentum lives inside the action-bar VM; MomentumEntityVM.Value is non-null only in turn-based
        // combat, so this is the combat gate. The raw value is private on the entity VM — recover it from
        // the public CurrentPercent × MaximalMomentum (RT momentum is a 0..200 pool).
        private static void AppendMomentum(List<string> parts)
        {
            var me = StaticPart()?.SurfaceHUDVM?.ActionBarVM?.SurfaceMomentumVM?.MomentumEntityVM?.Value;
            if (me == null) return;
            int max = Game.Instance.BlueprintRoot.WarhammerRoot.MomentumRoot.MaximalMomentum;
            int value = Mathf.RoundToInt(me.CurrentPercent.Value * max);
            parts.Add(Loc.T("gauge.momentum", new { value, max }));
            if (me.HeroicActActive.Value) parts.Add(Loc.T("gauge.heroic_act"));
            if (me.DesperateMeasureActive.Value) parts.Add(Loc.T("gauge.desperate_measure"));
        }

        // Reachable-movement extent for the current turn — the size of the game's blue move-area highlight
        // plus the movement-point budget (PathInfo.MoveAreaSummary reads UnitMovableAreaController's own set).
        // Self-gating: null outside a controllable turn-based turn (spent out / not your turn / not in combat),
        // so out of combat this adds nothing. Own-unit read — parity-safe, no fog gate.
        private static void AppendMoveArea(List<string> parts)
        {
            var s = RTAccess.Exploration.PathInfo.MoveAreaSummary();
            if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
        }

        // Veil persists across the area (psychic phenomena), so report it whenever it's non-zero or a
        // fight is on — not only while the action bar shows it.
        private static void AppendVeil(List<string> parts)
        {
            var veil = StaticPart()?.SurfaceHUDVM?.ActionBarVM?.VeilThickness;
            if (veil == null) return;
            int value = veil.Value.Value;
            if (value <= 0 && !(Game.Instance?.TurnController?.TurnBasedModeActive ?? false)) return;
            var root = Game.Instance.BlueprintRoot.WarhammerRoot.PsychicPhenomenaRoot;
            parts.Add(Loc.T("gauge.veil", new { value, max = root.MaximumVeilOnAllLocation }));
            if (value >= root.CriticalVeilOnAllLocation) parts.Add(Loc.T("gauge.veil_critical"));
        }

        // The strategic resource — always available; the HUD only shows it transiently as a notification.
        private static void AppendProfitFactor(List<string> parts)
        {
            var pf = Game.Instance?.Player?.ProfitFactor;
            if (pf == null) return;
            parts.Add(Loc.T("gauge.profit_factor", new { value = Mathf.RoundToInt(pf.Total) }));
        }

        private static void AppendBoss(List<string> parts)
        {
            var boss = StaticPart()?.BossHPBarVM;
            if (boss == null || !boss.IsShowing.Value) return;
            parts.Add(Loc.T("gauge.boss", new { name = boss.BossName.Value, hp = boss.HPLabel.Value }));
        }

        private static void AppendTurnTimer(List<string> parts)
        {
            var t = StaticPart()?.TurnTimerVM;
            if (t == null || !t.IsShowing.Value) return;
            parts.Add(Loc.T("gauge.turn_timer", new { time = t.Counter.Value }));
        }

        private static void AppendNecronTimer(List<string> parts)
        {
            var n = StaticPart()?.NecronTimerVM;
            if (n == null || !n.IsUnlockedAndVisible.Value) return;
            parts.Add(Loc.T("gauge.necron_timer", new { value = n.CurrentTimerValue.Value }));
        }

        private static void AppendObjective(List<string> parts)
        {
            var e = StaticPart()?.EtudeCounterVM;
            if (e == null || !e.IsShowing.Value) return;
            string counter = e.ShowCounter.Value ? e.Counter.Value : "";
            parts.Add(Loc.T("gauge.objective", new { label = e.Label.Value, counter }));
        }
    }
}
