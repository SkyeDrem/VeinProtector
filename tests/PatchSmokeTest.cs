using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class PatchSmokeTest
{
    private static int Main(string[] args)
    {
        string pluginPath = Path.GetFullPath(args[0]);
        string gameManaged = Path.GetFullPath(args[1]);
        string profileCore = Path.GetFullPath(args[2]);
        string pluginDir = Path.GetDirectoryName(pluginPath);

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
        {
            string name = new AssemblyName(eventArgs.Name).Name + ".dll";
            string[] roots = { pluginDir, profileCore, gameManaged };
            for (int i = 0; i < roots.Length; i++)
            {
                string candidate = Path.Combine(roots[i], name);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            return null;
        };

        try
        {
            Assembly plugin = Assembly.LoadFrom(pluginPath);
            bool logic = RunLogicChecks(plugin, gameManaged);
            Console.WriteLine("LogicChecksPassed=" + logic);
            if (args.Length > 3 && args[3] == "logic")
                return logic ? 0 : 2;

            bool transpilers = RunTranspilerChecks(plugin, profileCore, gameManaged);
            Console.WriteLine("TranspilerChecksPassed=" + transpilers);
            return logic && transpilers ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static bool RunTranspilerChecks(Assembly plugin, string profileCore, string gameManaged)
    {
        Assembly harmony = Assembly.LoadFrom(Path.Combine(profileCore, "0Harmony.dll"));
        Type patchProcessor = harmony.GetType("HarmonyLib.PatchProcessor", true);
        MethodInfo createGenerator = patchProcessor.GetMethod("CreateILGenerator", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(MethodBase) }, null);
        MethodInfo readInstructions = patchProcessor.GetMethod("GetOriginalInstructions", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(MethodBase), typeof(System.Reflection.Emit.ILGenerator) }, null);

        Assembly game = Assembly.LoadFrom(Path.Combine(gameManaged, "Assembly-CSharp.dll"));
        Type minerType = game.GetType("MinerComponent", true);
        MethodInfo internalUpdate = minerType.GetMethod("InternalUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { game.GetType("PlanetFactory", true), game.GetType("VeinData", true).MakeArrayType(),
                typeof(float), typeof(float), typeof(float), typeof(int[]) }, null);
        if (internalUpdate == null)
            return false;

        object generator = createGenerator.Invoke(null, new object[] { internalUpdate });
        object original = readInstructions.Invoke(null, new[] { (object)internalUpdate, generator });
        Type transpilerType = plugin.GetType("VeinProtector.Patches.MinerProtectionTranspilerPatch", true);
        MethodInfo transpiler = transpilerType.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        IEnumerable instructions = (IEnumerable)transpiler.Invoke(null, new[] { original, generator });
        var codes = new ArrayList();
        foreach (object instruction in instructions)
            codes.Add(instruction);

        bool time = ReadFlag(transpilerType, "TimeMultiplierPatched");
        bool skip = ReadFlag(transpilerType, "ProtectedVeinSkipPatched");
        bool boundary = ReadFlag(transpilerType, "CostBoundaryPatched");
        Console.WriteLine("TimeMultiplierPatternMatched=" + time);
        Console.WriteLine("ProtectedVeinSkipPatternMatched=" + skip);
        Console.WriteLine("CostBoundaryPatternMatched=" + boundary);

        MethodInfo productionMethod = plugin.GetType("VeinProtector.Core.VeinProtectionLogic", true)
            .GetMethod("GetProductionTickCount", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo costMethod = plugin.GetType("VeinProtector.Core.VeinProtectionLogic", true)
            .GetMethod("GetMaxAllowedCost", BindingFlags.Static | BindingFlags.NonPublic);
        bool productionStack = CheckCallStack(codes, productionMethod, new[] { "ldarg.0", "ldobj", "ldarg.2", "ldarg.s" }, "conv.r4");
        bool costStack = CheckCallStack(codes, costMethod, new[] { "ldloc.s", "ldarg.0", "ldobj", "ldarg.s" }, "stloc.s");

        Type uiType = game.GetType("UIMinerWindow", true);
        MethodInfo uiMethod = uiType.GetMethod("_OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        object uiGenerator = createGenerator.Invoke(null, new object[] { uiMethod });
        object uiOriginal = readInstructions.Invoke(null, new[] { (object)uiMethod, uiGenerator });
        Type uiPatch = plugin.GetType("VeinProtector.Patches.UIMinerWindowPatch", true);
        MethodInfo uiTranspiler = uiPatch.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        uiTranspiler.Invoke(null, new[] { uiOriginal });
        bool uiPattern = ReadFlag(uiPatch, "SpeedMultiplierPatched");
        Console.WriteLine("MinerUiPatternMatched=" + uiPattern);

        Type collectorType = game.GetType("UIVeinCollectorPanel", true);
        MethodInfo collectorMethod = collectorType.GetMethod("_OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        bool collectorPattern = RunSingleTranspiler(plugin, harmony, game, collectorMethod,
            "VeinProtector.Patches.UIVeinCollectorPanelPatch", "SpeedMultiplierPatched",
            new[] { "ldloc.0", "ldarg.0", "ldfld" }, "conv.r4");
        Console.WriteLine("AdvancedMinerUiPatternMatched=" + collectorPattern);

        Type optionWindowType = game.GetType("UIOptionWindow", true);
        Type optionsPatch = plugin.GetType("VeinProtector.Patches.OptionsProtectionTogglePatch", true);
        FieldInfo gameContentField = optionWindowType.GetField("gameScrollContentRect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        bool settingsPatchTarget = gameContentField != null
            && optionWindowType.GetMethod("_OnOpen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null) != null
            && optionsPatch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic) != null
            && optionsPatch.GetField("RowName", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue().ToString()
                == "VeinProtector-ProtectionRow"
            && optionsPatch.GetField("ToggleName", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue().ToString()
                == "VeinProtector-ProtectionToggle"
            && optionsPatch.GetMethod("CreateProtectionRow", BindingFlags.Static | BindingFlags.NonPublic) != null
            && optionsPatch.GetMethod("PlaceNewRow", BindingFlags.Static | BindingFlags.NonPublic) != null
            && optionsPatch.GetMethod("GetDirectSettingRows", BindingFlags.Static | BindingFlags.NonPublic) != null
            && optionsPatch.GetMethod("EnsureContentHeight", BindingFlags.Static | BindingFlags.NonPublic) != null
            && (int)optionsPatch.GetField("GameTabIndex", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue() == 2
            && (float)optionsPatch.GetField("BottomMargin", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue() == 22f
            && optionsPatch.GetMethod("ResolveMiscContent", BindingFlags.Static | BindingFlags.NonPublic) == null;
        Console.WriteLine("SettingsGameRowPlacementAndHeightCheckPassed=" + settingsPatchTarget);

        Type tipType = game.GetType("UIReferenceSpeedTip", true);
        MethodInfo tipMethod = tipType.GetMethod("SetTip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Type detailsPatch = plugin.GetType("VeinProtector.Patches.ReferenceSpeedDetailExtensionPatch", true);
        Type detailsState = plugin.GetType("VeinProtector.Patches.ReferenceSpeedTipExtensionState", true);
        Type obsoleteMinerDiagnostics = plugin.GetType("VeinProtector.Patches.MinerExecutionDiagnosticsPatch", false);
        bool detailsPattern = tipMethod != null
            && detailsPatch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic) != null
            && detailsState.GetMethod("ApplyReferenceDetailLayoutNextFrame", BindingFlags.Instance | BindingFlags.NonPublic) != null
            && detailsState.GetMethod("CaptureVanillaBaseline", BindingFlags.Instance | BindingFlags.NonPublic) != null
            && detailsState.GetMethod("RestoreBaseLayout", BindingFlags.Instance | BindingFlags.NonPublic) != null
            && detailsState.GetMethod("ApplyExtensionLayout", BindingFlags.Instance | BindingFlags.NonPublic) != null
            && (float)detailsState.GetField("AddedHeight", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue() == 44f
            && (float)detailsState.GetField("GapAbove", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue() == 2f
            && (float)detailsState.GetField("GapBelow", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue() == 3f
            && plugin.GetType("VeinProtector.Patches.ReferenceSpeedTipUpdatePatch", false) == null
            && plugin.GetType("VeinProtector.Patches.UIReferenceSpeedTipPatch", false) == null
            && plugin.GetType("VeinProtector.Patches.ProductionExtraInfoCalculatorPatch", false) == null
            && obsoleteMinerDiagnostics == null
            && plugin.GetType("VeinProtector.Patches.MinerAllProtectedPrefixPatch", true) != null;
        Console.WriteLine("ReferenceTooltipCoroutinePatchCheckPassed=" + detailsPattern);
        bool eligibility = RunDisplayEligibilityChecks(plugin, game);
        Console.WriteLine("ReferenceTooltipEligibilityChecksPassed=" + eligibility);
        return time && skip && boundary && productionStack && costStack && uiPattern
            && collectorPattern && settingsPatchTarget && detailsPattern && eligibility;
    }

    private static bool RunDisplayEligibilityChecks(Assembly plugin, Assembly game)
    {
        Type logic = plugin.GetType("VeinProtector.Core.VeinProtectionLogic", true);
        MethodInfo proto = logic.GetMethod("IsSupportedProtectedVeinPrototype", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo display = logic.GetMethod("ShouldShowReferenceExtension", BindingFlags.Static | BindingFlags.NonPublic);
        Type veinEnum = game.GetType("EVeinType", true);
        int none = Convert.ToInt32(Enum.Parse(veinEnum, "None"));
        int iron = Convert.ToInt32(Enum.Parse(veinEnum, "Iron"));
        int oil = Convert.ToInt32(Enum.Parse(veinEnum, "Oil"));
        int max = Convert.ToInt32(Enum.Parse(veinEnum, "Max"));
        bool ironMatches = (bool)proto.Invoke(null, new object[] { 1001, iron, 1001 });
        bool oilExcluded = !(bool)proto.Invoke(null, new object[] { 1002, oil, 1002 });
        bool outOfRangeExcluded = !(bool)proto.Invoke(null, new object[] { 1003, max, 1003 });
        bool mismatchedItemExcluded = !(bool)proto.Invoke(null, new object[] { 1004, iron, 1005 });
        bool protectionOff = !(bool)display.Invoke(null, new object[] { false, 1001, true });
        bool manufacturedExcluded = !(bool)display.Invoke(null, new object[] { true, 9001, false });
        bool supportedVisible = (bool)display.Invoke(null, new object[] { true, 1001, ironMatches });
        bool eligibleToIneligible = (bool)display.Invoke(null, new object[] { true, 1001, true })
            && !(bool)display.Invoke(null, new object[] { true, 1001, false });
        bool ineligibleToEligible = !(bool)display.Invoke(null, new object[] { true, 1001, false })
            && (bool)display.Invoke(null, new object[] { true, 1001, true });
        return none == 0 && ironMatches && oilExcluded && outOfRangeExcluded
            && mismatchedItemExcluded && protectionOff && manufacturedExcluded && supportedVisible
            && eligibleToIneligible && ineligibleToEligible;
    }

    private static bool RunSingleTranspiler(Assembly plugin, Assembly harmony, Assembly game, MethodInfo target,
        string patchTypeName, string flagName, string[] before, string after, bool expectNoSharedMutation = false)
    {
        if (target == null)
            return false;
        Type patchProcessor = harmony.GetType("HarmonyLib.PatchProcessor", true);
        MethodInfo createGenerator = patchProcessor.GetMethod("CreateILGenerator", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(MethodBase) }, null);
        MethodInfo readInstructions = patchProcessor.GetMethod("GetOriginalInstructions", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(MethodBase), typeof(System.Reflection.Emit.ILGenerator) }, null);
        object generator = createGenerator.Invoke(null, new object[] { target });
        object original = readInstructions.Invoke(null, new[] { (object)target, generator });
        Type patchType = plugin.GetType(patchTypeName, true);
        MethodInfo transpiler = patchType.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        IEnumerable transformed = (IEnumerable)transpiler.Invoke(null, new[] { original });
        var codes = new ArrayList();
        foreach (object instruction in transformed)
            codes.Add(instruction);
        MethodInfo helper = plugin.GetType("VeinProtector.Core.VeinProtectionLogic", true)
            .GetMethod("GetEffectiveVeinCount", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { game.GetType("MinerComponent", true), game.GetType("PlanetFactory", true) }, null);
        bool noMutation = true;
        if (expectNoSharedMutation)
        {
            noMutation = ReadFlag(patchType, "GlobalMutationsSuppressed");
            for (int i = 0; i < codes.Count; i++)
            {
                object operand = codes[i].GetType().GetField("operand").GetValue(codes[i]);
                MethodInfo call = operand as MethodInfo;
                if (call != null && (call.Name == "AddRefProductSpeed" || call.Name == "AddRefConsumeSpeed"))
                    noMutation = false;
            }
        }
        return ReadFlag(patchType, flagName) && CheckCallStack(codes, helper, before, after) && noMutation;
    }

    private static bool CheckCallStack(ArrayList codes, MethodInfo target, string[] before, string after)
    {
        if (target == null)
            return false;

        for (int i = 0; i < codes.Count; i++)
        {
            object code = codes[i];
            FieldInfo operandField = code.GetType().GetField("operand");
            MethodInfo call = operandField.GetValue(code) as MethodInfo;
            if (call == null || call != target)
                continue;

            for (int j = 0; j < before.Length; j++)
            {
                if (i - before.Length + j < 0 || !InstructionMatches(codes[i - before.Length + j], before[j]))
                    return false;
            }
            return i + 1 < codes.Count && InstructionMatches(codes[i + 1], after);
        }
        return false;
    }

    private static bool InstructionMatches(object instruction, string name)
    {
        FieldInfo opcodeField = instruction.GetType().GetField("opcode");
        System.Reflection.Emit.OpCode opcode = (System.Reflection.Emit.OpCode)opcodeField.GetValue(instruction);
        return String.Equals(opcode.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadFlag(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        return field != null && (bool)field.GetValue(null);
    }

    private static bool RunLogicChecks(Assembly plugin, string gameManaged)
    {
        Assembly game = Assembly.LoadFrom(Path.Combine(gameManaged, "Assembly-CSharp.dll"));
        Type minerType = game.GetType("MinerComponent", true);
        Type veinType = game.GetType("VeinData", true);
        object miner = Activator.CreateInstance(minerType);
        SetField(minerType, miner, "type", Enum.Parse(game.GetType("EMinerType", true), "Vein"));
        SetField(minerType, miner, "veinCount", 6);
        SetField(minerType, miner, "veins", new[] { 1, 2, 3, 4, 5, 6 });

        Array pool = Array.CreateInstance(veinType, 7);
        int[] amounts = { 500, 1, 300, 0, 100, 1 };
        for (int i = 0; i < amounts.Length; i++)
        {
            object vein = Activator.CreateInstance(veinType);
            SetField(veinType, vein, "id", i + 1);
            SetField(veinType, vein, "amount", amounts[i]);
            pool.SetValue(vein, i + 1);
        }

        Type logic = plugin.GetType("VeinProtector.Core.VeinProtectionLogic", true);
        MethodInfo countMethod = logic.GetMethod("GetEffectiveVeinCount", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { minerType, veinType.MakeArrayType(), typeof(float) }, null);
        MethodInfo tickCountMethod = logic.GetMethod("GetProductionTickCount", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { minerType, veinType.MakeArrayType(), typeof(float) }, null);
        int countPositive = (int)countMethod.Invoke(null, new[] { miner, pool, (object)0.25f });
        int countZeroRate = (int)countMethod.Invoke(null, new[] { miner, pool, (object)0f });
        int cleanupTicks = (int)tickCountMethod.Invoke(null, new[] { miner, pool, (object)0.25f });
        if (countPositive != 3 || countZeroRate != 6 || cleanupTicks != 3)
            return false;

        SetField(minerType, miner, "minimumVeinAmount", 2);
        int fastPathCount = (int)countMethod.Invoke(null, new[] { miner, pool, (object)0.25f });
        SetField(minerType, miner, "minimumVeinAmount", 0);
        if (fastPathCount != 6)
            return false;

        MethodInfo maxCost = logic.GetMethod("GetMaxAllowedCost", BindingFlags.Static | BindingFlags.NonPublic);
        int capAtTwo = (int)maxCost.Invoke(null, new[] { (object)2, miner, (object)0.1f });
        int noCapAtZeroRate = (int)maxCost.Invoke(null, new[] { (object)2, miner, (object)0f });
        if (capAtTwo != 1 || noCapAtZeroRate != 2)
            return false;

        MethodInfo lossMethod = logic.GetMethod("CalculateReferenceSpeedLoss", BindingFlags.Static | BindingFlags.NonPublic);
        double sampleLoss = (double)lossMethod.Invoke(null, new object[] { 100.0, 6, 1 });
        MethodInfo totalMethod = logic.GetMethod("CalculateProtectedReferenceSpeed", BindingFlags.Static | BindingFlags.NonPublic);
        float protectedTotal = (float)totalMethod.Invoke(null, new object[] { true, 0.25f, 10746f, 1792.0 });
        float zeroRateTotal = (float)totalMethod.Invoke(null, new object[] { true, 0f, 10746f, 1792.0 });
        bool referenceCases = sampleLoss == 500.0 && protectedTotal == 8954f && zeroRateTotal == 10746f;
        Console.WriteLine("ReferenceLossArithmeticPassed=" + referenceCases);
        MethodInfo addVeinMethod = logic.GetMethod("AddProtectedVeinId", BindingFlags.Static | BindingFlags.NonPublic);
        var protectedVeinIds = new HashSet<int>();
        addVeinMethod.Invoke(null, new object[] { protectedVeinIds, 17 });
        addVeinMethod.Invoke(null, new object[] { protectedVeinIds, 17 });
        bool veinDeduplication = protectedVeinIds.Count == 1;
        Console.WriteLine("ProtectedVeinDeduplicationPassed=" + veinDeduplication);
        if (!referenceCases || !veinDeduplication)
            return false;

        MethodInfo allProtected = logic.GetMethod("AreAllBoundVeinsProtected", BindingFlags.Static | BindingFlags.NonPublic);
        SetField(minerType, miner, "veinCount", 6);
        for (int i = 0; i < amounts.Length; i++)
        {
            object vein = pool.GetValue(i + 1);
            SetField(veinType, vein, "amount", 1);
            pool.SetValue(vein, i + 1);
        }
        if (!(bool)allProtected.Invoke(null, new[] { miner, pool, (object)0.1f }))
            return false;

        if ((int)tickCountMethod.Invoke(null, new[] { miner, pool, (object)0.1f }) != 0)
            return false;

        object exhausted = pool.GetValue(1);
        SetField(veinType, exhausted, "amount", 0);
        pool.SetValue(exhausted, 1);
        bool isNotAllProtected = !(bool)allProtected.Invoke(null, new[] { miner, pool, (object)0.1f });
        int tickForVanillaCleanup = (int)tickCountMethod.Invoke(null, new[] { miner, pool, (object)0.1f });
        return isNotAllProtected && tickForVanillaCleanup == 6;
    }

    private static void SetField(Type type, object instance, string name, object value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field.SetValue(instance, value);
    }
}
