using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace URAH_PluginManager
{
    /// <summary>
    /// index.txt of URAH_koh2_releases (PLUGIN_MANAGER_PLAN.md, "Index"), signed by index.sig. UTF-8, LF,
    /// one record per line:
    ///   format 1
    ///   seq &lt;n&gt;
    ///   plugin &lt;guid&gt; &lt;name&gt; &lt;version&gt; &lt;game build&gt; &lt;toggle|update-only&gt; &lt;zip path&gt; sha256:&lt;hex of the zip's SHA256SUMS&gt;
    ///   desc &lt;guid&gt; &lt;en|de&gt; &lt;text to end of line&gt;
    /// Unknown record types are skipped. Every field is checked against a strict pattern: the index is
    /// signed, but a parser that accepts only what the release tool writes leaves nothing to guess.
    /// </summary>
    internal sealed class Index
    {
        public const int Format = 1;

        internal sealed class Record
        {
            public string Guid, Name, GameBuild, ZipPath, SumsSha256;
            public Version Version;
            public bool UpdateOnly;
            public readonly Dictionary<string, string> Desc = new Dictionary<string, string>();

            public string Description(string lang)
            {
                if (lang != null && Desc.TryGetValue(lang, out string text))
                {
                    return text;
                }
                return Desc.TryGetValue("en", out string en) ? en : null;
            }
        }

        private static readonly Regex GuidRx = new Regex(@"^urah\.[a-z0-9]+(\.[a-z0-9]+)*$");
        private static readonly Regex NameRx = new Regex(@"^URAH_[A-Za-z0-9]+$");
        private static readonly Regex BuildRx = new Regex(@"^[0-9]{1,9}$");
        private static readonly Regex PathRx = new Regex(@"^[A-Za-z0-9_][A-Za-z0-9._-]*/[A-Za-z0-9_][A-Za-z0-9._-]*\.zip$");
        private static readonly Regex HashRx = new Regex(@"^sha256:([0-9a-f]{64})$");

        public int FileFormat;
        public long Seq = -1;
        public readonly List<Record> Plugins = new List<Record>();

        public Record Find(string guid) => Plugins.Find(r => r.Guid == guid);

        /// <summary>Throws FormatException with the line on anything it does not accept.</summary>
        public static Index Parse(byte[] data)
        {
            var index = new Index();
            string text = new UTF8Encoding(false, true).GetString(data);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }
                string[] f = line.Split(' ');
                switch (f[0])
                {
                    case "format":
                        Need(f.Length == 2 && int.TryParse(f[1], out index.FileFormat) && index.FileFormat > 0, i, line);
                        break;
                    case "seq":
                        Need(f.Length == 2 && long.TryParse(f[1], out index.Seq) && index.Seq >= 0, i, line);
                        break;
                    case "plugin":
                        Record p = Plugin(f, i, line);
                        Need(index.Find(p.Guid) == null, i, line);
                        index.Plugins.Add(p);
                        break;
                    case "desc":
                        Need(f.Length >= 4 && (f[2] == "en" || f[2] == "de"), i, line);
                        Record r = index.Find(f[1]);
                        Need(r != null, i, line); // after its plugin line
                        r.Desc[f[2]] = line.Substring(f[0].Length + f[1].Length + f[2].Length + 3);
                        break;
                }
                Need(index.FileFormat > 0, i, line); // format comes first
            }
            if (index.Seq < 0)
            {
                throw new FormatException("no seq");
            }
            return index;
        }

        private static Record Plugin(string[] f, int i, string line)
        {
            Need(f.Length == 8, i, line);
            Match hash = HashRx.Match(f[7]);
            Need(GuidRx.IsMatch(f[1]) && NameRx.IsMatch(f[2]) && BuildRx.IsMatch(f[4]) && (f[5] == "toggle" || f[5] == "update-only")
                && PathRx.IsMatch(f[6]) && !f[6].Contains("..") && hash.Success, i, line);
            Need(System.Version.TryParse(f[3], out Version version), i, line);
            return new Record
            {
                Guid = f[1],
                Name = f[2],
                Version = version,
                GameBuild = f[4],
                UpdateOnly = f[5] == "update-only",
                ZipPath = f[6],
                SumsSha256 = hash.Groups[1].Value,
            };
        }

        private static void Need(bool ok, int i, string line)
        {
            if (!ok)
            {
                throw new FormatException($"line {i + 1}: {line}");
            }
        }
    }
}
