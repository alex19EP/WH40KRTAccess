using System.Collections.Generic;
using Kingmaker;                                   // Game
using Kingmaker.Controllers.GlobalMap;             // SectorMapController
using Kingmaker.Globalmap.SectorMap;               // SectorMapObjectEntity, SectorMapPassageEntity
using RTAccess.Exploration;                         // Geo
using RTAccess.Localization;                        // Loc
using RTAccess.UI;                                  // Navigation
using RTAccess.UI.Graph;                            // ControlId
using UnityEngine;                                  // Mathf, Vector3

namespace RTAccess.Screens
{
    /// <summary>
    /// The sector-map LINK WALK: explore the warp-route graph link by link off the ONE selection the Systems list
    /// already carries — no second UI, just movement keys layered on the graph focus. <b>m / Shift+m</b> step to
    /// the next / previous explored link of the current ANCHOR; <b>/</b> re-anchors on the selected system ("explore
    /// links from here"); <b>c</b> snaps the anchor back to the ship's current system. The anchor starts at your
    /// current system; walking to a link and re-anchoring on it lets you go onward, hop by hop — the piece
    /// WrathAccess's connection cycle never had. Every step moves the graph focus (so Enter's Travel/Create/Upgrade
    /// verbs act on whatever you walked to) and speaks the link plus a jumps-from-your-position readout, so the walk
    /// doubles as a route sense without a tree.
    ///
    /// Parity: only EXPLORED links (via <see cref="SectorMapScreen.ExploredNeighbors"/>) — never topology a sighted
    /// player can't see. Keys live in <see cref="RTAccess.Input.InputCategory.WorldMap"/>, declared by
    /// <see cref="SectorMapScreen"/>, so they exist only on the sector map. READ/navigation only (no game mutation),
    /// so they work mid-jump too — the acting verbs do their own Interactive gating.
    /// </summary>
    internal static class SectorMapWalk
    {
        // The system whose links m/Shift+m walk. null → resolves to the ship's current system (the default anchor).
        private static SectorMapObjectEntity _anchor;

        // The ship's current system the last time we looked. A warp jump moves the ship WITHOUT re-pushing the
        // sector map (it stays open showing the route), so OnPush/Reset never fires and a stale _anchor set before
        // the trip (via c or /) would leave m walking the system we LEFT. When the current system changes we drop
        // the anchor so the walk re-centres on where we actually are now.
        private static SectorMapObjectEntity _lastCurrent;

        private static SectorMapController Ctrl => Game.Instance?.SectorMapController;

        /// <summary>Reset on screen (re)open (OnPush) so a stale anchor never carries across visits.</summary>
        internal static void Reset() { _anchor = null; _lastCurrent = null; }

        /// <summary>Re-sync to the ship's current system, dropping a stale anchor if we jumped since last time.
        /// Called at the top of every handler so a mid-walk warp can never leave m walking the old system.
        /// Returns the (new) current system.</summary>
        private static SectorMapObjectEntity SyncCurrent()
        {
            var cur = Ctrl?.CurrentStarSystem;
            if (cur != null && cur != _lastCurrent)
            {
                _lastCurrent = cur;
                _anchor = null; // ship moved — forget the pre-jump anchor, re-centre on the new current system
            }
            return cur;
        }

        // ---- registered handlers (InputBindings "sectormap.*") ----

        internal static void Next() => Step(+1);
        internal static void Prev() => Step(-1);

        /// <summary>/ — re-anchor the walk on the currently selected system.</summary>
        internal static void AnchorHere()
        {
            SyncCurrent(); // record where we are now, so a later step doesn't mistake this deliberate anchor for stale
            var focused = Navigation.FocusedReference as SectorMapObjectEntity;
            if (focused == null) { Tts.Speak(Loc.T("sectormap.walk_none"), interrupt: true); return; }
            _anchor = focused;
            AnnounceAnchor(focused);
        }

        /// <summary>c — snap the anchor back to the ship's current system and focus it.</summary>
        internal static void Home()
        {
            var cur = SyncCurrent();
            if (cur == null) { Tts.Speak(Loc.T("sectormap.walk_none"), interrupt: true); return; }
            _anchor = cur;
            FocusSystem(cur);
            Tts.Speak(Loc.T("sectormap.walk_home", new { name = Name(cur) }), interrupt: true);
        }

        // ---- guts ----

        private static SectorMapObjectEntity ResolveAnchor()
        {
            var cur = SyncCurrent(); // drops a stale anchor if the ship jumped since the last step
            return _anchor ?? cur;
        }

        private static void Step(int dir)
        {
            var anchor = ResolveAnchor();
            if (anchor == null) { Tts.Speak(Loc.T("sectormap.walk_none"), interrupt: true); return; }

            var list = ConnectionList(anchor); // [anchor] + explored neighbours, nearest-first (anchor is index 0)
            if (list.Count <= 1)
            {
                Tts.Speak(Loc.T("sectormap.walk_no_links", new { name = Name(anchor) }), interrupt: true);
                return;
            }

            var cur = Navigation.FocusedReference as SectorMapObjectEntity;
            int i = cur != null ? IndexOf(list, cur) : -1;
            i = i < 0 ? (dir > 0 ? 0 : list.Count - 1) : Mathf.Clamp(i + dir, 0, list.Count - 1);

            var landed = list[i];
            FocusSystem(landed.system);
            AnnounceStep(landed, anchor, i, list.Count);
        }

        private static int IndexOf(List<(SectorMapObjectEntity system, SectorMapPassageEntity passage)> list,
            SectorMapObjectEntity sys)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].system == sys) return i;
            return -1;
        }

        // [anchor (passage null)] + its explored neighbours, sorted by distance from the anchor so the anchor
        // (distance 0) is always index 0 — the "you are exploring from here" home base of the cycle (WA parity).
        private static List<(SectorMapObjectEntity system, SectorMapPassageEntity passage)> ConnectionList(
            SectorMapObjectEntity anchor)
        {
            var list = new List<(SectorMapObjectEntity system, SectorMapPassageEntity passage)> { (anchor, null) };
            list.AddRange(SectorMapScreen.ExploredNeighbors(anchor));
            Vector3 origin = anchor.Position;
            list.Sort((a, b) => Geo.Distance(origin, a.system.Position).CompareTo(Geo.Distance(origin, b.system.Position)));
            return list;
        }

        private static void FocusSystem(SectorMapObjectEntity sys)
        {
            // Structural-key match: the Systems-list node is ControlId.Referenced(Data, "sms:" + uid); focus
            // reconciles by structural key alone, so a Structural id with the same key lands on it. announce:false —
            // we speak our own richer walk line instead of the flat list label.
            if (sys != null) Navigation.FocusNode(ControlId.Structural("sms:" + sys.UniqueId), announce: false);
        }

        // ---- announcements ----

        private static void AnnounceStep((SectorMapObjectEntity system, SectorMapPassageEntity passage) item,
            SectorMapObjectEntity anchor, int index, int count)
        {
            if (item.system == anchor)
                Tts.Speak(Loc.T("sectormap.walk_at_anchor", new { name = Name(item.system), index = index + 1, count }),
                    interrupt: true);
            else
                Tts.Speak(Loc.T("sectormap.walk_link", new
                {
                    name = Name(item.system),
                    difficulty = SectorMapScreen.DifficultyWord(item.passage.CurrentDifficulty),
                    index = index + 1,
                    count
                }), interrupt: true);
        }

        private static void AnnounceAnchor(SectorMapObjectEntity anchor)
        {
            int links = SectorMapScreen.ExploredNeighbors(anchor).Count;
            if (links == 0) { Tts.Speak(Loc.T("sectormap.walk_no_links", new { name = Name(anchor) }), interrupt: true); return; }
            int jumps = SectorMapScreen.JumpsFromCurrent(anchor);
            if (jumps <= 0)
                Tts.Speak(Loc.T("sectormap.walk_anchor_home", new { name = Name(anchor), count = links }), interrupt: true);
            else
                Tts.Speak(Loc.T("sectormap.walk_anchor", new { name = Name(anchor), count = links, jumps }), interrupt: true);
        }

        private static string Name(SectorMapObjectEntity e)
        {
            var v = e?.View;
            return v != null && v.IsExploredOrHasQuests && !string.IsNullOrWhiteSpace(v.Name)
                ? v.Name : Loc.T("sectormap.unknown_system");
        }
    }
}
