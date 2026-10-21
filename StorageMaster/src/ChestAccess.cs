using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// Finding usable chests and safely taking them over in multiplayer.
    /// Taking over reuses the game's own "Place stacks" request: the chest's current owner
    /// (you, your friend or the server) checks it isn't open elsewhere, hands ownership to you
    /// and answers. We intercept that answer instead of letting the game stack items.
    /// </summary>
    public static class ChestAccess
    {
        private const string LabelKey = "InventoryTools.Label";

        private static readonly Dictionary<Container, Action<bool>> _waiting = new Dictionary<Container, Action<bool>>();

        public static void Request(Container c, Action<bool> onAnswer)
        {
            _waiting[c] = onAnswer;
            c.StackAll();
        }

        public static void Forget(Container c) => _waiting.Remove(c);

        /// <summary>Chests within range you may open, nearest first.</summary>
        public static List<Container> FindNearby(float range)
        {
            var result = new List<Container>();
            Player player = Player.m_localPlayer;
            if (player == null) return result;
            long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
            foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (Usable(c, player, playerID, range)) result.Add(c);
            }
            Vector3 p = player.transform.position;
            result.Sort((a, b) => Vector3.Distance(a.transform.position, p).CompareTo(Vector3.Distance(b.transform.position, p)));
            return result;
        }

        private static bool Usable(Container c, Player player, long playerID, float range)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return false;
            if (Vector3.Distance(c.transform.position, player.transform.position) > range) return false;
            if (c.GetComponent<TombStone>() != null) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, flash: false)) return false;
            return c.CheckAccess(playerID);
        }

        public static string GetLabel(Container c)
        {
            return c != null && c.m_nview != null && c.m_nview.IsValid() ? c.m_nview.GetZDO().GetString(LabelKey, "") : "";
        }

        public static void SetLabel(Container c, string label)
        {
            if (c == null || c.m_nview == null || !c.m_nview.IsValid() || !c.m_nview.IsOwner()) return;
            c.m_nview.GetZDO().Set(LabelKey, (label ?? "").Trim());
        }

        [HarmonyPatch]
        private static class Patches
        {
            // First, so other mods' patches know to skip our answers before theirs run (see OtherPatches). The finalizer
            // ends that after all postfixes, even when something throws.
            [HarmonyPatch(typeof(Container), nameof(Container.RPC_StackResponse)), HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static bool StackResponse(Container __instance, bool granted, MethodBase __originalMethod)
            {
                if (!_waiting.TryGetValue(__instance, out Action<bool> onAnswer)) return true;
                _waiting.Remove(__instance);
                OtherPatches.Begin(__originalMethod);
                onAnswer(granted);
                return false;
            }

            [HarmonyPatch(typeof(Container), nameof(Container.RPC_StackResponse)), HarmonyFinalizer]
            private static void StackResponseDone(MethodBase __originalMethod) => OtherPatches.End(__originalMethod);

            // Named chests show their name when you look at them.
            [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText)), HarmonyPostfix]
            private static void HoverText(Container __instance, ref string __result)
            {
                string label = GetLabel(__instance);
                if (Exclusions.IsExcluded(__instance))
                {
                    label = (string.IsNullOrEmpty(label) ? "" : label + "  ") + "<size=80%><color=#a9a49c>(skipped by Storage Master)</color></size>";
                }
                if (!string.IsNullOrEmpty(label)) __result = $"<color=#ffa13c>{label}</color>\n{__result}";
            }
        }
    }
}
