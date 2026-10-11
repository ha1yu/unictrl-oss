using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Unictrl.Tests;

/// Byte-level samples from tests/contract/binary-messages.json. Both platforms encode the same fields
/// and must produce these exact bytes; with a Windows sender both sides now build messages they never
/// built before, so this pins the little-endian numeric formats.
public class BinaryContractTests
{
    private static Dictionary<string, byte[]> Load()
    {
        var json = JsonSerializer.Deserialize<JsonElement[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "binary-messages.json")))!;
        return json.ToDictionary(
            e => e.GetProperty("name").GetString()!,
            e => Convert.FromHexString(e.GetProperty("hex").GetString()!));
    }

    [Fact]
    public void BuildersMatchTheFixtures()
    {
        var fixtures = Load();
        Assert.Equal(fixtures["enter-left-openright"], Messages.Enter(12.5f, false, false, true, false));
        Assert.Equal(fixtures["enter-right-resume-all-open"], Messages.Enter(-3.25f, true, true, true, true));
        Assert.Equal(fixtures["move"], Messages.Move(1.5f, -2.25f, 0x00ABCDEF));
        Assert.Equal(fixtures["move-raw"], Messages.MoveRaw(3f, -1f, uint.MaxValue));
        Assert.Equal(fixtures["button-back-down"], Messages.Button(3, true));
        Assert.Equal(fixtures["button-left-up"], Messages.Button(0, false));
        Assert.Equal(fixtures["wheel"], Messages.Wheel(1f, -0.5f));
        Assert.Equal(fixtures["key-a-down"], Messages.Key(0x07, 0x0004, true));
        Assert.Equal(fixtures["key-volup-up"], Messages.Key(0x0C, 0x00E9, false));
        Assert.Equal(fixtures["key-caps-state"], Messages.Key(0x07, 0x0039, true));
        Assert.Equal(fixtures["leave-right"], Messages.Leave(100f, true));
        Assert.Equal(fixtures["action-start"], Messages.Action(2));
        Assert.Equal(fixtures["ping"], Messages.Ping(0x0123456789ABCDEF));
    }

    [Fact]
    public void ReaderParsesTheFixtureFields()
    {
        var fixtures = Load();
        var enter = new MessageReader(fixtures["enter-right-resume-all-open"]);
        Assert.Equal(MessageType.Enter, enter.Type);
        Assert.Equal(-3.25f, enter.F32());
        Assert.Equal(1, enter.U8());
        Assert.Equal(0b111, enter.U8());

        var move = new MessageReader(fixtures["move"]);
        Assert.Equal(MessageType.Move, move.Type);
        Assert.Equal(1.5f, move.F32());
        Assert.Equal(-2.25f, move.F32());
        Assert.Equal(0x00ABCDEFu, move.U32());

        var key = new MessageReader(fixtures["key-volup-up"]);
        Assert.Equal(MessageType.Key, key.Type);
        Assert.Equal(0x0C, key.U8());
        Assert.Equal(0x00E9, key.U16());
        Assert.Equal(0, key.U8());
    }
}

/// The channel was only ever tested from the Swift side; the reversal makes the C# side seal the
/// sender-role messages, so its framing, key separation and replay rejection need their own tests.
public class SecureChannelTests
{
    private static (SecureChannel Client, SecureChannel Server) Pair(string key = "cGFzc6==")
    {
        var clientSalt = RandomNumberGenerator.GetBytes(SecureChannel.SaltLength);
        var serverSalt = RandomNumberGenerator.GetBytes(SecureChannel.SaltLength);
        return (new(key, clientSalt, serverSalt, false), new(key, clientSalt, serverSalt, true));
    }

    private static byte[] Body(byte[] frame) => frame[2..];

    [Fact]
    public void RoundTripsInBothDirections()
    {
        var (client, server) = Pair();
        var toServer = client.Seal(Messages.Ping(42));
        Assert.Equal(42ul, new MessageReader(server.Open(Body(toServer))).U64());

        var toClient = server.Seal(Messages.Leave(1.5f, true));
        var leave = new MessageReader(client.Open(Body(toClient)));
        Assert.Equal(MessageType.Leave, leave.Type);
        Assert.Equal(1.5f, leave.F32());
        Assert.Equal(1, leave.U8());
    }

    [Fact]
    public void FramesAreLengthPrefixed()
    {
        var (client, _) = Pair();
        var plaintext = Messages.Key(0x07, 0x0004, true);
        var frame = client.Seal(plaintext);
        Assert.Equal(2 + plaintext.Length + 16, frame.Length);
        Assert.Equal(plaintext.Length + 16, (frame[0] << 8) | frame[1]); // u16 big-endian
    }

    [Fact]
    public void WrongKeyFailsToOpen()
    {
        var clientSalt = RandomNumberGenerator.GetBytes(SecureChannel.SaltLength);
        var serverSalt = RandomNumberGenerator.GetBytes(SecureChannel.SaltLength);
        var client = new SecureChannel("one", clientSalt, serverSalt, false);
        var server = new SecureChannel("two", clientSalt, serverSalt, true);
        Assert.ThrowsAny<CryptographicException>(() => server.Open(Body(client.Seal(Messages.Ping(1)))));
    }

    [Fact]
    public void ReplayedFrameFails()
    {
        var (client, server) = Pair();
        var body = Body(client.Seal(Messages.Ping(1)));
        server.Open(body);
        Assert.ThrowsAny<CryptographicException>(() => server.Open(body));
    }

    [Fact]
    public void ReorderedFrameFails()
    {
        var (client, server) = Pair();
        _ = Body(client.Seal(Messages.Ping(1))); // dropped on the wire
        var third = Body(client.Seal(Messages.Ping(3)));
        // Arriving out of order (a frame was dropped) fails authentication; the connection is closed.
        Assert.ThrowsAny<CryptographicException>(() => server.Open(third));
    }

    [Fact]
    public void TamperedCiphertextFails()
    {
        var (client, server) = Pair();
        var body = Body(client.Seal(Messages.Move(1f, 2f, 3)));
        body[^20] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => server.Open(body));
    }
}
