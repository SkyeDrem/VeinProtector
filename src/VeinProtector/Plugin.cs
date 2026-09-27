using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using VeinProtector.Patches;

namespace VeinProtector
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "skye.dsp.veinprotector";
        public const string PluginName = "VeinProtector";
        public const string PluginVersion = "0.5.1";

        internal static ConfigEntry<bool> ProtectionEnabled;
        internal static ManualLogSource Log;
        internal static Plugin Instance { get; private set; }
        private static ConfigFile _configFile;

        internal static bool IsProtectionEnabled
        {
            get { return ProtectionEnabled == null || ProtectionEnabled.Value; }
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _configFile = Config;
            ProtectionEnabled = Config.Bind(
                "Vein Protection",
                "Enabled",
                true,
                "Keep ordinary solid veins at one remaining unit. Toggle this from DSP Settings.");

            Logger.LogInfo("Loaded");
            Logger.LogInfo("Version = " + PluginVersion);
            Logger.LogInfo("ProtectionEnabled = " + ProtectionEnabled.Value);
            ApplyPatch(PluginGuid + ".OptionsToggle", typeof(OptionsProtectionTogglePatch),
                AccessTools.Method(typeof(UIOptionWindow), "_OnOpen", Type.EmptyTypes), "UIOptionWindow._OnOpen options toggle");
            ApplyPatch(PluginGuid + ".MinerGuard", typeof(MinerAllProtectedPrefixPatch),
                GetInternalUpdateMethod(), "MinerComponent.InternalUpdate protection Prefix");
            ApplyPatch(PluginGuid + ".MinerLogic", typeof(MinerProtectionTranspilerPatch),
                GetInternalUpdateMethod(), "MinerComponent.InternalUpdate protection Transpiler");
            ApplyPatch(PluginGuid + ".MinerUI", typeof(UIMinerWindowPatch),
                AccessTools.Method(typeof(UIMinerWindow), "_OnUpdate", Type.EmptyTypes), "UIMinerWindow._OnUpdate");
            ApplyPatch(PluginGuid + ".CollectorUI", typeof(UIVeinCollectorPanelPatch),
                AccessTools.Method(typeof(UIVeinCollectorPanel), "_OnUpdate", Type.EmptyTypes), "UIVeinCollectorPanel._OnUpdate");
            ApplyPatch(PluginGuid + ".ReferenceDetails", typeof(ReferenceSpeedDetailExtensionPatch),
                AccessTools.Method(typeof(UIReferenceSpeedTip), "SetTip"),
                "UIReferenceSpeedTip.SetTip read-only detail extension");

            if (!(MinerProtectionTranspilerPatch.TimeMultiplierPatched
                && MinerProtectionTranspilerPatch.ProtectedVeinSkipPatched
                && MinerProtectionTranspilerPatch.CostBoundaryPatched
                && UIMinerWindowPatch.SpeedMultiplierPatched))
                Logger.LogError("Protection transpiler patch points incomplete: mining-rate="
                    + MinerProtectionTranspilerPatch.TimeMultiplierPatched
                    + ", vein-skip=" + MinerProtectionTranspilerPatch.ProtectedVeinSkipPatched
                    + ", cost-boundary=" + MinerProtectionTranspilerPatch.CostBoundaryPatched
                    + ", miner-UI=" + UIMinerWindowPatch.SpeedMultiplierPatched);

            if (!UIVeinCollectorPanelPatch.SpeedMultiplierPatched)
                Logger.LogError("Advanced miner detail speed multiplier patch point was not found; vanilla UI is retained");

            if (IsUxAssistLoaded())
                Logger.LogWarning("UXAssist also contains a vein-protection feature. Do not enable both vein-protection implementations simultaneously.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        internal static void SetProtectionEnabled(bool enabled)
        {
            if (ProtectionEnabled == null || ProtectionEnabled.Value == enabled)
                return;

            ProtectionEnabled.Value = enabled;
            if (_configFile != null)
                _configFile.Save();
        }

        internal static void LogError(string message)
        {
            if (Log != null)
                Log.LogError(message);
        }

        internal static void LogWarning(string message)
        {
            if (Log != null)
                Log.LogWarning(message);
        }

        private static MethodInfo GetInternalUpdateMethod()
        {
            return AccessTools.Method(typeof(MinerComponent), "InternalUpdate", new[]
            {
                typeof(PlanetFactory), typeof(VeinData[]), typeof(float), typeof(float), typeof(float), typeof(int[])
            });
        }

        private bool ApplyPatch(string owner, Type patchClass, MethodBase target, string description)
        {
            try
            {
                if (target == null)
                    throw new MissingMethodException("Target method was not found: " + description);

                var harmony = new Harmony(owner);
                harmony.CreateClassProcessor(patchClass).Patch();

                var info = Harmony.GetPatchInfo(target);
                bool installed = info != null && info.Owners.Contains(owner);
                if (!installed)
                    Logger.LogError(description + " patch was not found in Harmony patch info");
                return installed;
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to install " + description + " patch: " + exception);
                return false;
            }
        }

        private static bool IsUxAssistLoaded()
        {
            foreach (var plugin in Chainloader.PluginInfos.Values)
            {
                if (plugin.Metadata.Name.IndexOf("UXAssist", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
