---
name: rt-tutorial-popups
description: Tutorial-popup a11y (TutorialScreen) — RT tutorial VM API surface, the big-modal exclusivity rule, and how to trigger a tutorial for testing
metadata: 
  node_type: memory
  type: project
  originSessionId: 24ee1c31-eaa8-4649-aec1-0d613c5aa365
---

Tutorial-popup accessibility (new-paradigm [[ui-paradigm-pivot]] Screen). Files: `RTAccess/Screens/TutorialScreen.cs` (layer 28, registered in ScreenManager), `RTAccess/UI/Proxies/ProxyBoolToggle.cs` (reusable delegate-driven checkbox — RT's `ProxyToggle` only wraps a game `SettingsEntityBoolVM`, so a mod-owned bool needed this), locale keys `tutorial.dont_show`/`tutorial.page_of` (title/prefix/prev_page/next_page/dismiss pre-existed). Ported from WrathAccess `TutorialScreen`.

RT tutorial VM API (`Kingmaker.UI.MVVM.VM.Tutorial`, note: no `_VM` underscore that WotR used):
- Path: `Game.Instance.RootUiContext.CommonVM.TutorialVM` — a plain field, NOT a ReactiveProperty (WotR had `.Value`).
- `TutorialVM.BigWindowVM` (ReactiveProperty<TutorialModalWindowVM>, multi-page modal) / `.SmallWindowVM` (ReactiveProperty<TutorialHintWindowVM>, hint). "Shown" = slot `.Value?.Data != null`; big wins.
- **KEY DIFF from WotR: no `ShowWindow` flag.** Close via `vm.Hide(userInitiated:true)` — its callback DisposeAndRemoves the slot (→ our poll goes inactive → screen pops) and clears `Player.Tutorial.ShowingData`. Ban via `vm.BanTutor()` (call BEFORE Hide).
- `TutorialWindowVM`: Data, Pages, CanBeBanned, BanTutor(), Hide(). `TutorialModalWindowVM`: CurrentPageIndex (IntReactiveProperty), CurrentPage, PageCount, MultiplePages. `TutorialData.Page`: Title/TriggerText/Description/SolutionText.

Exclusivity nuance (adversarial-review fix): `Screen.Exclusive` is polled live per-frame by InputManager.RebuildLive. TutorialScreen uses `Exclusive => Vm() is TutorialModalWindowVM` — the BIG modal owns the keyboard (activeCats collapse to UI+Global, so Exploration/scanner keys don't leak under it, matching MessageBox/NameEntry), while the small HINT stays non-exclusive so exploration keeps working beneath it. Text spoken on appearance even Focus-Mode-off (SpeakText via `tutorial.prefix`); Focus-Mode-on the Navigator's EnsureFocus reads it (announces only when FocusMode.Active, so no double-speak).

Trigger one for testing via dev harness:
- Get a real blueprint: `Game.Instance.Player.Tutorial` is a `TutorialSystem`; `.Facts.GetAll<Kingmaker.Tutorial.Tutorial>()` yields facts whose `.Blueprint as BlueprintTutorial` is a real tutorial (52 in a mid-game save). RT blueprints are NOT UnityEngine.Objects, so `Resources.FindObjectsOfTypeAll` does NOT find them.
- Build data like `AllTutorialsTest`: `new TutorialData(bp,null,default,false); data.AddPage(bp); foreach(ITutorialPage c in bp.GetComponents<ITutorialPage>()) data.AddPage(c);` then `...TutorialVM.ShowTutorial(data)`. (`bp` itself is the primary ITutorialPage; components are extra pages.) Good multi-page candidate: `ConsoleText_ActivePointsInspection_BigTutorial` (2 pages, windowed, bannable). Eval needs `using Kingmaker.Blueprints;` for `GetComponents`. See [[rt-dev-harness]].
