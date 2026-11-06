using System.Collections;
using UnityEngine;

namespace VillageWorkers.Tests
{
    /// <summary>
    /// Goes from the main menu straight into the game with the test character, creating it on the first run. Depending on
    /// the setup it starts the test world (creating it too), as singleplayer or with "Start server" ticked, or joins the
    /// test server. Does what the menu's buttons do (FejdStartup.OnCharacterStart, OnWorldStart, JoinServer) without the UI.
    /// </summary>
    public static class AutoStart
    {
        // Lowercase like the menu names character files. The same name is used for the world.
        public const string Name = "modtest";

        // Joining fails while the server is still logging in to Steam; it's tried again this often.
        private const int JoinAttempts = 6;

        public static IEnumerator LoadTestWorld()
        {
            yield return WaitForMenu();
            const FileHelpers.FileSource local = FileHelpers.FileSource.Local;
            if (!PlayerProfile.HaveProfile(Name))
            {
                TestPlugin.Log.LogInfo($"Creating test character {Name}");
                var profile = new PlayerProfile(Name, local);
                profile.SetName("ModTest");
                // No Valkyrie ride and intro text.
                profile.m_firstSpawn = false;
                profile.Save();
            }
            // Not FejdStartup.SelectCharacter: that would also make it the character the menu offers next time.
            Game.SetProfile(Name, local);

            if (TestPlugin.Setup == Setup.Server) yield return Join();
            else
            {
                FejdStartup menu = FejdStartup.instance;
                World world = World.GetCreateWorld(Name, local);
                menu.m_world = world;
                ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
                menu.m_startingWorld = true;
                // Hosting is "Start server" without "Community server": friends can join (with the password), strangers can't find it.
                bool host = TestPlugin.Setup == Setup.Host;
                ZNet.SetServer(server: true, openServer: host, publicServer: false, world.m_name, host ? TestPlugin.Opt("password", "modtest") : "", world);
                ZNet.ResetServerHost();
                menu.TransitionToMainScene();
                // A new world generates its locations first, which takes a while.
                yield return TestKit.WaitUntil(PlayerReady, "the player to spawn in the test world", 600f);
            }

            Player player = Player.m_localPlayer;
            player.SetGodMode(true);
            TestKit.LowGraphics();
            // Let the area around the player load.
            yield return new WaitForSeconds(5f);
            yield return TestKit.ClearArea();
        }

        private static IEnumerator WaitForMenu()
        {
            yield return TestKit.WaitUntil(() => FejdStartup.instance != null && ObjectDB.instance != null, "the main menu", 120f);
            // FejdStartup.Start runs on the frame after Awake.
            yield return TestKit.Frames(5);
            if (CinematicsManager.IsStartedPlaying()) CinematicsManager.Stop();
            yield return TestKit.Frames(5);
            TestKit.LowGraphics();
        }

        /// <summary>Joins the dedicated server run-tests.ps1 started, like the menu's "Join IP" does.</summary>
        private static IEnumerator Join()
        {
            string host = TestPlugin.Opt("host", "127.0.0.1");
            int port = int.Parse(TestPlugin.Opt("port", "2456"));
            // Entered by ZNet when the server asks for it, instead of showing the password dialog.
            FejdStartup.ServerPassword = TestPlugin.Opt("password");
            for (int attempt = 1; ; attempt++)
            {
                TestPlugin.Log.LogInfo($"Joining {host}:{port} (attempt {attempt})");
                ZNet.SetServer(server: false, openServer: false, publicServer: false, "", "", null);
                ZNet.SetServerHost(host, port, OnlineBackendType.Steamworks);
                FejdStartup.instance.TransitionToMainScene();
                yield return TestKit.WaitUntil(() => FejdStartup.instance == null, "leaving the main menu", 60f);
                // Either we're in, or the connection failed and the game went back to the main menu.
                yield return TestKit.WaitUntil(() => PlayerReady() || FejdStartup.instance != null, "the player to spawn on the server", 600f);
                if (PlayerReady()) yield break;
                string error = ZNet.GetConnectionStatus().ToString();
                if (attempt >= JoinAttempts) throw new AssertionException($"Could not join the test server at {host}:{port}: {error}");
                TestPlugin.Log.LogInfo($"Joining failed ({error}), trying again");
                yield return WaitForMenu();
                FejdStartup.instance.m_connectionFailedPanel?.SetActive(false);
                yield return new WaitForSecondsRealtime(10f);
            }
        }

        private static bool PlayerReady()
        {
            Player player = Player.m_localPlayer;
            return player != null && !player.IsTeleporting() && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(player.transform.position)
                   && InventoryGui.instance != null;
        }
    }
}
