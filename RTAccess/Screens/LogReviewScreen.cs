using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;   // LogThreadService, LogChannelType, CombatLogMessage
using RTAccess.Accessibility;                            // TooltipReader, GlossaryLinks
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The message-log review buffer — the screen-reader equivalent of the sighted combat/message log
    /// window. Opened on demand (bare L, or the HUD "Log" button); a CHILD overlay (like
    /// <see cref="DrillMenuScreen"/>) that owns the keyboard while up and restores focus on Back. See
    /// docs/plans/unified-logging-shannon.md.
    ///
    /// Reads the game's OWN retained history directly — <see cref="LogThreadService"/> keeps every thread's
    /// messages (uncapped, session-scoped, wiped on load, but surviving surface↔space) — so there is no
    /// second copy to maintain: the tap (<see cref="RTAccess.Accessibility.LogTap"/>) only decides what to
    /// SPEAK live; the whole history is always here to review. A channel tab row mirrors the game's four
    /// tabs (All / Events / Dialogue / Combat via the same thread groupings the game's <c>CombatLogVM</c>
    /// uses); the selected channel's lines fill the content stop NEWEST-FIRST (so opening lands on "what
    /// just happened"), capped at <see cref="MaxLines"/>. Each line speaks its spaced-stripped text; Space
    /// drills into the message's rich tooltip and any inline glossary links; Backspace follows the line's
    /// unit (plants the tile cursor on it — the sighted left-click's camera jump), fog-gated.
    ///
    /// Graph-native and IMMEDIATE-MODE, which makes the log LIVE: new lines appear as they arrive — the
    /// old adapter version was a snapshot that needed a reopen to refresh. A line's key is its ABSOLUTE
    /// index in the channel's chronological history (with the message object as the id's reference tier),
    /// so focus stays on ITS line while new lines land above it; content keys carry the channel, so a tab
    /// switch re-keys the content only (tab focus survives). Per-line positions are suppressed — "37 of
    /// 200" per log line is noise.
    /// </summary>
    public sealed class LogReviewScreen : Screen
    {
        private const int MaxLines = 200;

        private readonly struct Tab
        {
            public readonly string LocKey;
            public readonly LogChannelType[] Channels;
            public Tab(string locKey, LogChannelType[] channels) { LocKey = locKey; Channels = channels; }
        }

        // Mirrors CombatLogVM.CreateInGameChannels so the tabs match what a sighted player sees.
        private static readonly Tab[] Tabs =
        {
            new Tab("log.tab.all", new[]
            {
                LogChannelType.Common, LogChannelType.Dialog, LogChannelType.LifeEvents,
                LogChannelType.DialogAndLife, LogChannelType.AnyCombat, LogChannelType.InGameCombat,
            }),
            new Tab("log.tab.events", new[] { LogChannelType.LifeEvents, LogChannelType.DialogAndLife }),
            new Tab("log.tab.dialogue", new[] { LogChannelType.Dialog, LogChannelType.DialogAndLife }),
            new Tab("log.tab.combat", new[] { LogChannelType.AnyCombat, LogChannelType.InGameCombat }),
        };

        // The selected channel tab — genuine mod-side VIEW state (which tab you're reading is
        // cursor-adjacent, not game state; the game's own log panel is not disturbed).
        private int _channel;

        private LogReviewScreen() { Wrap = true; }

        // One cached instance: per-screen nav state is keyed by Screen instance, so KeepStateOnPop only
        // resumes (and doesn't leak one GraphState per open) if reopening pushes the SAME screen object.
        private static LogReviewScreen _instance;

        /// <summary>Toggle the log review: open it as a child of the current screen, or close it when it
        /// (or one of its drill-in children) is already focused — bare L is a natural toggle, and re-pushing
        /// the cached instance onto its own chain is exactly the cycle PushChild refuses. No-op with no top screen.</summary>
        public static void Open()
        {
            var cur = ScreenManager.Current;
            if (cur == null) return;
            for (var s = cur; s != null; s = s.ParentScreen)
                if (s == _instance) { _instance.ParentScreen?.RemoveChild(_instance); return; }
            cur.PushChild(_instance ?? (_instance = new LogReviewScreen()));
        }

        public override string Key => "overlay.log";
        public override string ScreenName => Loc.T("log.title");
        // Nav state survives close/reopen (singleton + KeepStateOnPop): a line still in the window resumes
        // by identity, anything else reconciles toward the newest — reopen-at-newest is the accepted
        // behavior; no position memory beyond what identity reconciliation gives for free.
        public override bool KeepStateOnPop => true;
        public override bool IsActive() => false; // only ever a child

        public override void OnPush() { _channel = 0; } // always reopen on the All tab

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }


        public override void Build(GraphBuilder b)
        {
            var msgs = Messages(_channel);

            // The selected channel's lines, newest first — declared FIRST so opening lands on the newest
            // line (the graph's start node). Keys are the lines' absolute chronological indices (stable as
            // lines append) + the message object as reference (focus follows its line even if indices
            // shift); an empty channel emits a single placeholder line.
            b.BeginStop("content").PushContext(Loc.T(Tabs[_channel].LocKey), role: null, positions: false);
            if (msgs.Count == 0)
            {
                b.AddItem(ControlId.Structural("log:ch" + _channel + ":empty"),
                    GraphNodes.Text(() => Loc.T("log.empty")));
            }
            else
            {
                int start = msgs.Count > MaxLines ? msgs.Count - MaxLines : 0;
                for (int i = msgs.Count - 1; i >= start; i--)
                {
                    var m = msgs[i]; // capture: label and tooltip read the live message per speak/press
                    b.AddItem(ControlId.Referenced(m, MsgKey(_channel, i)), LogLine(m));
                }
            }
            b.PopContext();

            // The channel bar: one horizontal row of tabs (Left/Right walks it; Tab jumps lines ↔ channels).
            b.BeginStop("tabs").PushContext(Loc.T("log.channels"), Loc.T("role.list"));
            b.StartRow();
            for (int i = 0; i < Tabs.Length; i++)
                b.AddItem(ControlId.Structural("log:tab:" + i), TabNode(i));
            b.EndRow();
            b.PopContext();
        }

        private static string MsgKey(int channel, int absIndex) => "log:ch" + channel + ":msg" + absIndex;

        // One log line: a plain text node whose Space offers everything the line can drill into — the
        // message's own rich tooltip (rendered live per press) PLUS any inline glossary <link> terms in the
        // RAW (markup-intact) message text, through the shared chooser: exactly what the adapter path
        // (GraphNavigator.OpenTooltipOrLinks over the old LogRow element) offered.
        private static NodeVtable LogLine(CombatLogMessage msg)
        {
            var vt = GraphNodes.Text(() => Clean(msg.Message));
            vt.OnTooltip = () => OpenDetail(msg);
            // Backspace — the sighted LEFT-CLICK on a log line (CombatLogItemPCView.OnPointerClick scrolls
            // the camera to the message's unit): plant the tile cursor on that unit instead — PlantOn scrolls
            // the camera AND announces the tile, so the cursor verbs now answer from the line's subject.
            // Only lines that carry a unit advertise the verb (system lines get the navigator's "nothing").
            if (msg.Unit != null) vt.OnSecondary = () => FollowUnit(msg);
            return vt;
        }

        // Fog parity: the sighted click only MOVES THE CAMERA (fogged ground stays black), but planting the
        // cursor speaks the exact tile — so a unit the player can't currently see (fog / stealth / scripted-
        // hidden, or despawned since the line landed) refuses with a notice instead of leaking its position.
        // Same unit-level gate as Inspect (IsPlayerFaction || IsVisibleForPlayer).
        private static void FollowUnit(CombatLogMessage msg)
        {
            var unit = msg?.Unit;
            if (unit == null || !(unit.IsPlayerFaction || unit.IsVisibleForPlayer))
            {
                Tts.Speak(Loc.T("log.follow_hidden"), interrupt: true);
                return;
            }
            TileExplorer.PlantOn(unit.Position);
        }

        private static void OpenDetail(CombatLogMessage msg)
        {
            string raw = msg?.Message;
            var tpl = msg?.Tooltip;
            TooltipChooser.Open(Clean(raw), tpl != null ? TooltipReader.GetFull(tpl) : null,
                sections: null, links: GlossaryLinks.Gather(raw));
        }

        // A channel tab ("All, tab[, selected], n of 4"): selecting reads live view state, activation
        // switches the channel and lands on its newest line.
        private NodeVtable TabNode(int idx)
        {
            Func<string> label = () => Loc.T(Tabs[idx].LocKey);
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(() => _channel == idx),
                },
                SearchText = label,
                OnActivate = () => SelectChannel(idx),
                ActivateSound = Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick,
            };
        }

        // Switch the channel and land on its NEWEST line (the adapter behavior) — a focus request the
        // navigator applies on the next render; the landing is announced by the frame differ, never
        // hand-spoken. The content re-keys (keys carry the channel), the tabs keep their keys.
        private void SelectChannel(int idx)
        {
            if (idx < 0 || idx >= Tabs.Length) return;
            _channel = idx;
            var msgs = Messages(idx);
            var target = msgs.Count > 0 ? MsgKey(idx, msgs.Count - 1) : "log:ch" + idx + ":empty";
            Navigation.Active?.FocusNode(ControlId.Structural(target), announce: true);
        }

        // ---- the channel's merged history ----
        // A perf memo over the game's own store, NOT view state: immediate mode rebuilds the graph every
        // frame, and re-merging + re-sorting every thread's history per frame would churn. The merge is a
        // pure function of the store, which is append-only within a service lifetime (threads only Add;
        // Cleanup/game-load swaps or empties them), so (service, channel, total raw count) is a complete
        // invalidation key. Labels/tooltips still read each LIVE message at speak time (ReplaceMessage
        // edits flow through), and the stable OrderBy keeps every line's absolute index fixed as new
        // lines append — the key contract above.
        private LogThreadService _cacheService;
        private int _cacheChannel = -1;
        private int _cacheCount = -1;
        private List<CombatLogMessage> _cacheMsgs = new List<CombatLogMessage>();

        // The selected channel's retained messages, CHRONOLOGICAL (oldest first — index = the line's
        // absolute position in the channel history), separators dropped. Read straight from the game's
        // own store (the single source of truth); null-safe if services aren't up.
        private List<CombatLogMessage> Messages(int channel)
        {
            try
            {
                var svc = LogThreadService.Instance;
                if (svc == null) { _cacheService = null; return _cacheMsgs = new List<CombatLogMessage>(); }
                var threads = svc.GetThreadsByChannelType(Tabs[channel].Channels);
                if (threads == null) return _cacheMsgs = new List<CombatLogMessage>();

                int count = 0;
                foreach (var t in threads) count += t?.AllMessages?.Count ?? 0;
                if (ReferenceEquals(svc, _cacheService) && channel == _cacheChannel && count == _cacheCount)
                    return _cacheMsgs;

                var gathered = new List<CombatLogMessage>(count);
                foreach (var t in threads)
                {
                    var all = t?.AllMessages;
                    if (all == null) continue;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var m = all[i];
                        if (m != null && !m.IsSeparator) gathered.Add(m);
                    }
                }
                // STABLE sort (OrderBy): equal timestamps keep the fixed thread enumeration order, so a
                // line's index never changes between renders — appends land at the end.
                _cacheMsgs = gathered.OrderBy(m => m.Received).ToList();
                _cacheService = svc;
                _cacheChannel = channel;
                _cacheCount = count;
            }
            catch (Exception e) { Main.Log?.Log("log review gather failed: " + e.Message); }
            return _cacheMsgs;
        }

        private static string Clean(string raw)
            => string.IsNullOrWhiteSpace(raw) ? null : TextUtil.StripRichTextSpaced(raw);
    }
}
