#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RTAccess.Dev;

/// <summary>
/// Minimal loopback HTTP/1.1 server on raw <see cref="TcpListener"/>s. We speak just enough of the
/// protocol for curl (request line, Content-Length body, one response, connection close) and
/// deliberately avoid <c>System.Net.HttpListener</c> so there is no http.sys URL-ACL / admin
/// requirement. Loopback-only; a dev tool, not hardened. Ported from WrathAccess. DEBUG-only.
///
/// Listens on BOTH loopbacks — 127.0.0.1 and ::1 — as two sockets. <c>localhost</c> resolves to ::1 first on
/// Windows, and the IPv4 loopback path on the dev box is lossy (a VPN/AV filter driver resets roughly one
/// connection in three on teardown; ::1 measured clean), so clients should address <c>localhost:8772</c>.
/// Two sockets rather than one dual-mode <c>::</c> bind: <c>::</c> would expose the port on every interface
/// (and Windows defaults IPV6_V6ONLY on), and <c>::1</c> alone never answers 127.0.0.1 — v4-mapped loopback is
/// <c>::ffff:127.0.0.1</c>, not <c>::1</c>. IPv6 is best effort; a box without it still gets the v4 server.
/// </summary>
internal sealed class DevHttpServer
{
    // method, route+query, body -> response body
    public delegate string RequestHandler(string method, string path, string body);

    private readonly int _port;
    private readonly RequestHandler _handler;
    private readonly List<TcpListener> _listeners = new List<TcpListener>();
    private readonly List<Thread> _threads = new List<Thread>();
    private readonly object _handleLock = new object();
    private volatile bool _running;

    /// <summary>The loopback addresses actually bound, for the startup log line (e.g. "127.0.0.1 + [::1]").</summary>
    public string BoundAddresses { get; private set; } = "";

    public DevHttpServer(int port, RequestHandler handler)
    {
        _port = port;
        _handler = handler;
    }

    public void Start()
    {
        // IPv4 loopback is mandatory: a failed bind throws, as before, and the caller logs it. IPv6 loopback is
        // best effort — a machine without an IPv6 stack, or one where ::1 is taken, still gets the v4 server.
        var v4 = new TcpListener(IPAddress.Loopback, _port);
        v4.Start();
        _listeners.Add(v4);
        var bound = new List<string> { "127.0.0.1" };
        if (Socket.OSSupportsIPv6)
        {
            try
            {
                var v6 = new TcpListener(IPAddress.IPv6Loopback, _port);
                v6.Start();
                _listeners.Add(v6);
                bound.Add("[::1]");
            }
            catch (SocketException e) { Main.Log?.Warning("dev http: IPv6 loopback bind skipped: " + e.Message); }
        }
        BoundAddresses = string.Join(" + ", bound);

        _running = true;
        foreach (var listener in _listeners)
        {
            var own = listener; // per-thread capture
            var t = new Thread(() => Loop(own)) { IsBackground = true, Name = "RTADevHttp " + own.LocalEndpoint };
            _threads.Add(t);
            t.Start();
        }
    }

    /// <summary>Stop the listeners and join the accept threads, so a UMM hot-reload frees the port instead of
    /// leaking the sockets (the re-load's Start would otherwise throw SocketException). Idempotent. Clearing
    /// <c>_running</c> before <c>Stop()</c> makes the SocketException that unblocks AcceptTcpClient expected —
    /// the Loop swallows it silently.</summary>
    public void Stop()
    {
        _running = false;
        foreach (var l in _listeners) { try { l.Stop(); } catch { } }   // unblocks AcceptTcpClient; the Loop sees !_running and exits quietly
        foreach (var t in _threads) { try { t.Join(1000); } catch { } }  // background threads — a short join is enough
        _listeners.Clear();
        _threads.Clear();
    }

    private void Loop(TcpListener listener)
    {
        while (_running)
        {
            TcpClient client = null;
            try
            {
                client = listener.AcceptTcpClient();
                // One request at a time across both families: the handlers were written for the old single accept
                // thread (the /eval main-thread hop, the /speech ring buffer), so keep that serialization.
                lock (_handleLock) Handle(client);
            }
            catch (Exception e)
            {
                if (_running) Main.Log?.Warning("dev http: " + e.Message);
            }
            finally
            {
                if (client != null) { try { client.Close(); } catch { } }
            }
        }
    }

    private void Handle(TcpClient client)
    {
        client.ReceiveTimeout = 15000;
        NetworkStream stream = client.GetStream();
        var data = new List<byte>();
        var buf = new byte[8192];

        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return;
            for (int i = 0; i < n; i++) data.Add(buf[i]);
            headerEnd = IndexOfHeaderEnd(data);
            if (data.Count > 1 << 20) { Respond(client, stream, 400, "header too large\n"); return; }
        }

        string header = Encoding.ASCII.GetString(data.ToArray(), 0, headerEnd);
        string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ');
        string method = requestLine.Length > 0 ? requestLine[0] : "";
        string path = requestLine.Length > 1 ? requestLine[1] : "/";

        int contentLength = 0;
        foreach (string line in lines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);

        int bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            for (int i = 0; i < n; i++) data.Add(buf[i]);
        }
        int have = Math.Min(contentLength, data.Count - bodyStart);
        string body = have > 0 ? Encoding.UTF8.GetString(data.ToArray(), bodyStart, have) : "";

        string response;
        try { response = _handler(method, path, body) ?? ""; }
        catch (Exception e) { Respond(client, stream, 500, "handler error: " + e + "\n"); return; }
        Respond(client, stream, 200, response);
    }

    private static int IndexOfHeaderEnd(List<byte> d)
    {
        for (int i = 0; i + 3 < d.Count; i++)
            if (d[i] == 13 && d[i + 1] == 10 && d[i + 2] == 13 && d[i + 3] == 10) return i;
        return -1;
    }

    /// <summary>Write the response, then half-close and drain until the client closes (or 2 s pass) before the
    /// caller's Close(). A plain Close right after the write races the peer's read on this host: a loopback filter
    /// driver turns the teardown into a RST often enough that curl lost about one reply in three over 127.0.0.1
    /// (measured; the drain brought a standalone listener from ~35% loss to ~3%, and ::1 is clean regardless).
    /// Letting the client finish reading and close first leaves nothing for the reset to discard. A client that
    /// holds the connection open instead pays the 2 s receive timeout — acceptable for a dev tool.</summary>
    private static void Respond(TcpClient client, NetworkStream stream, int status, string body)
    {
        Write(stream, status, body);
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
            client.ReceiveTimeout = 2000;
            var sink = new byte[1024];
            while (stream.Read(sink, 0, sink.Length) > 0) { }
        }
        catch (Exception)
        {
            // The peer closing under us (or the 2 s drain timeout) is the normal end of this exchange, not an error;
            // the response bytes were already handed to the stack above.
        }
    }

    private static void Write(NetworkStream stream, int status, string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string reason = status == 200 ? "OK" : "ERROR";
        string head = "HTTP/1.1 " + status + " " + reason + "\r\n"
            + "Content-Type: text/plain; charset=utf-8\r\n"
            + "Content-Length: " + bodyBytes.Length + "\r\n"
            + "Connection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }
}
#endif
