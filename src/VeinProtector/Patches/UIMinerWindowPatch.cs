using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VeinProtector.Core;

namespace VeinProtector.Patches
{
    [HarmonyPatch(typeof(UIMinerWindow), "_OnUpdate")]
    internal static class UIMinerWindowPatch
    {
        internal static bool SpeedMultiplierPatched;
        private static readonly MethodInfo EffectiveCountMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "GetEffectiveVeinCount",
            new[] { typeof(MinerComponent), typeof(VeinData[]), typeof(PlanetFactory) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 1; i + 2 < codes.Count; i++)
            {
                FieldInfo field = codes[i].operand as FieldInfo;
                if (codes[i].opcode == OpCodes.Ldfld
                    && field != null
                    && field.Name == "veinCount"
                    && codes[i + 1].opcode == OpCodes.Conv_R4
                    && codes[i + 2].opcode == OpCodes.Mul
                    && codes[i - 1].opcode == OpCodes.Ldloc_0)
                {
                    // The native UI speed expression is preserved; only its vein multiplier changes.
                    codes.RemoveAt(i);
                    codes.RemoveAt(i - 1);
                    codes.InsertRange(i - 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Ldloc_2),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(UIMinerWindow), "factory")),
                        new CodeInstruction(OpCodes.Call, EffectiveCountMethod)
                    });
                    SpeedMultiplierPatched = true;
                    break;
                }
            }

            return codes;
        }
    }
}
