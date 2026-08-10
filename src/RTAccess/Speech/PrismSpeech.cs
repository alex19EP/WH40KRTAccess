using System.Runtime.InteropServices;
using System.Text;

namespace RTAccess.Speech;

/// <summary>
/// Speech via the native Prism library (https://github.com/ethindp/prism) — the same C ABI used by the
/// ONI and RimWorld accessibility mods (OniAccess <c>PrismBackend</c>, RimWorldAccess <c>PrismNative</c>).
///
/// We bind the C functions by hand instead of referencing the Prismatoid NuGet package because Prismatoid
/// targets <c>net10.0</c>, and the game runs on Unity's Mono (<c>net481</c>) which cannot load a net10
/// assembly. The native <c>prism.dll</c> is runtime-agnostic, so the same backends (NVDA/SAPI/OneCore/JAWS…)
/// are available. Text is marshalled as NUL-terminated UTF-8 (Prism expects UTF-8; ONI's CharSet.Ansi
/// binding corrupts non-ASCII — RT has plenty, so we follow RimWorld and marshal UTF-8 ourselves).
///
/// Ships <c>prism.dll</c> (+ <c>nvdaControllerClient64.dll</c> for the NVDA backend) in the mod folder.
/// We <c>LoadLibrary</c> them by full path first (Mono's DllImport search does not include the mod dir),
/// after which the plain <c>[DllImport("prism")]</c> calls resolve to the loaded module — the ONI pattern.
/// </summary>
internal sealed class PrismSpeech : ISpeech
{
    private const string Lib = "prism";
    private const int PRISM_OK = 0;
    private const int PRISM_ERROR_NOT_SPEAKING = 10;

    // PrismConfig is a single byte { version }. We get it from prism_config_init() like ONI/RimWorld.
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
    private struct PrismConfig { public byte version; }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern PrismConfig prism_config_init();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_init(ref PrismConfig cfg);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void prism_shutdown(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_registry_acquire_best(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_backend_name(IntPtr backend);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int prism_backend_speak(IntPtr backend, byte[] utf8Text, [MarshalAs(UnmanagedType.U1)] bool interrupt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int prism_backend_stop(IntPtr backend);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void prism_backend_free(IntPtr backend);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr prism_error_string(int error);

    // Mono resolves DllImport("prism") against the game exe dir / PATH, not the mod folder, so preload by path.
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private IntPtr _ctx;
    private IntPtr _backend;
    private readonly string _backendName;

    public string Name => "Prism (" + _backendName + ")";

    private PrismSpeech(IntPtr ctx, IntPtr backend, string backendName)
    {
        _ctx = ctx;
        _backend = backend;
        _backendName = backendName;
    }

    /// <summary>
    /// Initialise Prism and acquire the best backend. Returns null (no throw) if the native library is
    /// absent or no backend is available (e.g. no screen reader running), so the caller can fall back.
    /// </summary>
    public static PrismSpeech TryCreate(string modDir)
    {
        var prismPath = FindNative(modDir, "prism.dll");
        if (prismPath == null)
        {
            Main.Log?.Log("Prism: prism.dll not found in mod folder — skipping Prism backend.");
            return null;
        }

        try
        {
            // Preload the NVDA client (best effort) so Prism's NVDA backend can bind; then prism itself.
            var nvda = FindNative(modDir, "nvdaControllerClient64.dll");
            if (nvda != null) LoadLibrary(nvda);
            if (LoadLibrary(prismPath) == IntPtr.Zero)
            {
                Main.Log?.Log("Prism: LoadLibrary failed for " + prismPath);
                return null;
            }

            var cfg = prism_config_init();
            var ctx = prism_init(ref cfg);
            if (ctx == IntPtr.Zero)
            {
                Main.Log?.Log("Prism: prism_init returned null.");
                return null;
            }

            var backend = prism_registry_acquire_best(ctx);
            if (backend == IntPtr.Zero)
            {
                Main.Log?.Log("Prism: no backend available (is a screen reader running?).");
                prism_shutdown(ctx);
                return null;
            }

            var namePtr = prism_backend_name(backend);
            var name = namePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(namePtr) : "unknown";
            Main.Log?.Log("Prism: backend acquired — " + name);
            return new PrismSpeech(ctx, backend, name);
        }
        catch (Exception e)
        {
            Main.Log?.Log("Prism: native init failed — " + e.Message);
            return null;
        }
    }

    public void Speak(string text, bool interrupt = false)
    {
        if (_backend == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        var err = prism_backend_speak(_backend, Utf8Z(text), interrupt);
        if (err != PRISM_OK) Main.Log?.Log("Prism speak error: " + ErrorString(err));
    }

    public void Stop()
    {
        if (_backend == IntPtr.Zero) return;
        var err = prism_backend_stop(_backend);
        if (err != PRISM_OK && err != PRISM_ERROR_NOT_SPEAKING)
            Main.Log?.Log("Prism stop error: " + ErrorString(err));
    }

    public void Dispose()
    {
        if (_backend != IntPtr.Zero) prism_backend_free(_backend);
        if (_ctx != IntPtr.Zero) prism_shutdown(_ctx);
        _backend = IntPtr.Zero;
        _ctx = IntPtr.Zero;
    }

    // Look for a native dll flat in the mod folder, or under native/win-x64/ (the ONI layout).
    private static string FindNative(string modDir, string fileName)
    {
        if (string.IsNullOrEmpty(modDir)) modDir = ".";
        var flat = Path.Combine(modDir, fileName);
        if (File.Exists(flat)) return flat;
        var nested = Path.Combine(modDir, "native", "win-x64", fileName);
        return File.Exists(nested) ? nested : null;
    }

    private static string ErrorString(int err)
    {
        try
        {
            var p = prism_error_string(err);
            return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : ("code " + err);
        }
        catch { return "code " + err; }
    }

    // Prism expects a NUL-terminated UTF-8 string (const char*).
    private static byte[] Utf8Z(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var z = new byte[b.Length + 1]; // trailing byte stays 0 => NUL terminator
        Buffer.BlockCopy(b, 0, z, 0, b.Length);
        return z;
    }
}
