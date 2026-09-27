using System.Reflection;
using HarmonyLib;

namespace VeinProtector.Patches
{
    [HarmonyPatch]
    [HarmonyPatch]
    internal static class MinerAllProtectedPrefixPatch
    {
        [HarmonyTargetMethod]
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(MinerComponent), "InternalUpdate", new[]
            {
                typeof(PlanetFactory), typeof(VeinData[]), typeof(float), typeof(float), typeof(float), typeof(int[])
            });
        }

        [HarmonyPrefix]
        private static bool Prefix(ref MinerComponent __instance, VeinData[] __1, float __3, ref uint __result)
        {
            if (!Core.VeinProtectionLogic.AreAllBoundVeinsProtected(__instance, __1, __3))
                return true;

            // Preserve vanilla scheduling state while all bound veins are protected.
            __result = 0;
            return false;
        }
    }
}
