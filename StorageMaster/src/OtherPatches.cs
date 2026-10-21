using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// Keeps other mods' patches on a game method out of the calls Storage Master handles itself.
    /// Harmony runs every prefix even when ours already skipped the game's code, so a mod that replaces the same method
    /// would do its own thing on top of ours: Quick Stack Store Sort Trash Restock quick stacked into every chest we
    /// took over (#49), and SeneaL UI moved hotbar and locked items after our Quick stack (#18).
    /// Our prefix calls <see cref="Begin"/> when it handles a call and a finalizer calls <see cref="End"/>; meanwhile the
    /// other mods' prefixes and postfixes on that method are skipped. Every other call reaches them as before.
    /// </summary>
    public static class OtherPatches
    {
        private static readonly Dictionary<MethodBase, MethodBase> _targetOf = new Dictionary<MethodBase, MethodBase>();
        private static readonly HashSet<MethodBase> _handling = new HashSet<MethodBase>();

        public static void Begin(MethodBase target) => _handling.Add(target);

        public static void End(MethodBase target) => _handling.Remove(target);

        /// <summary>Puts a skip in front of every other mod's prefix and postfix on the target. Call it once all mods have patched the game.</summary>
        public static void Mute(Harmony harmony, MethodBase target)
        {
            HarmonyLib.Patches patches = Harmony.GetPatchInfo(target);
            if (patches == null) return;
            // A postfix that returns something passes the method's result through; skipping it would lose the result.
            foreach (Patch p in patches.Prefixes.Concat(patches.Postfixes.Where(p => p.PatchMethod.ReturnType == typeof(void))))
            {
                if (p.owner == harmony.Id || _targetOf.ContainsKey(p.PatchMethod)) continue;
                MethodInfo method = p.PatchMethod;
                string skip = method.ReturnType == typeof(bool) ? nameof(SkipBool) : nameof(Skip);
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(OtherPatches), skip));
                    _targetOf[method] = target;
                    Debug.Log($"[Storage Master] {p.owner} also patches {target.DeclaringType?.Name}.{target.Name}; it's skipped where Storage Master handles that itself.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Storage Master] Couldn't keep {p.owner}'s {target.DeclaringType?.Name}.{target.Name} patch out of Storage Master's calls: {e.Message}");
                }
            }
        }

        private static bool Handling(MethodBase patchMethod) => _targetOf.TryGetValue(patchMethod, out MethodBase target) && _handling.Contains(target);

        private static bool Skip(MethodBase __originalMethod) => !Handling(__originalMethod);

        private static bool SkipBool(MethodBase __originalMethod, ref bool __result)
        {
            if (!Handling(__originalMethod)) return true;
            __result = false; // A prefix returning false: the game's code is skipped, as ours does anyway.
            return false;
        }

        [HarmonyPatch]
        private static class Patches
        {
            // By the time a world is loaded, every mod has patched the game.
            [HarmonyPatch(typeof(Game), nameof(Game.Start)), HarmonyPostfix]
            private static void GameStart()
            {
                var harmony = new Harmony(Plugin.Guid);
                Mute(harmony, AccessTools.Method(typeof(Container), nameof(Container.RPC_StackResponse)));
                Mute(harmony, AccessTools.Method(typeof(Inventory), nameof(Inventory.StackAll)));
            }
        }
    }
}
