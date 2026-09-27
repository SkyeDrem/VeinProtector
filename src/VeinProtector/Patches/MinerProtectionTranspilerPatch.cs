using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VeinProtector.Core;

namespace VeinProtector.Patches
{
    [HarmonyPatch]
    internal static class MinerProtectionTranspilerPatch
    {
        private static readonly MethodInfo ProductionTickCountMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "GetProductionTickCount",
            new[] { typeof(MinerComponent), typeof(VeinData[]), typeof(float) });
        private static readonly MethodInfo ProtectedVeinMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "IsProtectedVein");
        private static readonly MethodInfo AdvanceMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "AdvanceCurrentVein");
        private static readonly MethodInfo MaxAllowedCostMethod = AccessTools.Method(
            typeof(VeinProtectionLogic), "GetMaxAllowedCost");

        internal static bool TimeMultiplierPatched;
        internal static bool ProtectedVeinSkipPatched;
        internal static bool CostBoundaryPatched;

        [HarmonyTargetMethod]
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(MinerComponent), "InternalUpdate", new[]
            {
                typeof(PlanetFactory), typeof(VeinData[]), typeof(float), typeof(float), typeof(float), typeof(int[])
            });
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Use effective vein count for time accumulation. Only an all-empty set temporarily
            // advances at its physical count so vanilla can remove zero/invalid bindings.
            for (int i = 1; i + 2 < codes.Count; i++)
            {
                FieldInfo field = codes[i].operand as FieldInfo;
                if (codes[i].opcode == OpCodes.Ldfld
                    && field != null
                    && field.Name == "veinCount"
                    && codes[i + 1].opcode == OpCodes.Conv_R4
                    && codes[i + 2].opcode == OpCodes.Mul
                    && codes[i - 1].opcode == OpCodes.Ldarg_0)
                {
                    codes.RemoveAt(i);
                    codes.RemoveAt(i - 1);
                    codes.InsertRange(i - 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldobj, typeof(MinerComponent)),
                        new CodeInstruction(OpCodes.Ldarg_2),          // VeinData[] pool
                        new CodeInstruction(OpCodes.Ldarg_S, (byte)4), // miningRate (IL argument 4)
                        new CodeInstruction(OpCodes.Call, ProductionTickCountMethod)
                    });
                    TimeMultiplierPatched = true;
                    break;
                }
            }

            // Limit the original capped-batch branch to amount - 1. The following original code
            // still derives outputCount and the fractional remainder from miningRate, times,
            // and costFrac; only the resource budget for that final batch changes.
            for (int i = 4; i + 1 < codes.Count; i++)
            {
                FieldInfo amountField = codes[i].operand as FieldInfo;
                LocalBuilder local = codes[i + 1].operand as LocalBuilder;
                if (codes[i].opcode == OpCodes.Ldfld
                    && amountField != null && amountField.Name == "amount"
                    && codes[i + 1].opcode == OpCodes.Stloc_S
                    && local != null && local.LocalIndex == 8
                    && codes[i - 3].opcode == OpCodes.Ldarg_2
                    && codes[i - 2].opcode == OpCodes.Ldloc_3
                    && codes[i - 1].opcode == OpCodes.Ldelema)
                {
                    codes.InsertRange(i + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldloc_S, local),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldobj, typeof(MinerComponent)),
                        new CodeInstruction(OpCodes.Ldarg_S, (byte)4), // miningRate
                        new CodeInstruction(OpCodes.Call, MaxAllowedCostMethod),
                        new CodeInstruction(OpCodes.Stloc_S, local)
                    });
                    CostBoundaryPatched = true;
                    break;
                }
            }

            // At each vein selection, skip amount==1 without changing costFrac, time, output,
            // productRegister or exhaustion state. amount<=0 deliberately remains vanilla cleanup.
            int selection = FindVeinSelection(codes);
            if (selection >= 0)
            {
                Label loopLabel = generator.DefineLabel();
                codes[selection].labels.Add(loopLabel);

                Label continueLabel = generator.DefineLabel();
                codes[selection + 6].labels.Add(continueLabel);

                codes.InsertRange(selection + 6, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldobj, typeof(MinerComponent)),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Ldarg_S, (byte)4), // miningRate
                    new CodeInstruction(OpCodes.Ldloc_3),          // selected vein id
                    new CodeInstruction(OpCodes.Call, ProtectedVeinMethod),
                    new CodeInstruction(OpCodes.Brfalse, continueLabel),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AdvanceMethod),
                    new CodeInstruction(OpCodes.Br, loopLabel)
                });
                ProtectedVeinSkipPatched = true;
            }

            return codes;
        }

        private static int FindVeinSelection(List<CodeInstruction> codes)
        {
            for (int i = 0; i + 5 < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldarg_0
                    && codes[i + 1].opcode == OpCodes.Ldfld
                    && codes[i + 1].operand is FieldInfo
                    && ((FieldInfo)codes[i + 1].operand).Name == "veins"
                    && codes[i + 2].opcode == OpCodes.Ldarg_0
                    && codes[i + 3].opcode == OpCodes.Ldfld
                    && codes[i + 3].operand is FieldInfo
                    && ((FieldInfo)codes[i + 3].operand).Name == "currentVeinIndex"
                    && codes[i + 4].opcode == OpCodes.Ldelem_I4
                    && codes[i + 5].opcode == OpCodes.Stloc_3)
                    return i;
            }

            return -1;
        }
    }
}
