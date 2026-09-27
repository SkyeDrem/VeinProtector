using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VeinProtector.Core;

namespace VeinProtector.Patches
{
    [HarmonyPatch(typeof(UIVeinCollectorPanel), "_OnUpdate")]
    internal static class UIVeinCollectorPanelPatch
    {
        internal static bool SpeedMultiplierPatched;
        private static readonly MethodInfo EffectiveCountMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "GetEffectiveVeinCount",
            new[] { typeof(MinerComponent), typeof(PlanetFactory) });
        private static readonly FieldInfo FactoryField = AccessTools.Field(typeof(UIVeinCollectorPanel), "factory");

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i + 3 < codes.Count; i++)
            {
                FieldInfo field = codes[i + 1].operand as FieldInfo;
                if (codes[i].opcode == OpCodes.Ldloc_0
                    && codes[i + 1].opcode == OpCodes.Ldfld
                    && field != null && field.Name == "veinCount"
                    && codes[i + 2].opcode == OpCodes.Conv_R4
                    && codes[i + 3].opcode == OpCodes.Mul)
                {
                    codes.RemoveAt(i + 1);
                    codes.InsertRange(i + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, FactoryField),
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
