---
name: rt-speechmod-reference
description: "SpeechMod (Osmodium) is the authoritative RT TTS reference; its hook targets + bridge shape, cross-checked into HOOKMAP.md"
metadata: 
  node_type: memory
  type: reference
  originSessionId: abb9bd34-f479-4987-8bc1-8a5029aa0007
---

SpeechMod (https://github.com/Osmodium/W40KRogueTraderSpeechMod, **MIT, Copyright 2021 Christian Schubert**) is the known-working RT TTS mod and the authoritative prior art for the text-UI surface. WotR sibling (PathfinderTextToSpeechMod, same license) is secondary (shared class lineage). Cloned read-only to `/tmp/speechmod-rt` and `/tmp/speechmod-wotr`.

Full cross-checked hook map written to **`HOOKMAP.md` at repo root** (2026-06-05) — 10 subsystems, exact signatures, VERIFIED/STATIC/DRIFT flags, tractability ranking, recommended first prototype = postfix `DialogVM.HandleOnCueShow` → read `Game.Instance.DialogController.CurrentCue.DisplayText`. See [[rt-a11y-architecture-verdict]].

Durable facts about SpeechMod (verified against this build):
- **Mouse-driven, NOT focus-driven.** Core read mechanism `Hooks.HookupTextToSpeech` attaches a `TextMeshProValues` component + subscribes UniRx `OnPointerEnter/Exit/Click` — speaks on mouse hover/click. It has zero focus awareness. THIS is the gap our mod fills (drive from `ConsoleEntityExtensions.SetFocused` + VM events instead).
- **No reflective/defensive method resolution** (corrects the common assumption). Every patch binds compile-time via `[HarmonyPatch(typeof(X), nameof(X.M))]`; relies on BepInEx AssemblyPublicizer (`Publicize="true"`) to reach private `m_*` fields. No `AccessTools`/`TargetMethod`/try-catch-per-patch — a renamed member aborts `PatchAll`. If we want per-patch resilience we must add it ourselves.
- Build: `net481`, references game `Code.dll`/`Kingmaker*`/`Owlcat*`/`RogueTrader*` + `UnityModManager.dll` + `0Harmony.dll`, most Publicized.
- Bridge shape to keep (swap SAPI→eSpeak NG/SRAL per [[rt-speech-backend]]): `interface ISpeech { GetAvailableVoices, IsSpeaking, SpeakDialog, SpeakAs, Speak, Stop, ... }` → `WindowsSpeech` (SSML) → `WindowsVoiceUnity : MonoBehaviour` with `[DllImport]` externs (initSpeech/addToSpeechQueue/destroySpeech/getSpeechState/...), hosted as a DontDestroyOnLoad singleton via `SpeechExtensions.AddUiElements<T>`. `VoiceType { Narrator, Female, Male, Protagonist }`. `PhoneticDictionary.PrepareText` (regex JSON map) is backend-independent, reusable verbatim.
- UMM lifecycle (`Main.cs`): `Load(ModEntry)` → SetSpeech (platform switch) → `ModSettings.Load<Settings>` → assign OnToggle/OnGUI/OnSaveGUI → `new Harmony(id).PatchAll()`. `Main.Enabled` (set by OnToggle) gates every patch.
- Keybinds use the game-native route `Game.Instance.Keyboard.Bind(name, action)` (returns IDisposable) registered in a postfix on `CommonPCView.BindViewImplementation` — but `Game.Instance.Keyboard` is `KeyboardAccess` (must `RegisterBinding(name, KeyCode, gameModes)` before `Bind` for a NEW key), NOT the Rewired keyboard.
- Verified-still-current targets (1-4): `DialogVM.HandleOnCueShow(CueShowData)` [now has a param but single overload, name-patch still binds], `DialogController.SelectAnswer(BlueprintAnswer, BaseUnitEntity)`, `DialogAnswerBaseView`/`SurfaceDialogBaseView<T>`/`BookEventCueView.SetText`, `InfoBaseView<TInfoBaseVM>.SetPart` [private, generic], `TooltipBrickIconStatValueView`, Journal/Encyclopedia views [text in UpdateView not BindView], `BarkPlayer.Bark`/`BarkExploration` [in `Kingmaker.Code.UI.MVVM.VM.Bark`, return `IBarkHandle`, BarkExploration private].
