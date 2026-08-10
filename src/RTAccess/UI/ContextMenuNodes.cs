using System;
using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;   // ContextMenuCollectionEntity
using RTAccess.Screens;                         // ChoiceSubmenuScreen

namespace RTAccess.UI
{
    /// <summary>
    /// The shared driver that surfaces a GAME context menu — a <see cref="ContextMenuCollectionEntity"/>
    /// collection, the one generic model behind every right-click menu (inventory / loot / vendor / cargo /
    /// ship customization / save-slot) — as an accessible submenu, honoring the game's OWN labels, gates,
    /// and commands. This is the collection analogue of <see cref="GraphNodes.MenuEntry"/> (which drives a
    /// single main-menu-sidebar <c>ContextMenuEntityVM</c>): read the entity, mirror its state, run its
    /// <c>Command</c> — never a hand-mirror of the entries (which drifts the moment the game patches a gate
    /// or a DLC adds a verb).
    ///
    /// Rendering follows the game's own <c>ContextMenuCollectionEntity.IsValid</c> filter, with one
    /// accessibility refinement: a header / separator that carries TEXT is announced (as a non-selectable
    /// row) rather than dropped, so a category label is never lost to a blind player.
    /// <list type="bullet">
    /// <item>A no-<c>Command</c> entry (header / separator) with text → a non-selectable header row; a
    /// textless separator → skipped.</item>
    /// <item>A disabled entry (<c>!IsEnabled</c>, non-header) → skipped, exactly as the game hides it.</item>
    /// <item>An enabled entry → a selectable row, greyed ("disabled", Enter inert) when
    /// <c>!IsInteractable</c> — mirroring the game's greyed-but-present entry.</item>
    /// </list>
    /// </summary>
    public static class ContextMenuNodes
    {
        /// <summary>Present <paramref name="entities"/> (a game context-menu collection) as a submenu titled
        /// <paramref name="title"/>. When nothing is actionable, <paramref name="onEmpty"/> runs
        /// (default: a spoken "No actions"). Read the collection at open time — for a virtualized source it
        /// is a snapshot, which matches the game (its own <c>SetupContextMenu</c> rebuilds on the same
        /// events).</summary>
        public static void Open(string title, IReadOnlyList<ContextMenuCollectionEntity> entities, Action onEmpty = null)
        {
            var rows = new List<ChoiceSubmenuScreen.Row>();
            bool anyAction = false;
            if (entities != null)
                foreach (var e in entities)
                {
                    if (e == null) continue;
                    var entity = e; // capture per row for the live label/command closures
                    if (entity.IsEmpty) // a header / separator (no Command)
                    {
                        if (!string.IsNullOrEmpty(Text(entity)))
                            rows.Add(ChoiceSubmenuScreen.Row.Header(() => Text(entity)));
                        continue;
                    }
                    if (!entity.IsEnabled) continue; // the game hides a disabled, non-header entry (IsValid)
                    rows.Add(ChoiceSubmenuScreen.Row.Action(
                        () => Text(entity), () => entity.Execute(), () => entity.IsInteractable));
                    anyAction = true;
                }

            if (!anyAction)
            {
                if (onEmpty != null) onEmpty();
                else Tts.Speak(Loc.T("menu.no_actions"), interrupt: true);
                return;
            }
            ChoiceSubmenuScreen.OpenRows(title, rows);
        }

        // The entity's live label: its LocalizedString title when it carries one, else its plain TitleText
        // (the composed "AutoAddToCargo, on" style), with any SubTitle folded in — so a header whose text
        // lives only in SubTitle is still announced. Resolved per read, so a re-focus re-speaks current text.
        private static string Text(ContextMenuCollectionEntity e)
        {
            var t = e.Title != null ? e.Title.Text : e.TitleText;
            if (!string.IsNullOrEmpty(e.SubTitle))
                t = string.IsNullOrEmpty(t) ? e.SubTitle : t + ", " + e.SubTitle;
            return t;
        }
    }
}
