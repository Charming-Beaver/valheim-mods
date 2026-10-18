using System.Collections;
using UnityEngine;

namespace StorageMaster.Tests
{
    /// <summary>
    /// Goes from the main menu straight into the test world with the test character, creating both on the first run.
    /// Does what the menu's Start buttons do (FejdStartup.OnCharacterStart and OnWorldStart) without the UI.
    /// </summary>
    public static class AutoStart
    {
        // Lowercase like the menu names character files. The same name is used for the world.
        public const string Name = "modtest";

        public static IEnumerator LoadTestWorld()
        {
            yield return TestKit.WaitUntil(() => FejdStartup.instance != null && ObjectDB.instance != null, "the main menu", 120f);
            // FejdStartup.Start runs on the frame after Awake.
            yield return TestKit.Frames(5);
            if (CinematicsManager.IsStartedPlaying()) CinematicsManager.Stop();
            yield return TestKit.Frames(5);

            FejdStartup menu = FejdStartup.instance;
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

            World world = World.GetCreateWorld(Name, local);
            menu.m_world = world;
            ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
            menu.m_startingWorld = true;
            ZNet.SetServer(server: true, openServer: false, publicServer: false, world.m_name, "", world);
            ZNet.ResetServerHost();
            menu.TransitionToMainScene();

            // A new world generates its locations first, which takes a while.
            yield return TestKit.WaitUntil(PlayerReady, "the player to spawn in the test world", 600f);
            Player player = Player.m_localPlayer;
            player.SetGodMode(true);
            // Let the area around the player load.
            yield return new WaitForSeconds(5f);
            yield return TestKit.ClearArea(30f);
        }

        private static bool PlayerReady()
        {
            Player player = Player.m_localPlayer;
            return player != null && !player.IsTeleporting() && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(player.transform.position)
                   && InventoryGui.instance != null;
        }
    }
}
