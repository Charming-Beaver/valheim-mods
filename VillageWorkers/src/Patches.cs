using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillageWorkers
{
    [HarmonyPatch]
    internal static class Patches
    {
        // Villager hover text / name (Character is the Hoverable on the villager object).
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText)), HarmonyPostfix]
        private static void HoverText(Character __instance, ref string __result)
        {
            if (__instance.TryGetComponent(out WorkerAI w)) __result = w.GetHoverText();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName)), HarmonyPostfix]
        private static void HoverName(Character __instance, ref string __result)
        {
            if (__instance.TryGetComponent(out WorkerAI w)) __result = w.GetName();
        }

        // The owner is the authority over a villager's inventory - never reload it from the ZDO, as that
        // would replace the item objects the villager is holding.
        [HarmonyPatch(typeof(Container), nameof(Container.Load)), HarmonyPrefix]
        private static bool ContainerLoad(Container __instance, ref bool __result)
        {
            if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return true;
            if (!__instance.TryGetComponent(out WorkerAI w) || w.AllowBoxLoad) return true;
            __result = false;
            return false;
        }

        // Villagers fully draw their bows.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage)), HarmonyPostfix]
        private static void DrawPercentage(Humanoid __instance, ref float __result)
        {
            if (!__instance.IsPlayer() && __instance.TryGetComponent(out WorkerAI _)) __result = 1f;
        }

        // Our window blocks player input and frees the cursor exactly like the inventory does.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.IsVisible)), HarmonyPostfix]
        private static void InventoryVisible(ref bool __result)
        {
            if (UI.VillageWindow.BlocksInput) __result = true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Interact)), HarmonyPrefix]
        private static bool PlayerInteract(Player __instance, GameObject go, bool hold)
        {
            if (!WorldPicker.Active || __instance != Player.m_localPlayer) return true;
            if (!hold) WorldPicker.OnInteract(go);
            return false;
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.Update)), HarmonyPrefix]
        private static bool MenuUpdate()
        {
            if (WorldPicker.Active && ZInput.GetKeyDown(KeyCode.Escape))
            {
                WorldPicker.Finish(true);
                return false;
            }
            return !WorldPicker.SwallowEscapeThisFrame && !UI.VillageWindow.SwallowEscape;
        }

        private static float _camDistance;

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera)), HarmonyPrefix]
        private static void CameraPre(GameCamera __instance) => _camDistance = __instance.m_distance;

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera)), HarmonyPostfix]
        private static void CameraPost(GameCamera __instance)
        {
            // The mouse wheel sizes the work area while placing it.
            if (WorldPicker.Active && WorldPicker.CurrentMode == WorldPicker.Mode.Area) __instance.m_distance = _camDistance;
        }

        // Show who uses a bed or a chest.
        [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText)), HarmonyPostfix]
        private static void BedHover(Bed __instance, ref string __result)
        {
            ZNetView nv = __instance.m_nview;
            if (nv == null || !nv.IsValid()) return;
            WorkerAI w = WorkerAI.All.FirstOrDefault(x => x.State == WorkerState.Villager && x.Config.Bed != null && x.Config.Bed.Matches(nv));
            if (w != null) __result += $"\n<color=orange>Bed of {w.GetName()}</color>";
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText)), HarmonyPostfix]
        private static void ChestHover(Container __instance, ref string __result)
        {
            ZNetView nv = __instance.m_nview;
            if (nv == null || !nv.IsValid() || __instance.GetComponent<Piece>() == null) return;
            string users = string.Join(", ", WorkerAI.All
                .Where(x => x.State != WorkerState.Wild && x.Config.Chests.Concat(x.Config.Tasks.SelectMany(t => t.Chests)).Any(c => c.Matches(nv)))
                .Select(x => x.GetName()));
            if (users.Length > 0) __result += $"\n<color=orange>Used by {users}</color>";
        }
    }
}
