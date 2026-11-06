using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VillageWorkers.Tests
{
    /// <summary>
    /// The test plugin on the dedicated server (run-tests.ps1 -Server). It writes VillageWorkers.Tests.server.txt in the
    /// server's BepInEx folder: LOADED once the mod runs, READY once players can join, and a JOINED line per player.
    /// It carries out the commands the tests in the game write to VillageWorkers.Tests.server.cmd next to it (both run on
    /// this machine): "set Section.Key value" changes a setting as if the config file had been edited, and "quit" stops
    /// the server, saving the world.
    /// </summary>
    public static class ServerSide
    {
        public const string StatusFile = "VillageWorkers.Tests.server.txt";
        public const string CommandFile = "VillageWorkers.Tests.server.cmd";

        /// <summary>
        /// The server's Behaviour.StatsDaysKept (the default is 30). Only the server has it, so a player using it got the
        /// server's settings.
        /// </summary>
        public const int ServerStatsDays = 77;

        private static string _status;

        public static IEnumerator Run()
        {
            _status = Path.Combine(Paths.BepInExRootPath, StatusFile);
            string commands = Path.Combine(Paths.BepInExRootPath, CommandFile);
            File.WriteAllText(_status, "");
            if (File.Exists(commands)) File.Delete(commands);
            Write($"LOADED {Plugin.Name} {Plugin.Version} headless={Plugin.Headless}");
            Plugin.StatsDaysKept.Entry.Value = ServerStatsDays;

            while (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.m_hostSocket == null) yield return null;
            Write("READY");

            var joined = new HashSet<long>();
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (ZNet.instance != null)
                {
                    foreach (ZNetPeer peer in ZNet.instance.GetPeers().Where(p => p.IsReady() && joined.Add(p.m_uid)))
                        Write($"JOINED {peer.m_playerName}");
                }
                if (!File.Exists(commands)) continue;
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(commands);
                    File.Delete(commands);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (string line in lines.Where(l => l.Trim().Length > 0)) Execute(line.Trim());
            }
        }

        private static void Write(string line)
        {
            TestPlugin.Log.LogInfo("[server] " + line);
            File.AppendAllText(_status, line + Environment.NewLine);
        }

        private static void Execute(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, 3);
            try
            {
                switch (parts[0])
                {
                    case "set":
                        ConfigEntryBase entry = Plugin.Instance.Config.Keys
                            .Where(k => k.Section + "." + k.Key == parts[1])
                            .Select(k => Plugin.Instance.Config[k]).FirstOrDefault();
                        if (entry == null) throw new ArgumentException("no setting " + parts[1]);
                        entry.BoxedValue = TomlTypeConverter.ConvertToValue(parts[2], entry.SettingType);
                        Write($"SET {parts[1]} {parts[2]}");
                        break;
                    case "quit":
                        Write("QUIT");
                        Application.Quit();
                        break;
                    default:
                        throw new ArgumentException("unknown command");
                }
            }
            catch (Exception e)
            {
                Write($"ERROR {command}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ used by the tests in the game

        private static string ServerBepInEx => Path.Combine(TestPlugin.Opt("server-dir") ?? "", "BepInEx");

        /// <summary>Asks the test server to do something (see the class summary).</summary>
        public static void Send(string command)
        {
            if (TestPlugin.Setup != Setup.Server) return;
            File.AppendAllText(Path.Combine(ServerBepInEx, CommandFile), command + Environment.NewLine);
        }

        /// <summary>What the test server has written so far.</summary>
        public static string[] Status()
        {
            string path = Path.Combine(ServerBepInEx, StatusFile);
            if (!File.Exists(path)) return new string[0];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
