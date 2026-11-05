using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;

namespace VillageWorkers
{
    /// <summary>A setting the server decides for everyone: while connected, the server's value replaces the local one.</summary>
    public sealed class Synced<T>
    {
        public readonly ConfigEntry<T> Entry;
        private bool _fromServer;
        private T _server;

        public Synced(ConfigEntry<T> entry)
        {
            Entry = entry;
            ServerSync.Register(entry, s => { _server = (T)TomlTypeConverter.ConvertToValue(s, typeof(T)); _fromServer = true; },
                () => _fromServer = false);
        }

        public T Value => _fromServer ? _server : Entry.Value;
    }

    /// <summary>
    /// Multiplayer: villagers are run by whichever player (or the server) is closest, so everyone needs the mod and the
    /// same settings. Joining players send their mod version; the server turns away anyone without a compatible one and
    /// sends the rest its settings. Works the same for dedicated servers and for players hosting from the game.
    /// </summary>
    [HarmonyPatch]
    public static class ServerSync
    {
        private const string RpcVersion = "VillageWorkers_Version";
        private const string RpcSettings = "VillageWorkers_Settings";
        private const string RpcRejected = "VillageWorkers_Rejected";

        private class Setting
        {
            public ConfigEntryBase Entry;
            public Action<string> Apply;
            public Action Clear;
        }

        private static readonly List<Setting> Settings = new List<Setting>();
        private static readonly Dictionary<ZRpc, string> PeerVersions = new Dictionary<ZRpc, string>();
        private static string _rejectedReason;
        private static FileSystemWatcher _watcher;
        private static volatile bool _fileChanged;
        private static bool _localChanged;

        /// <summary>True while this game uses settings received from a server.</summary>
        public static bool UsingServerSettings { get; private set; }

        public static event Action Changed;

        internal static void Register(ConfigEntryBase entry, Action<string> apply, Action clear)
        {
            Settings.Add(new Setting { Entry = entry, Apply = apply, Clear = clear });
            entry.ConfigFile.SettingChanged += (_, e) =>
            {
                if (e.ChangedSetting == entry) OnLocalChange();
            };
        }

        /// <summary>
        /// Versions with the same major and minor number can play together: patch releases only fix bugs.
        /// </summary>
        public static bool Compatible(string theirs, string mine)
        {
            if (!System.Version.TryParse(theirs ?? "", out System.Version a) || !System.Version.TryParse(mine ?? "", out System.Version b)) return false;
            return a.Major == b.Major && a.Minor == b.Minor;
        }

        private static string MajorMinor(string version) =>
            System.Version.TryParse(version, out System.Version v) ? $"{v.Major}.{v.Minor}.x" : version;

        // ------------------------------------------------------------------ connecting

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection)), HarmonyPostfix]
        private static void OnNewConnection(ZNet __instance, ZNetPeer peer)
        {
            if (__instance.IsServer())
            {
                peer.m_rpc.Register<string>(RpcVersion, RPC_Version);
                return;
            }
            ClearServerSettings();
            _rejectedReason = null;
            peer.m_rpc.Register<ZPackage>(RpcSettings, RPC_Settings);
            peer.m_rpc.Register<string>(RpcRejected, (rpc, reason) => _rejectedReason = reason);
            peer.m_rpc.Invoke(RpcVersion, Plugin.Version);
        }

        // Server: a joining player's version arrives before their peer info (which needs a round trip more).
        private static void RPC_Version(ZRpc rpc, string version) => PeerVersions[rpc] = version;

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo)), HarmonyPrefix]
        private static bool CheckVersion(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer()) return true;
            PeerVersions.TryGetValue(rpc, out string version);
            PeerVersions.Remove(rpc);
            if (Compatible(version, Plugin.Version)) return true;

            string who = rpc.GetSocket()?.GetEndPointString() ?? "?";
            if (version == null)
            {
                Plugin.Log.LogWarning($"Turned away {who}: they don't have {Plugin.Name}, which this server requires.");
            }
            else
            {
                Plugin.Log.LogWarning($"Turned away {who}: they have {Plugin.Name} {version}, this server has {Plugin.Version}.");
                rpc.Invoke(RpcRejected, $"This server uses {Plugin.Name} {MajorMinor(Plugin.Version)}, you have {version}.\nInstall the same version to join.");
            }
            rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
            return false;
        }

        // The game accepted them (password, ban list and so on): send them the settings.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo)), HarmonyPostfix]
        private static void SendSettings(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer()) return;
            ZNetPeer peer = __instance.GetPeer(rpc);
            if (peer != null && peer.IsReady()) rpc.Invoke(RpcSettings, Pack());
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect)), HarmonyPrefix]
        private static void Disconnect(ZNetPeer peer)
        {
            if (peer?.m_rpc != null) PeerVersions.Remove(peer.m_rpc);
        }

        // A player with the wrong version learns which one the server needs instead of just "Incompatible version".
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError)), HarmonyPostfix]
        private static void ShowConnectError(FejdStartup __instance)
        {
            if (string.IsNullOrEmpty(_rejectedReason) || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorVersion) return;
            __instance.m_connectionFailedError.text = _rejectedReason;
            _rejectedReason = null;
        }

        // Back in the main menu, or about to start a world of our own: use the local settings again.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy)), HarmonyPostfix]
        private static void LeftGame() => ClearServerSettings();

        // ------------------------------------------------------------------ settings

        private static ZPackage Pack()
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(Settings.Count);
            foreach (Setting s in Settings)
            {
                pkg.Write(s.Entry.Definition.Section + "." + s.Entry.Definition.Key);
                pkg.Write(s.Entry.GetSerializedValue());
            }
            return pkg;
        }

        private static void RPC_Settings(ZRpc rpc, ZPackage pkg)
        {
            Dictionary<string, Setting> byKey = new Dictionary<string, Setting>();
            foreach (Setting s in Settings) byKey[s.Entry.Definition.Section + "." + s.Entry.Definition.Key] = s;

            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string key = pkg.ReadString();
                string value = pkg.ReadString();
                if (!byKey.TryGetValue(key, out Setting s)) continue;
                try { s.Apply(value); }
                catch (Exception e) { Plugin.Log.LogWarning($"Ignored the server's {key} = {value}: {e.Message}"); }
            }
            UsingServerSettings = true;
            Plugin.Log.LogInfo("Using the server's settings.");
            Changed?.Invoke();
        }

        private static void ClearServerSettings()
        {
            if (!UsingServerSettings) return;
            foreach (Setting s in Settings) s.Clear();
            UsingServerSettings = false;
            Plugin.Log.LogInfo("Using your own settings again.");
            Changed?.Invoke();
        }

        // Handled once per frame, as reloading the file changes many settings at once.
        private static void OnLocalChange() => _localChanged = true;

        private static void ApplyLocalChanges()
        {
            if (!_localChanged) return;
            _localChanged = false;
            if (UsingServerSettings) return;
            Changed?.Invoke();
            // The server (or host) passes its new settings on to everyone playing.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            int sent = 0;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (!peer.IsReady()) continue;
                peer.m_rpc.Invoke(RpcSettings, Pack());
                sent++;
            }
            if (sent > 0) Plugin.Log.LogInfo($"Sent the changed settings to {sent} player(s).");
        }

        // ------------------------------------------------------------------ editing the config file while running

        /// <summary>Reloads the config file when it's saved, so a server owner doesn't have to restart.</summary>
        public static void WatchConfigFile(ConfigFile config)
        {
            try
            {
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(config.ConfigFilePath), Path.GetFileName(config.ConfigFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                _watcher.Changed += (_, __) => _fileChanged = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Can't watch the config file for changes: " + e.Message);
            }
        }

        /// <summary>Called every frame on the main thread, which the reload's change events need.</summary>
        public static void Update(ConfigFile config)
        {
            if (_fileChanged)
            {
                _fileChanged = false;
                try
                {
                    config.Reload();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Couldn't reload the config file: " + e.Message);
                }
            }
            ApplyLocalChanges();
        }

        public static void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
