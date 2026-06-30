#if DEBUG
using System.Reflection;
using RTAccess.Speech;

namespace RTAccess.Dev;

/// <summary>
/// A tiny PUBLIC surface for eval'd code to reach from. Mono.CSharp eval runs in its own dynamic
/// assembly and sees only PUBLIC members of the mod, while almost all of RTAccess is internal/static — so
/// rather than mirror the codebase, this exposes a handle to reflect from (<see cref="Asm"/>) plus a
/// couple of high-use probes. Reflection into internals is expected and fine (we reflect into the game
/// throughout the mod anyway); this just removes the boilerplate from the common cases. DEBUG-only.
///
/// <see cref="DevApi.Screen"/> and other tree probes land in Phase 2 once the ScreenManager exists.
/// </summary>
public static class DevApi
{
    /// <summary>The mod assembly — reflect into internals with
    /// <c>DevApi.Asm.GetType("RTAccess.Accessibility.UiTextReader")</c> etc.</summary>
    public static Assembly Asm => typeof(DevApi).Assembly;

    /// <summary>Speak a probe line through the real speech path (also lands in /speech).</summary>
    public static void Say(string text) => Speaker.Speak(text, true);
}
#endif
