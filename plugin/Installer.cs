using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;

namespace URAH_PluginManager
{
    /// <summary>
    /// Install, update and remove in BepInEx/plugins/URAH_&lt;Name&gt;/, the same folder as a manual install.
    ///
    /// A zip is a release asset of URAH_koh2_releases: BepInEx/plugins/&lt;name&gt;/... plus SHA256SUMS
    /// (sha256sum format over its files) and SHA256SUMS.sig. The signed index carries the SHA-256 of that
    /// SHA256SUMS, so: SHA256SUMS must match the index, every other entry must be listed in it with its
    /// hash and lie under BepInEx/plugins/&lt;name&gt;/. Anything else (another path, an absolute path, "..")
    /// fails the whole install before a byte is written: nothing unverified lands in BepInEx/plugins.
    ///
    /// Loaded DLLs can't be unloaded, and on Windows a mapped file can't be deleted or overwritten, only
    /// renamed: BepInEx loads plugins with Assembly.LoadFile, and Unity's Mono (do_mono_image_open in
    /// mono/metadata/image.c) opens the file with _wfopen, maps it and closes it again, so only a mapping
    /// stays and no share mode blocks a rename. So a file is replaced by writing &lt;file&gt;.new, renaming
    /// the old &lt;file&gt; to &lt;file&gt;.old and &lt;file&gt;.new to &lt;file&gt;.
    /// BepInEx only loads *.dll, the next start picks up the new one and Cleanup deletes the leftovers.
    /// Never an extension that starts with "dll" (Windows patterns let *.dll match .dllnew).
    /// </summary>
    internal static class Installer
    {
        public const string Sums = "SHA256SUMS";

        /// <summary>The verified files of a zip: path below the plugin folder, contents.</summary>
        public static List<KeyValuePair<string, byte[]>> Verify(byte[] zip, Index.Record record)
        {
            string prefix = $"BepInEx/plugins/{record.Name}/";
            var files = new List<KeyValuePair<string, byte[]>>();
            Dictionary<string, string> sums = null;
            var entries = new List<KeyValuePair<string, byte[]>>();
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/"))
                    {
                        continue; // a folder entry
                    }
                    byte[] data = Read(entry);
                    if (name == Sums)
                    {
                        if (Sha256(data) != record.SumsSha256)
                        {
                            throw new InvalidDataException($"{Sums} does not match the signed index");
                        }
                        sums = ParseSums(data);
                    }
                    else if (name != Sums + ".sig")
                    {
                        entries.Add(new KeyValuePair<string, byte[]>(name, data));
                    }
                }
            }
            if (sums == null)
            {
                throw new InvalidDataException($"no {Sums} in the zip");
            }
            foreach (KeyValuePair<string, byte[]> e in entries)
            {
                string name = e.Key;
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || !SafePath(name.Substring(prefix.Length)))
                {
                    throw new InvalidDataException($"{name}: outside {prefix}");
                }
                if (!sums.TryGetValue(name, out string want) || Sha256(e.Value) != want)
                {
                    throw new InvalidDataException($"{name}: not in {Sums} or wrong hash");
                }
                files.Add(new KeyValuePair<string, byte[]>(name.Substring(prefix.Length), e.Value));
            }
            if (!files.Exists(f => f.Key == record.Name + ".dll"))
            {
                throw new InvalidDataException($"no {record.Name}.dll in the zip");
            }
            return files;
        }

        /// <summary>Relative, no "..", no drive or root, no odd characters, never ending in .old/.new.</summary>
        private static bool SafePath(string rel)
        {
            if (rel.Length == 0 || rel.StartsWith("/") || rel.Contains(":") || rel.EndsWith(".old") || rel.EndsWith(".new"))
            {
                return false;
            }
            foreach (string part in rel.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    return false;
                }
                foreach (char c in part)
                {
                    if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '_' && c != '-')
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static Dictionary<string, string> ParseSums(byte[] data)
        {
            var sums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in System.Text.Encoding.UTF8.GetString(data).Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }
                int sep = line.IndexOf("  ", StringComparison.Ordinal);
                if (sep != 64)
                {
                    throw new InvalidDataException($"{Sums}: bad line {line}");
                }
                sums[line.Substring(66)] = line.Substring(0, 64);
            }
            return sums;
        }

        private static byte[] Read(ZipArchiveEntry entry)
        {
            using (Stream s = entry.Open())
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return m.ToArray();
            }
        }

        public static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string Folder(string name) => Path.Combine(Paths.PluginPath, name);

        /// <summary>Writes the verified files: all as .new first, then each swapped in.</summary>
        public static void Install(string name, List<KeyValuePair<string, byte[]>> files)
        {
            string dir = Folder(name);
            var written = new List<string>();
            try
            {
                foreach (KeyValuePair<string, byte[]> f in files)
                {
                    string target = Path.Combine(dir, f.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target + ".new", f.Value);
                    written.Add(target);
                }
            }
            catch
            {
                foreach (string target in written)
                {
                    TryDelete(target + ".new");
                }
                throw;
            }
            foreach (string target in written)
            {
                Swap(target);
            }
        }

        /// <summary>
        /// &lt;target&gt;.new becomes &lt;target&gt;. A loaded &lt;target&gt; is renamed to .old first. If a .old is
        /// already there and can't go (it is the DLL loaded at start, renamed by an earlier update in this
        /// session), the current &lt;target&gt; is not loaded and is simply deleted.
        /// </summary>
        private static void Swap(string target)
        {
            string old = target + ".old";
            bool moved = false;
            if (File.Exists(target))
            {
                TryDelete(old);
                if (File.Exists(old))
                {
                    Retry(() => File.Delete(target));
                }
                else
                {
                    Retry(() => File.Move(target, old));
                    moved = true;
                }
            }
            try
            {
                Retry(() => File.Move(target + ".new", target));
            }
            catch
            {
                if (moved && !File.Exists(target))
                {
                    TryMove(old, target); // put the old one back
                }
                throw;
            }
        }

        /// <summary>Every file of the plugin out of the way: DLLs to .old (they may be loaded), the rest deleted.</summary>
        public static void Remove(string name)
        {
            string dir = Folder(name);
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".old"))
                {
                    continue;
                }
                if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string old = file + ".old";
                    TryDelete(old);
                    if (File.Exists(old))
                    {
                        Retry(() => File.Delete(file));
                    }
                    else
                    {
                        Retry(() => File.Move(file, old));
                    }
                }
                else
                {
                    Retry(() => File.Delete(file));
                }
            }
        }

        /// <summary>At start, before anything else: leftovers of updates and removals, then empty URAH_ folders.</summary>
        public static void Cleanup()
        {
            if (!Directory.Exists(Paths.PluginPath))
            {
                return;
            }
            foreach (string dir in Directory.GetDirectories(Paths.PluginPath, "URAH_*"))
            {
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".old") || file.EndsWith(".new"))
                        {
                            File.Delete(file);
                            Plugin.Log.LogInfo($"Deleted {file}");
                        }
                    }
                    DeleteEmpty(dir);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Cleanup of {dir}: {e.Message}");
                }
            }
        }

        private static void DeleteEmpty(string dir)
        {
            foreach (string sub in Directory.GetDirectories(dir))
            {
                DeleteEmpty(sub);
            }
            if (Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
                Plugin.Log.LogInfo($"Deleted empty {dir}");
            }
        }

        /// <summary>A virus scanner can hold a freshly written file for a moment.</summary>
        private static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(200);
                }
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // still mapped: stays until the next start
            }
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                File.Move(from, to);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not put {from} back: {e.Message}");
            }
        }
    }
}
