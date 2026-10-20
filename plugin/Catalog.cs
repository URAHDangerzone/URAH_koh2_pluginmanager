using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace URAH_PluginManager
{
    /// <summary>
    /// One standalone URAH plugin: what BepInEx loaded at start (Chainloader.PluginInfos, no registry of
    /// its own), what the fetched index offers, and what changed on disk since (needs a restart).
    /// Toggling flips the plugin's [General] Enabled entry; the plugin patches and unpatches itself on
    /// SettingChanged. A version without that entry still runs; the manager then writes the entry to its
    /// cfg, which takes effect at the next start.
    /// </summary>
    internal sealed class Entry
    {
        private static readonly ConfigDefinition EnabledKey = new ConfigDefinition("General", "Enabled");

        /// <summary>Entries the manager wrote itself, with the value the plugin is running with (on: it had no switch).</summary>
        private static readonly Dictionary<string, bool> written = new Dictionary<string, bool>();

        public enum Change
        {
            None,
            Installed,
            Removed,
        }

        public readonly string Guid;
        public readonly bool KnownUpdateOnly;
        public PluginInfo Info;
        public Index.Record Record;
        public Change Pending;
        public Version PendingVersion;

        public Entry(string guid, bool updateOnly)
        {
            Guid = guid;
            KnownUpdateOnly = updateOnly;
        }

        public string Name => Info?.Metadata.Name ?? Record?.Name ?? Guid;
        public bool UpdateOnly => Record?.UpdateOnly ?? KnownUpdateOnly;
        public bool Loaded => Info?.Instance != null;

        /// <summary>The version on disk: the loaded one, or what was installed since.</summary>
        public Version Version => Pending == Change.Installed ? PendingVersion : Pending == Change.Removed ? null : Info?.Metadata.Version;

        public bool OnDisk => Version != null;

        public bool UpdateAvailable => Record != null && OnDisk && Record.Version > Version;

        public bool CanInstall => Record != null && !OnDisk;

        /// <summary>The cfg of a plugin installed in this session and not loaded: what its next start reads.</summary>
        private ConfigFile pendingConfig;

        /// <summary>The loaded plugin's config, else for a fresh install its cfg file, else null.</summary>
        private ConfigFile PluginConfig
        {
            get
            {
                if (Loaded)
                {
                    return Info.Instance.Config;
                }
                if (Pending != Change.Installed)
                {
                    return null;
                }
                return pendingConfig ?? (pendingConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, Guid + ".cfg"), saveOnInit: false));
            }
        }

        private ConfigEntry<bool> EnabledEntry()
        {
            ConfigFile config = PluginConfig;
            if (config == null)
            {
                return null;
            }
            if (!Loaded)
            {
                // not running: bind it, which reads a value left in the file (e.g. off before a removal)
                return config.Bind(EnabledKey, true, new ConfigDescription("Switch the plugin on and off (takes effect right away)"));
            }
            config.TryGetEntry(EnabledKey, out ConfigEntry<bool> entry);
            return entry;
        }

        /// <summary>What the toggle shows: the entry (for a fresh install the one its next start reads), or on for a plugin without one.</summary>
        public bool Enabled => UpdateOnly || (Pending != Change.Removed && PluginConfig != null && (EnabledEntry()?.Value ?? true));

        /// <summary>The toggle works on the plugin that runs now, or on the cfg of a fresh install.</summary>
        public bool CanToggle => !UpdateOnly && Pending != Change.Removed && PluginConfig != null;

        public bool RestartRequired => Pending != Change.None || (written.TryGetValue(Guid, out bool running) && Enabled != running);

        public void SetEnabled(bool on)
        {
            if (!CanToggle || on == Enabled)
            {
                return;
            }
            ConfigEntry<bool> entry = EnabledEntry();
            if (entry == null)
            {
                // an old version: create the entry in its cfg for the next start
                entry = Info.Instance.Config.Bind(EnabledKey, true,
                    new ConfigDescription("Switch the plugin on and off (written by URAH_PluginManager)"));
                if (!written.ContainsKey(Guid))
                {
                    written[Guid] = true;
                }
            }
            entry.Value = on; // saves the cfg (SaveOnConfigSet) and fires SettingChanged
            Plugin.Log.LogInfo($"{Name} {(on ? "enabled" : "disabled")}{(RestartRequired ? ", takes effect after a restart" : "")}");
        }
    }

    /// <summary>The list the tab shows: the known standalone plugins, then whatever else the index offers.</summary>
    internal static class Catalog
    {
        /// <summary>
        /// The standalone plugins, in list order, until an index is fetched. Plugins of Workshop mods (HRE,
        /// MapKit, ...) are never listed, even when a dev build of one sits in BepInEx/plugins.
        /// true: update only, no toggle.
        /// </summary>
        private static readonly KeyValuePair<string, bool>[] Known =
        {
            new KeyValuePair<string, bool>("urah.koh2.net", false),
            new KeyValuePair<string, bool>("urah.koh2.hover", false),
            new KeyValuePair<string, bool>("urah.koh2.questlinks", false),
            new KeyValuePair<string, bool>("urah.koh2.loader", true),
            new KeyValuePair<string, bool>(Plugin.Guid, true),
        };

        private static List<Entry> entries;

        public static List<Entry> Entries
        {
            get
            {
                if (entries == null)
                {
                    entries = new List<Entry>();
                    foreach (KeyValuePair<string, bool> known in Known)
                    {
                        var e = new Entry(known.Key, known.Value);
                        if (Chainloader.PluginInfos.TryGetValue(known.Key, out PluginInfo info) && info?.Instance != null && InPlugins(info))
                        {
                            e.Info = info;
                        }
                        entries.Add(e);
                    }
                }
                return entries;
            }
        }

        /// <summary>What the tab lists: installed or offered.</summary>
        public static IEnumerable<Entry> Shown()
        {
            foreach (Entry e in Entries)
            {
                if (e.Info != null || e.Record != null || e.Pending != Entry.Change.None)
                {
                    yield return e;
                }
            }
        }

        public static void SetIndex(Index index)
        {
            foreach (Entry e in Entries)
            {
                e.Record = null;
            }
            foreach (Index.Record r in index.Plugins)
            {
                Entry e = Entries.Find(x => x.Guid == r.Guid);
                if (e == null)
                {
                    e = new Entry(r.Guid, r.UpdateOnly);
                    Entries.Add(e);
                }
                if (e.Info != null && e.Info.Metadata.Name != r.Name)
                {
                    Plugin.Log.LogWarning($"Index names {r.Guid} {r.Name}, installed as {e.Info.Metadata.Name}: not offered");
                    continue;
                }
                e.Record = r;
            }
        }

        /// <summary>Loaded from BepInEx/plugins, not by URAH_Loader from a mod (no Location).</summary>
        private static bool InPlugins(PluginInfo info)
        {
            if (string.IsNullOrEmpty(info.Location))
            {
                return false;
            }
            string dir = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(info.Location).StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }
    }
}
