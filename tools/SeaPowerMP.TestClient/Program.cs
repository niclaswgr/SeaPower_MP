using System.Diagnostics;
using SeaPowerMP;
using SeaPowerMP.Core.Session;
using SeaPowerMP.Net;

// Usage: SeaPowerMP.TestClient [--address 127.0.0.1] [--port 7777] [--count 1] [--name Bot]
//        [--seconds 30] [--game-version 0.8.2-23444+s358] [--mods "Anchor Chain;Quick Start"] [--switch-team]
//        SeaPowerMP.TestClient --host [--port 7777] [--seconds 30]   (stand-in host, for testing without the game)

var options = Options.Parse(args);
var clock = Stopwatch.StartNew();
double Now() => clock.Elapsed.TotalSeconds;
void Print(string message) => Console.WriteLine($"[{Now(),5:F1}s] {message}");

if (options.Host)
{
    var identity = new SessionIdentity { PlayerName = "TestHost", GameVersion = options.GameVersion, ModVersion = options.ModVersion, Mods = options.Mods };
    var host = new HostSession(LiteNetTransport.Host(options.Port), identity, new HostSettings(), Now);
    host.Log += message => Print(message);
    host.RosterChanged += () => Print("Roster: " + string.Join(", ", host.Players.Select(p => $"{p.Slot}:{p.Name}/{p.Team}")));
    Print($"Hosting on UDP {options.Port} for {options.Seconds}s");
    while (Now() < options.Seconds)
    {
        host.Tick();
        Thread.Sleep(15);
    }
    int players = host.Players.Count;
    host.Stop("Test host finished.");
    Print($"Done: {players} players at the end.");
    return 0;
}

var bots = new List<Bot>();
for (int i = 0; i < options.Count; i++)
{
    string name = options.Count == 1 ? options.Name : $"{options.Name} {i + 1}";
    var identity = new SessionIdentity
    {
        PlayerName = name,
        GameVersion = options.GameVersion,
        ModVersion = options.ModVersion,
        Mods = options.Mods,
    };
    var session = new ClientSession(LiteNetTransport.Connect(options.Address, options.Port), identity, Now);
    var bot = new Bot(name, session);
    session.StateChanged += state =>
    {
        if (state == ClientState.Connected)
            bot.EverConnected = true;
        Print($"{name}: {state}" + (state == ClientState.Disconnected ? $" - {session.DisconnectReason}" : $" (slot {session.LocalSlot})"));
    };
    if (i == 0)
    {
        session.RosterChanged += () =>
        {
            if (session.Players.Count > 0)
                Print("Roster: " + string.Join(", ", session.Players.Select(p => $"{p.Slot}:{p.Name}/{p.Team}")));
        };
    }
    bots.Add(bot);
}

Print($"{options.Count} bot(s) connecting to {options.Address}:{options.Port} (game {options.GameVersion}, mod {options.ModVersion}, {options.Mods.Count} mods)");
bool switched = false;
while (Now() < options.Seconds && bots.Any(b => b.Session.State != ClientState.Disconnected))
{
    foreach (var bot in bots)
        bot.Session.Tick();

    if (options.SwitchTeam && !switched && bots.All(b => b.Session.State == ClientState.Connected))
    {
        switched = true;
        foreach (var bot in bots.Where((_, index) => index % 2 == 0))
            bot.Session.RequestTeam(Team.Red);
        Print("Requested team Red for every second bot.");
    }
    Thread.Sleep(15);
}

foreach (var bot in bots.Where(b => b.Session.State != ClientState.Disconnected))
    bot.Session.Leave();

int connected = bots.Count(b => b.EverConnected);
Print($"Done: {connected}/{bots.Count} bots reached the lobby.");
return connected == bots.Count ? 0 : 1;

internal sealed class Bot(string name, ClientSession session)
{
    public string Name { get; } = name;
    public ClientSession Session { get; } = session;
    public bool EverConnected { get; set; }
}

internal sealed class Options
{
    public string Address { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 7777;
    public int Count { get; private set; } = 1;
    public string Name { get; private set; } = "Bot";
    public double Seconds { get; private set; } = 30;
    public string GameVersion { get; private set; } = "0.8.2-23444+s358";
    public string ModVersion { get; private set; } = PluginInfo.Version;
    public List<string> Mods { get; private set; } = new();
    public bool SwitchTeam { get; private set; }
    public bool Host { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i]);
            switch (args[i])
            {
                case "--address": options.Address = Next(); break;
                case "--port": options.Port = int.Parse(Next()); break;
                case "--count": options.Count = Math.Clamp(int.Parse(Next()), 1, 16); break;
                case "--name": options.Name = Next(); break;
                case "--seconds": options.Seconds = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--game-version": options.GameVersion = Next(); break;
                case "--mod-version": options.ModVersion = Next(); break;
                case "--mods": options.Mods = Next().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                case "--switch-team": options.SwitchTeam = true; break;
                case "--host": options.Host = true; break;
                default: throw new ArgumentException("Unknown option " + args[i]);
            }
        }
        return options;
    }
}
