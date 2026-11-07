// Nessafy installer. Run it to get the window, or from a command line:
//
//   NessafyInstaller.exe --install | --repair | --uninstall | --status
//       [--keep | --no-keep]          put Nessafy back after Spotify updates (default: keep)
//       [--theme <folder>]            install a local build of the theme instead of the one inside
//       [--spotify-dir <folder>]      Spotify's folder (default %APPDATA%\Spotify)
//       [--data-dir <folder>]         Nessafy's data folder (default %APPDATA%\Nessafy)
//       [--spicetify-config <folder>] [--spicetify-state <folder>]   a separate Spicetify setup
//       [--no-restart]                never close or open Spotify
//       [--quiet]                     no windows or questions
//
//   --check                          the background check (scheduled): puts Nessafy back after a Spotify update

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Nessafy Installer")]
[assembly: AssemblyProduct("Nessafy")]
[assembly: AssemblyDescription("Installs the Nessafy theme into Spotify")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Nessafy.Installer
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var options = new InstallOptions();
            string action = null;
            bool quiet = false;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--install": case "--repair": action = "install"; break;
                        case "--uninstall": action = "uninstall"; break;
                        case "--status": action = "status"; break;
                        case "--check": action = "check"; break;
                        case "--keep": options.KeepAfterUpdates = true; break;
                        case "--no-keep": options.KeepAfterUpdates = false; break;
                        case "--theme": options.ThemeSource = Path.GetFullPath(args[++i]); break;
                        case "--spotify-dir": options.SpotifyDir = Path.GetFullPath(args[++i]); break;
                        case "--data-dir": options.DataDir = Path.GetFullPath(args[++i]); break;
                        case "--spicetify-config": options.SpicetifyConfig = Path.GetFullPath(args[++i]); break;
                        case "--spicetify-state": options.SpicetifyState = Path.GetFullPath(args[++i]); break;
                        case "--no-restart": options.NoRestart = true; break;
                        case "--quiet": quiet = true; break;
                        default: throw new ArgumentException("Unknown option " + args[i]);
                    }
                }
            }
            catch (Exception err)
            {
                return Fail(err.Message, quiet || action != null);
            }

            if (action == "check")
            {
                Installer.Check(options.DataDir);
                return 0;
            }

            if (action == null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerForm(options));
                return 0;
            }

            // Apps & features runs "--uninstall" without --quiet: ask first, then say how it went.
            bool windowed = action == "uninstall" && !quiet;
            if (!windowed) AttachConsole(-1);

            if (windowed && MessageBox.Show("Remove Nessafy from Spotify?", "Nessafy", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return 1;

            var log = new List<string>();
            Action<string> write = line =>
            {
                log.Add(line);
                if (!windowed) Console.WriteLine(line);
            };

            try
            {
                switch (action)
                {
                    case "status":
                        Status status = Installer.ReadStatus(options);
                        write("Spotify: " + (status.Spotify == SpotifyKind.Desktop ? status.SpotifyVersion : status.Spotify == SpotifyKind.Store ? "Microsoft Store version (not supported)" : "not installed"));
                        write("Spicetify: " + (status.SpicetifyVersion ?? "not installed"));
                        write("Theme: " + (string.IsNullOrEmpty(status.CurrentTheme) ? "none" : status.CurrentTheme));
                        write("Nessafy: " + (status.Installed ? "installed" + (status.KeepAfterUpdates ? ", kept on after Spotify updates" : "") : "not installed"));
                        break;

                    case "install":
                        Installer.Install(options, write);
                        write("Done.");
                        break;

                    case "uninstall":
                        Installer.Uninstall(options, write);
                        write("Nessafy is uninstalled.");
                        break;
                }
            }
            catch (Exception err)
            {
                return Fail(err.Message, !windowed);
            }

            if (windowed) MessageBox.Show(string.Join("\n", log), "Nessafy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static int Fail(string message, bool console)
        {
            if (console)
            {
                AttachConsole(-1);
                Console.Error.WriteLine("Error: " + message);
            }
            else
            {
                MessageBox.Show(message, "Nessafy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);
    }
}
