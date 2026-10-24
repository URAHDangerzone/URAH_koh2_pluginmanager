using System.Collections.Generic;
using HarmonyLib;
using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_PluginManager
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The tab "Plugins (URAH)" in UIPreferences (def PreferencesWindow). Its tabs come from Preferences.def;
    /// a def entry would need a mod, so this one is added in code after BuildLayout, from the same
    /// category prototype, below the Mods tab. Its def is a synthetic field (type urah_plugin_category, no
    /// children): SelectCategory reads def.type, ShowCategoryPreferences def.Children(). It stays out of
    /// m_ModCategories, which BuildCategoriesLayout would hide beyond the def count, so SelectCategory
    /// does not deselect it: a postfix does. Rows (a status line from the label prefab, then one row per
    /// plugin from the Mods tab's row prefab) go into m_ActivePreferences, so the next tab switch destroys
    /// them; they have no setting, so Apply Changes and Revert Defaults ignore them. Toggles act at once.
    /// A plugin with settings gets a "Settings" button that opens them in a popup (ConfigPopup).
    /// </summary>
    internal static class PluginsTab
    {
        private const string CategoryType = "urah_plugin_category";

        private static readonly AccessTools.FieldRef<UIPreferences, RectTransform> categoriesContainer =
            AccessTools.FieldRefAccess<UIPreferences, RectTransform>("m_CategoriesContainer");
        private static readonly AccessTools.FieldRef<UIPreferences, GameObject> categoryPrototype =
            AccessTools.FieldRefAccess<UIPreferences, GameObject>("m_CategoryPrototype");
        private static readonly AccessTools.FieldRef<UIPreferences, RectTransform> categoryContent =
            AccessTools.FieldRefAccess<UIPreferences, RectTransform>("m_CategoryContent");
        private static readonly AccessTools.FieldRef<UIPreferences, UIPreferences.Category> selectedCategory =
            AccessTools.FieldRefAccess<UIPreferences, UIPreferences.Category>("m_SelectedCategory");
        private static readonly AccessTools.FieldRef<UIPreferences, List<UIPreferences.Preference>> activePreferences =
            AccessTools.FieldRefAccess<UIPreferences, List<UIPreferences.Preference>>("m_ActivePreferences");
        private static readonly AccessTools.FieldRef<UIPreferences, BSGButton> revertDefaults =
            AccessTools.FieldRefAccess<UIPreferences, BSGButton>("m_RevertDefaults");
        private static readonly AccessTools.FieldRef<UIPreferences, BSGButton> applyChanges =
            AccessTools.FieldRefAccess<UIPreferences, BSGButton>("m_ApplyChanges");
        private static readonly AccessTools.FieldRef<UIPreferences, BSGButton> openWorkshop =
            AccessTools.FieldRefAccess<UIPreferences, BSGButton>("m_OpenWorkshop");

        /// <summary>The window showing our tab, for refreshing it after a download.</summary>
        private static UIPreferences shown;

        /// <summary>Template for the row buttons: the window's Revert Defaults.</summary>
        internal static BSGButton ButtonTemplate;

        internal static bool Locked() => GameLogic.Get()?.IsRunning() ?? false;

        /// <summary>The content list's spacing before our rows widened it, restored on any other tab.</summary>
        private static VerticalLayoutGroup spacedList;
        private static float spacing;

        internal static void SpaceRows(float overhang)
        {
            UIPreferences window = shown;
            RectTransform content = window != null ? categoryContent(window) : null;
            VerticalLayoutGroup list = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
            if (list == null)
            {
                return;
            }
            if (spacedList != list)
            {
                RestoreSpacing();
                spacedList = list;
                spacing = list.spacing;
            }
            float gap = TabGap(window, content);
            float want = Mathf.Max(overhang, 0f) + gap;
            if (!Mathf.Approximately(list.spacing, want))
            {
                list.spacing = want;
                Plugin.Log.LogInfo($"Plugins tab: row spacing {spacing:0.#} -> {want:0.#} (backgrounds reach {overhang:0.#} beyond a row, gap {gap:0.#} like the tabs)");
            }
        }

        /// <summary>The visible gap between two neighbouring tabs on the left, in the content's units (2 if unknown).</summary>
        private static float TabGap(UIPreferences window, RectTransform content)
        {
            RectTransform container = categoriesContainer(window);
            if (container == null)
            {
                return 2f;
            }
            RectTransform prev = null;
            foreach (Transform child in container)
            {
                if (!child.gameObject.activeInHierarchy || child.GetComponent<UIPreferences.Category>() == null)
                {
                    continue;
                }
                var rt = (RectTransform)child;
                if (prev != null)
                {
                    float scale = Mathf.Abs(content.lossyScale.y) > 1e-6f ? Mathf.Abs(content.lossyScale.y) : 1f;
                    float gap = (Visual(prev, top: false) - Visual(rt, top: true)) / scale;
                    return Mathf.Max(gap, 0f);
                }
                prev = rt;
            }
            return 2f;
        }

        /// <summary>Top or bottom of what is visible of rt: its rect and its active images, in world space.</summary>
        internal static float Visual(RectTransform rt, bool top)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float edge = top ? corners[1].y : corners[0].y;
            foreach (Image image in rt.GetComponentsInChildren<Image>())
            {
                if (!image.enabled || image.overrideSprite == null)
                {
                    continue;
                }
                image.rectTransform.GetWorldCorners(corners);
                edge = top ? Mathf.Max(edge, corners[1].y) : Mathf.Min(edge, corners[0].y);
            }
            return edge;
        }

        private static void RestoreSpacing()
        {
            if (spacedList != null)
            {
                spacedList.spacing = spacing;
            }
            spacedList = null;
        }

        /// <summary>The window's tab, made on its first BuildLayout (that runs on every OnEnable).</summary>
        private static PluginCategory Tab(UIPreferences window)
        {
            RectTransform container = categoriesContainer(window);
            return container != null ? container.GetComponentInChildren<PluginCategory>(includeInactive: true) : null;
        }

        private static void AddTab(UIPreferences window)
        {
            PluginCategory tab = Tab(window);
            if (tab == null)
            {
                DT.Field preferences = Defs.GetDefField("Preferences");
                var def = new DT.Field(preferences?.dt) { key = "urah_plugins", type = CategoryType, children = new List<DT.Field>() };
                tab = UIPreferences.Category.Create<PluginCategory>(def, categoryPrototype(window), categoriesContainer(window)) as PluginCategory;
                if (tab == null)
                {
                    Plugin.Log.LogError("Plugins tab: no category prototype");
                    return;
                }
                tab.OnSelect = c => window.SelectCategory(c);
                tab.transform.SetAsLastSibling();
                WindowFit.Fit(categoriesContainer(window), categoryContent(window),
                    (RectTransform)tab.transform, window.transform);
                // Revert Defaults and Open Workshop share one slot (the game shows one of them): URAH
                // releases goes there, like Open Workshop on the Mods tab. Apply Changes has its own slot
                // and stays hidden here (no setting to apply): Fetch plugins goes there
                tab.fetch = TabButtons.Copy(applyChanges(window), "URAH_FetchPlugins", Texts.Get("fetch"), b => Fetch());
                if (tab.fetch != null)
                {
                    Tooltip.Get(tab.fetch.gameObject).SetText("#" + Texts.Get("fetch_tip"));
                }
                tab.releases = TabButtons.Copy(openWorkshop(window), "URAH_Releases", Texts.Get("releases"), TabButtons.OpenReleases);
                if (tab.releases != null)
                {
                    Tooltip.Get(tab.releases.gameObject).SetText("#" + TabButtons.ReleasesUrl);
                }
                tab.status = TabButtons.StatusLine(tab.fetch != null ? tab.fetch : revertDefaults(window), categoryContent(window));
            }
            ButtonTemplate = revertDefaults(window);
            tab.transform.SetAsLastSibling();
        }

        private static void Fetch()
        {
            if (!Fetcher.Busy && !Locked())
            {
                Plugin.Instance.StartCoroutine(Fetcher.Fetch(Refresh));
            }
        }

        internal static void Install(Entry entry)
        {
            if (!Fetcher.Busy && !Locked())
            {
                Plugin.Instance.StartCoroutine(Fetcher.Install(entry, Refresh));
            }
        }

        internal static void Remove(Entry entry)
        {
            if (!Fetcher.Busy && !Locked())
            {
                Fetcher.Remove(entry);
                Refresh();
            }
        }

        /// <summary>Rows again from the current state, if our tab is still the one shown.</summary>
        internal static void ShowSettings(Entry entry)
        {
            if (shown != null && !Locked())
            {
                ConfigPopup.Show(shown, entry, categoryContent(shown));
            }
        }

        internal static void Refresh()
        {
            UIPreferences window = shown;
            if (window == null || !window.isActiveAndEnabled || !(selectedCategory(window) is PluginCategory))
            {
                return;
            }
            List<UIPreferences.Preference> active = activePreferences(window);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] is PluginRow || active[i] is StatusRow)
                {
                    Object.Destroy(active[i].gameObject);
                    active.RemoveAt(i);
                }
            }
            ShowRows(window);
        }

        /// <summary>A status line, then one row per plugin, like PopulateMods.</summary>
        private static void ShowRows(UIPreferences window)
        {
            shown = window;
            DT.Field preferences = Defs.GetDefField("Preferences");
            GameObject rowPrefab = Defs.GetObj<GameObject>(preferences, "mod");
            GameObject labelPrefab = Defs.GetObj<GameObject>(preferences, "label");
            RectTransform content = categoryContent(window);
            List<UIPreferences.Preference> active = activePreferences(window);
            if (rowPrefab == null || content == null || active == null)
            {
                Plugin.Log.LogError("Plugins tab: no row prefab or content");
                return;
            }
            if (labelPrefab != null)
            {
                // the title, like "Select mod" on the Mods tab; messages go to the line above the bottom buttons
                StatusRow title = Spawn<StatusRow>(labelPrefab, content);
                title.Show(Texts.Get("tab"), failed: false);
                active.Add(title);
            }
            foreach (Entry entry in Catalog.Shown())
            {
                PluginRow row = Spawn<PluginRow>(rowPrefab, content);
                row.SetEntry(entry);
                active.Add(row);
            }
        }

        /// <summary>
        /// Not through the ObjectPool: a pooled row could come back to the Mods tab with our component on it.
        /// The prefab's own Preference component (if it has one) goes, ours takes its place.
        /// </summary>
        private static T Spawn<T>(GameObject prefab, RectTransform content) where T : UIPreferences.Preference
        {
            GameObject go = Object.Instantiate(prefab, content, worldPositionStays: false);
            foreach (UIPreferences.Preference p in go.GetComponents<UIPreferences.Preference>())
            {
                Object.DestroyImmediate(p);
            }
            go.SetActive(true);
            return go.AddComponent<T>();
        }

        private static bool RowsShown(UIPreferences window)
        {
            List<UIPreferences.Preference> active = activePreferences(window);
            return active != null && active.Exists(p => p is PluginRow);
        }

        [HarmonyPatch(typeof(UIPreferences), nameof(UIPreferences.BuildLayout))]
        private static class BuildLayoutPatch
        {
            private static void Postfix(UIPreferences __instance) => AddTab(__instance);
        }

        /// <summary>The game only (de)selects its own three lists; this does ours and fills its rows.</summary>
        [HarmonyPatch(typeof(UIPreferences), nameof(UIPreferences.SelectCategory))]
        private static class SelectCategoryPatch
        {
            private static void Postfix(UIPreferences __instance)
            {
                PluginCategory tab = Tab(__instance);
                if (tab == null)
                {
                    return;
                }
                if (selectedCategory(__instance) != tab)
                {
                    ConfigPopup.Close();
                    tab.Select(selected: false);
                    RestoreSpacing();
                }
                else if (!RowsShown(__instance))
                {
                    ShowRows(__instance);
                }
            }
        }

        /// <summary>Revert Defaults has nothing to revert on our tab: "URAH releases" is in its place.</summary>
        [HarmonyPatch(typeof(UIPreferences), "UpdateButtons")]
        private static class UpdateButtonsPatch
        {
            private static void Postfix(UIPreferences __instance)
            {
                UIPreferences.Category selected = selectedCategory(__instance);
                BSGButton revert = revertDefaults(__instance);
                if (selected is PluginCategory && revert != null)
                {
                    revert.gameObject.SetActive(false);
                }
            }
        }
    }

    /// <summary>
    /// The tab itself: the Mods tab's look (its settings field), our own name, our bottom buttons
    /// ("URAH releases" in the slot of Revert Defaults / Open Workshop, "Fetch plugins" in Apply Changes').
    /// </summary>
    public class PluginCategory : UIPreferences.Category
    {
        internal BSGButton fetch, releases;
        internal TMPro.TextMeshProUGUI status;
        private string shownStatus;
        private bool shownFailed;

        /// <summary>Our bottom buttons while this tab is selected (the game sets its own in its Update).</summary>
        private void Update()
        {
            if (fetch != null)
            {
                if (fetch.gameObject.activeSelf != m_Selected)
                {
                    fetch.gameObject.SetActive(m_Selected);
                }
                fetch.Enable(!Fetcher.Busy && !PluginsTab.Locked());
            }
            if (releases != null && releases.gameObject.activeSelf != m_Selected)
            {
                releases.gameObject.SetActive(m_Selected);
            }
            if (status != null)
            {
                if (status.gameObject.activeSelf != m_Selected)
                {
                    status.gameObject.SetActive(m_Selected);
                }
                string text = Fetcher.Status ?? Texts.Get("not_fetched");
                if (m_Selected && (text != shownStatus || Fetcher.Failed != shownFailed))
                {
                    shownStatus = text;
                    shownFailed = Fetcher.Failed;
                    UIText.SetText(status, text);
                    status.color = Defs.GetColor("Preferences", shownFailed ? "category_settings.color_disbaled" : "category_settings.color_label_normal");
                }
            }
        }

        public override void SetDefField(DT.Field def)
        {
            DT.Field preferences = Defs.GetDefField("Preferences");
            DT.Field look = preferences?.FindChild("mods")?.FindChild("settings") ?? preferences?.FindChild("category_settings");
            base.SetDefField(def);
            settings = look;
            Refresh();
        }

        /// <summary>The base reads the name from the def; ours has no texts. No sub categories.</summary>
        protected override void Refresh()
        {
            if (m_CategoryName != null)
            {
                UIText.SetText(m_CategoryName, Texts.Get("tab"));
            }
            if (m_CategoriesContainer != null)
            {
                m_CategoriesContainer.gameObject.SetActive(false);
            }
            Updatehighlight();
        }
    }

    /// <summary>The title line above the rows.</summary>
    public class StatusRow : UIPreferences.Preference
    {
        internal void Show(string text, bool failed)
        {
            Init();
            def = Defs.GetDefField("Preferences", "Preference");
            if (m_Label != null)
            {
                UIText.SetText(m_Label, text);
                m_Label.color = Defs.GetColor("Preferences", failed ? "category_settings.color_disbaled" : "category_settings.color_label_normal");
                m_Label.raycastTarget = false;
            }
        }

        public override void Populate()
        {
        }

        public override void RefreshEnabled()
        {
        }
    }

    /// <summary>
    /// A row of the Mods tab's prefab (id_Toggle, id_HitBox, id_InvalidMod, label) for one plugin, with
    /// square icon buttons (copies of the window's button) at its right edge: Install / Remove always
    /// rightmost, Update left of it (when the index has a newer version); the text is the tooltip.
    /// Remove only while the plugin is off, never for the loader and the manager.
    /// Everything is locked while a campaign runs, like ModPreference (Settings can not be opened from a
    /// lobby): this also keeps URAH_Net from changing mid-session.
    /// </summary>
    public class PluginRow : UIPreferences.Preference
    {
        private const float Gap = 6f;

        [UIFieldTarget("id_Toggle")]
        protected Toggle m_Toggle;

        [UIFieldTarget("id_HitBox")]
        protected BSGButton m_HitBox;

        [UIFieldTarget("id_InvalidMod")]
        protected GameObject m_InvalidMod;

        private Entry entry;
        private BSGButton action, update, settings;
        private float laidOutHeight = -1f;
        private bool spaced;

        internal void SetEntry(Entry e)
        {
            Init();
            entry = e;
            def = Defs.GetDefField("Preferences", "Preference");
            action = AddButton("URAH_Action", OnAction);
            update = AddButton("URAH_Update", b => PluginsTab.Install(entry));
            settings = AddTextButton("URAH_Settings", Texts.Get("config"), b => PluginsTab.ShowSettings(entry));
            Populate();
        }

        protected override void Init()
        {
            if (!m_Initialzied)
            {
                base.Init();
                if (m_Toggle != null)
                {
                    m_Toggle.onValueChanged.AddListener(OnToggle);
                }
                if (m_HitBox != null)
                {
                    m_HitBox.onClick = b => OnToggle(!entry.Enabled);
                }
            }
        }

        /// <summary>
        /// A copy of the window's button, square, anchored to the row's right edge (LateUpdate sizes and
        /// places it by slot, 0 rightmost). Its text goes; an icon in the text's color takes its place.
        /// </summary>
        private BSGButton AddButton(string name, BSGButton.OnClick onClick)
        {
            BSGButton template = PluginsTab.ButtonTemplate;
            if (template == null)
            {
                return null;
            }
            BSGButton b = Object.Instantiate(template, transform, worldPositionStays: false);
            b.name = name;
            b.onClick = onClick;
            LayoutElement element = b.gameObject.GetComponent<LayoutElement>();
            (element != null ? element : b.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            Color tint = Color.white;
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                tint = label.color;
                label.gameObject.SetActive(false);
            }
            var icon = new GameObject("URAH_Icon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)icon.transform;
            irt.SetParent(b.transform, worldPositionStays: false);
            irt.anchorMin = new Vector2(0.2f, 0.2f);
            irt.anchorMax = new Vector2(0.8f, 0.8f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            Image image = icon.GetComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;
            image.preserveAspect = true;
            b.gameObject.SetActive(true);
            return b;
        }

        /// <summary>A copy of the window's button that keeps its text, sized to it in LateUpdate.</summary>
        private BSGButton AddTextButton(string name, string text, BSGButton.OnClick onClick)
        {
            BSGButton template = PluginsTab.ButtonTemplate;
            if (template == null)
            {
                return null;
            }
            BSGButton b = Object.Instantiate(template, transform, worldPositionStays: false);
            b.name = name;
            b.onClick = onClick;
            LayoutElement element = b.gameObject.GetComponent<LayoutElement>();
            (element != null ? element : b.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                UIText.SetText(label, text);
            }
            b.gameObject.SetActive(true);
            return b;
        }

        private static void SetIcon(BSGButton b, Sprite sprite)
        {
            Transform icon = b != null ? b.transform.Find("URAH_Icon") : null;
            if (icon != null)
            {
                icon.GetComponent<Image>().sprite = sprite;
            }
        }

        /// <summary>
        /// Once the row has its size: square buttons as high as the row, Install / Remove rightmost, Update
        /// left of it. And the list spacing, so the rows' backgrounds (which reach beyond the row, also on
        /// the Mods tab) don't overlap here.
        /// </summary>
        private void LateUpdate()
        {
            var row = (RectTransform)transform;
            float h = row.rect.height;
            if (h <= 0f || Mathf.Approximately(h, laidOutHeight))
            {
                return;
            }
            laidOutHeight = h;
            float side = Mathf.Max(h - 4f, 8f);
            float margin = ToggleMargin(row);
            Place(action, 0, side, margin);
            Place(update, 1, side, margin);
            PlaceText(settings, (action != null && action.gameObject.activeSelf ? 1 : 0) + (update != null && update.gameObject.activeSelf ? 1 : 0), side, h, margin);
            if (!spaced)
            {
                spaced = true;
                PluginsTab.SpaceRows(Overhang(row));
            }
        }

        private static void Place(BSGButton b, int slot, float side, float margin)
        {
            if (b != null)
            {
                var rt = (RectTransform)b.transform;
                rt.sizeDelta = new Vector2(side, -4f);
                rt.anchoredPosition = new Vector2(-margin - slot * (side + Gap), 0f);
            }
        }

        /// <summary>Left of the first free icon slots, as wide as its text plus a margin.</summary>
        private static void PlaceText(BSGButton b, int slots, float side, float h, float margin)
        {
            if (b == null)
            {
                return;
            }
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
            float width = label != null ? label.GetPreferredValues(label.text, 1000f, h).x + h : side * 3f;
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(width, -4f);
            rt.anchoredPosition = new Vector2(-margin - slots * (side + Gap), 0f);
        }

        /// <summary>How far the toggle sits from the row's left edge: the buttons keep the same distance on the right.</summary>
        private float ToggleMargin(RectTransform row)
        {
            if (m_Toggle == null)
            {
                return 0f;
            }
            var corners = new Vector3[4];
            ((RectTransform)m_Toggle.transform).GetWorldCorners(corners);
            float left = row.InverseTransformPoint(corners[0]).x - row.rect.xMin;
            return Mathf.Max(left, 0f);
        }

        /// <summary>How far the row's own images reach beyond its rect, top and bottom together, in its units.</summary>
        private float Overhang(RectTransform row)
        {
            var corners = new Vector3[4];
            row.GetWorldCorners(corners);
            float top = corners[1].y, bottom = corners[0].y, rowTop = top, rowBottom = bottom;
            foreach (Image image in GetComponentsInChildren<Image>())
            {
                if ((action != null && image.transform.IsChildOf(action.transform)) || (update != null && image.transform.IsChildOf(update.transform))
                    || (settings != null && image.transform.IsChildOf(settings.transform)))
                {
                    continue;
                }
                image.rectTransform.GetWorldCorners(corners);
                top = Mathf.Max(top, corners[1].y);
                bottom = Mathf.Min(bottom, corners[0].y);
            }
            float scale = Mathf.Abs(row.lossyScale.y) > 1e-6f ? Mathf.Abs(row.lossyScale.y) : 1f;
            return ((top - rowTop) + (rowBottom - bottom)) / scale;
        }

        private static void SetTooltip(GameObject go, string text)
        {
            if (go == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                Tooltip.Get(go).Clear();
            }
            else
            {
                Tooltip.Get(go).SetText("#" + text);
            }
        }

        public override void Populate()
        {
            if (entry == null)
            {
                return;
            }
            if (m_InvalidMod != null)
            {
                m_InvalidMod.SetActive(false);
            }
            RefreshValues();
        }

        /// <summary>Why the toggle can't be used, or null.</summary>
        private string ToggleBlocked()
        {
            if (entry.UpdateOnly)
            {
                return Texts.Get(entry.Guid == Plugin.Guid ? "manager" : "loader");
            }
            if (PluginsTab.Locked())
            {
                return Texts.Get("locked");
            }
            return entry.CanToggle ? null : Texts.Get("restart");
        }

        /// <summary>Why Install / Remove / Update can't be used, or null.</summary>
        private string ActionBlocked(bool remove)
        {
            if (PluginsTab.Locked())
            {
                return Texts.Get("locked");
            }
            if (Fetcher.Busy)
            {
                return Texts.Get("busy");
            }
            if (remove && entry.UpdateOnly)
            {
                return Texts.Get(entry.Guid == Plugin.Guid ? "remove_manager" : "remove_loader");
            }
            if (remove && entry.Enabled)
            {
                return Texts.Get("remove_on"); // also after an update or a fresh install
            }
            return null;
        }

        public override void RefreshValues()
        {
            if (entry == null)
            {
                return;
            }
            string toggleBlocked = ToggleBlocked();
            if (m_Toggle != null)
            {
                m_Toggle.SetIsOnWithoutNotify(entry.Enabled && entry.Pending != Entry.Change.Removed);
                m_Toggle.interactable = toggleBlocked == null;
                SetTooltip(m_Toggle.gameObject, toggleBlocked);
            }
            if (m_Label != null)
            {
                Index.Record record = entry.Record;
                string text = entry.Name + (entry.Version != null ? " " + entry.Version : "");
                var notes = new List<string>();
                if (entry.Pending == Entry.Change.Removed)
                {
                    notes.Add(Texts.Get("removed_short"));
                }
                else if (!entry.OnDisk)
                {
                    notes.Add(Texts.Get("not_installed"));
                }
                if (entry.RestartRequired)
                {
                    notes.Add(Texts.Get("restart"));
                }
                if (record != null && !string.IsNullOrEmpty(Fetcher.GameBuild) && record.GameBuild != Fetcher.GameBuild)
                {
                    notes.Add(string.Format(Texts.Get("build"), record.GameBuild, Fetcher.GameBuild));
                }
                if (notes.Count > 0)
                {
                    text += " (" + string.Join(", ", notes.ToArray()) + ")";
                }
                UIText.SetText(m_Label, text);
                m_Label.color = Defs.GetColor("Preferences", entry.Enabled && entry.OnDisk ? "category_settings.color_label_normal" : "category_settings.color_disbaled");
                string desc = record?.Description(Texts.Lang);
                m_Label.raycastTarget = !string.IsNullOrEmpty(desc);
                SetTooltip(m_Label.gameObject, desc);
            }
            if (action != null)
            {
                bool install = !entry.OnDisk;
                bool show = install ? entry.CanInstall : true;
                action.gameObject.SetActive(show);
                SetIcon(action, install ? Icons.Install : Icons.Remove);
                string blocked = ActionBlocked(!install);
                action.Enable(blocked == null);
                SetTooltip(action.gameObject, blocked ?? Texts.Get(install ? "install" : "remove"));
            }
            if (settings != null)
            {
                settings.gameObject.SetActive(ConfigRows.Editable(entry).Count > 0);
                settings.Enable(!PluginsTab.Locked());
                SetTooltip(settings.gameObject, PluginsTab.Locked() ? Texts.Get("locked") : string.Format(Texts.Get("config_tip"), entry.Name));
                laidOutHeight = -1f; // the icon slots left of which it sits may have changed
            }
            if (update != null)
            {
                update.gameObject.SetActive(entry.UpdateAvailable);
                if (entry.UpdateAvailable)
                {
                    SetIcon(update, Icons.Update);
                    string blocked = ActionBlocked(false);
                    update.Enable(blocked == null);
                    SetTooltip(update.gameObject, blocked ?? string.Format(Texts.Get("update_tip"), entry.Record.Version));
                }
            }
        }

        /// <summary>No setting behind the row: nothing for the base's def checks.</summary>
        public override void RefreshEnabled()
        {
        }

        private void OnAction(BSGButton b)
        {
            if (entry == null || ActionBlocked(entry.OnDisk) != null)
            {
                return;
            }
            if (entry.OnDisk)
            {
                PluginsTab.Remove(entry);
            }
            else
            {
                PluginsTab.Install(entry);
            }
        }

        private void OnToggle(bool on)
        {
            if (entry != null && ToggleBlocked() == null)
            {
                entry.SetEnabled(on);
            }
            RefreshValues();
        }
    }
}
