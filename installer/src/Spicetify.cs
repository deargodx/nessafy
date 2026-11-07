// Nessafy installer: Spicetify, the tool that themes Spotify. Found, installed if missing, and run.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nessafy.Installer
{
    class SpicetifyResult
    {
        public int ExitCode { get; set; }
        public List<string> Lines { get; set; }

        public bool Ok
        {
            get { return ExitCode == 0 && !Lines.Any(l => Regex.IsMatch(l, @"^error\b")); }
        }

        public string Text
        {
            get { return string.Join("\n", Lines); }
        }
    }

    class Spicetify
    {
        static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;?]*[A-Za-z]");

        public readonly string Exe;
        readonly InstallOptions options;

        public Spicetify(string exe, InstallOptions options)
        {
            Exe = exe;
            this.options = options;
        }

        /// Spicetify's own install location, then PATH.
        public static string FindExe()
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "spicetify", "spicetify.exe");
            if (File.Exists(local)) return local;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "spicetify.exe");
                    if (dir.Trim().Length > 0 && File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
            return null;
        }

        /// Downloads the latest Spicetify from its GitHub releases into %LOCALAPPDATA%\spicetify, where its own installer puts it.
        public static string Install(Action<string> log)
        {
            log("Finding the latest Spicetify release");
            Dictionary<string, object> release = Http.GetJson("https://api.github.com/repos/spicetify/cli/releases/latest");
            string arch = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "")
                .Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64";

            string url = null, name = null;
            foreach (Dictionary<string, object> asset in (System.Collections.ArrayList)release["assets"])
            {
                string assetName = (string)asset["name"];
                if (assetName.EndsWith("-windows-" + arch + ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    url = (string)asset["browser_download_url"];
                    name = assetName;
                }
            }
            if (url == null) throw new InvalidDataException("The latest Spicetify release has no Windows " + arch + " download.");

            log("Downloading " + name);
            byte[] zip = Http.Get(url);

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "spicetify");
            Directory.CreateDirectory(dir);
            using (var archive = new ZipArchive(new MemoryStream(zip)))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!target.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }

            string exe = Path.Combine(dir, "spicetify.exe");
            if (!File.Exists(exe)) throw new InvalidDataException("The Spicetify download had no spicetify.exe in it.");
            log("Installed Spicetify in " + dir);
            return exe;
        }

        /// Runs Spicetify, always with -n: Spicetify's own restart force-closes every spotify.exe on the
        /// computer, so Nessafy restarts only the Spotify it themed, itself.
        public SpicetifyResult Run(params string[] args)
        {
            var info = new ProcessStartInfo(Exe, "-n " + string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (options.SpicetifyConfig != null) info.EnvironmentVariables["SPICETIFY_CONFIG"] = options.SpicetifyConfig;
            if (options.SpicetifyState != null) info.EnvironmentVariables["SPICETIFY_STATE"] = options.SpicetifyState;

            var output = new StringBuilder();
            using (var process = new Process { StartInfo = info })
            {
                DataReceivedEventHandler collect = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) output.Append(e.Data).Append('\n');
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                // No answers to give: a question from Spicetify reads end-of-input instead of waiting forever.
                process.StandardInput.Close();
                if (!process.WaitForExit(10 * 60 * 1000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Spicetify didn't finish within 10 minutes.");
                }
                process.WaitForExit();

                // Progress bars and spinners redraw one line with \r; keep what each line ended as.
                List<string> lines = Ansi.Replace(output.ToString(), "")
                    .Split('\n')
                    .Select(l => l.Split('\r').Last().Trim())
                    .Where(l => l.Length > 0 && !Regex.IsMatch(l, @"^([-\\|/] |Patching files|spicetify v\d)"))
                    .ToList();
                return new SpicetifyResult { ExitCode = process.ExitCode, Lines = lines };
            }
        }

        static string Quote(string arg)
        {
            return arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '"', '\t' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        public string Version
        {
            get
            {
                SpicetifyResult result = Run("-v");
                return result.Ok && result.Lines.Count > 0 ? result.Lines.Last() : null;
            }
        }

        public string ConfigDir
        {
            get
            {
                SpicetifyResult result = Run("-c");
                if (!result.Ok || result.Lines.Count == 0) throw new InvalidOperationException("Spicetify didn't say where its settings are:\n" + result.Text);
                return Path.GetDirectoryName(result.Lines.Last());
            }
        }

        public string Get(string field)
        {
            SpicetifyResult result = Run("config", field);
            return result.Ok && result.Lines.Count > 0 ? result.Lines.Last() : "";
        }

        public void Set(params string[] fieldsAndValues)
        {
            SpicetifyResult result = Run(new[] { "config" }.Concat(fieldsAndValues).ToArray());
            if (!result.Ok) throw new InvalidOperationException("Spicetify couldn't change its settings:\n" + result.Text);
        }
    }
}
