// Nessafy installer: newer versions of the theme, from Nessafy's GitHub releases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nessafy.Installer
{
    class LatestRelease
    {
        public string Version { get; set; }
        public Dictionary<string, string> Assets { get; set; }
    }

    static class Release
    {
        public const string Repo = "deargodx/nessafy";
        const string Checksums = "SHA256SUMS";

        // The theme's files. theme.js is optional.
        public static readonly string[] ThemeFiles = { "color.ini", "user.css", "theme.js" };

        /// The latest release. Its version is the last word of its name, as the release workflow writes it.
        public static LatestRelease Latest()
        {
            Dictionary<string, object> release = Http.GetJson("https://api.github.com/repos/" + Repo + "/releases/latest");
            var assets = new Dictionary<string, string>();
            foreach (Dictionary<string, object> asset in (System.Collections.ArrayList)release["assets"])
                assets[(string)asset["name"]] = (string)asset["browser_download_url"];
            string name = ((string)release["name"]).Trim();
            return new LatestRelease { Version = name.Substring(name.LastIndexOf(' ') + 1), Assets = assets };
        }

        /// The release's theme files, each checked against its SHA256SUMS. Throws if any doesn't match.
        public static Dictionary<string, byte[]> DownloadTheme(LatestRelease release, Action<string> log)
        {
            if (!release.Assets.ContainsKey(Checksums)) throw new InvalidDataException("The latest release has no " + Checksums + ", so its files can't be checked.");
            Dictionary<string, string> expected = ParseChecksums(Encoding.UTF8.GetString(Http.Get(release.Assets[Checksums])));

            var files = new Dictionary<string, byte[]>();
            foreach (string name in ThemeFiles.Where(release.Assets.ContainsKey))
            {
                log("Downloading " + name);
                byte[] data = Http.Get(release.Assets[name]);
                string hash;
                if (!expected.TryGetValue(name, out hash) || hash != Sha256(data))
                    throw new InvalidDataException(name + " doesn't match the release's checksum. Nothing was changed.");
                files[name] = data;
            }
            if (!files.ContainsKey("color.ini") || !files.ContainsKey("user.css"))
                throw new InvalidDataException("The latest release is missing the theme's files.");
            return files;
        }

        // "sha256  name" per line, as sha256sum writes them.
        static Dictionary<string, string> ParseChecksums(string text)
        {
            var sums = new Dictionary<string, string>();
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '*' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) sums[parts[1].Trim().TrimStart('*')] = parts[0].ToLowerInvariant();
            }
            return sums;
        }

        static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }
    }
}
