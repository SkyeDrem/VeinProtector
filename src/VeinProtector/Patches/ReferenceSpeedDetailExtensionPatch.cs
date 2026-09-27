using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VeinProtector.Core;

namespace VeinProtector.Patches
{
    [HarmonyPatch(typeof(UIReferenceSpeedTip), "SetTip")]
    internal static class ReferenceSpeedDetailExtensionPatch
    {
        private const int ProductCycle = 1;
        private static readonly FieldInfo EntriesField = AccessTools.Field(typeof(UIReferenceSpeedTip), "entries");
        private static readonly FieldInfo EntryRectField = AccessTools.Field(typeof(UIReferenceSpeedTipEntry), "rectTrans");
        private static int _loggedFailure;

        [HarmonyPrefix]
        private static void Prefix(UIReferenceSpeedTip __instance)
        {
            try
            {
                if (__instance == null)
                    return;
                ReferenceSpeedTipExtensionState state = __instance.GetComponent<ReferenceSpeedTipExtensionState>();
                if (state != null)
                    state.BeforeSetTip();
            }
            catch (Exception exception)
            {
                if (Interlocked.Exchange(ref _loggedFailure, 1) == 0)
                    Plugin.LogError("Reference detail pre-refresh cleanup failed: " + exception);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(UIReferenceSpeedTip __instance, int _itemId, int _astroFilter,
            object _itemCycle, Transform _parent)
        {
            ReferenceSpeedTipExtensionState state = null;
            try
            {
                if (__instance == null || __instance.rectTrans == null)
                    return;

                int itemCycle = Convert.ToInt32(_itemCycle, CultureInfo.InvariantCulture);
                state = __instance.GetComponent<ReferenceSpeedTipExtensionState>();
                if (state == null)
                    state = __instance.gameObject.AddComponent<ReferenceSpeedTipExtensionState>();

                state.CaptureVanillaBaseline(__instance);

                if (itemCycle != ProductCycle)
                {
                    state.Hide();
                    return;
                }

                bool supportedVeinItem = VeinProtectionLogic.IsSupportedProtectedResourceItem(_itemId);
                if (!VeinProtectionLogic.ShouldShowReferenceExtension(
                    Plugin.IsProtectionEnabled, _itemId, supportedVeinItem))
                {
                    state.Hide();
                    return;
                }

                int statisticsAstroFilter = GetStatisticsAstroFilter(_parent, _astroFilter);
                GameData data = GameMain.data;
                float vanillaTotal = ReadVanillaReferenceTotal(_itemId, statisticsAstroFilter);
                int protectedVeinCount;
                double protectionLoss;
                CalculateForTooltip(_itemId, statisticsAstroFilter, out protectedVeinCount, out protectionLoss);
                float protectedTotal = VeinProtectionLogic.CalculateProtectedReferenceSpeed(
                    Plugin.IsProtectionEnabled,
                    data == null || data.history == null ? 0f : data.history.miningCostRate,
                    vanillaTotal,
                    protectionLoss);
                state.Refresh(__instance, GetProtectedVeinLabel(protectedVeinCount),
                    GetProtectedSpeedLabel(protectedTotal));
            }
            catch (Exception exception)
            {
                if (state != null)
                {
                    try { state.AbortExtension(); }
                    catch (Exception cleanupException) { exception = new AggregateException(exception, cleanupException); }
                }
                if (Interlocked.Exchange(ref _loggedFailure, 1) == 0)
                    Plugin.LogError("Reference detail extension failed: " + exception);
            }
        }

        private static int GetStatisticsAstroFilter(Transform parent, int tooltipAstroFilter)
        {
            UIProductEntry entry = parent == null ? null : parent.GetComponentInParent<UIProductEntry>();
            if (entry != null && entry.productionStatWindow != null)
                return entry.productionStatWindow.astroFilter;

            // UIReferenceSpeedTip.SetTip uses zero for its all-factory aggregation.
            return tooltipAstroFilter == 0 ? -1 : tooltipAstroFilter;
        }

        private static float ReadVanillaReferenceTotal(int itemId, int astroFilter)
        {
            GameData data = GameMain.data;
            if (data == null || data.statistics == null || data.statistics.production == null
                || data.statistics.production.factoryStatPool == null)
                return 0f;

            FactoryProductionStat[] statPool = data.statistics.production.factoryStatPool;
            float total = 0f;
            foreach (PlanetFactory factory in VeinProtectionLogic.EnumerateFactoriesForAstroFilter(data, astroFilter))
            {
                if (factory == null || factory.index < 0 || factory.index >= statPool.Length)
                    continue;
                FactoryProductionStat factoryStat = statPool[factory.index];
                if (factoryStat == null || factoryStat.productIndices == null || itemId < 0
                    || itemId >= factoryStat.productIndices.Length || factoryStat.productPool == null)
                    continue;

                int productIndex = factoryStat.productIndices[itemId];
                if (productIndex > 0 && productIndex < factoryStat.productPool.Length
                    && factoryStat.productPool[productIndex] != null)
                    total += factoryStat.productPool[productIndex].refProductSpeed;
            }

            return total;
        }

        private static void CalculateForTooltip(int itemId, int astroFilter, out int protectedVeinCount, out double protectionLoss)
        {
            protectedVeinCount = 0;
            protectionLoss = 0.0;
            GameData data = GameMain.data;
            if (!Plugin.IsProtectionEnabled || data == null || data.history == null)
                return;

            float miningRate = data.history.miningCostRate;
            if (miningRate <= 0f)
                return;

            foreach (PlanetFactory factory in VeinProtectionLogic.EnumerateFactoriesForAstroFilter(data, astroFilter))
            {
                if (factory == null || factory.veinPool == null || factory.factorySystem == null)
                    continue;

                MinerComponent[] miners = factory.factorySystem.minerPool;
                if (miners == null)
                    continue;

                int minerLimit = Math.Min(factory.factorySystem.minerCursor, miners.Length);
                var factoryProtectedVeinIds = new HashSet<int>();
                for (int i = 1; i < minerLimit; i++)
                {
                    MinerComponent miner = miners[i];
                    if (miner.id != i || miner.type != EMinerType.Vein || miner.veinCount <= 0
                        || miner.minimumVeinAmount > VeinProtectionLogic.KeepVeinAmount || miner.veins == null)
                        continue;

                    int boundCount = Math.Min(miner.veinCount, miner.veins.Length);
                    for (int j = 0; j < boundCount; j++)
                    {
                        int veinId = miner.veins[j];
                        if (veinId <= 0 || veinId >= factory.veinPool.Length)
                            continue;
                        VeinData vein = factory.veinPool[veinId];
                        if (vein.id != 0 && vein.productId == itemId && vein.amount <= VeinProtectionLogic.KeepVeinAmount)
                            VeinProtectionLogic.AddProtectedVeinId(factoryProtectedVeinIds, veinId);
                    }

                    if (miner.period <= 0 || miner.currentVeinIndex < 0 || miner.currentVeinIndex >= boundCount
                        || factory.powerSystem == null || factory.powerSystem.consumerPool == null
                        || miner.pcId < 0 || miner.pcId >= factory.powerSystem.consumerPool.Length)
                        continue;

                    int currentVeinId = miner.veins[miner.currentVeinIndex];
                    if (currentVeinId <= 0 || currentVeinId >= factory.veinPool.Length)
                        continue;
                    VeinData currentVein = factory.veinPool[currentVeinId];
                    if (currentVein.id == 0 || currentVein.productId != itemId
                        || factory.powerSystem.consumerPool[miner.pcId].networkId <= 0)
                        continue;

                    int effectiveCount = VeinProtectionLogic.GetEffectiveVeinCount(miner, factory.veinPool, miningRate);
                    double baseReferenceSpeed = 3600.0 / miner.period * data.history.miningSpeedScale * miner.speed;
                    protectionLoss += VeinProtectionLogic.CalculateReferenceSpeedLoss(
                        baseReferenceSpeed, miner.veinCount, effectiveCount);
                }
                protectedVeinCount += factoryProtectedVeinIds.Count;
            }
        }

        private static string GetProtectedVeinLabel(int count)
        {
            string number = count.ToString("#,##0", CultureInfo.CurrentCulture);
            return IsEnglishUi() ? "Protected veins: " + number : "已保护矿点：" + number;
        }

        private static string GetProtectedSpeedLabel(float speed)
        {
            string number = speed.ToString("#,##0.##", CultureInfo.CurrentCulture) + " / min";
            return IsEnglishUi() ? "Protected-State Reference Rate: " + number : "保护状态参考速率：" + number;
        }

        private static bool IsEnglishUi()
        {
            try
            {
                return CultureInfo.GetCultureInfo(Localization.CurrentLanguageLCID).TwoLetterISOLanguageName == "en";
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class ReferenceSpeedTipExtensionState : MonoBehaviour
    {
        private const float AddedHeight = 44f;
        private const float GapAbove = 2f;
        private const float GapBelow = 3f;
        private const float RowHeight = 18f;
        private static int _loggedFailure;
        private static readonly FieldInfo EntriesField = AccessTools.Field(typeof(UIReferenceSpeedTip), "entries");
        private static readonly FieldInfo EntryRectField = AccessTools.Field(typeof(UIReferenceSpeedTipEntry), "rectTrans");
        private readonly List<RectState> _layout = new List<RectState>();
        private RectTransform _panel;
        private Vector2 _panelBaseSize;
        private Vector2 _panelBasePosition;
        private Bounds _baseTableHeaderBounds;
        private Vector2 _baseTableHeaderPosition;
        private float _layoutOffset;
        private RectTransform _extensionRoot;
        private Text _protectedVeinText;
        private Text _protectedSpeedText;
        private string _expectedVeinText;
        private string _expectedSpeedText;
        private Coroutine _detailLayoutCoroutine;
        private int _layoutGeneration;

        internal void BeforeSetTip()
        {
            CancelPendingLayout();
            RestoreBaseLayout();
            if (_extensionRoot != null)
                _extensionRoot.gameObject.SetActive(false);
        }

        internal void Hide()
        {
            CancelPendingLayout();
            RestoreBaseLayout();
            if (_extensionRoot != null)
                _extensionRoot.gameObject.SetActive(false);
        }

        internal void AbortExtension()
        {
            CancelPendingLayout();
            RestoreBaseLayout();
            if (_extensionRoot != null)
                _extensionRoot.gameObject.SetActive(false);
        }

        internal void Refresh(UIReferenceSpeedTip tip, string protectedVeins, string protectedSpeed)
        {
            EnsureExtensionRoot(tip);
            _protectedVeinText.text = protectedVeins;
            _protectedSpeedText.text = protectedSpeed;
            _expectedVeinText = protectedVeins;
            _expectedSpeedText = protectedSpeed;
            CaptureBaseLayout(tip);
            RefreshCanvasLayout(tip);
            ValidateVisibleText();
            ScheduleNextFrame(tip);
        }

        internal void CaptureVanillaBaseline(UIReferenceSpeedTip tip)
        {
            CaptureBaseLayout(tip);
        }

        private void EnsureExtensionRoot(UIReferenceSpeedTip tip)
        {
            if (_extensionRoot != null && _protectedVeinText != null && _protectedSpeedText != null)
                return;

            Transform existing = tip.rectTrans.Find("VeinProtector-ReferenceExtension");
            if (existing != null)
            {
                _extensionRoot = existing as RectTransform;
                _protectedVeinText = FindText(existing, "VeinProtector-ProtectedVeinText");
                _protectedSpeedText = FindText(existing, "VeinProtector-ProtectedRefSpeedText");
            }

            if (_extensionRoot == null)
            {
                GameObject root = new GameObject("VeinProtector-ReferenceExtension", typeof(RectTransform));
                _extensionRoot = root.transform as RectTransform;
                _extensionRoot.SetParent(tip.rectTrans, false);
            }

            if (_protectedVeinText == null || _protectedSpeedText == null)
            {
                Text visibleTemplate = tip.totalSpeedText;
                if (visibleTemplate == null || !visibleTemplate.enabled || !visibleTemplate.gameObject.activeInHierarchy
                    || String.IsNullOrEmpty(visibleTemplate.text))
                    throw new InvalidOperationException("visible vanilla Reference Rate Text template was not found");
                if (_protectedVeinText == null)
                {
                    _protectedVeinText = CloneText(visibleTemplate, _extensionRoot, "VeinProtector-ProtectedVeinText");
                    PlaceChildText(_protectedVeinText.rectTransform, 0f);
                }
                if (_protectedSpeedText == null)
                {
                    _protectedSpeedText = CloneText(visibleTemplate, _extensionRoot, "VeinProtector-ProtectedRefSpeedText");
                    PlaceChildText(_protectedSpeedText.rectTransform, -(RowHeight + 3f));
                }
            }
            _extensionRoot.gameObject.SetActive(false);
        }

        private static Text FindText(Transform root, string name)
        {
            Transform child = root.Find(name);
            return child == null ? null : child.GetComponent<Text>();
        }

        private static Text CloneText(Text source, Transform parent, string name)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            clone.name = name;
            Text text = clone.GetComponent<Text>();
            if (text == null)
                throw new InvalidOperationException("cloned reference detail row has no Unity UI Text component");
            RemoveLocalizerComponents(clone.transform);
            text.raycastTarget = false;
            text.resizeTextForBestFit = false;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleLeft;
            text.enabled = true;
            clone.SetActive(true);
            return text;
        }

        private static void PlaceChildText(RectTransform rect, float y)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private void CaptureBaseLayout(UIReferenceSpeedTip tip)
        {
            _layout.Clear();
            _panel = tip.rectTrans;
            _panelBaseSize = _panel.sizeDelta;
            _panelBasePosition = _panel.anchoredPosition;
            if (tip.tableHeaderTrans == null)
                throw new InvalidOperationException("reference table header RectTransform is missing");
            _baseTableHeaderBounds = GetLocalBounds(tip.tableHeaderTrans, tip.rectTrans);
            _baseTableHeaderPosition = tip.tableHeaderTrans.anchoredPosition;
            _layoutOffset = AddedHeight + GapAbove + GapBelow;

            CaptureRect(tip.headerTrans, false);
            CaptureRect(tip.totalSpeedText == null ? null : tip.totalSpeedText.rectTransform, false);
            CaptureRect(tip.emptyTipTrans, false);
            CaptureRect(tip.zeroCountTipText == null ? null : tip.zeroCountTipText.rectTransform, false);
            CaptureRect(tip.tableHeaderTrans, true);
            CaptureRect(tip.subTipRectTrans, true);
            if (tip.previousPageBtn != null)
                CaptureRect(tip.previousPageBtn.transform as RectTransform, true);
            if (tip.nextPageBtn != null)
                CaptureRect(tip.nextPageBtn.transform as RectTransform, true);

            if (EntriesField != null)
            {
                IEnumerable entries = EntriesField.GetValue(tip) as IEnumerable;
                if (entries != null)
                {
                    foreach (object entry in entries)
                    {
                        if (entry != null && EntryRectField != null)
                            CaptureRect(EntryRectField.GetValue(entry) as RectTransform, true);
                    }
                }
            }
        }

        private void CaptureRect(RectTransform rect, bool shift)
        {
            if (rect == null || rect == _panel || rect == _extensionRoot)
                return;
            for (int i = 0; i < _layout.Count; i++)
            {
                if (_layout[i].Rect == rect)
                {
                    _layout[i].Shift = _layout[i].Shift || shift;
                    return;
                }
            }
            _layout.Add(new RectState(rect, rect.anchoredPosition, rect.sizeDelta, shift));
        }

        private static Bounds GetLocalBounds(RectTransform rect, Transform localTo)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 first = localTo.InverseTransformPoint(corners[0]);
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
                bounds.Encapsulate(localTo.InverseTransformPoint(corners[i]));
            return bounds;
        }

        private void ApplyExtensionLayout(UIReferenceSpeedTip tip)
        {
            _panel.sizeDelta = new Vector2(_panelBaseSize.x, _panelBaseSize.y + _layoutOffset);
            for (int i = 0; i < _layout.Count; i++)
            {
                RectState state = _layout[i];
                if (state.Rect != null && state.Shift && !IsChildOfShiftedRect(state.Rect))
                    state.Rect.anchoredPosition = new Vector2(state.BasePosition.x, state.BasePosition.y - _layoutOffset);
            }

            _extensionRoot.anchorMin = new Vector2(0f, 1f);
            _extensionRoot.anchorMax = new Vector2(0f, 1f);
            _extensionRoot.pivot = new Vector2(0f, 1f);
            _extensionRoot.sizeDelta = new Vector2(_baseTableHeaderBounds.size.x, AddedHeight);
            _extensionRoot.anchoredPosition = new Vector2(
                _baseTableHeaderBounds.min.x - tip.rectTrans.rect.xMin,
                _baseTableHeaderPosition.y - GapAbove);
            if (_extensionRoot.parent != tip.rectTrans)
                _extensionRoot.SetParent(tip.rectTrans, false);
            if (tip.tableHeaderTrans.parent == _extensionRoot.parent)
                _extensionRoot.SetSiblingIndex(tip.tableHeaderTrans.GetSiblingIndex());
            _extensionRoot.gameObject.SetActive(true);
            _protectedVeinText.text = _expectedVeinText;
            _protectedSpeedText.text = _expectedSpeedText;
            _protectedVeinText.enabled = true;
            _protectedSpeedText.enabled = true;
            _protectedVeinText.gameObject.SetActive(true);
            _protectedSpeedText.gameObject.SetActive(true);
            PlaceChildText(_protectedVeinText.rectTransform, 0f);
            PlaceChildText(_protectedSpeedText.rectTransform, -(RowHeight + 3f));
        }

        private void RefreshCanvasLayout(UIReferenceSpeedTip tip)
        {
            _extensionRoot.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip.rectTrans);
            ApplyExtensionLayout(tip);
            Canvas.ForceUpdateCanvases();
        }

        private static void RemoveLocalizerComponents(Transform root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
                if (components[i] != null && components[i].GetType().Name.IndexOf("Localizer", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.Destroy(components[i]);
        }

        private void ScheduleNextFrame(UIReferenceSpeedTip tip)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                throw new InvalidOperationException("Plugin coroutine runner is unavailable");
            CancelPendingLayout();
            int generation = ++_layoutGeneration;
            _detailLayoutCoroutine = plugin.StartCoroutine(ApplyReferenceDetailLayoutNextFrame(tip, generation));
        }

        private IEnumerator ApplyReferenceDetailLayoutNextFrame(UIReferenceSpeedTip tip, int generation)
        {
            yield return null;
            if (generation != _layoutGeneration)
                yield break;
            _detailLayoutCoroutine = null;
            if (tip == null)
                yield break;
            if (_extensionRoot == null || !_extensionRoot)
            {
                AbortExtension();
                yield break;
            }
            if (!tip.gameObject.activeInHierarchy)
            {
                AbortExtension();
                yield break;
            }

            try
            {
                _protectedVeinText.text = _expectedVeinText;
                _protectedSpeedText.text = _expectedSpeedText;
                RefreshCanvasLayout(tip);
                ValidateVisibleText();
            }
            catch (Exception exception)
            {
                try { AbortExtension(); }
                catch (Exception cleanupException) { exception = new AggregateException(exception, cleanupException); }
                if (System.Threading.Interlocked.Exchange(ref _loggedFailure, 1) == 0)
                    Plugin.LogWarning("Reference detail next-frame layout failed; vanilla tooltip layout restored: " + exception.Message);
            }
        }

        private void CancelPendingLayout()
        {
            _layoutGeneration++;
            Plugin plugin = Plugin.Instance;
            if (_detailLayoutCoroutine != null && plugin != null)
            {
                plugin.StopCoroutine(_detailLayoutCoroutine);
                _detailLayoutCoroutine = null;
            }
        }

        private void ValidateVisibleText()
        {
            ValidateText(_protectedVeinText);
            ValidateText(_protectedSpeedText);
            if (_extensionRoot == null || !_extensionRoot.gameObject.activeInHierarchy)
                throw new InvalidOperationException("reference extension root is not active");
        }

        private static void ValidateText(Text text)
        {
            if (text == null || !text.enabled || !text.gameObject.activeInHierarchy
                || String.IsNullOrEmpty(text.text) || text.color.a <= 0.01f)
                throw new InvalidOperationException("VeinProtector detail Text is empty, inactive, or transparent");
            CanvasRenderer renderer = text.GetComponent<CanvasRenderer>();
            if (renderer != null && renderer.GetAlpha() <= 0.01f)
                throw new InvalidOperationException("VeinProtector detail CanvasRenderer alpha is zero");
            Rect rect = text.rectTransform.rect;
            if (rect.width <= 0.5f || rect.height <= 0.5f)
                throw new InvalidOperationException("VeinProtector detail Text has an empty RectTransform");
        }

        private void RestoreBaseLayout()
        {
            for (int i = 0; i < _layout.Count; i++)
            {
                RectState state = _layout[i];
                if (state.Rect != null)
                {
                    state.Rect.anchoredPosition = state.BasePosition;
                    state.Rect.sizeDelta = state.BaseSize;
                }
            }
            if (_panel != null)
            {
                _panel.sizeDelta = _panelBaseSize;
                _panel.anchoredPosition = _panelBasePosition;
            }
            _layoutOffset = 0f;
            _layout.Clear();
            _panel = null;
        }

        private bool IsChildOfShiftedRect(RectTransform rect)
        {
            for (int i = 0; i < _layout.Count; i++)
                if (_layout[i].Shift && _layout[i].Rect != null && rect != _layout[i].Rect
                    && rect.IsChildOf(_layout[i].Rect))
                    return true;
            return false;
        }

        private sealed class RectState
        {
            internal readonly RectTransform Rect;
            internal readonly Vector2 BasePosition;
            internal readonly Vector2 BaseSize;
            internal bool Shift;

            internal RectState(RectTransform rect, Vector2 basePosition, Vector2 baseSize, bool shift)
            {
                Rect = rect;
                BasePosition = basePosition;
                BaseSize = baseSize;
                Shift = shift;
            }
        }
    }
}
