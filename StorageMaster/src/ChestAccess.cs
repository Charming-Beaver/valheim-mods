using System;
using System.Collections.Generic;
using System.Linq;
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

        // The chest whose answer we're handling right now; other mods' hooks on the answer are skipped meanwhile.
        private static Container _answering;
        private static bool _othersMuted;

        public static void Request(Container c, Action<bool> onAnswer)
        {
            MuteOthers();
            _waiting[c] = onAnswer;
            c.StackAll();
        }

        /// <summary>
        /// Some mods replace the answer to "Place stacks" with their own quick stack (Quick Stack Store Sort Trash Restock
        /// does). Harmony runs every prefix even when ours already skipped the game's code, so their quick stack would run
        /// into every chest we take over. Done on the first request, when all mods have patched the game.
        /// </summary>
        private static void MuteOthers()
        {
            if (_othersMuted) return;
            _othersMuted = true;
            MethodInfo answer = AccessTools.Method(typeof(Container), nameof(Container.RPC_StackResponse));
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(answer);
            if (patches == null) return;
            var harmony = new Harmony(Plugin.Guid);
            foreach (Patch p in patches.Prefixes.Concat(patches.Postfixes))
            {
                if (p.owner == Plugin.Guid) continue;
                MethodInfo method = p.PatchMethod;
                string skip = method.ReturnType == typeof(bool) ? nameof(SkipBoolWhileAnswering) : nameof(SkipWhileAnswering);
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ChestAccess), skip));
                    Debug.Log($"[Storage Master] {p.owner} also handles \"Place stacks\" answers; it's skipped for the chests Storage Master opens.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Storage Master] Couldn't keep {p.owner} from handling the chests Storage Master opens: {e.Message}");
                }
            }
        }

        private static bool SkipWhileAnswering() => _answering == null;

        private static bool SkipBoolWhileAnswering(ref bool __result)
        {
            if (_answering == null) return true;
            __result = false; // A prefix returning false: the game's code is skipped, as ours does anyway.
            return false;
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
            // First, so _answering is set before other mods' prefixes run. A finalizer clears it after all postfixes,
            // even when something throws.
            [HarmonyPatch(typeof(Container), nameof(Container.RPC_StackResponse)), HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static bool StackResponse(Container __instance, bool granted)
            {
                if (!_waiting.TryGetValue(__instance, out Action<bool> onAnswer)) return true;
                _waiting.Remove(__instance);
                _answering = __instance;
                onAnswer(granted);
                return false;
            }

            [HarmonyPatch(typeof(Container), nameof(Container.RPC_StackResponse)), HarmonyFinalizer]
            private static void StackResponseDone() => _answering = null;

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
