using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VeinProtector.Patches
{
    [HarmonyPatch(typeof(UIOptionWindow), "_OnOpen")]
    internal static class OptionsProtectionTogglePatch
    {
        private const string RowName = "VeinProtector-ProtectionRow";
        private const string ToggleName = "VeinProtector-ProtectionToggle";
        private const int GameTabIndex = 2;
        private const float BottomMargin = 22f;
        private static int _loggedFailure;

        [HarmonyPostfix]
        private static void Postfix(UIOptionWindow __instance)
        {
            try
            {
                if (__instance == null) throw new InvalidOperationException("UIOptionWindow is null");
                RectTransform content = __instance.gameScrollContentRect;
                if (content == null)
                    throw new InvalidOperationException("confirmed Game page gameScrollContentRect is missing");
                EnsureProtectionRow(__instance, content);
            }
            catch (Exception exception)
            {
                if (Interlocked.Exchange(ref _loggedFailure, 1) == 0)
                    Plugin.LogError("Settings protection row creation failed: " + exception);
            }
        }

        private static void EnsureProtectionRow(UIOptionWindow window, RectTransform content)
        {
            Transform existing = FindNamedChild(window.transform, RowName);
            RectTransform row = existing as RectTransform;
            bool created = row == null;
            if (created)
                row = CreateProtectionRow(window, content);
            bool moved = row.parent != content;
            if (moved) row.SetParent(content, false);

            row.gameObject.SetActive(true);
            Toggle nativeToggle = GetNativeToggle(row);
            if (nativeToggle == null)
                throw new InvalidOperationException("the cloned settings row has no Unity Toggle");
            UIToggle wrapper = row.GetComponentInChildren<UIToggle>(true);
            if (wrapper == null)
                throw new InvalidOperationException("the cloned settings row has no UIToggle wrapper");
            Text label = FindLabel(row, wrapper.transform);
            if (label == null)
                throw new InvalidOperationException("the cloned settings row has no label Text");

            RemoveLocalizers(row);
            label.text = GetProtectionLabel();
            label.enabled = true;
            label.gameObject.SetActive(true);
            nativeToggle.gameObject.name = ToggleName;
            ProtectionToggleBinding binding = row.GetComponent<ProtectionToggleBinding>();
            if (binding == null) binding = row.gameObject.AddComponent<ProtectionToggleBinding>();
            binding.Bind(nativeToggle);
            binding.Sync(Plugin.IsProtectionEnabled);

            if (created || moved)
                PlaceNewRow(content, row);
        }

        private static RectTransform CreateProtectionRow(UIOptionWindow window, RectTransform targetContent)
        {
            if (window.techQueueComp == null || window.gameScrollContentRect == null)
                throw new InvalidOperationException("confirmed Game tech-queue Toggle template or content is missing");
            RectTransform templateContent = window.gameScrollContentRect;
            Text templateLabel = FindLabel(templateContent, window.techQueueComp.transform);
            if (templateLabel == null)
                throw new InvalidOperationException("the confirmed tech-queue row label could not be resolved");
            Transform templateRow = FindCommonRowRoot(templateContent, window.techQueueComp.transform, templateLabel.transform);
            if (templateRow == null || templateRow == templateContent)
                throw new InvalidOperationException("tech-queue label and Toggle do not share a setting-row parent");

            GameObject clone = UnityEngine.Object.Instantiate(templateRow.gameObject, targetContent, false);
            clone.name = RowName;
            clone.SetActive(true);
            RectTransform row = clone.transform as RectTransform;
            if (row == null)
                throw new InvalidOperationException("tech-queue row has no RectTransform");
            UIToggle[] wrappers = row.GetComponentsInChildren<UIToggle>(true);
            if (wrappers.Length != 1)
                throw new InvalidOperationException("tech-queue row must contain exactly one UIToggle; found " + wrappers.Length);
            Toggle clonedToggle = GetNativeToggle(row);
            if (clonedToggle == null)
                throw new InvalidOperationException("tech-queue row clone has no Unity Toggle");
            ClearAllListeners(clonedToggle);
            return row;
        }

        private static Transform FindCommonRowRoot(Transform content, Transform toggle, Transform label)
        {
            Transform cursor = toggle;
            while (cursor != null && cursor != content)
            {
                if (label == cursor || label.IsChildOf(cursor))
                    return cursor;
                cursor = cursor.parent;
            }
            return null;
        }

        private static void PlaceNewRow(RectTransform content, RectTransform row)
        {
            LayoutGroup layout = content.GetComponent<LayoutGroup>();
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            float contentHeightBefore = Mathf.Max(content.rect.height, content.sizeDelta.y);
            if (layout != null)
            {
                row.SetAsLastSibling();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                EnsureContentHeight(content, row, contentHeightBefore);
                return;
            }

            List<RectTransform> nativeRows = GetDirectSettingRows(content, row);
            if (nativeRows.Count == 0)
                throw new InvalidOperationException("Game content has no active direct-child setting rows for manual spacing");
            nativeRows.Sort(delegate(RectTransform a, RectTransform b)
            {
                return GetLocalBounds(a, content).min.y.CompareTo(GetLocalBounds(b, content).min.y);
            });
            RectTransform lowest = nativeRows[0];
            Bounds lowestBounds = GetLocalBounds(lowest, content);
            float gap = MeasureBottomGap(nativeRows, content, lowestBounds.size.y);
            float targetTop = lowestBounds.min.y - gap;
            Bounds cloneBounds = GetLocalBounds(row, content);
            row.anchoredPosition = new Vector2(row.anchoredPosition.x,
                row.anchoredPosition.y + targetTop - cloneBounds.max.y);
            row.SetAsLastSibling();

            if (fitter != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            EnsureContentHeight(content, row, contentHeightBefore);
        }

        private static float GetRequiredHeight(RectTransform row)
        {
            return Mathf.Abs(row.anchoredPosition.y) + row.rect.height + BottomMargin;
        }

        private static void EnsureContentHeight(RectTransform content, RectTransform row, float previousHeight)
        {
            float requiredHeight = GetRequiredHeight(row);
            float preferredHeight = LayoutUtility.GetPreferredHeight(content);
            float newHeight = Mathf.Max(content.rect.height, content.sizeDelta.y, previousHeight,
                requiredHeight, preferredHeight);
            if (newHeight > content.sizeDelta.y)
                content.sizeDelta = new Vector2(content.sizeDelta.x, newHeight);
        }

        private static float MeasureBottomGap(List<RectTransform> rows, RectTransform content, float fallbackRowHeight)
        {
            int samples = Math.Min(3, rows.Count - 1);
            float sum = 0f;
            int used = 0;
            for (int i = 0; i < samples; i++)
            {
                Bounds lower = GetLocalBounds(rows[i], content);
                Bounds upper = GetLocalBounds(rows[i + 1], content);
                float gap = upper.min.y - lower.max.y;
                if (gap >= 0f && gap < 100f) { sum += gap; used++; }
            }
            if (used > 0) return sum / used;
            return Mathf.Max(0f, Mathf.Max(fallbackRowHeight + 8f, 40f) - fallbackRowHeight);
        }

        private static List<RectTransform> GetDirectSettingRows(RectTransform content, RectTransform excluded)
        {
            var rows = new List<RectTransform>();
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform child = content.GetChild(i) as RectTransform;
                if (child == null || child == excluded || !child.gameObject.activeSelf || IsDropdownListItem(child))
                    continue;
                if (HasSettingControl(child))
                    rows.Add(child);
            }
            return rows;
        }

        private static bool HasSettingControl(Transform row)
        {
            return row.GetComponentInChildren<UIToggle>(true) != null
                || row.GetComponentInChildren<Slider>(true) != null
                || row.GetComponentInChildren<UIComboBox>(true) != null
                || row.GetComponentInChildren<Dropdown>(true) != null
                || row.GetComponentInChildren<UIButton>(true) != null
                || row.GetComponentInChildren<Button>(true) != null;
        }

        private static bool IsDropdownListItem(Transform row)
        {
            string name = row.name;
            return name.IndexOf("Dropdown List", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Dropdown Item", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Scrollbar", StringComparison.OrdinalIgnoreCase) >= 0
                || row.GetComponent<Scrollbar>() != null;
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

        private static Toggle GetNativeToggle(Transform root)
        {
            if (root == null) return null;
            UIToggle wrapper = root.GetComponentInChildren<UIToggle>(true);
            if (wrapper == null) return null;
            FieldInfo field = AccessTools.Field(typeof(UIToggle), "toggle");
            return field == null ? null : field.GetValue(wrapper) as Toggle;
        }

        private static Text FindLabel(Transform root, Transform toggle)
        {
            if (root == null || toggle == null) return null;
            Text[] texts = root.GetComponentsInChildren<Text>(true);
            Text best = null;
            float bestDistance = Single.PositiveInfinity;
            Vector3 togglePoint = root.InverseTransformPoint(toggle.position);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || texts[i].transform == toggle || texts[i].transform.IsChildOf(toggle)
                    || String.IsNullOrWhiteSpace(texts[i].text)) continue;
                Vector3 point = root.InverseTransformPoint(texts[i].transform.position);
                float distance = Mathf.Abs(point.y - togglePoint.y) + Mathf.Abs(point.x - togglePoint.x) * 0.01f;
                if (distance < bestDistance) { best = texts[i]; bestDistance = distance; }
            }
            return best;
        }

        private static void RemoveLocalizers(Transform root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
                if (components[i] != null && components[i].GetType().Name.IndexOf("Localizer", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(components[i]);
        }

        private static void ClearAllListeners(Toggle toggle)
        {
            toggle.onValueChanged.RemoveAllListeners();
            FieldInfo persistentField = AccessTools.Field(typeof(UnityEventBase), "m_PersistentCalls");
            object calls = persistentField == null ? null : persistentField.GetValue(toggle.onValueChanged);
            MethodInfo clear = calls == null ? null : AccessTools.Method(calls.GetType(), "Clear");
            if (clear != null) clear.Invoke(calls, null);
            MethodInfo dirty = AccessTools.Method(typeof(UnityEventBase), "DirtyPersistentCalls");
            if (dirty != null) dirty.Invoke(toggle.onValueChanged, null);
        }

        private static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamedChild(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetProtectionLabel()
        {
            try { return CultureInfo.GetCultureInfo(Localization.CurrentLanguageLCID).TwoLetterISOLanguageName == "en" ? "Vein Protection" : "矿脉保护"; }
            catch { return "矿脉保护"; }
        }

    }

    internal sealed class ProtectionToggleBinding : MonoBehaviour
    {
        private Toggle _toggle;
        private UnityAction<bool> _listener;
        internal void Bind(Toggle toggle)
        {
            if (_toggle == toggle && _listener != null) return;
            if (_toggle != null && _listener != null) _toggle.onValueChanged.RemoveListener(_listener);
            _toggle = toggle;
            ClearListeners(_toggle);
            _listener = OnValueChanged;
            _toggle.onValueChanged.AddListener(_listener);
        }
        internal void Sync(bool value) { if (_toggle != null && _toggle.isOn != value) _toggle.isOn = value; }
        private void OnValueChanged(bool value) { Plugin.SetProtectionEnabled(value); }
        private static void ClearListeners(Toggle toggle)
        {
            toggle.onValueChanged.RemoveAllListeners();
            FieldInfo persistentField = AccessTools.Field(typeof(UnityEventBase), "m_PersistentCalls");
            object calls = persistentField == null ? null : persistentField.GetValue(toggle.onValueChanged);
            MethodInfo clear = calls == null ? null : AccessTools.Method(calls.GetType(), "Clear");
            if (clear != null) clear.Invoke(calls, null);
            MethodInfo dirty = AccessTools.Method(typeof(UnityEventBase), "DirtyPersistentCalls");
            if (dirty != null) dirty.Invoke(toggle.onValueChanged, null);
        }
    }
}
