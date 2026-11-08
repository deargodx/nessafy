// Nessafy installer: the window.

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Nessafy.Installer
{
    class InstallerForm : Form
    {
        static readonly Color Background = Color.FromArgb(8, 8, 8);
        static readonly Color Panel = Color.FromArgb(17, 17, 17);
        static readonly Color Line = Color.FromArgb(40, 40, 40);
        static readonly Color Ivory = Color.FromArgb(233, 230, 225);
        static readonly Color Muted = Color.FromArgb(140, 140, 140);

        readonly InstallOptions options;
        readonly Label spotifyValue, spicetifyValue, themeValue;
        readonly CheckBox keepBox;
        readonly Button installButton, uninstallButton;
        readonly TextBox logBox;
        Status status;

        public InstallerForm(InstallOptions options)
        {
            this.options = options;

            Text = "Nessafy Installer";
            Icon = new Icon(new MemoryStream(Installer.ReadResource("icon.ico")));
            BackColor = Background;
            ForeColor = Ivory;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 556);

            var mark = new PictureBox
            {
                Image = Image.FromStream(new MemoryStream(Installer.ReadResource("appicon.png"))),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(32, 28, 64, 64)
            };
            var title = new Label { Text = "Nessafy", Font = new Font("Georgia", 26f), AutoSize = true, Location = new Point(106, 30) };
            var subtitle = new Label { Text = "for Spotify", Font = new Font("Georgia", 11f, FontStyle.Italic), ForeColor = Muted, AutoSize = true, Location = new Point(110, 74) };
            Controls.AddRange(new Control[] { mark, title, subtitle });

            Controls.Add(SectionLabel("SPOTIFY", 120));
            spotifyValue = StatusRow("Spotify", 146);
            spicetifyValue = StatusRow("Spicetify", 172);
            themeValue = StatusRow("Theme", 198);

            Controls.Add(SectionLabel("OPTIONS", 240));
            keepBox = new CheckBox
            {
                Text = "Keep Nessafy on after Spotify updates",
                Checked = options.KeepAfterUpdates,
                AutoSize = true,
                Location = new Point(32, 266),
                FlatStyle = FlatStyle.Flat
            };
            Controls.Add(keepBox);
            Controls.Add(new Label
            {
                Text = "Spotify's updates remove themes. Nessafy checks every few minutes in the background, puts itself back, and brings in new versions of the theme.",
                ForeColor = Muted,
                Font = new Font("Segoe UI", 9f),
                Bounds = new Rectangle(50, 292, 478, 36)
            });

            installButton = MakeButton("Install", true, new Rectangle(32, 344, 150, 40));
            uninstallButton = MakeButton("Uninstall", false, new Rectangle(194, 344, 150, 40));
            installButton.Click += (s, e) => Run(true);
            uninstallButton.Click += (s, e) => Run(false);
            Controls.AddRange(new Control[] { installButton, uninstallButton });

            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Panel,
                ForeColor = Color.FromArgb(190, 190, 190),
                Font = new Font("Consolas", 9f),
                Bounds = new Rectangle(32, 400, 496, 108)
            };
            Controls.Add(logBox);
            logBox.HandleCreated += (s, e) => SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null);

            Controls.Add(new Label
            {
                Text = "By deargod  ·  Thank you bella, for being fucking amazing  ·  Made with Spicetify",
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(32, 522)
            });

            ShowStatus(Installer.ReadStatus(options));
            if (!status.Installed) logBox.Text = "Choose Install to put Nessafy into Spotify.";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Line))
                e.Graphics.DrawLine(pen, 32, 108, ClientSize.Width - 32, 108);
        }

        static Label SectionLabel(string text, int y)
        {
            return new Label { Text = text, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true, Location = new Point(32, y) };
        }

        Label StatusRow(string name, int y)
        {
            Controls.Add(new Label { Text = name, ForeColor = Muted, AutoSize = true, Location = new Point(32, y) });
            var value = new Label { AutoSize = true, Location = new Point(130, y) };
            Controls.Add(value);
            return value;
        }

        static Button MakeButton(string text, bool primary, Rectangle bounds)
        {
            var button = new Button
            {
                Text = text,
                Bounds = bounds,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Ivory : Background,
                ForeColor = primary ? Color.Black : Ivory,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.White : Panel;
            return button;
        }

        void ShowStatus(Status newStatus)
        {
            status = newStatus;
            switch (status.Spotify)
            {
                case SpotifyKind.Desktop: spotifyValue.Text = status.SpotifyVersion; break;
                case SpotifyKind.Store: spotifyValue.Text = "Microsoft Store version. Install swaps it for spotify.com's."; break;
                default: spotifyValue.Text = "Not installed yet. Install gets it from spotify.com."; break;
            }
            spicetifyValue.Text = status.SpicetifyVersion ?? "Not installed yet. Install adds it.";

            string theme = string.IsNullOrEmpty(status.CurrentTheme) ? "None" : status.CurrentTheme;
            themeValue.Text = status.Installed || theme == "None" || theme == Installer.ThemeName
                ? theme
                : theme + "  ·  Nessafy replaces it, and Uninstall brings it back";

            if (status.Installed) keepBox.Checked = status.KeepAfterUpdates;
            installButton.Text = status.Installed ? "Update / Repair" : "Install";
            installButton.Enabled = true;
            uninstallButton.Visible = status.Installed;
            uninstallButton.Enabled = true;
            keepBox.Enabled = true;
        }

        void Log(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), line);
                return;
            }
            logBox.AppendText(line + Environment.NewLine);
        }

        void Run(bool install)
        {
            if (install && status.Spotify == SpotifyKind.Store &&
                MessageBox.Show(this,
                    "Spicetify can't theme Spotify from the Microsoft Store, so Nessafy will uninstall it and install Spotify from spotify.com instead.\n\n" +
                    "Your playlists and library stay, they're in your account. You'll need to log in again, and songs downloaded for offline listening need downloading again.",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            if (Installer.IsSpotifyRunning(options) && !options.NoRestart &&
                MessageBox.Show(this, "Spotify will be closed and opened again.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            options.KeepAfterUpdates = keepBox.Checked;
            installButton.Enabled = uninstallButton.Enabled = keepBox.Enabled = false;
            logBox.Clear();

            var worker = new Thread(() =>
            {
                bool ok = true;
                try
                {
                    if (install) Installer.Install(options, Log);
                    else Installer.Uninstall(options, Log);
                }
                catch (Exception err)
                {
                    ok = false;
                    Log("Error: " + err.Message);
                }

                Status after = Installer.ReadStatus(options);
                BeginInvoke(new Action(() =>
                {
                    ShowStatus(after);
                    if (ok) Log(install ? "Done. Nessafy is on Spotify." : "Done. Nessafy is uninstalled.");
                }));
            }) { IsBackground = true };
            worker.Start();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
    }
}
