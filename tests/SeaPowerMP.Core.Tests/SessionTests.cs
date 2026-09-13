using SeaPowerMP.Core.Serialization;
using SeaPowerMP.Core.Session;
using SeaPowerMP.Core.Transport;
using Xunit;

namespace SeaPowerMP.Core.Tests;

public class SessionTests
{
    private readonly LoopbackNetwork _network = new();
    private double _now;

    private static SessionIdentity Identity(string name, params string[] mods) => new()
    {
        PlayerName = name,
        GameVersion = "0.8.2",
        ModVersion = "0.1.0",
        Mods = mods,
    };

    private HostSession StartHost(HostSettings? settings = null, params string[] mods) =>
        new(_network.CreateHost(), Identity("Host", mods), settings ?? new HostSettings(), () => _now);

    private ClientSession Join(string name, params string[] mods) =>
        new(_network.CreateClient(), Identity(name, mods), () => _now);

    /// <summary>Delivers everything in flight. Each round trip needs a couple of ticks on both sides.</summary>
    private static void Pump(HostSession host, params ClientSession[] clients)
    {
        for (int i = 0; i < 6; i++)
        {
            host.Tick();
            foreach (var client in clients)
                client.Tick();
        }
    }

    [Fact]
    public void ThreeClientsJoinAndEveryoneSeesTheSameRoster()
    {
        var host = StartHost();
        var a = Join("Anna");
        var b = Join("Ben");
        var c = Join("Cem");
        Pump(host, a, b, c);

        Assert.Equal(new[] { "Host", "Anna", "Ben", "Cem" }, host.Players.Select(p => p.Name));
        foreach (var client in new[] { a, b, c })
        {
            Assert.Equal(ClientState.Connected, client.State);
            Assert.Equal(host.Players.Select(p => (p.Slot, p.Name)), client.Players.Select(p => (p.Slot, p.Name)));
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, new[] { a.LocalSlot, b.LocalSlot, c.LocalSlot });
        Assert.Equal("Ben", b.LocalPlayer?.Name);
    }

    [Fact]
    public void FullSessionRefusesFurtherPlayers()
    {
        var host = StartHost(new HostSettings { MaxPlayers = 2 });
        var first = Join("First");
        var second = Join("Second");
        Pump(host, first, second);

        Assert.Equal(ClientState.Connected, first.State);
        Assert.Equal(ClientState.Disconnected, second.State);
        Assert.Contains("full", second.DisconnectReason);
        Assert.Equal(2, host.Players.Count);
    }

    [Fact]
    public void LeavingFreesTheSlotForTheNextPlayer()
    {
        var host = StartHost();
        var a = Join("Anna");
        var b = Join("Ben");
        Pump(host, a, b);

        a.Leave();
        Pump(host, a, b);
        Assert.Equal(new[] { "Host", "Ben" }, b.Players.Select(p => p.Name));

        var c = Join("Cem");
        Pump(host, b, c);
        Assert.Equal(1, c.LocalSlot);
    }

    [Fact]
    public void GameVersionMismatchIsRefusedWithBothVersions()
    {
        var host = StartHost();
        var identity = Identity("Old");
        identity.GameVersion = "0.7.9";
        var client = new ClientSession(_network.CreateClient(), identity, () => _now);
        Pump(host, client);

        Assert.Equal(ClientState.Disconnected, client.State);
        Assert.Contains("0.8.2", client.DisconnectReason);
        Assert.Contains("0.7.9", client.DisconnectReason);
        Assert.Single(host.Players);
    }

    [Fact]
    public void ModMismatchNamesTheDifferingMods()
    {
        var host = StartHost(null, "SeaPowerMP", "BetterRadars");
        var client = Join("Anna", "SeaPowerMP", "ExtraShips");
        Pump(host, client);

        Assert.Equal(ClientState.Disconnected, client.State);
        Assert.Contains("BetterRadars", client.DisconnectReason);
        Assert.Contains("ExtraShips", client.DisconnectReason);
    }

    [Fact]
    public void FullRefusalReasonSurvivesTransportTruncation()
    {
        _network.MaxDisconnectReasonLength = 20;
        var host = StartHost(null, "SeaPowerMP", "BetterRadars", "GermanNavyPack", "ColdWarAircraft");
        var client = Join("Anna", "SeaPowerMP", "ExtraShips");
        Pump(host, client);

        Assert.Contains("ColdWarAircraft", client.DisconnectReason);
        Assert.Contains("ExtraShips", client.DisconnectReason);
    }

    [Fact]
    public void KickedPlayerSeesTheReason()
    {
        var host = StartHost();
        var a = Join("Anna");
        var b = Join("Ben");
        Pump(host, a, b);

        host.Kick(1, "AFK");
        Pump(host, a, b);

        Assert.Equal(ClientState.Disconnected, a.State);
        Assert.Equal("Kicked by host: AFK", a.DisconnectReason);
        Assert.Equal(new[] { "Host", "Ben" }, b.Players.Select(p => p.Name));
    }

    [Fact]
    public void ModOrderAndCaseDoNotMatter()
    {
        var host = StartHost(null, "SeaPowerMP", "BetterRadars");
        var client = Join("Anna", "betterradars", "SeaPowerMP");
        Pump(host, client);

        Assert.Equal(ClientState.Connected, client.State);
    }

    [Fact]
    public void ProtocolMismatchIsRefusedBeforeReadingTheRest()
    {
        var host = StartHost();
        var raw = _network.CreateClient();
        string? reason = null;
        raw.PeerConnected += peer =>
        {
            var w = new PacketWriter();
            w.WriteByte((byte)MessageId.Hello);
            w.WriteUInt16(Protocol.Version + 1);
            w.WriteByte(0xFF); // garbage in a layout this host cannot know
            raw.Send(peer, w.Buffer, w.Length, Delivery.Reliable);
        };
        raw.PeerDisconnected += (_, r) => reason = r;

        for (int i = 0; i < 6; i++)
        {
            host.Tick();
            raw.Poll();
        }

        Assert.Contains("protocol mismatch", reason);
        Assert.Single(host.Players);
    }

    [Fact]
    public void SilentPeerIsDroppedAfterHandshakeTimeout()
    {
        var host = StartHost(new HostSettings { HandshakeTimeoutSec = 10 });
        var raw = _network.CreateClient();
        string? reason = null;
        raw.PeerDisconnected += (_, r) => reason = r;

        host.Tick();
        raw.Poll();
        _now = 11;
        host.Tick();
        raw.Poll();

        Assert.Contains("timed out", reason);
    }

    [Fact]
    public void MalformedHelloIsRefused()
    {
        var host = StartHost();
        var raw = _network.CreateClient();
        string? reason = null;
        raw.PeerConnected += peer => raw.Send(peer, new byte[] { (byte)MessageId.Hello, (byte)Protocol.Version }, 2, Delivery.Reliable);
        raw.PeerDisconnected += (_, r) => reason = r;

        for (int i = 0; i < 6; i++)
        {
            host.Tick();
            raw.Poll();
        }

        Assert.Contains("Protocol error", reason);
    }

    [Fact]
    public void ClientCanSwitchTeamUnlessTeamsAreLocked()
    {
        var host = StartHost();
        var a = Join("Anna");
        Pump(host, a);

        a.RequestTeam(Team.Red);
        Pump(host, a);
        Assert.Equal(Team.Red, host.Players[1].Team);
        Assert.Equal(Team.Red, a.LocalPlayer?.Team);

        host.TeamsLocked = true;
        a.RequestTeam(Team.Blue);
        Pump(host, a);
        Assert.Equal(Team.Red, a.LocalPlayer?.Team);
    }

    [Fact]
    public void GameMessagesAreRoutedWithTheSendersSlot()
    {
        var host = StartHost();
        var a = Join("Anna");
        var b = Join("Ben");
        Pump(host, a, b);

        (byte slot, MessageId id, uint value)? received = null;
        host.MessageReceived += (player, id, reader) => received = (player.Slot, id, reader.ReadVarUInt());
        string? clientGot = null;
        a.MessageReceived += (_, reader) => clientGot = reader.ReadString();

        var message = b.BeginMessage(MessageId.FirstGameMessage + 1);
        message.WriteVarUInt(4711);
        b.Send(message, Delivery.Reliable);
        var reply = host.BeginMessage(MessageId.FirstGameMessage + 2);
        reply.WriteString("ahoy");
        host.SendTo(1, reply, Delivery.Reliable);
        Pump(host, a, b);

        Assert.Equal(((byte)2, MessageId.FirstGameMessage + 1, 4711u), received);
        Assert.Equal("ahoy", clientGot);
    }

    [Fact]
    public void ClientTimesOutWhenNoHostAnswers()
    {
        var host = StartHost();
        host.Stop();
        var client = Join("Anna");
        _now = 30;
        client.Tick();
        client.Tick();

        Assert.Equal(ClientState.Disconnected, client.State);
        Assert.NotNull(client.DisconnectReason);
    }

    [Fact]
    public void HostStoppingDisconnectsEveryoneWithTheReason()
    {
        var host = StartHost();
        var a = Join("Anna");
        var b = Join("Ben");
        Pump(host, a, b);

        host.Stop("Mission over");
        Pump(host, a, b);

        Assert.All(new[] { a, b }, c =>
        {
            Assert.Equal(ClientState.Disconnected, c.State);
            Assert.Equal("Mission over", c.DisconnectReason);
            Assert.Empty(c.Players);
        });
    }
}
