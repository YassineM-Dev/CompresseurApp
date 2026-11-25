using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CompresseurPatch;

/// <summary>
/// Lightweight, zero-dependency Discord Rich Presence (IPC) client.
/// Uses synchronous sequential Write -> Read on a dedicated background thread
/// so it works reliably on any Windows PC without external libraries.
/// </summary>
internal sealed class DiscordRpc : IDisposable
{
    private const string ClientId = "1505149137501290526";

    private readonly CancellationTokenSource _cts = new();
    private readonly long _sessionStartUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly int _pid = Environment.ProcessId;
    private readonly Action<string>? _log;

    private NamedPipeClientStream? _pipe;
    private bool _ready;
    private bool _loggedConnect;
    private string _details = "Compresseur/Patch";
    private string _state = "Idle • Ready to compress or patch";
    private volatile bool _dirty = true;
    private DateTime _lastSentUtc = DateTime.MinValue;

    public DiscordRpc(Action<string>? log = null)
    {
        _log = log;
        var thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "DiscordRpcThread",
        };
        thread.Start();
    }

    public void UpdateStatus(string details, string state, bool forceImmediate = false)
    {
        _details = details;
        _state = state;
        _dirty = true;

        if (forceImmediate)
        {
            _lastSentUtc = DateTime.MinValue;
        }
    }

    private void RunLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_pipe is null || !_pipe.IsConnected || !_ready)
                {
                    ConnectAndHandshake(ct);
                }

                if (_ready && _dirty && (DateTime.UtcNow - _lastSentUtc).TotalSeconds >= 1.5)
                {
                    _dirty = false;
                    _lastSentUtc = DateTime.UtcNow;
                    SendActivity(_details, _state);
                }

                Thread.Sleep(250);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                ResetPipe();
                if (ct.WaitHandle.WaitOne(3000))
                {
                    break;
                }
            }
        }
    }

    private void ConnectAndHandshake(CancellationToken ct)
    {
        ResetPipe();

        for (int i = 0; i < 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pipeName = $"discord-ipc-{i}";
            var candidate = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                candidate.Connect(800);
                _pipe = candidate;
                break;
            }
            catch
            {
                candidate.Dispose();
            }
        }

        if (_pipe is null || !_pipe.IsConnected)
        {
            throw new IOException("Discord IPC pipe not available.");
        }

        var handshakeJson = JsonSerializer.Serialize(new
        {
            v = 1,
            client_id = ClientId,
        });

        WriteFrame(0, handshakeJson);

        var (op, payload) = ReadFrame();
        if (op == 1 && payload.Contains("\"READY\"", StringComparison.OrdinalIgnoreCase))
        {
            _ready = true;
            _dirty = true;
            _lastSentUtc = DateTime.MinValue;
        }
        else
        {
            throw new IOException("Discord IPC handshake rejected: " + payload);
        }
    }

    private void SendActivity(string details, string state)
    {
        var packet = new
        {
            cmd = "SET_ACTIVITY",
            nonce = Guid.NewGuid().ToString("N"),
            args = new
            {
                pid = _pid,
                activity = new
                {
                    name = "Compresseur/Patch",
                    details,
                    state,
                    timestamps = new
                    {
                        start = _sessionStartUnix,
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(packet);
        WriteFrame(1, json);

        var (op, reply) = ReadFrame();
        if (op == 3)
        {
            WriteFrame(4, reply);
            (_, reply) = ReadFrame();
        }

        if (reply.Contains("\"ERROR\"", StringComparison.OrdinalIgnoreCase))
        {
            _log?.Invoke("[!] Discord RPC warning: " + reply);
            return;
        }

        if (!_loggedConnect)
        {
            _loggedConnect = true;
            _log?.Invoke("[OK] Discord Rich Presence active.");
        }
    }

    private void WriteFrame(int op, string json)
    {
        if (_pipe is null || !_pipe.IsConnected)
        {
            throw new IOException("Pipe disconnected.");
        }

        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);

        _pipe.Write(frame, 0, frame.Length);
    }

    private (int Op, string Json) ReadFrame()
    {
        if (_pipe is null || !_pipe.IsConnected)
        {
            throw new IOException("Pipe disconnected.");
        }

        var header = new byte[8];
        ReadExact(_pipe, header);

        int op = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);
        if (length <= 0 || length > 1024 * 1024)
        {
            throw new IOException("Invalid Discord IPC frame length.");
        }

        var buffer = new byte[length];
        ReadExact(_pipe, buffer);
        return (op, Encoding.UTF8.GetString(buffer));
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
    }

    private void ResetPipe()
    {
        _ready = false;
        if (_pipe is not null)
        {
            try
            {
                _pipe.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
            _pipe = null;
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Ignore.
        }
        ResetPipe();
        _cts.Dispose();
    }
}
