#if DEBUG
using RTAccess.Screens;
using RTAccess.UI;

namespace RTAccess.Dev;

/// <summary>
/// Phase 1 smoke test for the ported UI framework: build a scratch tree, walk it via the live
/// <see cref="Navigator"/>, and let the composed announcements flow through Tts → Speaker → /speech.
/// Drive from the dev REPL:
///   RTAccess.Dev.FrameworkProbe.Build()   // build + announce the landing
///   RTAccess.Dev.FrameworkProbe.Down()    // step to the next item (announces the diff)
///   RTAccess.Dev.FrameworkProbe.Up()
/// Then read /speech to hear what was composed (label + role + position).
/// </summary>
public static class FrameworkProbe
{
    private sealed class SmokeScreen : Screen
    {
        public override string Key => "smoke";
        public override string ScreenName => "Smoke Test";
        public override bool IsActive() => true;
    }

    private static SmokeScreen _screen;

    public static string Build()
    {
        _screen = new SmokeScreen();
        var list = new ListContainer("Test Menu");
        list.Add(new TextElement("Alpha", "option"));
        list.Add(new TextElement("Bravo", "option"));
        list.Add(new TextElement("Charlie", "option"));
        _screen.Add(list);

        var nav = Navigation.Active;
        nav.Attach(_screen);      // initial focus, silent
        nav.AnnounceCurrent();    // speak the full landing path
        return "built; current = " + (nav.Current?.GetFocusMessage().Resolve() ?? "(none)");
    }

    public static string Down() => Step(NavDirection.Down);
    public static string Up() => Step(NavDirection.Up);

    private static string Step(NavDirection dir)
    {
        var nav = Navigation.Active;
        var cur = nav.Current;
        var next = cur?.Parent is Container c ? c.GetNeighbor(cur, dir) : null;
        if (next == null) return "(no neighbor " + dir + ")";
        nav.Focus(next);          // walks via the Navigator + announces the diff
        return nav.Current?.GetFocusMessage().Resolve() ?? "(none)";
    }
}
#endif
