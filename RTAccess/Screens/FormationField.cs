using System.Collections.Generic;
using System.Globalization;
using Kingmaker.Code.UI.MVVM.VM.Formation; // FormationCharacterVM
using RTAccess.Audio;                      // Earcons (enter/exit + review cues)
using RTAccess.Input;                      // InputManager (glide-key Held polling)
using UnityEngine;                         // Vector2, Mathf, Time

namespace RTAccess.Screens
{
    /// <summary>
    /// The formation editor — one Tab stop holding a 2-D cursor over the formation's layout space (ported
    /// from WrathAccess's FormationField). WASD step the cursor one grid cell (wired in InputBindings via the
    /// Formation input category, live only while the field is focused); each step announces the party member
    /// there + its position, or the empty cell's position. Enter picks up the member at the cursor and drops
    /// the held one where you press it again. Editing only applies to a Custom formation (the predefined
    /// shapes arrange themselves). Shift+WASD glides the cursor continuously with an enter/exit cue as it
    /// crosses a member, and reads where it landed on release. Comma/Shift+Comma review the members (name +
    /// position + a panned cue at the offset) without moving the cursor; Slash plants the cursor on the
    /// reviewed member; C re-centres; Alt+1..6 grab the Nth member of the window's own character list
    /// (shadowing the global party-select digits only while the field is focused).
    ///
    /// The cursor lives in the formation OFFSET space (metres; +x = east/right of the marching direction,
    /// +y = north/forward) — the same value GetOffset/MoveCharacter use and the game adds to the destination
    /// on a party move, so placement is 1:1 with the layout. The grid step is the game's own drag snap
    /// (FormationCharacterBaseView.SetupPosition: 23 UI px at 40 px-per-metre), so our placements land
    /// exactly on the sighted grid. Positions read in metres + compass words — the maintainer's pick,
    /// matching WrathAccess exactly.
    /// </summary>
    public sealed class FormationField
    {
        private const float GridStep = 23f / 40f;        // one cell: the game's own 23 px snap at 40 px/m ≈ 0.58 m
        private const float FieldHalf = 388f / 2f / 40f; // the draggable field's half-extent ≈ 4.85 m (WA constant)
        private const float GrabRadius = GridStep;        // "on" a member when within ~one cell
        private const float GlideSpeed = 1.5f;            // Shift+WASD continuous speed, m/s (small field → slow)

        private Vector2 _cursor;                 // offset metres; +x = east (right), +y = north (forward)
        private FormationCharacterVM _held;      // the picked-up member, or null
        private FormationCharacterVM _cueInside; // member the glide cursor is over (for the enter/exit cue)
        private bool _wasGliding;                // last frame's glide state (to fire read-on-release)
        private FormationCharacterVM _reviewed;  // member last reached by the Comma cycle (Slash jumps here)

        /// <summary>Step the cursor one grid cell (the Formation WASD actions, live while focused).</summary>
        public void MoveStep(int dx, int dy)
        {
            _cursor = new Vector2(
                Mathf.Clamp(_cursor.x + dx * GridStep, -FieldHalf, FieldHalf),
                Mathf.Clamp(_cursor.y + dy * GridStep, -FieldHalf, FieldHalf));
            Tts.Speak(CellReadout(), interrupt: true);
        }

        // Continuous mode (Shift+WASD), ticked by the screen while the field node is focused: glide the
        // cursor freely (no grid snap), play an enter/exit cue as it crosses a member, and on key release
        // read where it landed. Gliding is too fast to narrate per-frame, so it stays silent on move (the
        // cue carries it) and speaks once on release — the same feel as WA's editor. Unscaled time: the
        // formation window UI-pauses the game clock.
        public void Tick()
        {
            int ix = (InputManager.Held("formation.glide_right") ? 1 : 0) - (InputManager.Held("formation.glide_left") ? 1 : 0);
            int iy = (InputManager.Held("formation.glide_up") ? 1 : 0) - (InputManager.Held("formation.glide_down") ? 1 : 0);
            bool gliding = ix != 0 || iy != 0;

            if (gliding)
            {
                if (!_wasGliding) _cueInside = MemberAt(_cursor); // baseline on glide start (no cue this frame)
                var dir = new Vector2(ix, iy).normalized;
                float step = GlideSpeed * Time.unscaledDeltaTime;
                _cursor = new Vector2(
                    Mathf.Clamp(_cursor.x + dir.x * step, -FieldHalf, FieldHalf),
                    Mathf.Clamp(_cursor.y + dir.y * step, -FieldHalf, FieldHalf));
                var inside = MemberAt(_cursor);
                if (!ReferenceEquals(inside, _cueInside))
                {
                    if (inside != null) Earcons.FormationEnter(); else Earcons.FormationExit();
                    _cueInside = inside;
                }
            }
            else if (_wasGliding)
            {
                Tts.Speak(CellReadout(), interrupt: true); // released → read where the cursor landed
            }
            _wasGliding = gliding;
        }

        /// <summary>Enter: pick up the member at the cursor / drop the held one here (Custom only).</summary>
        public void PickOrDrop()
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }
            if (_held == null)
            {
                var who = MemberAt(_cursor, requireInteractable: true);
                if (who == null) { Tts.Speak(Loc.T("formation.nothing_here"), interrupt: true); return; }
                _held = who;
                Tts.Speak(Loc.T("formation.picked_up", new { name = Name(who) }), interrupt: true);
            }
            else
            {
                _held.MoveCharacter(_cursor); // the game's own drag-drop command (queued, replicated)
                Tts.Speak(Loc.T("formation.placed", new { name = Name(_held) }) + ", " + PositionStr(_cursor),
                    interrupt: true);
                _held = null;
            }
        }

        /// <summary>Comma / Shift+Comma: REVIEW the next/previous member — name + position (relative to the
        /// formation's origin) plus a panned cue at its offset, WITHOUT moving the editing cursor (Slash
        /// plants it there). Works on the predefined shapes too, to hear the layout.</summary>
        public void CycleMember(int dir)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            var members = new List<FormationCharacterVM>();
            foreach (var c in vm.Characters)
                if (c != null && c.m_Unit != null) members.Add(c);
            if (members.Count == 0) { Tts.Speak(Loc.T("formation.no_members"), interrupt: true); _reviewed = null; return; }

            int idx = (_reviewed != null && members.Contains(_reviewed))
                ? Wrap(members.IndexOf(_reviewed) + dir, members.Count)
                : (dir > 0 ? 0 : members.Count - 1); // no baseline → first (next) or last (prev)
            _reviewed = members[idx];
            var off = _reviewed.GetOffset();
            PlayReviewCue(off);
            Tts.Speak(Name(_reviewed) + ", " + PositionStr(off), interrupt: true);
        }

        /// <summary>Slash: plant the editing cursor on the member last reviewed with Comma (mirrors the
        /// exploration "plant the cursor on the review target" idiom).</summary>
        public void JumpToReviewed()
        {
            if (_reviewed == null) { Tts.Speak(Loc.T("formation.no_review"), interrupt: true); return; }
            _cursor = _reviewed.GetOffset();
            Tts.Speak(CellReadout(), interrupt: true);
        }

        /// <summary>Alt+1..6: grab the Nth entry of the window's own character list straight away (start
        /// dragging) — Alt+N, move the cursor, Enter places. The list is the FormationVM's Characters, in
        /// the window's own order (party members, then pets as their own rows), so the digit matches what
        /// Comma-review counts through. Custom only. Does NOT move the cursor (only WASD/Slash do).</summary>
        public void PickMember(int index)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }
            var list = vm.Characters;
            if (list == null || index < 0 || index >= list.Count || list[index] == null || list[index].m_Unit == null
                || !list[index].IsInteractable.Value)
            { Tts.Speak(Loc.T("party.no_member", new { index = index + 1 }), interrupt: true); return; }
            _held = list[index];
            Tts.Speak(Loc.T("formation.picked_up", new { name = Name(_held) }), interrupt: true);
        }

        /// <summary>C: jump the cursor to the formation's origin (0, 0).</summary>
        public void CenterCursor()
        {
            _cursor = Vector2.zero;
            Tts.Speak(CellReadout(), interrupt: true);
        }

        /// <summary>"&lt;member or empty&gt;[, held: X], &lt;position&gt;" — what the cursor is over (the field
        /// node's value part).</summary>
        public string CellReadout()
        {
            var who = MemberAt(_cursor);
            string name = who != null ? Name(who) : Loc.T("formation.empty");
            string line = name + ", " + PositionStr(_cursor);
            if (_held != null) line += ", " + Loc.T("formation.holding", new { name = Name(_held) });
            return line;
        }

        // The party member at/near the cursor (nearest within the grab radius), or null.
        private static FormationCharacterVM MemberAt(Vector2 at, bool requireInteractable = false)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return null;
            FormationCharacterVM best = null;
            float bestSq = GrabRadius * GrabRadius;
            foreach (var c in vm.Characters)
            {
                if (c == null || c.m_Unit == null) continue;
                if (requireInteractable && !c.IsInteractable.Value) continue;
                float sq = (c.GetOffset() - at).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        // The review cue at a layout offset: panned by the lateral (east) component, attenuated with
        // distance from the origin — the field is only ~±4.85 m, so short reference distances.
        private static void PlayReviewCue(Vector2 off)
        {
            const float refDist = 3f, panWidth = 3f; // metres
            float gain = Mathf.Clamp(refDist / (refDist + off.magnitude), 0.1f, 1f);
            Earcons.FormationReview(off.x / panWidth, gain);
        }

        // The offset as "X metres east/west, Y metres north/south" (or "centre" at the origin) — metres +
        // compass, the exact WrathAccess phrasing (the maintainer's pick over the mod's usual tiles).
        private static string PositionStr(Vector2 off)
        {
            const float eps = 0.001f; // collapse only a truly-zero axis (and the exact centre) — never a real value
            if (Mathf.Abs(off.x) < eps && Mathf.Abs(off.y) < eps) return Loc.T("formation.center");
            var parts = new List<string>(2);
            if (Mathf.Abs(off.x) >= eps)
                parts.Add(Loc.T("formation.metres", new { value = Metres(off.x) })
                    + " " + Loc.T(off.x > 0 ? "formation.east" : "formation.west"));
            if (Mathf.Abs(off.y) >= eps)
                parts.Add(Loc.T("formation.metres", new { value = Metres(off.y) })
                    + " " + Loc.T(off.y > 0 ? "formation.north" : "formation.south"));
            return string.Join(", ", parts);
        }

        // Metres to 2 decimals — the grid step is ~0.58 m, so whole-metre rounding would read adjacent
        // cells identically and feel dead; the editor needs the finer precision (WA's convention).
        private static string Metres(float v)
            => Mathf.Abs(v).ToString("0.##", CultureInfo.InvariantCulture);

        private static string Name(FormationCharacterVM c) => c.m_Unit.CharacterName;

        private static int Wrap(int i, int n) => ((i % n) + n) % n;
    }
}
