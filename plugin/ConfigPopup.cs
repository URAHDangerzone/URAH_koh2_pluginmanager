using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using Logic;
using UnityEngine.UI;

namespace URAH_PluginManager
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// A plugin's settings in a popup over the Settings window, opened by the "Settings" button of its row.
    /// A dark overlay over the whole window takes the clicks; on it a panel with the window's own
    /// background (its largest image), the plugin's name, one row per setting (ConfigRows, from the
    /// window's toggle and slider prefabs) and a Close button (a copy of the window's button). Not in
    /// m_ActivePreferences, so the window never touches the rows; gone when closed, on another tab or
    /// when the window closes (OnDisable).
    /// </summary>
    internal class ConfigPopup : MonoBehaviour
    {
        private const float Padding = 24f;

        private static ConfigPopup open;

        internal static void Show(UIPreferences window, Entry entry, RectTransform content)
        {
            Close();
            List<ConfigEntryBase> settings = ConfigRows.Editable(entry);
            if (window == null || content == null || settings.Count == 0)
            {
                return;
            }
            DT.Field preferences = Defs.GetDefField("Preferences");
            GameObject labelPrefab = Defs.GetObj<GameObject>(preferences, "label");
            GameObject togglePrefab = Defs.GetObj<GameObject>(preferences, "toggle");
            GameObject sliderPrefab = Defs.GetObj<GameObject>(preferences, "slider");

            // the overlay: the whole window, dark, takes every click
            var overlay = new GameObject("URAH_ConfigPopup", typeof(RectTransform), typeof(Image));
            var ort = (RectTransform)overlay.transform;
            ort.SetParent(window.transform, worldPositionStays: false);
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;
            ort.SetAsLastSibling();
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            open = overlay.AddComponent<ConfigPopup>();

            // the panel: as wide as the settings list, as high as its rows
            var panel = new GameObject("URAH_ConfigPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(ort, worldPositionStays: false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
            float width = (scroll != null ? ((RectTransform)scroll.transform).rect.width : content.rect.width) + 2f * Padding;
            prt.sizeDelta = new Vector2(width, 0f);
            CopyBackground(window, panel.GetComponent<Image>(), ort);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
            VerticalLayoutGroup list = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = list != null ? list.spacing : 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (labelPrefab != null)
            {
                StatusRow title = Spawn<StatusRow>(labelPrefab, prt);
                title.Show(string.Format(Texts.Get("config_title"), entry.Name), failed: false);
            }
            foreach (ConfigEntryBase setting in settings)
            {
                bool toggle = setting.SettingType == typeof(bool);
                GameObject prefab = toggle ? togglePrefab : sliderPrefab;
                if (prefab == null)
                {
                    continue;
                }
                ConfigRow row = toggle ? (ConfigRow)Spawn<ConfigToggleRow>(prefab, prt) : Spawn<ConfigSliderRow>(prefab, prt);
                row.SetEntry(setting);
            }
            AddClose(prt);
            Plugin.Log.LogInfo($"Settings of {entry.Name}: {settings.Count} entries");
        }

        internal static void Close()
        {
            if (open != null)
            {
                Object.Destroy(open.gameObject);
            }
            open = null;
        }

        private void OnDisable() => Close();

        /// <summary>The window's largest visible image (its frame), else a plain dark panel.</summary>
        private static void CopyBackground(UIPreferences window, Image target, Transform skip)
        {
            Image best = null;
            float bestArea = 0f;
            foreach (Image image in window.GetComponentsInChildren<Image>())
            {
                if (image.transform.IsChildOf(skip) || image.sprite == null || !image.enabled)
                {
                    continue;
                }
                Rect r = image.rectTransform.rect;
                float area = r.width * r.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }
            if (best != null)
            {
                target.sprite = best.sprite;
                target.type = best.type;
                target.color = best.color;
                target.pixelsPerUnitMultiplier = best.pixelsPerUnitMultiplier;
            }
            else
            {
                target.color = new Color(0.12f, 0.1f, 0.08f, 0.97f);
            }
        }

        private static void AddClose(RectTransform panel)
        {
            BSGButton template = PluginsTab.ButtonTemplate;
            if (template == null)
            {
                return;
            }
            // in a holder, so the panel's layout does not stretch the button
            var holder = new GameObject("URAH_CloseRow", typeof(RectTransform), typeof(LayoutElement));
            var hrt = (RectTransform)holder.transform;
            hrt.SetParent(panel, worldPositionStays: false);
            var trt = (RectTransform)template.transform;
            float h = trt.rect.height > 0f ? trt.rect.height : 40f;
            holder.GetComponent<LayoutElement>().preferredHeight = h + 8f;
            hrt.sizeDelta = new Vector2(0f, h + 8f);
            BSGButton b = Object.Instantiate(template, hrt, worldPositionStays: false);
            b.name = "URAH_Close";
            b.onClick = btn => Close();
            LayoutElement element = b.GetComponent<LayoutElement>();
            (element != null ? element : b.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(trt.rect.width > 0f ? trt.rect.width : 200f, h);
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                UIText.SetText(label, Texts.Get("close"));
            }
            b.gameObject.SetActive(true);
            b.Enable(true);
        }

        /// <summary>Like PluginsTab.Spawn: the prefab's own Preference goes, ours takes its place.</summary>
        private static T Spawn<T>(GameObject prefab, RectTransform parent) where T : UIPreferences.Preference
        {
            GameObject go = Object.Instantiate(prefab, parent, worldPositionStays: false);
            foreach (UIPreferences.Preference p in go.GetComponents<UIPreferences.Preference>())
            {
                Object.DestroyImmediate(p);
            }
            go.SetActive(true);
            return go.AddComponent<T>();
        }
    }
}
