using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace URAH_PluginManager
{
    /// <summary>
    /// Adds the tab "Plugins (URAH)" to the game's Settings window (PluginsTab), laid out like Settings →
    /// Mods: the standalone URAH plugins (Catalog), each with a toggle that flips its [General] Enabled
    /// config entry, and after "Fetch plugins" (Fetcher, the only network access, on a click) whatever the
    /// signed index of URAH_koh2_releases offers, to install, update and remove (Installer). Standalone
    /// plugins are not Workshop mods, so the game does not count as modded and achievements stay
    /// available; nothing here activates a mod, writes to the game folder's data or touches the
    /// achievement check. Plugins that come with a Workshop mod are never
    /// listed.
    ///
    /// Patching UIPreferences at load is safe: its only static field is a string constant.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "urah.koh2.pluginmanager";
        public const string Name = "URAH_PluginManager";
        public const string Version = URAH.BuildVersion.Value; // plugin/version.txt

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static Signature Keys;
        internal static ConfigEntry<int> LastSeq;
        internal static ConfigEntry<string> DevIndexUrl, DevDownloadUrl;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Installer.Cleanup(); // leftovers of the last session's updates: none of them is loaded
            LastSeq = Config.Bind("Index", "LastSeq", 0,
                "Highest index sequence number seen; an older index is rejected (so an old signed index can't be replayed)");
            DevIndexUrl = Config.Bind("Development", "IndexUrl", "",
                "Empty: the URAH_koh2_releases index. Else a base URL (also file:///) with index.txt and index.sig, for testing");
            DevDownloadUrl = Config.Bind("Development", "DownloadUrl", "",
                "Empty: the URAH_koh2_releases downloads. Else the base URL the index's zip paths are relative to, for testing");
            Keys = new Signature(Path.Combine(Path.Combine(Paths.ConfigPath, "urah.pluginmanager"), "trusted_keys"), Log);
            var harmony = new Harmony(Guid);
            foreach (Type patch in typeof(PluginsTab).GetNestedTypes(AccessTools.all))
            {
                if (patch.IsDefined(typeof(HarmonyPatch), inherit: false))
                {
                    Patch(harmony, patch);
                }
            }
            Log.LogInfo($"{Name} {Version} loaded");
        }

        /// <summary>Each patch on its own, so one that no longer fits the game does not take the others with it.</summary>
        private static void Patch(Harmony harmony, Type patch)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception e)
            {
                Log.LogError($"Patch {patch.Name} failed: {e}");
            }
        }
    }
}
