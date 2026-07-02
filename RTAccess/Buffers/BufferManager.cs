using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Controllers.Combat;     // GetCombatStateOptional()
using Kingmaker.EntitySystem.Entities;  // BaseUnitEntity

namespace RTAccess.Buffers;

/// <summary>
/// The ring of <see cref="Buffer"/>s and the current position within it. Alt+Left/Right cycle between enabled
/// buffers (skipping disabled ones, wrapping); Alt+Up/Down move within the current buffer. Ported from
/// WrathAccess. v1 ships two unit buffers — the selected unit and the current combat target — both always
/// enabled. The resolvers read the live unit from the game each refresh, so a buffer always reflects the
/// current selection / target without an explicit re-bind.
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
    /// the game each refresh: the selected unit (the game's real single selection) and the current combat
    /// target. Leaves <c>_position</c> at -1 so the first Alt+Left/Right ENTERS a buffer and reads its first
    /// line (the unit name), then Alt+Up/Down advance from there (the SayTheSpire buffer convention).</summary>
    public void RegisterDefaults()
    {
        if (_buffers.Count > 0) return;
        // Labels resolve now (RegisterDefaults runs after LocalizationManager.Initialize; see Main). A
        // mid-session language change won't retranslate these two boot-time labels — an accepted edge case.
        Add(new UnitBuffer(Loc.T("buffer.selected_unit"), SelectedUnit));
        Add(new UnitBuffer(Loc.T("buffer.target"), TargetUnit));
        foreach (var b in _buffers) b.Enabled = true;
    }

    // The game's current single selection. Mirrors RTAccess PartyHotkeys.Current(): the real selection, then
    // the UI selection, then the first of the multi-select. Null when nothing's selected or out of game.
    private static BaseUnitEntity SelectedUnit()
    {
        var s = Game.Instance?.SelectionCharacter;
        if (s == null) return null;
        return s.SelectedUnit.Value ?? s.SelectedUnitInUI.Value ?? s.FirstSelectedUnit;
    }

    // The selected unit's manual combat target if it has one; otherwise the unit whose turn it currently is.
    private static BaseUnitEntity TargetUnit()
    {
        var manual = SelectedUnit()?.GetCombatStateOptional()?.ManualTarget as BaseUnitEntity;
        if (manual != null) return manual;
        return Game.Instance?.TurnController?.CurrentUnit as BaseUnitEntity;
    }
}
