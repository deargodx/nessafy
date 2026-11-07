// Nessafy installer: finding Spotify, putting Nessafy into Spicetify, and keeping it there after Spotify updates.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Nessafy.Installer
{
    enum SpotifyKind { Desktop, Store, Missing }

    class InstallOptions
    {
        public string DataDir { get; set; }
        public string SpotifyDir { get; set; }
        public string SpicetifyConfig { get; set; }   // SPICETIFY_CONFIG for Spicetify (null: its default)
        public string SpicetifyState { get; set; }    // SPICETIFY_STATE, where it keeps its backups (null: its default)
        public string ThemeSource { get; set; }       // a theme folder to install instead of the one inside the installer
        public bool NoRestart { get; set; }           // never close or start Spotify
        public bool KeepAfterUpdates { get; set; }

        public InstallOptions()
        {
            DataDir = Installer.DefaultDataDir;
            SpotifyDir = Installer.DefaultSpotifyDir;
            KeepAfterUpdates = true;
        }
    }

    /// What Nessafy remembers between runs, in its data folder's state.json.
    class State
    {
        public bool Installed { get; set; }
        public string PreviousTheme { get; set; }
        public string PreviousScheme { get; set; }
        public string ThemeVersion { get; set; }
        public DateTime LastReleaseCheck { get; set; }
        public bool KeepAfterUpdates { get; set; }

        // The options Nessafy was installed with, for the background check.
        public string SpotifyDir { get; set; }
        public string SpicetifyConfig { get; set; }
        public string SpicetifyState { get; set; }
        public bool NoRestart { get; set; }
    }

    class Status
    {
        public SpotifyKind Spotify { get; set; }
        public string SpotifyVersion { get; set; }
        public string SpicetifyVersion { get; set; }  // null: Spicetify isn't installed
        public string CurrentTheme { get; set; }
        public bool Installed { get; set; }
        public bool KeepAfterUpdates { get; set; }
    }

    static class Installer
    {
        public const string AppName = "Nessafy";
        public const string ThemeName = "Nessafy";
        public const string InstallerExe = "NessafyInstaller.exe";
        const string TaskName = "Nessafy";

        static string AppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        public static readonly string DefaultDataDir = Path.Combine(AppData, AppName);
        public static readonly string DefaultSpotifyDir = Path.Combine(AppData, "Spotify");

        // ── Status ──────────────────────────────────────────────────────────────────────────

        public static Status ReadStatus(InstallOptions options)
        {
            var status = new Status { Spotify = FindSpotify(options.SpotifyDir) };
            if (status.Spotify == SpotifyKind.Desktop)
                status.SpotifyVersion = FileVersionInfo.GetVersionInfo(Path.Combine(options.SpotifyDir, "Spotify.exe")).FileVersion;

            string exe = Spicetify.FindExe();
            if (exe != null)
            {
                var spicetify = new Spicetify(exe, options);
                status.SpicetifyVersion = spicetify.Version;
                status.CurrentTheme = spicetify.Get("current_theme");
            }

            State state = LoadState(options.DataDir);
            status.Installed = state.Installed;
            status.KeepAfterUpdates = !state.Installed || state.KeepAfterUpdates;
            return status;
        }

        public static SpotifyKind FindSpotify(string spotifyDir)
        {
            if (File.Exists(Path.Combine(spotifyDir, "Spotify.exe"))) return SpotifyKind.Desktop;
            string storeAlias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe");
            return File.Exists(storeAlias) ? SpotifyKind.Store : SpotifyKind.Missing;
        }

        static void RequireDesktopSpotify(InstallOptions options)
        {
            switch (FindSpotify(options.SpotifyDir))
            {
                case SpotifyKind.Store:
                    throw new InvalidOperationException("This is Spotify from the Microsoft Store, which Spicetify can't theme. Uninstall it, install Spotify from spotify.com/download, then run this again.");
                case SpotifyKind.Missing:
                    throw new InvalidOperationException("Spotify isn't installed. Install it from spotify.com/download, then run this again.");
            }
        }

        /// Spicetify has unpacked and patched Spotify's interface. A Spotify update puts the packed xpui.spa back.
        public static bool IsApplied(string spotifyDir)
        {
            string apps = Path.Combine(spotifyDir, "Apps");
            return !File.Exists(Path.Combine(apps, "xpui.spa")) && File.Exists(Path.Combine(apps, "xpui", "index.html"));
        }

        // ── Install ─────────────────────────────────────────────────────────────────────────

        public static void Install(InstallOptions options, Action<string> log)
        {
            RequireDesktopSpotify(options);
            State state = LoadState(options.DataDir);

            string exe = Spicetify.FindExe() ?? Spicetify.Install(log);
            var spicetify = new Spicetify(exe, options);
            if (!string.Equals(options.SpotifyDir, DefaultSpotifyDir, StringComparison.OrdinalIgnoreCase))
                spicetify.Set("spotify_path", options.SpotifyDir);

            // Remembered once, so uninstalling puts back what was there before Nessafy.
            string current = spicetify.Get("current_theme");
            if (!state.Installed && current != ThemeName)
            {
                state.PreviousTheme = current;
                state.PreviousScheme = spicetify.Get("color_scheme");
            }

            string version;
            WriteTheme(spicetify, ThemeFromInstaller(options, out version), log);
            state.ThemeVersion = version;

            spicetify.Set("current_theme", ThemeName, "color_scheme", ThemeName, "inject_css", "1", "replace_colors", "1", "inject_theme_js", "1");
            Apply(spicetify, options, true, log);

            KeepInstaller(options.DataDir);
            state.Installed = true;
            state.KeepAfterUpdates = options.KeepAfterUpdates;
            state.SpotifyDir = options.SpotifyDir;
            state.SpicetifyConfig = options.SpicetifyConfig;
            state.SpicetifyState = options.SpicetifyState;
            state.NoRestart = options.NoRestart;
            SaveState(options.DataDir, state);

            if (options.KeepAfterUpdates) CreateTask(options.DataDir, log);
            else DeleteTask();
            RegisterUninstaller(options.DataDir);
        }

        static Dictionary<string, byte[]> ThemeFromInstaller(InstallOptions options, out string version)
        {
            var files = new Dictionary<string, byte[]>();
            foreach (string name in Release.ThemeFiles)
            {
                byte[] data = options.ThemeSource != null
                    ? (File.Exists(Path.Combine(options.ThemeSource, name)) ? File.ReadAllBytes(Path.Combine(options.ThemeSource, name)) : null)
                    : TryReadResource("theme/" + name);
                if (data != null) files[name] = data;
            }
            if (!files.ContainsKey("color.ini") || !files.ContainsKey("user.css"))
                throw new InvalidOperationException("Nessafy's theme files are missing" + (options.ThemeSource != null ? " from " + options.ThemeSource : " from the installer"));

            // A theme from a local folder isn't a release, so the background check leaves it alone.
            version = options.ThemeSource != null ? "local" : Encoding.UTF8.GetString(ReadResource("version.txt")).Trim();
            return files;
        }

        static void WriteTheme(Spicetify spicetify, Dictionary<string, byte[]> files, Action<string> log)
        {
            string dir = Path.Combine(spicetify.ConfigDir, "Themes", ThemeName);
            log("Adding the Nessafy theme to Spicetify");
            Directory.CreateDirectory(dir);
            foreach (string name in Release.ThemeFiles)
            {
                string path = Path.Combine(dir, name);
                if (files.ContainsKey(name)) File.WriteAllBytes(path, files[name]);
                else if (File.Exists(path)) File.Delete(path);
            }
        }

        /// Applies Spicetify to Spotify, unpacking Spotify's files first if they're new. Restarts Spotify to show
        /// it only if Spotify is open and restart is true.
        static void Apply(Spicetify spicetify, InstallOptions options, bool restart, Action<string> log)
        {
            bool wasRunning = SpotifyProcesses(options.SpotifyDir).Count > 0;
            bool applied = IsApplied(options.SpotifyDir);
            string[] command = applied ? new[] { "restore", "backup", "apply" } : new[] { "backup", "apply" };

            log("Applying " + spicetify.Get("current_theme") + " to Spotify");
            SpicetifyResult result = spicetify.Run(applied ? new[] { "apply" } : command);
            if (!result.Ok && applied)
            {
                log("Starting again from Spotify's original files");
                result = spicetify.Run(command);
            }
            if (!result.Ok)
            {
                // A Spotify update can need a newer Spicetify.
                log("Updating Spicetify");
                if (spicetify.Run("update").Ok) result = spicetify.Run(command);
            }
            if (!result.Ok)
                throw new InvalidOperationException("Spicetify couldn't apply Nessafy:\n" + string.Join("\n", result.Lines.Skip(Math.Max(0, result.Lines.Count - 6))));

            if (wasRunning && restart && !options.NoRestart) RestartSpotify(options.SpotifyDir, log);
        }

        // ── Uninstall ───────────────────────────────────────────────────────────────────────

        public static void Uninstall(InstallOptions options, Action<string> log)
        {
            State state = LoadState(options.DataDir);
            if (state.Installed)
            {
                options.SpotifyDir = state.SpotifyDir ?? options.SpotifyDir;
                options.SpicetifyConfig = options.SpicetifyConfig ?? state.SpicetifyConfig;
                options.SpicetifyState = options.SpicetifyState ?? state.SpicetifyState;
                options.NoRestart = options.NoRestart || state.NoRestart;
            }
            DeleteTask();

            string exe = Spicetify.FindExe();
            if (exe != null)
            {
                var spicetify = new Spicetify(exe, options);
                string themes = Path.Combine(spicetify.ConfigDir, "Themes");

                // Only if Nessafy is still the theme: if another was picked since, that's left as it is.
                if (spicetify.Get("current_theme") == ThemeName)
                {
                    string previous = state.PreviousTheme;
                    if (!string.IsNullOrEmpty(previous) && Directory.Exists(Path.Combine(themes, previous)))
                    {
                        log("Switching Spicetify back to " + previous);
                        spicetify.Set("current_theme", previous);
                        if (!string.IsNullOrEmpty(state.PreviousScheme)) spicetify.Set("color_scheme", state.PreviousScheme);
                        Apply(spicetify, options, true, log);
                    }
                    else
                    {
                        log("Putting Spotify back to how it was");
                        bool wasRunning = SpotifyProcesses(options.SpotifyDir).Count > 0;
                        spicetify.Set("current_theme", "", "color_scheme", "");
                        SpicetifyResult result = spicetify.Run("restore");
                        if (!result.Ok) throw new InvalidOperationException("Spicetify couldn't put Spotify back:\n" + result.Text);
                        if (wasRunning && !options.NoRestart) RestartSpotify(options.SpotifyDir, log);
                    }
                }

                string ours = Path.Combine(themes, ThemeName);
                if (Directory.Exists(ours)) Directory.Delete(ours, true);
            }

            UnregisterUninstaller();
            state.Installed = false;
            SaveState(options.DataDir, state);
        }

        // ── After Spotify updates ───────────────────────────────────────────────────────────

        /// Run every few minutes by the scheduled task: puts Nessafy back after a Spotify update, and installs
        /// newer releases of the theme. Does nothing if another Spicetify theme has been picked since.
        public static void Check(string dataDir)
        {
            using (var mutex = new Mutex(false, @"Local\NessafyCheck"))
            {
                if (!mutex.WaitOne(0)) return;
                try
                {
                    CheckOnce(dataDir);
                }
                catch (Exception err)
                {
                    Log(dataDir, "Error: " + err.Message);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        static void CheckOnce(string dataDir)
        {
            State state = LoadState(dataDir);
            if (!state.Installed) return;

            var options = new InstallOptions
            {
                DataDir = dataDir,
                SpotifyDir = state.SpotifyDir ?? DefaultSpotifyDir,
                SpicetifyConfig = state.SpicetifyConfig,
                SpicetifyState = state.SpicetifyState,
                NoRestart = state.NoRestart
            };
            Action<string> log = line => Log(dataDir, line);

            string exe = Spicetify.FindExe();
            if (exe == null || FindSpotify(options.SpotifyDir) != SpotifyKind.Desktop) return;
            var spicetify = new Spicetify(exe, options);
            if (spicetify.Get("current_theme") != ThemeName) return;

            bool themeUpdated = false;
            if (state.ThemeVersion != "local" && DateTime.UtcNow - state.LastReleaseCheck > TimeSpan.FromHours(6))
            {
                state.LastReleaseCheck = DateTime.UtcNow;
                SaveState(dataDir, state);
                try
                {
                    LatestRelease latest = Release.Latest();
                    if (latest.Version != state.ThemeVersion)
                    {
                        WriteTheme(spicetify, Release.DownloadTheme(latest, log), log);
                        state.ThemeVersion = latest.Version;
                        SaveState(dataDir, state);
                        themeUpdated = true;
                        log("Updated the theme to " + latest.Version);
                    }
                }
                catch (Exception err)
                {
                    log("Couldn't check for a newer theme: " + err.Message);
                }
            }

            if (!IsApplied(options.SpotifyDir))
            {
                // Spotify has updated. It restarts itself to finish an update, so restart it again only if it
                // opened in the last few minutes, not in the middle of someone's music.
                List<Process> running = SpotifyProcesses(options.SpotifyDir);
                bool justOpened = running.Count > 0 && DateTime.Now - running.Min(p => StartTime(p)) < TimeSpan.FromMinutes(6);
                log("Spotify has updated: applying Nessafy again" + (running.Count > 0 && !justOpened ? " (shows next time Spotify opens)" : ""));
                Apply(spicetify, options, justOpened, log);
            }
            else if (themeUpdated)
            {
                SpicetifyResult result = spicetify.Run("refresh");
                if (!result.Ok) log("Spicetify couldn't refresh the theme:\n" + result.Text);
            }
        }

        static DateTime StartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.MaxValue;
            }
        }

        static void CreateTask(string dataDir, Action<string> log)
        {
            string exe = Path.Combine(dataDir, InstallerExe);
            string args = "--check" + (string.Equals(dataDir, DefaultDataDir, StringComparison.OrdinalIgnoreCase) ? "" : " --data-dir \"" + dataDir + "\"");
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>Puts Nessafy back on Spotify after Spotify updates, and keeps the theme up to date.</Description></RegistrationInfo>\n" +
                "  <Triggers><TimeTrigger><Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>" +
                "<StartBoundary>" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>\n" +
                "  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT15M</ExecutionTimeLimit>" +
                "<Priority>7</Priority><Enabled>true</Enabled></Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command><Arguments>" + SecurityElement.Escape(args) + "</Arguments></Exec></Actions>\n" +
                "</Task>\n";

            string file = Path.Combine(Path.GetTempPath(), "nessafy-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            try
            {
                int code = RunHidden("schtasks.exe", "/Create /F /TN \"" + TaskName + "\" /XML \"" + file + "\"");
                if (code != 0) throw new InvalidOperationException("Windows couldn't set up the check after Spotify updates (schtasks exit code " + code + ").");
            }
            finally
            {
                File.Delete(file);
            }
            log("Nessafy will put itself back after Spotify updates");
        }

        static void DeleteTask()
        {
            RunHidden("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"");
        }

        static int RunHidden(string file, string args)
        {
            var info = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process process = Process.Start(info))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        // ── Spotify ─────────────────────────────────────────────────────────────────────────

        /// Spotify's processes from this install only, so a second Spotify (or a test copy) is never touched.
        public static List<Process> SpotifyProcesses(string spotifyDir)
        {
            string root = Path.GetFullPath(spotifyDir).TrimEnd('\\') + "\\";
            var found = new List<Process>();
            foreach (Process process in Process.GetProcessesByName("Spotify"))
            {
                try
                {
                    if (process.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase)) found.Add(process);
                }
                catch
                {
                    // Another user's process, or it already exited.
                }
            }
            return found;
        }

        public static bool IsSpotifyRunning(InstallOptions options)
        {
            return SpotifyProcesses(options.SpotifyDir).Count > 0;
        }

        static void RestartSpotify(string spotifyDir, Action<string> log)
        {
            log("Restarting Spotify");
            List<Process> processes = SpotifyProcesses(spotifyDir);
            foreach (Process process in processes)
            {
                try { process.Kill(); } catch { }
            }
            foreach (Process process in processes)
            {
                try { process.WaitForExit(5000); } catch { }
            }
            Thread.Sleep(500);
            // Through the shell, so Spotify doesn't inherit (and hold open) the installer's console.
            Process.Start(new ProcessStartInfo(Path.Combine(spotifyDir, "Spotify.exe")) { UseShellExecute = true });
        }

        // ── Nessafy's own files ─────────────────────────────────────────────────────────────

        static State LoadState(string dataDir)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(Path.Combine(dataDir, "state.json"))) ?? new State();
            }
            catch
            {
                return new State();
            }
        }

        static void SaveState(string dataDir, State state)
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "state.json"), new JavaScriptSerializer().Serialize(state));
        }

        static void Log(string dataDir, string line)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                string file = Path.Combine(dataDir, "nessafy.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024)
                {
                    string[] lines = File.ReadAllLines(file);
                    File.WriteAllLines(file, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch
            {
                // Logging must never stop the check.
            }
        }

        /// A copy of the installer stays in the data folder: it's the uninstaller and the background check.
        static void KeepInstaller(string dataDir)
        {
            Directory.CreateDirectory(dataDir);
            string self = Assembly.GetExecutingAssembly().Location;
            string kept = Path.Combine(dataDir, InstallerExe);
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, kept, true);
        }

        static void RegisterUninstaller(string dataDir)
        {
            string exe = Path.Combine(dataDir, InstallerExe);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                key.SetValue("InstallLocation", dataDir);
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("ModifyPath", "\"" + exe + "\"");
                key.SetValue("NoRepair", 1);
            }
        }

        static void UnregisterUninstaller()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);
        }

        public static byte[] ReadResource(string name)
        {
            byte[] data = TryReadResource(name);
            if (data == null) throw new InvalidOperationException("The installer is missing " + name);
            return data;
        }

        static byte[] TryReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
