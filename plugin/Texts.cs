using System.Collections.Generic;

namespace URAH_PluginManager
{
    /// <summary>The manager's texts ship with it (no mod, no game text files): English and German, English as fallback.</summary>
    internal static class Texts
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["tab"] = "Plugins (URAH)",
            ["restart"] = "restart required",
            ["locked"] = "Plugins can only be changed in the main menu, not while a game is running.",
            ["loader"] = "Always on: loads the plugins that come with Workshop mods.",
            ["manager"] = "Always on: this list.",
            ["remove_loader"] = "To remove the loader, delete BepInEx/plugins/URAH_Loader/ (or BepInEx as a whole).",
            ["remove_manager"] = "To remove the plugin manager, delete BepInEx/plugins/URAH_PluginManager/.",
            ["remove_on"] = "Switch the plugin off first.",
            ["busy"] = "Wait until the download is finished.",
            ["fetch"] = "Fetch plugins",
            ["fetch_tip"] = "Loads the list of URAH plugins from GitHub (URAH_koh2_releases). Nothing is downloaded before you click.",
            ["releases"] = "URAH releases",
            ["install"] = "Install",
            ["remove"] = "Remove",
            ["update"] = "Update",
            ["update_tip"] = "Update to {0}",
            ["not_fetched"] = "Installed URAH plugins. \"Fetch plugins\" shows what else is available and updates.",
            ["fetching"] = "Fetching the plugin list...",
            ["fetched"] = "Plugin list {0}: {1} plugins.",
            ["fetch_failed"] = "Fetching the plugin list failed:",
            ["download_failed"] = "download failed (see BepInEx/LogOutput.log).",
            ["bad_signature"] = "the signature does not match, nothing is used.",
            ["old_index"] = "the list ({0}) is older than one seen before ({1}).",
            ["downloading"] = "Downloading {0} {1}...",
            ["installed"] = "{0} {1} installed, active after a restart.",
            ["install_failed"] = "Installing {0} failed:",
            ["removed"] = "{0} removed, gone after a restart.",
            ["remove_failed"] = "Removing {0} failed:",
            ["build"] = "built for game {0}, you run {1}",
            ["not_installed"] = "not installed",
            ["removed_short"] = "removed",
        };

        private static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            ["tab"] = "Plugins (URAH)",
            ["restart"] = "Neustart nötig",
            ["locked"] = "Plugins lassen sich nur im Hauptmenü ändern, nicht während ein Spiel läuft.",
            ["loader"] = "Immer an: lädt die Plugins, die mit Workshop-Mods kommen.",
            ["manager"] = "Immer an: diese Liste.",
            ["remove_loader"] = "Um den Loader zu entfernen, BepInEx/plugins/URAH_Loader/ löschen (oder BepInEx ganz).",
            ["remove_manager"] = "Um den Plugin-Manager zu entfernen, BepInEx/plugins/URAH_PluginManager/ löschen.",
            ["remove_on"] = "Zuerst das Plugin ausschalten.",
            ["busy"] = "Warten, bis der Download fertig ist.",
            ["fetch"] = "Plugins abrufen",
            ["fetch_tip"] = "Lädt die Liste der URAH-Plugins von GitHub (URAH_koh2_releases). Vor dem Klick wird nichts heruntergeladen.",
            ["releases"] = "URAH-Releases",
            ["install"] = "Installieren",
            ["remove"] = "Entfernen",
            ["update"] = "Update",
            ["update_tip"] = "Auf {0} aktualisieren",
            ["not_fetched"] = "Installierte URAH-Plugins. \"Plugins abrufen\" zeigt, was es sonst gibt, und Updates.",
            ["fetching"] = "Plugin-Liste wird abgerufen...",
            ["fetched"] = "Plugin-Liste {0}: {1} Plugins.",
            ["fetch_failed"] = "Abrufen der Plugin-Liste fehlgeschlagen:",
            ["download_failed"] = "Download fehlgeschlagen (siehe BepInEx/LogOutput.log).",
            ["bad_signature"] = "die Signatur passt nicht, nichts wird verwendet.",
            ["old_index"] = "die Liste ({0}) ist älter als eine schon gesehene ({1}).",
            ["downloading"] = "{0} {1} wird heruntergeladen...",
            ["installed"] = "{0} {1} installiert, aktiv nach einem Neustart.",
            ["install_failed"] = "Installieren von {0} fehlgeschlagen:",
            ["removed"] = "{0} entfernt, weg nach einem Neustart.",
            ["remove_failed"] = "Entfernen von {0} fehlgeschlagen:",
            ["build"] = "für Spielversion {0} gebaut, du hast {1}",
            ["not_installed"] = "nicht installiert",
            ["removed_short"] = "entfernt",
        };

        public static string Get(string key)
        {
            string lang = Defs.Language ?? "en";
            if (lang.StartsWith("de") && De.TryGetValue(key, out string de))
            {
                return de;
            }
            return En.TryGetValue(key, out string en) ? en : key;
        }

        /// <summary>"en" or "de", for the index's descriptions.</summary>
        public static string Lang => (Defs.Language ?? "en").StartsWith("de") ? "de" : "en";
    }
}
