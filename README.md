# URAH_PluginManager

A BepInEx 5 plugin for **Knights of Honor II: Sovereign** that adds the tab **Plugins (URAH)** to the
game's Settings window, laid out like Settings → Mods. It lists the standalone URAH plugins (URAH_Net,
URAH_Hover, URAH_Loader and itself), switches them on and off without a restart, and installs, updates
and removes them. Needs no mod and works in vanilla; achievements stay available.

- The toggle flips the plugin's `[General] Enabled` entry in its `BepInEx/config/<guid>.cfg` and takes
  effect at once. A plugin version without that entry shows "restart required".
- **Fetch plugins** loads the list of URAH plugins from
  [URAH_koh2_releases](https://github.com/URAHDangerzone/URAH_koh2_releases) (`index.txt`, signed with
  the URAH release key). **This is the only time the plugin contacts the network (GitHub), and only
  when you click it.** Downloads come from that repo's releases and are checked against the signed
  list before anything is written to `BepInEx/plugins/`.
- Install, update and remove act on `BepInEx/plugins/URAH_<Name>/`, the same folder as a manual
  install, and take effect after a restart (a loaded DLL is renamed to `.old` and deleted at the next
  start).
- Only in the main menu: while a game runs, everything is locked.
- URAH_Loader and the manager itself have no toggle and can't be removed here.
- Plugins that come with a Workshop mod (HRE, MapKit, RoyalWedding) are never listed; URAH_Loader
  handles them.

## Install

BepInEx 5 in the game folder, then `URAH_PluginManager.dll` into `BepInEx/plugins/URAH_PluginManager/`.

## Releases

Built and signed releases are in [URAH_koh2_releases](https://github.com/URAHDangerzone/URAH_koh2_releases/releases?q=URAH_PluginManager&expanded=true): [`URAH_PluginManager-v<version>`](https://github.com/URAHDangerzone/URAH_koh2_releases/releases?q=URAH_PluginManager&expanded=true) is this plugin alone
(`BepInEx/plugins/URAH_PluginManager/`), [`URAH_Starter`](https://github.com/URAHDangerzone/URAH_koh2_releases/releases?q=URAH_Starter&expanded=true) bundles BepInEx 5 with URAH_Loader, URAH_Net and URAH_PluginManager.
Every release is signed with the URAH release key; [URAH_koh2_releases](https://github.com/URAHDangerzone/URAH_koh2_releases) explains how to check it.
The plugin updates itself from there (Settings → Plugins (URAH) → "Fetch plugins").

## Building

```sh
git clone --recursive <this repo>      # external/URAH_shared is a submodule
cd plugin && dotnet build -c Release -p:Deploy=true
```

The plugin compiles against your own game install with BepInEx 5 (`GameDir`, default: Steam on Linux).
