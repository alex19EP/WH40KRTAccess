using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;  // BaseUnitEntity
using RTAccess.Exploration;             // MapCursor / CursorTarget / Scanner (the two review tools)

namespace RTAccess.Buffers;

/// <summary>
/// The ring of <see cref="Buffer"/>s and the current position within it. Alt+Left/Right cycle between enabled
/// buffers (skipping disabled ones, wrapping); Alt+Up/Down move within the current buffer. Ported from
/// WrathAccess, and the same two-buffer shape: the selected unit and the reviewed unit, both always enabled.
/// (An RT-only third "Target" buffer — manual target, else whose turn it is — was dropped: on the player's own
/// turn it always resolved to the acting unit, i.e. a copy of Selected unit.) The resolvers read the live unit
/// from the game each refresh, so a buffer always reflects the current selection / review without a re-bind.
/// </summary>
internal sealed class BufferManager
{
    public static BufferManager Instance { get; } = new BufferManager();

    private readonly List<Buffer> _buffers = new List<Buffer>();
    private int _position = -1;

    public void Add(Buffer buffer) => _buffers.Add(buffer);

    public Buffer CurrentBuffer
    {
        get
        {
            if (_position < 0 || _position >= _buffers.Count) return null;
            var b = _buffers[_position];
            return b.Enabled ? b : null;
        }
    }

    public bool MoveToNext() => Step(+1);
    public bool MoveToPrevious() => Step(-1);

    // Walk the ring in the given direction to the next enabled buffer; refresh it and (if it follows the
    // latest) jump to its last line. Returns false when no enabled buffer exists.
    private bool Step(int dir)
    {
        if (_buffers.Count == 0) return false;
        int start = _position < 0 ? (dir > 0 ? _buffers.Count - 1 : 0) : _position;
        int i = start;
        do
        {
            i += dir;
            if (i >= _buffers.Count) i = 0;
            if (i < 0) i = _buffers.Count - 1;
            if (_buffers[i].Enabled)
            {
                _position = i;
                _buffers[i].Update();
                if (_buffers[i].FollowLatest && _buffers[i].Count > 0)
                    _buffers[i].MoveToPosition(_buffers[i].Count - 1);
                return true;
            }
        } while (i != start);
        return false;
    }

    /// <summary>Build the standard buffer set (once, at boot). The two unit buffers read their live unit from
    /// the game each refresh: the selected unit (the game's real single selection) and the reviewed unit (point
    /// the tile cursor or the scanner at anyone — the channel that works in turn-based combat where the game
    /// locks the selection). Leaves <c>_position</c> at -1 so the first Alt+Left/Right ENTERS a buffer and reads
    /// its first line (the unit name), then Alt+Up/Down advance from there (the SayTheSpire buffer convention).</summary>
    public void RegisterDefaults()
    {
        if (_buffers.Count > 0) return;
        // Labels resolve now (RegisterDefaults runs after LocalizationManager.Initialize; see Main). A
        // mid-session language change won't retranslate these boot-time labels — an accepted edge case.
        Add(new UnitBuffer(Loc.T("buffer.selected_unit"), SelectedUnit));
        Add(new UnitBuffer(Loc.T("buffer.reviewed"), ReviewedUnit));
        foreach (var b in _buffers) b.Enabled = true;
    }

    // The unit whose data the player is currently LOOKING AT. Inside a member-switching service window
    // (Inventory / Character Info / Augmentations) that's the window's viewed character: SetSelected only
    // retargets SelectedUnitInUI there (the world selection stays put — see ViewedCharacter.SwitchMember),
    // so with the old world-selection-first order a Shift+A/D switch never moved this buffer. Outside those
    // windows: the real selection, then the first of the multi-select. Null when out of game.
    private static BaseUnitEntity SelectedUnit()
    {
        var s = Game.Instance?.SelectionCharacter;
        if (s == null) return null;
        if (RTAccess.Accessibility.ViewedCharacter.WindowActive)
            return s.SelectedUnitInUI.Value ?? s.SelectedUnit.Value ?? s.FirstSelectedUnit;
        return s.SelectedUnit.Value ?? s.SelectedUnitInUI.Value ?? s.FirstSelectedUnit;
    }

    // The unit under REVIEW: whichever of the two review tools the player touched LAST — the tile cursor (the
    // visible unit whose footprint the cursor stands in: the same lens as the tile readout and the aim-commit
    // key) or the scanner's review selection (Period / Comma / N cycles). Last-touched wins, so Period after a
    // cursor step reads the cycled enemy even while the cursor still sits on your own character (the cursor
    // self-plants there — a cursor-first rule would have pinned this buffer to the selected unit, the very
    // complaint that shaped it). When the last-touched tool is not on a unit (cursor over open floor, selection
    // on a chest) the OTHER tool's unit fills in, so stepping across empty tiles never blanks a buffer the
    // scanner filled. Neither depends on the game's unit selection, so this is the buffer that reads anyone in
    // turn-based combat, where CanSelectUnit locks the party selection. CursorTarget already applies the
    // visibility lens; UnitBuffer.Populate gates the scanner path as a backstop.
    private static BaseUnitEntity ReviewedUnit()
    {
        bool cursorLast = MapCursor.TouchedFrame >= Scanner.SelectionFrame;
        var first = cursorLast ? CursorUnit() : Scanner.SelectedUnit();
        return first ?? (cursorLast ? Scanner.SelectedUnit() : CursorUnit());
    }

    private static BaseUnitEntity CursorUnit() => CursorTarget.Inside()?.TargetUnit;
}
