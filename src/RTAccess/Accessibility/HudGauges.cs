using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Surface;   // SurfaceStaticPartVM
using Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.MomentumAndVeil; // MomentumEntityVM / VeilThicknessVM (they own their tooltips)
using Kingmaker.Code.UI.MVVM.VM.NecronTimer;                   // NecronTimerVM
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
            var s = MomentumLine();
            if (s != null) parts.Add(s);
        }

        /// <summary>The momentum gauge as one line, or null out of turn-based combat. Shared with the HUD
        /// tree's momentum row (<see cref="RTAccess.Screens.InGameScreen"/>) so the K readout and the
        /// browsable node never drift apart.</summary>
        internal static string MomentumLine()
        {
            var me = MomentumVm();
            if (me == null) return null;
            int max = Game.Instance.BlueprintRoot.WarhammerRoot.MomentumRoot.MaximalMomentum;
            int value = Mathf.RoundToInt(me.CurrentPercent.Value * max);
            var s = Loc.T("gauge.momentum", new { value, max });
            if (me.HeroicActActive.Value) s += ", " + Loc.T("gauge.heroic_act");
            if (me.DesperateMeasureActive.Value) s += ", " + Loc.T("gauge.desperate_measure");
            return s;
        }

        /// <summary>The live momentum entity VM (non-null only in turn-based combat) — it OWNS the
        /// TooltipTemplateMomentum the sighted HUD hover shows, so the node hands this VM's own reactive to
        /// the chooser rather than re-deriving thresholds from MomentumRoot.</summary>
        internal static MomentumEntityVM MomentumVm()
            => StaticPart()?.SurfaceHUDVM?.ActionBarVM?.SurfaceMomentumVM?.MomentumEntityVM?.Value;

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
            var s = VeilLine();
            if (s != null) parts.Add(s);
        }

        /// <summary>The veil-thickness gauge as one line, or null while it does not apply. Shared with the
        /// HUD tree's veil row.</summary>
        internal static string VeilLine()
        {
            var veil = VeilVm();
            if (veil == null) return null;
            int value = veil.Value.Value;
            if (value <= 0 && !(Game.Instance?.TurnController?.TurnBasedModeActive ?? false)) return null;
            var root = Game.Instance.BlueprintRoot.WarhammerRoot.PsychicPhenomenaRoot;
            var s = Loc.T("gauge.veil", new { value, max = root.MaximumVeilOnAllLocation });
            if (value >= root.CriticalVeilOnAllLocation) s += ", " + Loc.T("gauge.veil_critical");
            return s;
        }

        /// <summary>The live veil VM. Its <c>Tooltip</c> is a LONG-LIVED TooltipTemplateVail kept fresh by a
        /// subscription on Value — hand that field straight to the chooser; a freshly constructed one would
        /// carry no value at all.</summary>
        internal static VeilThicknessVM VeilVm()
            => StaticPart()?.SurfaceHUDVM?.ActionBarVM?.VeilThickness;

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
            var s = NecronLine();
            if (s != null) parts.Add(s);
        }

        /// <summary>The Necron countdown as one line, or null while it is locked / hidden. The label leads
        /// with the game's OWN header string (NecronTimerView titles its tooltip with it), falling back to
        /// the mod table.</summary>
        internal static string NecronLine()
        {
            var n = NecronVm();
            if (n == null || !n.IsUnlockedAndVisible.Value) return null;
            return Loc.T("gauge.necron_timer", new { value = n.CurrentTimerValue.Value });
        }

        internal static NecronTimerVM NecronVm() => StaticPart()?.NecronTimerVM;

        private static void AppendObjective(List<string> parts)
        {
            var e = StaticPart()?.EtudeCounterVM;
            if (e == null || !e.IsShowing.Value) return;
            // Fail/success flip (main-HUD audit #6): the sighted view swaps the counter for a red FAIL label /
            // a success icon AND hides the digit container — so speak the state word and never the stale digits.
            bool failed = e.IsSystemFailEnabled.Value;
            bool succeeded = e.IsSystemSuccessEnabled.Value;
            string counter = !failed && !succeeded && e.ShowCounter.Value ? e.Counter.Value : "";
            // A Slider-flag-only etude routes its value exclusively through Progress (ShowCounter false,
            // ShowProgress requires a positive target) — speak it as a rounded percent so it isn't silent.
            if (counter.Length == 0 && !failed && !succeeded && e.ShowProgress.Value)
                counter = Loc.T("gauge.objective_percent", new { percent = Mathf.RoundToInt(e.Progress.Value * 100f) });
            string line = Loc.T("gauge.objective", new { label = e.Label.Value, counter });
            if (failed) line += ", " + Loc.T("gauge.objective_failed");
            else if (succeeded) line += ", " + Loc.T("gauge.objective_succeeded");
            parts.Add(line);
        }
    }
}
