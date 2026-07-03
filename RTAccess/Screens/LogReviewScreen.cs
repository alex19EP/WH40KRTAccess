using System;
using System.Collections.Generic;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;   // LogThreadService, LogChannelType, LogThreadBase, CombatLogMessage
using RTAccess.UI;
using RTAccess.UI.Proxies;

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
    /// SPEAK live; the whole history is always here to review. A channel Bar mirrors the game's four tabs
    /// (All / Events / Dialogue / Combat via the same thread groupings the game's <c>CombatLogVM</c> uses);
    /// the selected channel's lines fill a list NEWEST-FIRST (so opening lands on "what just happened"),
    /// capped at <see cref="MaxLines"/>. Each row is a <see cref="LogRow"/>: spoken as the spaced-stripped
    /// text, but Space drills into the message's rich tooltip and any inline glossary links (reusing the #4
    /// tooltip/link machinery). It is a snapshot taken on open / channel switch (reopen to refresh).
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

        private int _channel;
        private Panel _content;

        private LogReviewScreen() { Wrap = true; }

        // One cached instance: per-screen nav state is keyed by Screen instance, so KeepStateOnPop only
        // resumes (and doesn't leak one GraphState per open) if reopening pushes the SAME screen object.
        private static LogReviewScreen _instance;

        /// <summary>Open the log review (pushed as a child of the current screen). No-op with no top screen.</summary>
        public static void Open() => ScreenManager.Current?.PushChild(_instance ?? (_instance = new LogReviewScreen()));

        public override string Key => "overlay.log";
        public override string ScreenName => Loc.T("log.title");
        // Resuming your place in the history is the point of the log — keep nav state across close/reopen.
        public override bool KeepStateOnPop => true;
        public override bool IsActive() => false; // only ever a child

        public override void OnPush() { _channel = 0; Clear(); _content = new Panel(); Add(_content); Fill(); }
        public override void OnPop() { Clear(); _content = null; }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ParentScreen?.RemoveChild(this));
        }

        // (Re)build the content: the selected channel's messages (newest-first) then the channel bar. On the
        // initial build the framework's EnsureFocus lands first-focus on the newest line; on a channel switch
        // we focus it explicitly (the navigator is already attached to this screen).
        private void Fill(bool focusNewest = false)
        {
            if (_content == null) return;
            _content.Clear();

            var sheet = new FlowSheet();

            var msgs = Messages(_channel);
            var list = sheet.List(Loc.T(Tabs[_channel].LocKey));
            if (msgs.Count == 0) list.Item(new TextElement(Loc.T("log.empty")));
            else foreach (var m in msgs) list.Item(new LogRow(m));

            var bar = sheet.Bar(Loc.T("log.channels"));
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                bar.Cell(new ProxyActionButton(() => TabLabel(idx), () => true, () => SelectChannel(idx),
                    actionVerb: "select"));
            }

            sheet.Reflow();
            _content.Add(sheet);

            if (focusNewest)
            {
                var cell = sheet.CellAt(0, 0); // first row = newest line (or the "No messages" placeholder)
                if (cell != null) Navigation.Focus(cell, announce: true);
            }
        }

        private void SelectChannel(int idx)
        {
            if (idx < 0 || idx >= Tabs.Length) return;
            _channel = idx;
            Fill(focusNewest: true);
        }

        private string TabLabel(int idx)
        {
            var name = Loc.T(Tabs[idx].LocKey);
            return idx == _channel ? name + ", " + Loc.T("log.active_marker") : name;
        }

        // The selected channel's retained messages, newest-first, separators dropped, capped at MaxLines.
        // Read straight from the game's own store (the single source of truth); null-safe if services aren't up.
        private static List<CombatLogMessage> Messages(int channel)
        {
            var result = new List<CombatLogMessage>();
            try
            {
                var svc = LogThreadService.Instance;
                if (svc == null) return result;
                var threads = svc.GetThreadsByChannelType(Tabs[channel].Channels);
                if (threads == null) return result;
                foreach (var t in threads)
                {
                    var all = t?.AllMessages;
                    if (all == null) continue;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var m = all[i];
                        if (m != null && !m.IsSeparator) result.Add(m);
                    }
                }
                result.Sort((a, b) => b.Received.CompareTo(a.Received)); // newest first
                if (result.Count > MaxLines) result.RemoveRange(MaxLines, result.Count - MaxLines);
            }
            catch (Exception e) { Main.Log?.Log("log review gather failed: " + e.Message); }
            return result;
        }

        // One log line: spoken as the spaced-stripped text, but exposes the RAW (markup-intact) message as its
        // link source so Space drills any inline glossary <link> (via GlossaryLinks), and carries the message's
        // own rich tooltip for the fuller breakdown.
        private sealed class LogRow : TextElement
        {
            private readonly string _raw;

            public LogRow(CombatLogMessage msg)
                : base(() => msg == null ? null : Clean(msg.Message), tooltip: () => msg?.Tooltip)
            {
                _raw = msg?.Message;
            }

            public override string GetLinkSourceText() => _raw;

            private static string Clean(string raw)
                => string.IsNullOrWhiteSpace(raw) ? null : TextUtil.StripRichTextSpaced(raw);
        }
    }
}
