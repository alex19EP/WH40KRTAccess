using Owlcat.Runtime.UI.ConsoleTools;
using Owlcat.Runtime.UI.ConsoleTools.ClickHandlers;
using Owlcat.Runtime.UI.ConsoleTools.NavigationTool;
using Owlcat.Runtime.UI.MVVM;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// A synthetic, non-visual focus stop we inject into the game's own console-navigation ring (e.g. a dialog's
/// <see cref="GridConsoleNavigationBehaviour"/>). It carries our own spoken text via
/// <see cref="IAccessibleTextProvider"/> so the existing <see cref="SetFocusedPatch"/> reader announces it
/// exactly like a real widget — letting us add accessibility content (the dialogue cue, a heading, a status
/// line) as a navigable item without building a parallel cursor.
///
/// It implements just enough to participate in arrow navigation:
/// <see cref="IConsoleNavigationEntity"/> (focus/validity), <see cref="IConfirmClickHandler"/> (Confirm is a
/// no-op by default, so a pure-text stop does nothing on select), and <see cref="IMonoBehaviour"/> pointing at
/// an optional on-screen anchor so the game's scroll-to-focus brings the real text into view.
/// </summary>
internal sealed class VirtualNavItem : IConsoleNavigationEntity, IConsoleEntity, IConfirmClickHandler, IMonoBehaviour, IAccessibleTextProvider
{
    private readonly Func<string> _text;
    private readonly MonoBehaviour _anchor;
    private readonly Action _onConfirm;
    private readonly string _confirmHint;

    /// <param name="text">Lazy provider of the spoken text (re-read reflects current state).</param>
    /// <param name="anchor">Optional on-screen widget the game's focus handler scrolls to (e.g. the cue view).</param>
    /// <param name="onConfirm">Optional action on Confirm; null = a read-only stop that does nothing on select.</param>
    /// <param name="confirmHint">Optional hint-bar label for Confirm (only meaningful when onConfirm != null).</param>
    public VirtualNavItem(Func<string> text, MonoBehaviour anchor = null, Action onConfirm = null, string confirmHint = null)
    {
        _text = text;
        _anchor = anchor;
        _onConfirm = onConfirm;
        _confirmHint = confirmHint ?? string.Empty;
    }

    public string GetAccessibleText() => _text?.Invoke();

    // IConsoleNavigationEntity — no visual focus state of our own; IsValid keeps us reachable in the ring.
    public void SetFocus(bool value) { }
    public bool IsValid() => true;
    public bool IsSelected() => false;

    // IMonoBehaviour — lets the game's OnFocusChanged scroll to a real on-screen anchor (may be null).
    public MonoBehaviour MonoBehaviour => _anchor;

    // IConfirmClickHandler — default is a read-only stop. The dialog view calls OnConfirmClick WITHOUT first
    // checking CanConfirmClick, so OnConfirmClick must be safe to call when there is no action.
    public bool CanConfirmClick() => _onConfirm != null;
    public void OnConfirmClick() => _onConfirm?.Invoke();
    public string GetConfirmClickHint() => _confirmHint;
}
