using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Unictrl;

/// The sender role: this PC's keyboard and mouse are shared with a Mac. Connects out exactly like
/// the receiver role (the Mac still listens, so Windows still needs no inbound firewall rule); only
/// the direction of the input messages flips - here we send `enter`/`move`/`button`/`wheel`/`key`
/// from SenderCapture and receive `screen`/`leave`/`pong`.
internal sealed class Sender : IDisposable
{
    private readonly Settings settings;
    private readonly Action<string> status;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sendGate = new();
    private readonly SenderCapture capture;
    private TcpClient? client;
    private Action<byte[]>? send;
    private DateTime lastDiscovery = DateTime.MinValue;
    private Timer? pingTimer;
    private long pingStamp;
    private DateTime pongAt = DateTime.MinValue;
    private int pingTick;

    /// Raised on the connection thread once the Mac's hello is in.
    public event Action? Connected;

    public bool Online { get; private set; }
    /// The Mac's screen size in mm, from its `screen` message.
    public (float WidthMm, float HeightMm)? MacScreen { get; private set; }
    /// Round-trip time in ms from the last pong.
    public double RttMs { get; private set; }

    public Sender(Settings settings, Action<string> status)
    {
        this.settings = settings;
        this.status = status;
        capture = new SenderCapture(settings, this);
    }

    public void Start()
    {
        capture.Start();
        var thread = new Thread(Run) { IsBackground = true, Name = "Unictrl sender", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// Thread-safe: the capture thread, the ping timer and the UI all send.
    public void Send(byte[] message)
    {
        var target = send;
        if (target is null) return;
        try { target(message); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    /// The `enter` for a crossing, then this PC's Caps Lock state (the receiver follows the sender).
    public void SendEnter(SenderCore.Entry entry)
    {
        Send(Messages.Enter(entry.YMm, entry.Edge == 1, entry.LeftOpen, entry.RightOpen, resume: false));
        Send(Messages.Key(0x07, 0x39, Native.CapsLockOn()));
    }

    private void Run()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                status("Connecting to your Mac…");
                Session();
            }
            catch (Exception ex) when (!lifetime.IsCancellationRequested)
            {
                Log.Write("session_ended", ex.GetType().Name + ": " + ex.Message);
                if (ex is NotPairedException)
                {
                    status("This Mac no longer has this PC paired. Pair again.");
                    lifetime.Token.WaitHandle.WaitOne(15_000);
                }
                else if (ex is CryptographicException)
                {
                    status("Not paired with this Mac. Pair again.");
                }
                else
                {
                    status("Looking for your Mac…");
                    Rediscover();
                }
            }
            catch { }
            finally
            {
                Online = false;
                send = null;
                pingTimer?.Dispose();
                pingTimer = null;
                capture.ForceReturn("disconnected"); // the pointer always comes home
                client?.Dispose();
                client = null;
            }
            lifetime.Token.WaitHandle.WaitOne(1500);
        }
    }

    /// The Mac's address may have changed: ask the LAN, at most every 10 s (as the receiver does).
    private void Rediscover()
    {
        if (DateTime.UtcNow - lastDiscovery < TimeSpan.FromSeconds(10)) return;
        lastDiscovery = DateTime.UtcNow;
        var macs = Discovery.Find(TimeSpan.FromMilliseconds(1200));
        var mac = macs.FirstOrDefault(m => m.Id == settings.MacId)
                  ?? (string.IsNullOrEmpty(settings.MacId) && macs.Count == 1 ? macs[0] : null);
        if (mac is null || (mac.Address == settings.MacAddress && mac.Id == settings.MacId && mac.Port == settings.Port)) return;
        Log.Write("mac_moved", $"id={mac.Id}");
        settings.MacAddress = mac.Address;
        settings.MacId = mac.Id;
        settings.MacName = mac.Name;
        settings.Port = mac.Port;
        settings.Save();
    }

    private void Session()
    {
        if (string.IsNullOrWhiteSpace(settings.MacAddress)) throw new SocketException((int)SocketError.HostNotFound);
        client = new TcpClient { NoDelay = true };
        client.ConnectAsync(settings.MacAddress, settings.Port, lifetime.Token).AsTask().Wait(lifetime.Token);
        var stream = client.GetStream();
        stream.ReadTimeout = 4000; // a dropped link must not block reads forever

        var clientSalt = RandomNumberGenerator.GetBytes(SecureChannel.SaltLength);
        stream.Write([.. SecureChannel.MagicV2, .. clientSalt, .. settings.DeviceIdBytes]);
        var handshake = new byte[SecureChannel.Magic.Length + SecureChannel.SaltLength];
        stream.ReadExactly(handshake, 0, SecureChannel.Magic.Length);
        if (handshake.AsSpan(0, SecureChannel.Magic.Length).SequenceEqual(SecureChannel.NotPaired))
            throw new NotPairedException();
        stream.ReadExactly(handshake, SecureChannel.Magic.Length, SecureChannel.SaltLength);
        if (!handshake.AsSpan(0, SecureChannel.Magic.Length).SequenceEqual(SecureChannel.Magic))
            throw new InvalidDataException("Not a Unictrl peer");
        using var channel = new SecureChannel(settings.PairingKey, clientSalt,
            handshake[SecureChannel.Magic.Length..], isServer: false);

        void Send(byte[] message)
        {
            // Seal inside the lock: the capture thread, the ping timer and the UI all send, and the
            // nonce counter and AesGcm are not thread-safe.
            lock (sendGate) stream.Write(channel.Seal(message));
        }

        Send(Messages.Hello(Environment.MachineName));
        var header = new byte[2];
        var body = new byte[SecureChannel.MaxFrame];
        var greeted = false;
        while (!lifetime.IsCancellationRequested)
        {
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header);
            if (length < 17 || length > SecureChannel.MaxFrame) throw new InvalidDataException("Bad frame");
            stream.ReadExactly(body, 0, length);
            var message = channel.Open(body.AsSpan(0, length));
            if (message.Length == 0) continue;
            var reader = new MessageReader(message);
            if (!greeted)
            {
                if (reader.Type != MessageType.Hello) throw new InvalidDataException("Expected hello");
                greeted = true;
                reader.U8(); // platform
                var name = reader.Utf8Rest();
                Log.Write("connected", $"peer={name}");
                status($"Connected to {name} · sharing this PC's keyboard and mouse");
                Online = true;
                send = Send;
                pongAt = DateTime.UtcNow;
                Connected?.Invoke();
                if (string.IsNullOrEmpty(settings.MacId)) Task.Run(LearnMacId);
                StartPinging();
                continue;
            }
            Dispatch(ref reader);
        }
    }

    /// The sender pings (protocol: sender → receiver); while the pointer is away it does so often,
    /// and a silent link brings the pointer home promptly instead of waiting for the read timeout.
    private void StartPinging()
    {
        pingTick = 0;
        pingTimer = new Timer(_ =>
        {
            if (!Online) return;
            pingTick++;
            if (capture.Remote || pingTick % 5 == 0)
            {
                pingStamp = Stopwatch.GetTimestamp();
                Send(Messages.Ping((ulong)pingStamp));
            }
            if (capture.Remote && DateTime.UtcNow - pongAt > TimeSpan.FromMilliseconds(1500))
            {
                capture.ForceReturn("timeout");
                Log.Write("return_timeout", $"rtt_ms={RttMs:F0}");
            }
            if (!settings.Enabled) capture.ForceReturn("disabled");
        }, null, 100, 100);
    }

    private void Dispatch(ref MessageReader reader)
    {
        switch (reader.Type)
        {
            case MessageType.Screen when reader.Has(8):
                MacScreen = (reader.F32(), reader.F32());
                Log.Write("mac_screen", $"{MacScreen:0}mm");
                break;
            case MessageType.Leave when reader.Has(4):
                var yMm = reader.F32();
                _ = reader.Has(1) ? reader.U8() : 0; // which Mac edge; our settings say where ours is
                capture.ReturnFromMac(yMm, "leave");
                break;
            case MessageType.Pong when reader.Has(8) && reader.U64() == (ulong)pingStamp:
                RttMs = Stopwatch.GetElapsedTime(pingStamp).TotalMilliseconds;
                pongAt = DateTime.UtcNow;
                break;
            default:
                break; // arrangementEdit/rename/hosts/stats: no sender-side handling yet
        }
    }

    /// Settings from before discovery have an address but no device ID; learn it once.
    private void LearnMacId()
    {
        var mac = Discovery.Find(TimeSpan.FromMilliseconds(1200)).FirstOrDefault(m => m.Address == settings.MacAddress);
        if (mac is null) return;
        settings.MacId = mac.Id;
        settings.MacName = mac.Name;
        settings.Save();
        Log.Write("mac_id_learned");
    }

    public void Dispose()
    {
        lifetime.Cancel();
        capture.Dispose();
        pingTimer?.Dispose();
        client?.Dispose();
    }
}
