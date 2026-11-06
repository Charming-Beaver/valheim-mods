using System.Collections;
using System.Linq;
using HarmonyLib;

namespace VillageWorkers.Tests
{
    /// <summary>
    /// Running on servers. A world hosted from the game (-Hosting) has to be open for other players, and the mod's server
    /// side has to be in place. On a dedicated server (-Server) this game is the player who joins: the server runs the
    /// mod too, accepts us and sends its settings, also when they change. The farming tests run in both setups as well.
    /// </summary>
    public static class MultiplayerTests
    {
        [Test]
        public static void VersionsWithTheSameMajorAndMinorPlayTogether()
        {
            Assert.True(ServerSync.Compatible("1.2.0", "1.2.5"), "1.2.0 and 1.2.5");
            Assert.True(!ServerSync.Compatible("1.2.0", "1.3.0"), "1.2.0 and 1.3.0");
            Assert.True(!ServerSync.Compatible("2.2.0", "1.2.0"), "2.2.0 and 1.2.0");
            Assert.True(!ServerSync.Compatible(null, Plugin.Version), "no version (the mod is missing)");
        }

        /// <summary>"Start server" opens the world for other players, with the mod checking who joins.</summary>
        [Test, RequiresSetup(Setup.Host)]
        public static void HostingOpensTheWorldWithTheModsChecks()
        {
            ZNet net = ZNet.instance;
            Assert.True(net.IsServer() && !net.IsDedicated(), "This game is the server");
            Assert.True(ZNet.m_openServer && net.m_hostSocket != null, "Other players can connect");
            Assert.True(!ZNet.m_publicServer, "The test world isn't listed for strangers");
            var checks = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZNet), nameof(ZNet.RPC_PeerInfo)));
            Assert.True(checks != null && checks.Prefixes.Concat(checks.Postfixes).Any(p => p.owner == Plugin.Guid),
                "The mod checks joining players' versions and sends them its settings");
            Assert.True(!ServerSync.UsingServerSettings && Plugin.StatsDaysKept.Value == Plugin.StatsDaysKept.Entry.Value,
                "The host uses its own settings");
        }

        /// <summary>The dedicated server runs the mod without a screen and let us in, sending its settings.</summary>
        [Test, RequiresSetup(Setup.Server)]
        public static void JoinedTheDedicatedServer()
        {
            string[] server = ServerSide.Status();
            Assert.True(server.Contains($"LOADED {Plugin.Name} {Plugin.Version} headless=True"),
                $"The server runs the mod as a dedicated server; it wrote: {string.Join(" | ", server)}");
            Assert.True(server.Any(l => l.StartsWith("JOINED")), "The server let us in");
            Assert.True(!ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "This game is connected to the server");
            Assert.True(ServerSync.UsingServerSettings, "This game uses the server's settings");
            Assert.Equal(ServerSide.ServerStatsDays, Plugin.StatsDaysKept.Value, "StatsDaysKept (the server's)");
            Assert.Equal((int)Plugin.StatsDaysKept.Entry.DefaultValue, Plugin.StatsDaysKept.Entry.Value, "StatsDaysKept in our own config file");
        }

        /// <summary>A setting changed on the server (as if its config file was edited) reaches the players online.</summary>
        [Test, RequiresSetup(Setup.Server)]
        public static IEnumerator ServerSettingChangesReachPlayers()
        {
            ServerSide.Send("set Behaviour.StatsDaysKept 55");
            yield return TestKit.WaitUntil(() => Plugin.StatsDaysKept.Value == 55, "the changed setting to arrive", 15f);
            ServerSide.Send($"set Behaviour.StatsDaysKept {ServerSide.ServerStatsDays}");
            yield return TestKit.WaitUntil(() => Plugin.StatsDaysKept.Value == ServerSide.ServerStatsDays, "the setting to change back", 15f);
        }
    }
}
