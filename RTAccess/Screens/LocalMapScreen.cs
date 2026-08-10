using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;   // ServiceWindowsType, ServiceWindowsVM
using RTAccess.Exploration;                       // LocalMapCursor, LocalMapReview
using RTAccess.Input;                             // InputCategory
using RTAccess.UI;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Local Map service window (<c>LocalMapVM</c>) — a place you sweep, not a list you read.
    ///
    /// The sighted window is a baked top-down photograph of the area part (<c>WarhammerLocalMapRenderer</c>, 5 px
    /// per world unit, fog-masked in the shader) with pins laid over it. Its only load-bearing verb is right-click →
    /// <c>UnitCommandsRunner.MoveSelectedUnitsToPoint</c>; left-click merely scrolls the camera, and the pins are not
    /// clickable. The picture itself carries no text, so there is nothing to transcribe — what a sighted player
    /// actually DOES with this window is move their eye over the level and pick a spot. This screen mirrors that
    /// act rather than its pixels (the WrathAccess map-viewer paradigm): a free movement cursor
    /// (<see cref="LocalMapCursor"/> — arrows sweep the map rectangle, over walls and across fog, narrating rooms as
    /// it crosses them) paired with review cycles over the map's content (<see cref="LocalMapReview"/> — party,
    /// enemies, neutrals, pins, exits, nearest-first FROM the cursor). Cycle to a thing, jump the cursor onto it,
    /// then plant or travel.
    ///
    /// It therefore declares NO graph nodes and starts unfocused: the cursor owns the keyboard, exactly as the
    /// in-game screen hands the world cursor its arrows. The keys live in
    /// <see cref="InputCategory.LocalMap"/> — a category of its own so the map's verbs are fully isolated from the
    /// in-area ones and the same physical keys route to whichever surface is up (the pattern
    /// <see cref="InputCategory.WorldMap"/> already set for the sector map). Escape closes, via UI.
    ///
    /// ScreenName is null — <c>ServiceWindowAnnounce</c> already speaks "Local Map" from
    /// <see cref="ServiceWindowInfo"/>.
    /// </summary>
    public sealed class LocalMapScreen : Screen
    {
        public const string ScreenKey = "service.LocalMap";

        public override string Key => ScreenKey;
        public override int Layer => 10;
        public override string ScreenName => null; // ServiceWindowAnnounce speaks "Local Map"

        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.LocalMap;

        /// <summary>The map cursor owns the arrows from the moment the window opens; there is nothing to Tab to.</summary>
        public override bool StartUnfocused => true;

        /// <summary>Letters are map keys (c / x / n / m / b), never a type-ahead search.</summary>
        public override bool AllowsTypeahead => false;

        // LocalMap first, so its arrows shadow the navigator's identical ui.* chords; UI second, for Escape.
        private static readonly IReadOnlyList<InputCategory> Cats = new[] { InputCategory.LocalMap, InputCategory.UI };
        public override IReadOnlyList<InputCategory> InputCategories => Cats;

        /// <summary>
        /// Open: seed the map cursor from the in-area cursor (the spot you were exploring), clear the last peek's
        /// review selection, and hand the SOUNDSCAPE over to the map cursor.
        ///
        /// That last step is what makes the map a place rather than a readout: sonar, the wall-tone bed and the fog
        /// cue all listen from <c>MapCursor.ListenPosition</c>, so sweeping the map walks the whole soundscape with
        /// you — walls close in and open out, objects ping from their real bearings — instead of leaving it parked
        /// on the party. Order matters: seed the cursor BEFORE redirecting, so the first frame of audio is heard
        /// from the seeded point and not from wherever the map cursor was left last time.
        /// </summary>
        public override void OnPush()
        {
            LocalMapReview.Reset();
            LocalMapCursor.Reset();
            MapCursor.SetListenOverride(() => LocalMapCursor.Position);
        }

        /// <summary>Close: give the soundscape back to the in-area cursor, then drop the map's own point and
        /// selection. The in-area cursor itself is untouched — Enter is the only thing that writes it — so closing
        /// the map snaps every world readout AND the soundscape back to where you were exploring.</summary>
        public override void OnPop()
        {
            MapCursor.ClearListenOverride();
            LocalMapCursor.Clear();
            LocalMapReview.Clear();
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Close());
        }

        public static void Close() => UiContexts.ServiceWindows()?.HandleCloseAll();

        public override List<string> GetHelpMessages() => new List<string>
        {
            Loc.T("localmap.help.move"),
            Loc.T("localmap.help.review"),
            Loc.T("localmap.help.verbs"),
        };
    }
}
