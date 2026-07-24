using System.Text;
using Kingmaker;                                       // Game (the common UI context)
using Kingmaker.Blueprints.Credits;                    // BlueprintCreditsGroup
using Kingmaker.Blueprints.Root.Strings;               // UIStrings (The End / skip prompt)
using Kingmaker.Code.UI.MVVM.View.Credits;             // PageGenerator (the block markup reader)
using Kingmaker.UI.MVVM.VM.Credits;                    // TitlesVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The end-of-campaign titles roll (<see cref="TitlesVM"/>, raised by the <c>StartEndGameTitles</c> game
    /// action through <c>CommonVM.HandleShowEndGameTitles</c>) — the self-scrolling credits the game plays after
    /// the final scene. Sighted, it is a timed scroll over the "The End" plate; read with a screen reader it
    /// becomes one navigable line per printed row, moving at the player's pace.
    ///
    /// The VM hands out its blocks already formatted with the credits page markup
    /// (<c>&lt;company&gt;</c> / <c>&lt;header&gt;</c> / <c>&lt;person&gt;&lt;role&gt;</c> / <c>&lt;text&gt;</c>),
    /// exactly what <c>CreditsOneColumnPage.Append</c> renders — so the rows are unwrapped with the game's own
    /// <see cref="PageGenerator"/> readers and the role KEY resolved through the block's own
    /// <c>RolesData</c>, the same lookup the visual page does. The flattening otherwise matches
    /// <see cref="CreditsScreen"/>'s: "person — role", roles comma-joined.
    ///
    /// Escape drives the VM's own <see cref="TitlesVM.OpenCancelSettingsDialog"/> — the game's "skip the
    /// titles?" confirm box, which lands on <see cref="MessageBoxScreen"/> (30) and only then closes. The roll
    /// still runs underneath (the game's own view owns the scroll and ends it), so leaving this screen alone
    /// simply lets the campaign finish as it would.
    ///
    /// Layer 26, Exclusive: a terminal full-screen reading surface, below the confirm modal it raises.
    /// </summary>
    public sealed class TitlesScreen : Screen
    {
        public TitlesScreen() { Wrap = true; }

        public override string Key => "overlay.titles";
        public override string ScreenName => Loc.T("titles.screen");
        public override int Layer => 26;
        public override bool Exclusive => true;

        private static TitlesVM Vm() => Game.Instance?.RootUiContext?.CommonVM?.TitlesVM?.Value;

        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "titles.skip"),
                    _ => Skip(vm));
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The blocks are generated on demand — the game's own view does this on bind, but our screen can
            // come up first (the poll runs every frame), so generate if nobody has yet.
            if (vm.Titles == null)
            {
                try { vm.TryGenerateTitles(); }
                catch (System.Exception e) { Main.Log?.Error("TitlesScreen: title generation failed: " + e); }
            }

            b.BeginStop("titles").PushContext(Loc.T("titles.screen"), Loc.T("role.list"));
            b.AddLabel(ControlId.Structural("titles:theend"),
                () => GameText.Or(() => UIStrings.Instance.Credits.TheEndText, "titles.the_end"));

            var blocks = vm.Titles;
            int row = 0;
            if (blocks != null)
                foreach (var block in blocks)
                    foreach (var line in Rows(block.Item1, block.Item2))
                    {
                        var l = line; // capture
                        b.AddItem(ControlId.Structural("titles:row:" + row++), GraphNodes.Text(() => l));
                    }
            b.PopContext();

            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("titles:skip"),
                GraphNodes.Button(() => Loc.T("titles.skip"), () => Skip(Vm())));
            b.PopContext();
        }

        // The game's own skip path: a confirm box that closes the titles on Yes.
        private static void Skip(TitlesVM vm)
        {
            if (vm == null) return;
            try { vm.OpenCancelSettingsDialog(); }
            catch (System.Exception e) { Main.Log?.Error("TitlesScreen.Skip: " + e); }
        }

        // One spoken line per printed row of a block, unwrapped with the game's own markup readers. A
        // &lt;text&gt; paragraph may carry embedded newlines (the generator appends it whole), so an unterminated
        // text tag keeps absorbing lines until it closes — the visual page truncates there, we don't.
        private static IEnumerable<string> Rows(string block, BlueprintCreditsGroup group)
        {
            if (string.IsNullOrEmpty(block)) yield break;
            var pending = new StringBuilder();

            foreach (var raw in block.Split('\n'))
            {
                string line = raw.Replace("\r", "");
                if (pending.Length > 0)
                {
                    pending.Append('\n').Append(line);
                    if (line.IndexOf("</text>", System.StringComparison.Ordinal) < 0) continue;
                    line = pending.ToString();
                    pending.Clear();
                }
                else if (line.IndexOf("<text>", System.StringComparison.Ordinal) >= 0
                         && line.IndexOf("</text>", System.StringComparison.Ordinal) < 0)
                {
                    pending.Append(line);
                    continue;
                }

                foreach (var s in Unwrap(line, group)) yield return s;
            }

            // An unterminated paragraph at the end of a block: read what there is rather than drop it.
            if (pending.Length > 0)
                foreach (var s in Unwrap(pending.ToString(), group)) yield return s;
        }

        private static IEnumerable<string> Unwrap(string line, BlueprintCreditsGroup group)
        {
            if (string.IsNullOrWhiteSpace(line)) yield break;

            string company = PageGenerator.ReadCompany(line);
            if (!string.IsNullOrWhiteSpace(company)) yield return TextUtil.StripRichTextSpaced(company);

            string header = PageGenerator.ReadHeader(line);
            if (!string.IsNullOrWhiteSpace(header)) yield return TextUtil.StripRichTextSpaced(header);

            string person = PageGenerator.ReadPerson(line);
            if (!string.IsNullOrWhiteSpace(person))
            {
                // The role tag carries the KEY (possibly several, pipe-separated); the block's roles blueprint
                // resolves it to display names, newline-joined — read them comma-joined.
                string role = null;
                try { role = group?.RolesData?.GetRole(PageGenerator.ReadRole(line)); } catch { }
                role = string.IsNullOrWhiteSpace(role) ? null : role.Replace("\n", ", ").Trim();
                yield return role == null ? person : person + " — " + role;
            }

            string text = PageGenerator.ReadText(line);
            if (!string.IsNullOrWhiteSpace(text)) yield return TextUtil.StripRichTextSpaced(text);
        }
    }
}
