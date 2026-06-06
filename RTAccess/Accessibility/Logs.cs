namespace RTAccess.Accessibility;

/// <summary>Coordinates the mod's log files (focus + speech). Reset together on each new game/area load.</summary>
internal static class Logs
{
    public static void Init(string modDir)
    {
        FocusLog.Init(modDir);
        SpeechLog.Init(modDir);
    }

    public static void ResetAll()
    {
        FocusLog.Reset();
        SpeechLog.Reset();
    }
}
