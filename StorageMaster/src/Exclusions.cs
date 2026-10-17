using System;
using System.Collections.Generic;

namespace StorageMaster
{
    /// <summary>
    /// Chests you told the mod to leave alone (for example your friend's): quick stack, gather, tidy up and
    /// sort all skip them. Saved with your character, so it only affects you.
    /// </summary>
    public static class Exclusions
    {
        private const string Key = "InventoryTools.Excluded";

        private static readonly HashSet<string> _excluded = new HashSet<string>();
        private static Player _loadedFor;

        public static bool IsExcluded(Container c)
        {
            string id = Id(c);
            if (id == null) return false;
            EnsureLoaded();
            return _excluded.Contains(id);
        }

        public static void Set(Container c, bool excluded)
        {
            Player player = Player.m_localPlayer;
            string id = Id(c);
            if (player == null || id == null) return;
            EnsureLoaded();
            if (excluded) _excluded.Add(id);
            else _excluded.Remove(id);
            player.m_customData[Key] = string.Join(";", _excluded);
        }

        private static string Id(Container c)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return null;
            ZDOID uid = c.m_nview.GetZDO().m_uid;
            return $"{uid.UserID}:{uid.ID}";
        }

        private static void EnsureLoaded()
        {
            Player player = Player.m_localPlayer;
            if (player == _loadedFor) return;
            _loadedFor = player;
            _excluded.Clear();
            if (player == null || !player.m_customData.TryGetValue(Key, out string data)) return;
            foreach (string id in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) _excluded.Add(id);
        }
    }
}
