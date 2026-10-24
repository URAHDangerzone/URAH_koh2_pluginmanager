using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_PluginManager
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The settings of a loaded plugin, below its row: bools as the Settings window's toggle row, ints and
    /// floats with an AcceptableValueRange as its slider row (a slider needs both ends; a number without a
    /// range is left to the cfg file). [General] Enabled is the plugin row's own toggle, strings and keys
    /// have no row type, the manager's own entries are bookkeeping. A change sets the ConfigEntry, which
    /// saves the cfg and fires SettingChanged: whether it acts at once is up to the plugin.
    /// </summary>
    internal static class ConfigRows
    {
        private const string Indent = "      ";

        private static readonly ConfigDefinition EnabledKey = new ConfigDefinition("General", "Enabled");

        /// <summary>The entries that get a row, in the cfg's order.</summary>
        internal static List<ConfigEntryBase> Editable(Entry entry)
        {
            var list = new List<ConfigEntryBase>();
            if (!entry.Loaded || entry.Guid == Plugin.Guid)
            {
                return list;
            }
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in entry.Info.Instance.Config)
            {
                ConfigEntryBase e = kv.Value;
                if (kv.Key.Equals(EnabledKey))
                {
                    continue;
                }
                if (e.SettingType == typeof(bool) || (IsNumber(e.SettingType) && e.Description?.AcceptableValues is AcceptableValueBase range && Range(range, out _, out _)))
                {
                    list.Add(e);
                }
            }
            return list;
        }

        internal static bool IsNumber(Type t) => t == typeof(int) || t == typeof(float);

        /// <summary>The ends of an AcceptableValueRange&lt;int or float&gt;.</summary>
        internal static bool Range(AcceptableValueBase values, out float min, out float max)
        {
            min = max = 0f;
            Type t = values.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
            {
                return false;
            }
            min = Convert.ToSingle(t.GetProperty("MinValue").GetValue(values, null), CultureInfo.InvariantCulture);
            max = Convert.ToSingle(t.GetProperty("MaxValue").GetValue(values, null), CultureInfo.InvariantCulture);
            return max > min;
        }

        /// <summary>"HostOnlyUnpause" → "Host only unpause", indented under the plugin.</summary>
        internal static string Label(ConfigEntryBase e)
        {
            string key = e.Definition.Key;
            var sb = new StringBuilder(Indent);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(key[i - 1]))
                {
                    sb.Append(' ').Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        internal static string Tooltip(ConfigEntryBase e)
        {
            string desc = e.Description?.Description;
            string where = $"[{e.Definition.Section}] {e.Definition.Key} (default {Format(e, e.DefaultValue)})";
            return string.IsNullOrEmpty(desc) ? where : desc + "\n\n" + where;
        }

        internal static string Format(ConfigEntryBase e, object value)
        {
            if (value is float f)
            {
                return f.ToString("0.##", CultureInfo.InvariantCulture);
            }
            if (value is bool b)
            {
                return b ? "on" : "off";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        internal static void SetTooltip(GameObject go, string text)
        {
            if (go != null)
            {
                global::Tooltip.Get(go).SetText("#" + text);
            }
        }
    }

    /// <summary>A setting row: no game setting behind it, so Apply Changes and Revert Defaults ignore it.</summary>
    public abstract class ConfigRow : UIPreferences.Preference
    {
        internal ConfigEntryBase entry;

        internal void SetEntry(ConfigEntryBase e)
        {
            Init();
            entry = e;
            def = Defs.GetDefField("Preferences", "Preference");
            Populate();
        }

        public override void Populate()
        {
            if (entry == null)
            {
                return;
            }
            if (m_Label != null)
            {
                UIText.SetText(m_Label, ConfigRows.Label(entry));
                m_Label.color = Defs.GetColor("Preferences", "category_settings.color_label_normal");
                m_Label.raycastTarget = true;
                ConfigRows.SetTooltip(m_Label.gameObject, ConfigRows.Tooltip(entry));
            }
            RefreshValues();
        }

        /// <summary>Locked while a campaign runs, like the plugin rows.</summary>
        protected static bool Locked() => PluginsTab.Locked();

        public override void RefreshEnabled()
        {
        }
    }

    public class ConfigToggleRow : ConfigRow
    {
        [UIFieldTarget("id_Toggle")]
        protected Toggle m_Toggle;

        protected override void Init()
        {
            if (!m_Initialzied)
            {
                base.Init();
                if (m_Toggle != null)
                {
                    m_Toggle.onValueChanged.AddListener(OnToggle);
                }
            }
        }

        public override void RefreshValues()
        {
            if (entry == null || m_Toggle == null)
            {
                return;
            }
            m_Toggle.SetIsOnWithoutNotify((bool)entry.BoxedValue);
            m_Toggle.interactable = !Locked();
            if (Locked())
            {
                ConfigRows.SetTooltip(m_Toggle.gameObject, Texts.Get("locked"));
            }
            else
            {
                global::Tooltip.Get(m_Toggle.gameObject).Clear();
            }
        }

        private void OnToggle(bool on)
        {
            if (entry != null && !Locked() && (bool)entry.BoxedValue != on)
            {
                entry.BoxedValue = on; // saves the cfg (SaveOnConfigSet) and fires SettingChanged
                Plugin.Log.LogInfo($"{entry.ConfigFile.ConfigFilePath}: [{entry.Definition.Section}] {entry.Definition.Key} = {on}");
            }
            RefreshValues();
        }
    }

    public class ConfigSliderRow : ConfigRow
    {
        [UIFieldTarget("id_Slider")]
        protected Slider m_Slider;

        [UIFieldTarget("id_ValueLabel")]
        protected TMPro.TextMeshProUGUI m_ValueLabel;

        public override void Populate()
        {
            if (entry != null && m_Slider != null)
            {
                ConfigRows.Range(entry.Description.AcceptableValues, out float min, out float max);
                m_Slider.onValueChanged.RemoveListener(OnSlide);
                m_Slider.minValue = min;
                m_Slider.maxValue = max;
                m_Slider.wholeNumbers = entry.SettingType == typeof(int);
                m_Slider.onValueChanged.AddListener(OnSlide);
            }
            base.Populate();
        }

        public override void RefreshValues()
        {
            if (entry == null)
            {
                return;
            }
            if (m_Slider != null)
            {
                m_Slider.SetValueWithoutNotify(Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture));
                m_Slider.interactable = !Locked();
                if (Locked())
                {
                    ConfigRows.SetTooltip(m_Slider.gameObject, Texts.Get("locked"));
                }
                else
                {
                    global::Tooltip.Get(m_Slider.gameObject).Clear();
                }
            }
            if (m_ValueLabel != null)
            {
                UIText.SetText(m_ValueLabel, ConfigRows.Format(entry, entry.BoxedValue));
            }
        }

        private void OnSlide(float v)
        {
            if (entry == null || Locked())
            {
                RefreshValues();
                return;
            }
            object value = entry.SettingType == typeof(int) ? (object)Mathf.RoundToInt(v) : (float)Math.Round(v, 2);
            if (!value.Equals(entry.BoxedValue))
            {
                entry.BoxedValue = value; // clamped to the range, saved, SettingChanged
            }
            RefreshValues();
        }
    }
}
