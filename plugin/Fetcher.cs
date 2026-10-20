using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace URAH_PluginManager
{
    /// <summary>
    /// Everything that touches the network, and only after a click: "Fetch plugins" loads index.txt and
    /// index.sig from the master branch of URAH_koh2_releases, Install / Update load a release zip.
    /// UnityWebRequest (old Mono HttpWebRequest often fails on TLS 1.2) follows GitHub's redirect to its
    /// asset host. The zip location comes from the index relative to the releases download URL built in
    /// here, so the index can't point anywhere else. [Development] URLs in the cfg replace both bases for
    /// testing (file:///... works): writing there needs the same access as BepInEx/plugins.
    /// </summary>
    internal static class Fetcher
    {
        public const string IndexBase = "https://raw.githubusercontent.com/URAHDangerzone/URAH_koh2_releases/master/";
        public const string DownloadBase = "https://github.com/URAHDangerzone/URAH_koh2_releases/releases/download/";

        public static Index Current;
        public static string Status;
        public static bool Failed;
        public static bool Busy;

        /// <summary>The game's build: first field of StreamingAssets/version.txt, like tools/make_refs.sh writes into refs/README.txt.</summary>
        public static string GameBuild
        {
            get
            {
                if (gameBuild == null)
                {
                    try
                    {
                        string text = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "version.txt")).Trim();
                        gameBuild = text.Split(' ')[0];
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Game build unknown: {e.Message}");
                        gameBuild = "";
                    }
                }
                return gameBuild;
            }
        }

        private static string gameBuild;

        private static string Base(string dev, string release)
        {
            if (string.IsNullOrEmpty(dev))
            {
                return release;
            }
            return dev.EndsWith("/") ? dev : dev + "/";
        }

        public static IEnumerator Fetch(Action done)
        {
            Busy = true;
            Status = Texts.Get("fetching");
            Failed = false;
            done();
            string root = Base(Plugin.DevIndexUrl.Value, IndexBase);
            var result = new byte[2][];
            yield return Get(root + "index.txt", b => result[0] = b);
            if (result[0] != null)
            {
                yield return Get(root + "index.sig", b => result[1] = b);
            }
            try
            {
                if (result[0] == null || result[1] == null)
                {
                    throw new IOException(Texts.Get("download_failed"));
                }
                if (!Plugin.Keys.Verify(result[0], result[1]))
                {
                    throw new InvalidDataException(Texts.Get("bad_signature"));
                }
                Index index = Index.Parse(result[0]);
                if (index.Seq < Plugin.LastSeq.Value)
                {
                    throw new InvalidDataException(string.Format(Texts.Get("old_index"), index.Seq, Plugin.LastSeq.Value));
                }
                Plugin.LastSeq.Value = (int)Math.Min(index.Seq, int.MaxValue);
                if (index.FileFormat > Index.Format)
                {
                    // newer format: only the manager itself may update, to learn it
                    index.Plugins.RemoveAll(r => r.Guid != Plugin.Guid);
                    Plugin.Log.LogWarning($"Index format {index.FileFormat}, this manager reads {Index.Format}: only its own update offered");
                }
                Current = index;
                Catalog.SetIndex(index);
                Status = string.Format(Texts.Get("fetched"), index.Seq, index.Plugins.Count);
                Plugin.Log.LogInfo($"Index {index.Seq} fetched from {root}, {index.Plugins.Count} plugins");
            }
            catch (Exception e)
            {
                Status = Texts.Get("fetch_failed") + " " + e.Message;
                Failed = true;
                Plugin.Log.LogError($"Fetch from {root} failed: {e}");
            }
            Busy = false;
            done();
        }

        public static IEnumerator Install(Entry entry, Action done)
        {
            Index.Record record = entry.Record;
            if (record == null)
            {
                yield break;
            }
            Busy = true;
            Failed = false;
            Status = string.Format(Texts.Get("downloading"), record.Name, record.Version);
            done();
            string url = Base(Plugin.DevDownloadUrl.Value, DownloadBase) + record.ZipPath;
            bool fresh = !entry.OnDisk; // not an update
            byte[] zip = null;
            yield return Get(url, b => zip = b);
            try
            {
                if (zip == null)
                {
                    throw new IOException(Texts.Get("download_failed"));
                }
                List<KeyValuePair<string, byte[]>> files = Installer.Verify(zip, record);
                Installer.Install(record.Name, files);
                entry.Pending = Entry.Change.Installed;
                entry.PendingVersion = record.Version;
                if (fresh)
                {
                    entry.SetEnabled(true); // a new plugin starts switched on, also after an earlier removal
                }
                Status = string.Format(Texts.Get("installed"), record.Name, record.Version);
                Plugin.Log.LogInfo($"{record.Name} {record.Version} installed from {url} ({files.Count} files), active after a restart");
            }
            catch (Exception e)
            {
                Status = string.Format(Texts.Get("install_failed"), record.Name) + " " + e.Message;
                Failed = true;
                Plugin.Log.LogError($"Install of {record.Name} from {url} failed: {e}");
            }
            Busy = false;
            done();
        }

        public static void Remove(Entry entry)
        {
            Failed = false;
            try
            {
                Installer.Remove(entry.Name);
                entry.Pending = Entry.Change.Removed;
                entry.PendingVersion = null;
                Status = string.Format(Texts.Get("removed"), entry.Name);
                Plugin.Log.LogInfo($"{entry.Name} removed, gone after a restart");
            }
            catch (Exception e)
            {
                Status = string.Format(Texts.Get("remove_failed"), entry.Name) + " " + e.Message;
                Failed = true;
                Plugin.Log.LogError($"Remove of {entry.Name} failed: {e}");
            }
        }

        private static IEnumerator Get(string url, Action<byte[]> result)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError)
                {
                    Plugin.Log.LogWarning($"{url}: {req.error}");
                    result(null);
                }
                else
                {
                    result(req.downloadHandler.data);
                }
            }
        }
    }
}
