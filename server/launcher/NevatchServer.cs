// Nevatch Server: a window around "node server.js", so the server runs without a console window.
// Runs first-time setup when needed, shows the log, hides next to the clock when closed and can
// start with Windows. The web console is built in too (ConsolePages.cs). The look lives in ..\..\shared\NevatchUi.cs.
// Built by build.cmd with the C# compiler that ships with Windows (.NET Framework 4), so it is C# 5 only.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Nevatch.Ui;

[assembly: System.Reflection.AssemblyTitle("Nevatch Server")]
[assembly: System.Reflection.AssemblyProduct("Nevatch")]

namespace Nevatch.Server
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool firstInstance;
            using (new Mutex(true, "Nevatch.Server", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Nevatch Server is already running. Look for its icon next to the clock.", "Nevatch Server");
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ServerFiles.Locate();
                Application.Run(new LauncherForm(Array.IndexOf(args, "--minimized") >= 0));
            }
        }
    }

    // Where the server files and the data (keys, people, password) are. Two ways to run:
    //  - next to server.js (the project's server folder): runs those files, data in .\data, as always;
    //  - the single-file release (Nevatch-Server-<version>.exe): the server files, Xray and Nevatch.exe are packed
    //    inside as payload.zip and unpacked to %LOCALAPPDATA%\Nevatch Server\app\<id>. Its data is the data folder
    //    a server-folder copy last used on this PC (so the same keys and people), otherwise %LOCALAPPDATA%\Nevatch Server\data.
    static class ServerFiles
    {
        const string RegistryKey = @"Software\Nevatch\Server";
        public static string Dir;     // the folder with server.js, ending in \
        public static string DataDir; // keys, people, console password
        public static string Problem; // why the server can't run from here, or null

        public static void Locate()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(exeDir, "server.js")))
            {
                Dir = exeDir;
                DataDir = Path.Combine(exeDir, "data");
                try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey)) key.SetValue("DataDir", DataDir); } catch { }
                return;
            }

            string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nevatch Server");
            Dir = exeDir;
            DataDir = Path.Combine(home, "data");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey))
                {
                    string remembered = key != null ? key.GetValue("DataDir") as string : null;
                    if (remembered != null && File.Exists(Path.Combine(remembered, "state.json"))) DataDir = remembered;
                    else
                    {
                        // Nothing remembered (that copy never ran since the registry was added, or it ran from a
                        // sandboxed app whose registry Windows keeps apart): look for the project's server\data
                        // above this exe (release\<version>\), so a release next to the project keeps the same keys
                        // instead of quietly making new ones that no handed-out key matches.
                        string nearby = FindNearbyData(exeDir);
                        if (nearby != null)
                        {
                            DataDir = nearby;
                            try { using (RegistryKey save = Registry.CurrentUser.CreateSubKey(RegistryKey)) save.SetValue("DataDir", DataDir); } catch { }
                        }
                    }
                }
            }
            catch { }

            using (Stream payload = typeof(ServerFiles).Assembly.GetManifestResourceStream("payload.zip"))
            {
                if (payload == null)
                {
                    Problem = "server.js is not next to this Nevatch Server.exe. Start the Nevatch Server.exe in the server folder, " +
                              "or the single-file Nevatch-Server-<version>.exe from a release.";
                    return;
                }
                try { Dir = Unpack(payload, Path.Combine(home, "app")); }
                catch (Exception ex) { Problem = "Could not unpack the server files: " + ex.Message; }
            }
        }

        // A server folder's data with saved keys in this folder or up to four folders above it, or null.
        static string FindNearbyData(string start)
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
                {
                    string data = Path.Combine(dir.FullName, @"server\data");
                    if (File.Exists(Path.Combine(data, "state.json"))) return data;
                }
            }
            catch { }
            return null;
        }

        // Unpacks once per build (named after the payload's hash) and removes older unpacked builds.
        static string Unpack(Stream payload, string appRoot)
        {
            byte[] data;
            using (var buffer = new MemoryStream()) { payload.CopyTo(buffer); data = buffer.ToArray(); }
            string id;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                id = BitConverter.ToString(sha.ComputeHash(data), 0, 6).Replace("-", "").ToLowerInvariant();
            string target = Path.Combine(appRoot, id), server = Path.Combine(target, "server") + "\\";
            if (!File.Exists(Path.Combine(target, ".unpacked")))
            {
                string temp = target + ".tmp";
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(data)))
                {
                    foreach (System.IO.Compression.ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith("/")) continue;
                        string path = Path.GetFullPath(Path.Combine(temp, entry.FullName.Replace('/', '\\')));
                        if (!path.StartsWith(Path.GetFullPath(temp) + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (Stream from = entry.Open())
                        using (FileStream to = File.Create(path)) from.CopyTo(to);
                    }
                }
                File.WriteAllText(Path.Combine(temp, ".unpacked"), "");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(temp, target);
            }
            foreach (string old in Directory.GetDirectories(appRoot))
            {
                if (string.Equals(old.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(old, true); } catch { }
            }
            return server;
        }
    }

    public class LauncherForm : ShellForm
    {
        enum State { Stopped, Starting, Running, Stopping }

        const string AutostartName = "Nevatch Server";
        const int PeoplePage = 1, SettingsPage = 3;
        static string Dir { get { return ServerFiles.Dir; } }
        static string DataDir { get { return ServerFiles.DataDir; } }
        static string StateFile { get { return Path.Combine(DataDir, "state.json"); } }
        // server.js reads CONSOLE_PORT from the environment it gets from here.
        static readonly int ConsolePort = ReadConsolePort();

        // Handed to server.js at start; it lets the built-in console pages in without the password.
        readonly string token = NewToken();
        readonly ServerConsole serverConsole;
        readonly HeroView hero = new HeroView();
        readonly PillButton startStop = new PillButton();
        readonly LogPage logPage = new LogPage();
        readonly ToolStripItem trayToggle, trayConsole;
        readonly System.Windows.Forms.Timer restartTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        readonly bool startMinimized;
        readonly InfoCard accessCard = new InfoCard(Glyph.Key, "Change");

        Process server;
        State state = State.Stopped;
        bool stopRequested;
        DateTime startedAt;
        string[] connected = new string[0]; // names of the people connected right now, from server.js

        public LauncherForm(bool startMinimized) : base("Nevatch Server", "Nevatch", "Server")
        {
            this.startMinimized = startMinimized;
            if (startMinimized) Opacity = 0;

            startStop.Click += (s, e) => { if (state == State.Running) StopServer(); else StartServer(); };
            serverConsole = new ServerConsole(this, ConsolePort, token, PeoplePage, SettingsPage);
            MiniLog homeLog = MiniLog.Following(logPage);
            homeLog.OpenLog = () => ShowPage(4); // Home, People, Lag, Settings, Log
            AddPage(Glyph.Home, "Home", new HomePage(hero, startStop, serverConsole.HomeCard, homeLog));
            AddPage(Glyph.People, "People", serverConsole.People);
            AddPage(Glyph.Speed, "Lag", serverConsole.Lag);

            // Setup checklist and server settings first, then this program's own settings.
            var settings = new Page("Settings");
            settings.Add(serverConsole.Settings, -1);
            var autostart = new Toggle("Start with Windows", "Nevatch Server starts next to the clock when you sign in, and turns the server on.");
            autostart.Checked = StartWithWindows.IsOn(AutostartName);
            autostart.CheckedChanged += (s, e) =>
            {
                try { StartWithWindows.Set(AutostartName, autostart.Checked); }
                catch (Exception ex) { logPage.Append("Could not change start with Windows: " + ex.Message); }
            };
            serverConsole.Settings.Add(autostart, 84);
            accessCard.Button.Click += (s, e) => ChangeAccess();
            ShowAccess();
            serverConsole.Settings.Add(accessCard, 110);
            AddPage(Glyph.Settings, "Settings", settings);

            AddPage(Glyph.Log, "Log", logPage);
            AddPage(Glyph.Info, "About", new AboutPage("Nevatch Server", "Your own VPN server for friends and family."));

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Nevatch Server", null, (s, e) => ShowFromTray());
            trayConsole = menu.Items.Add("Open console", null, (s, e) => OpenConsole());
            trayToggle = menu.Items.Add("", null, (s, e) => { if (state == State.Running) StopServer(); else StartServer(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => Quit());
            Tray.ContextMenuStrip = menu;
            TrayHint = "It keeps running next to the clock. Right-click the shield there and choose Quit to close it (that stops the server too).";

            restartTimer.Tick += (s, e) => { restartTimer.Stop(); StartServer(); };
            SystemEvents.SessionEnding += (s, e) => StopServerNow();
            SetState(State.Stopped, null, false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startMinimized)
            {
                HideToTray(false);
                Opacity = 1;
            }
            // Opening Nevatch Server by hand leaves the server off until Start server is clicked.
            // Starting with Windows (--minimized) still turns it on, as that toggle promises.
            if (startMinimized) BeginInvoke((MethodInvoker)StartServer);
        }

        // ---------- web console: open or password ----------

        static string ReadState()
        {
            try { return File.ReadAllText(StateFile); } catch { return ""; }
        }

        static bool HasPassword() { return ReadState().Contains("\"adminHash\""); }

        static bool ConsoleOpen()
        {
            string s = ReadState();
            return !s.Contains("\"adminHash\"") && System.Text.RegularExpressions.Regex.IsMatch(s, "\"consoleOpen\"\\s*:\\s*true");
        }

        void ShowAccess()
        {
            string url = "http://localhost:" + ConsolePort;
            if (ConsoleOpen())
                accessCard.Set("Web console: open", "No password. " + url + " works on this PC only. " +
                    "Set a password to use it from your phone or other devices at home too.");
            else if (HasPassword())
                accessCard.Set("Web console: password", "Protected with a password, so it also works from other devices at home (" +
                    url.Replace("localhost", "this PC's address") + "). These pages never ask for it.");
            else
                accessCard.Set("Web console", "You choose open or password when the server first starts.");
        }

        // Settings → Web console → Change: the server has to be running, as it saves the choice itself.
        async void ChangeAccess()
        {
            if (state != State.Running)
            {
                MessageBox.Show(this, "Start the server first, then change this.", "Nevatch Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dialog = new ConsoleAccessDialog(ConsoleOpen() || !HasPassword(), false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var body = new System.Collections.Generic.Dictionary<string, object> { { "mode", dialog.Open ? "open" : "password" } };
                if (!dialog.Open) body["password"] = dialog.Password;
                accessCard.Button.Enabled = false;
                try
                {
                    var api = new ConsoleApi(ConsolePort, token);
                    await Task.Run(() => api.Post("/api/console-access", body));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not change it: " + ex.Message, "Nevatch Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { accessCard.Button.Enabled = true; }
                ShowAccess();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) StopServerNow();
        }

        void OpenConsole()
        {
            try { Process.Start("http://localhost:" + ConsolePort); } catch (Exception ex) { logPage.Append("Could not open the browser: " + ex.Message); }
        }

        // message: shown under the status; null means the normal text for that state.
        void SetState(State next, string message, bool isError)
        {
            State wasState = state;
            state = next;
            switch (next)
            {
                case State.Running:
                    hero.Set(HeroMode.On, "Running", message ?? ConnectedText(), isError);
                    startStop.SetLook(Glyph.Stop, "Stop server", Theme.SlateLight, Theme.Slate);
                    break;
                case State.Starting:
                    hero.Set(HeroMode.Busy, "Starting…", message ?? "One moment…", isError);
                    startStop.SetLook(null, "Please wait…", Theme.AccentLight, Theme.AccentDark);
                    break;
                case State.Stopping:
                    hero.Set(HeroMode.Busy, "Stopping…", message ?? "One moment…", isError);
                    startStop.SetLook(null, "Please wait…", Theme.AccentLight, Theme.AccentDark);
                    break;
                default:
                    hero.Set(HeroMode.Off, "Stopped", message ?? "Your server is not running. Click Start server to turn it on.", isError);
                    startStop.SetLook(Glyph.Play, "Start server", Theme.AccentLight, Theme.AccentDark);
                    break;
            }
            bool idle = next == State.Running || next == State.Stopped;
            startStop.Enabled = idle;
            trayToggle.Text = next == State.Running ? "Stop server" : "Start server";
            trayToggle.Enabled = idle;
            trayConsole.Enabled = next == State.Running;
            Tray.Text = "Nevatch Server: " + (next == State.Running ? "running, " + (connected.Length == 0 ? "nobody" : connected.Length.ToString()) + " connected"
                : next == State.Stopped ? "stopped" : "busy");
            TrayGreen = next == State.Running && connected.Length > 0;
            if (next != wasState) serverConsole.SetRunning(next == State.Running);
        }

        static int ReadConsolePort()
        {
            int port;
            return int.TryParse(Environment.GetEnvironmentVariable("CONSOLE_PORT"), out port) && port > 0 ? port : 8088;
        }

        static string NewToken()
        {
            var bytes = new byte[32];
            using (var random = new System.Security.Cryptography.RNGCryptoServiceProvider()) random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        string ConnectedText()
        {
            int n = connected.Length;
            if (n == 0) return "Your server is on. Nobody is connected right now.";
            if (n == 1) return connected[0] + " is connected.";
            if (n == 2) return connected[0] + " and " + connected[1] + " are connected.";
            return n + " people are connected: " + string.Join(", ", connected) + ".";
        }

        // server.js prints "@connected" and the names (tab between them) whenever someone connects or disconnects.
        void SetConnected(string names)
        {
            connected = names.Length == 0 ? new string[0] : names.Split('\t');
            if (state == State.Running) SetState(State.Running, null, false);
        }

        // ---------- first-time setup ----------

        static bool FilesReady()
        {
            return File.Exists(Path.Combine(Dir, @"bin\xray.exe")) && File.Exists(Path.Combine(Dir, @"..\client\dist\Nevatch.exe"));
        }

        // The first-run choice for the web console, handed to server.js (which saves it) on the next start.
        bool? chosenOpen;
        string chosenPassword;

        // The only question: open or password for the web console, once. Xray is downloaded only when this copy
        // doesn't carry it. Returns false when setup failed.
        async Task<bool> EnsureSetup()
        {
            if (!HasPassword() && !ConsoleOpen() && chosenOpen == null)
            {
                ShowFromTray();
                using (var dialog = new ConsoleAccessDialog(true, true))
                {
                    // Closing the window without choosing leaves it open; Settings can change it later.
                    bool ok = dialog.ShowDialog(this) == DialogResult.OK;
                    chosenOpen = !ok || dialog.Open;
                    chosenPassword = ok && !dialog.Open ? dialog.Password : null;
                }
            }
            if (FilesReady()) return true;

            logPage.Append("Running first-time setup (downloads Xray)…");
            Process setup = Spawn("setup.js");
            if (setup == null) return false;
            await Task.Run(() => setup.WaitForExit());
            bool done = setup.ExitCode == 0;
            setup.Dispose();
            return done;
        }

        // ---------- the node process ----------

        // The node.exe packed into the single-file release (..\node\node.exe next to the server files), else an
        // installed Node.js: from where its installer puts it, or from PATH (PATH can be stale right after installing).
        static string NodePath()
        {
            string bundled = Path.GetFullPath(Path.Combine(Dir, @"..\node\node.exe"));
            if (File.Exists(bundled)) return bundled;
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramW6432") })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                string candidate = Path.Combine(folder, @"nodejs\node.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "node.exe";
        }

        Process Spawn(string script)
        {
            var psi = new ProcessStartInfo(NodePath(), "\"" + script + "\"");
            psi.WorkingDirectory = Dir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["FVPN_LAUNCHER"] = "1";
            psi.EnvironmentVariables["FVPN_LAUNCHER_TOKEN"] = token;
            psi.EnvironmentVariables["FVPN_DATA"] = DataDir;
            // The first-run choice; server.js only uses it while nothing is saved yet.
            if (chosenOpen == true) psi.EnvironmentVariables["FVPN_CONSOLE_OPEN"] = "1";
            if (chosenPassword != null) psi.EnvironmentVariables["ADMIN_PASSWORD"] = chosenPassword;

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => OnOutput(e.Data);
            p.ErrorDataReceived += (s, e) => OnOutput(e.Data);
            try
            {
                p.Start();
            }
            catch (Win32Exception)
            {
                logPage.Append("Node.js was not found. Install it from https://nodejs.org (the LTS version), then click Start server.");
                return null;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (script.EndsWith("server.js", StringComparison.OrdinalIgnoreCase))
                logPage.Append("Keys and people: " + DataDir);
            return p;
        }

        void OnOutput(string line)
        {
            if (line == null) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (line.StartsWith("@connected", StringComparison.Ordinal))
                {
                    SetConnected(line.Substring("@connected".Length).TrimStart(' '));
                    return;
                }
                logPage.Append(line);
                // server.js prints this once the console (and so the pages here) can be reached.
                if (line.Contains("Console: ") && state == State.Starting)
                {
                    SetState(State.Running, null, false);
                    ShowAccess(); // server.js has saved the first-run choice by now
                }
            });
        }

        async void StartServer()
        {
            if (state != State.Stopped) return;
            restartTimer.Stop();
            SetState(State.Starting, null, false);
            // A plain launcher copied away from the server files has nothing to run.
            if (ServerFiles.Problem != null || !File.Exists(Path.Combine(Dir, "server.js")))
            {
                logPage.Append(ServerFiles.Problem ?? "server.js is not in " + Dir.TrimEnd('\\') + ".");
                SetState(State.Stopped, "The server files aren't here; see Log.", true);
                return;
            }
            if (!await EnsureSetup())
            {
                SetState(State.Stopped, "Setup didn't finish; see Log. Click Start server to try again.", true);
                return;
            }

            Process p = Spawn("server.js");
            if (p == null)
            {
                SetState(State.Stopped, "Node.js is needed to run the server: install it from nodejs.org, then click Start server.", true);
                return;
            }
            server = p;
            stopRequested = false;
            startedAt = DateTime.Now;
            p.Exited += (s, e) => BeginInvoke((MethodInvoker)delegate { OnServerExited(p); });
        }

        void OnServerExited(Process p)
        {
            if (server != p) return;
            int code = p.ExitCode;
            server = null;
            p.Dispose();
            connected = new string[0];
            if (stopRequested || Quitting)
            {
                SetState(State.Stopped, null, false);
                return;
            }
            // Restart after a crash, but not in a loop if it can't start at all.
            if (DateTime.Now - startedAt > TimeSpan.FromSeconds(20))
            {
                SetState(State.Stopped, "The server stopped unexpectedly. Restarting in 5 seconds…", true);
                restartTimer.Start();
            }
            else
            {
                SetState(State.Stopped, "The server could not start (exit code " + code + "); see Log.", true);
                ShowFromTray();
            }
        }

        async void StopServer()
        {
            Process p = server;
            if (p == null) return;
            stopRequested = true;
            restartTimer.Stop();
            SetState(State.Stopping, null, false);
            AskToStop(p);
            bool exited = await Task.Run(() => p.WaitForExit(8000));
            if (!exited) try { p.Kill(); } catch { }
        }

        // Used when quitting or when Windows shuts down: stop and wait right here.
        void StopServerNow()
        {
            Process p = server;
            if (p == null) return;
            stopRequested = true;
            AskToStop(p);
            if (!p.WaitForExit(8000)) try { p.Kill(); } catch { }
        }

        // server.js stops Xray and saves its stats when it reads "stop" (or when this pipe closes).
        static void AskToStop(Process p)
        {
            try
            {
                p.StandardInput.WriteLine("stop");
                p.StandardInput.Flush();
            }
            catch { }
        }
    }

    // Open (no password, this PC only) or a password (other devices at home too) for the web console.
    // Asked once on the first start, and from Settings → Web console → Change.
    class ConsoleAccessDialog : Form
    {
        readonly RadioButton open = new RadioButton(), locked = new RadioButton();
        readonly TextBox first = new TextBox(), second = new TextBox();
        public bool Open { get { return open.Checked; } }
        public string Password { get { return first.Text; } }

        public ConsoleAccessDialog(bool startOpen, bool firstRun)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Nevatch Server";
            Icon = Art.ShieldIcon(Theme.S(32));
            Font = new Font("Segoe UI", 9.75F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 340);

            var title = new Label
            {
                Text = firstRun ? "One question before your server starts" : "Web console",
                Location = new Point(18, 16), AutoSize = true, Font = new Font("Segoe UI Semibold", 12F),
            };
            var intro = new Label
            {
                Text = "This window never asks for a password. The web console in your browser can be:",
                Bounds = new Rectangle(18, 50, 424, 42),
            };
            open.Text = "Open, no password: only on this PC";
            open.SetBounds(20, 100, 420, 24);
            locked.Text = "Password: also from your phone or other devices at home";
            locked.SetBounds(20, 128, 420, 24);
            open.Checked = startOpen;
            locked.Checked = !startOpen;

            var firstLabel = new Label { Text = "Password", Location = new Point(42, 168), AutoSize = true };
            first.SetBounds(150, 165, 290, 26);
            first.UseSystemPasswordChar = true;
            var secondLabel = new Label { Text = "Repeat it", Location = new Point(42, 204), AutoSize = true };
            second.SetBounds(150, 201, 290, 26);
            second.UseSystemPasswordChar = true;
            var hint = new Label
            {
                Text = "At least 8 characters. You can change this any time in Settings.",
                Bounds = new Rectangle(42, 236, 400, 22), ForeColor = Theme.Muted,
            };
            // Labels stay enabled (Windows draws disabled labels dark grey, unreadable here) and just dim instead.
            EventHandler sync = (s, e) =>
            {
                first.Enabled = second.Enabled = locked.Checked;
                firstLabel.ForeColor = secondLabel.ForeColor = locked.Checked ? Theme.Text : Theme.NavText;
            };
            open.CheckedChanged += sync;
            locked.CheckedChanged += sync;
            sync(null, EventArgs.Empty);

            var ok = new Button { Text = firstRun ? "Start server" : "Save", Size = new Size(120, 36), Location = new Point(196, 284) };
            var cancel = new Button { Text = "Cancel", Size = new Size(120, 36), Location = new Point(322, 284), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                string problem = !locked.Checked ? null
                    : first.Text.Length < 8 ? "The password must be at least 8 characters."
                    : first.Text != second.Text ? "The two passwords are different."
                    : null;
                if (problem == null) DialogResult = DialogResult.OK;
                else MessageBox.Show(this, problem, "Nevatch Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            var controls = new List<Control> { title, intro, open, locked, firstLabel, first, secondLabel, second, hint, ok };
            if (!firstRun) controls.Add(cancel);
            else ok.Location = new Point(322, 284);
            Controls.AddRange(controls.ToArray());
            AcceptButton = ok;
            if (!firstRun) CancelButton = cancel;
            Dark.Dialog(this);
        }
    }
}
