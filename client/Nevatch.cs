// Nevatch: Windows client for a Nevatch server, as one self-contained program.
//   - Xray is packed inside (build.cmd embeds it) and unpacked to %LOCALAPPDATA%\Nevatch the first time it's needed.
//   - The server appends the person's key to the end of the .exe when you download it from the console;
//     "Add key" adds more servers, and the Servers page picks between them.
// It runs Xray as a local proxy and points the Windows proxy setting at it.
// The look (sidebar, shield, buttons) lives in ..\shared\NevatchUi.cs.
// Built by build.cmd with the C# compiler that ships with Windows (.NET Framework 4), so it is C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Nevatch.Ui;

[assembly: System.Reflection.AssemblyTitle("Nevatch")]
[assembly: System.Reflection.AssemblyProduct("Nevatch")]

namespace Nevatch
{
    public static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        // Arguments: --minimized (start next to the clock), --connect (connect right away), --uninstall (from
        // Windows' list of apps), and --data <folder>, --relaunched, --updated (used when Nevatch restarts itself
        // with administrator rights, after installing, or after an update).
        [STAThread]
        public static int Main(string[] args)
        {
            int data = Array.IndexOf(args, "--data");
            if (data >= 0 && data + 1 < args.Length) Paths.DataDir = args[data + 1];

            // For testing: unpack Xray and write the config for the picked server, without changing any Windows settings.
            if (args.Length >= 2 && args[0] == "--prepare")
            {
                ServerList.ImportKeys();
                Server server = ServerList.Current();
                if (server == null) return 2;
                string xray = EmbeddedXray.Extract();
                File.WriteAllText(args[1], XrayConfig.Build(server.Key, 10808, 10809, Settings.Mode, AppList.Load()), new UTF8Encoding(false));
                File.WriteAllText(args[1] + ".xray-path", xray);
                return 0;
            }
            // For testing: connect (proxy only, no Windows settings), ask the server for an update and install it.
            // Writes what happened to the given file; the new copy is started with --prepare <file>.json.
            if (args.Length >= 2 && args[0] == "--check-update")
            {
                ServerList.ImportKeys();
                Server server = ServerList.Current();
                var core = new XrayCore();
                string result;
                try
                {
                    string error = server == null ? "no server" : core.Start(server.Key, VpnMode.Proxy, new List<string>());
                    if (error != null) result = "error: " + error;
                    else
                    {
                        byte[] exe = Updater.Fetch(core.HttpPort, server.Key.Id);
                        if (exe == null) result = "up to date";
                        else
                        {
                            Updater.Install(exe, "--prepare \"" + args[1] + ".json\" --data \"" + Paths.DataDir + "\"");
                            result = "updated";
                        }
                    }
                }
                catch (Exception ex) { result = "error: " + ex.Message; }
                finally { core.Stop(); }
                File.WriteAllText(args[1], result);
                return 0;
            }

            // The Monitor page's network reader, started with administrator rights while the monitor is on (Monitor.cs).
            if (args.Length >= 2 && args[0] == "--monitor") return NetMonitor.Feed(args[1]);
            // For testing: run the monitor for <seconds> like the page does, and write its list to <file>.
            if (args.Length >= 3 && args[0] == "--monitor-test") return NetMonitor.Test(args[1], int.Parse(args[2]));

            bool relaunched = Array.IndexOf(args, "--relaunched") >= 0;
            Mutex mutex = null;
            bool owned;
            try
            {
                mutex = new Mutex(false, "Nevatch.Client");
                // After a restart with administrator rights, the old copy needs a moment to close.
                try { owned = mutex.WaitOne(relaunched ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
            }
            catch (UnauthorizedAccessException)
            {
                owned = false; // a copy running with administrator rights holds it
            }
            bool uninstall = Array.IndexOf(args, "--uninstall") >= 0;
            if (!owned)
            {
                MessageBox.Show(uninstall
                    ? "Nevatch is still running. Right-click its icon next to the clock, choose Quit, then try again."
                    : "Nevatch is already running. Look for its icon next to the clock.", "Nevatch");
                return 0;
            }
            try
            {
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (uninstall)
                {
                    if (MessageBox.Show("Remove Nevatch from this PC?\n\nThis also removes its list of servers and its settings.",
                            "Nevatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        Installer.Uninstall();
                        MessageBox.Show("Nevatch was removed.", "Nevatch");
                    }
                    return 0;
                }
                Updater.CleanUp();
                if (!relaunched && OfferInstall()) return 0;
                SystemProxy.RecoverAfterCrash();
                XrayCore.KillLeftovers();
                try { ServerList.ImportKeys(); }
                catch { }
                Application.Run(new MainForm(Array.IndexOf(args, "--minimized") >= 0, Array.IndexOf(args, "--connect") >= 0,
                    Array.IndexOf(args, "--updated") >= 0));
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
            return 0;
        }

        // Run from anywhere but its own folder (usually Downloads), Nevatch offers to install itself, or to replace
        // the installed copy (a fresh download may carry a new key). True when the installed copy took over.
        static bool OfferInstall()
        {
            if (Installer.IsInstalled) return false;
            bool replace = Installer.InstalledCopyExists;
            if (!replace && Settings.Get("askinstall") == "no") return false;

            DialogResult answer;
            using (var dialog = new InstallDialog(replace)) answer = dialog.ShowDialog();
            if (answer == DialogResult.No && !replace) Settings.Set("askinstall", "no");
            if (answer != DialogResult.Yes) return false;
            try
            {
                Installer.Install();
                Process.Start(new ProcessStartInfo(Installer.Exe, Admin.RestartArgs(false, false, null)) { UseShellExecute = false });
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not install Nevatch: " + ex.Message + "\n\nIt runs from here this time.", "Nevatch",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }

    public static class Admin
    {
        public static bool IsElevated
        {
            get
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // Arguments for a copy of Nevatch that takes over from this one (it waits for this one to close).
        public static string RestartArgs(bool connect, bool minimized, string extra)
        {
            return "--relaunched --data \"" + Paths.DataDir.TrimEnd('\\') + "\"" +
                   (connect ? " --connect" : "") + (minimized ? " --minimized" : "") + (extra ?? "");
        }

        // Starts Nevatch again with administrator rights (Windows asks first). False if the person said no.
        public static bool Relaunch(bool minimized)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Paths.Exe, RestartArgs(true, minimized, null)) { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return false; // ERROR_CANCELLED: "No" in the Windows prompt
                throw;
            }
        }
    }

    // Installs Nevatch into its own folder (no administrator rights needed), with Start menu and desktop shortcuts
    // and an entry in Windows' list of apps, so it can be removed like any other program.
    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Nevatch";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr data, int flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nevatch"); }
        }
        public static string Exe { get { return Path.Combine(Dir, "Nevatch.exe"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nevatch.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nevatch.lnk"); } }

        public static bool IsInstalled
        {
            get { return string.Equals(Path.GetFullPath(Paths.Exe), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); }
        }

        public static bool InstalledCopyExists { get { return File.Exists(Exe); } }

        // Copies this Nevatch.exe (with its key) into the folder, replacing an older one.
        public static void Install()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Exe, File.ReadAllBytes(Paths.Exe));
            CreateShortcut(StartMenuLink);
            CreateShortcut(DesktopLink);

            Version v = typeof(Installer).Assembly.GetName().Version;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Nevatch");
                key.SetValue("DisplayVersion", v.Major + "." + v.Minor + "." + v.Build);
                key.SetValue("Publisher", "Nevatch");
                key.SetValue("DisplayIcon", Exe);
                key.SetValue("InstallLocation", Dir);
                key.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            }
            // "Start with Windows" pointed at the downloaded copy; point it here instead.
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run != null && run.GetValue("Nevatch") != null)
                    run.SetValue("Nevatch", "\"" + Exe + "\" --minimized", RegistryValueKind.String);
            }
        }

        // Removes everything: shortcuts, the apps-list entry, start with Windows, settings and servers, and the folder.
        public static void Uninstall()
        {
            SystemProxy.RecoverAfterCrash();
            XrayCore.KillLeftovers();
            foreach (string link in new[] { StartMenuLink, DesktopLink })
            {
                try { File.Delete(link); }
                catch { }
            }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run != null) run.DeleteValue("Nevatch", false);
            }
            try { Directory.Delete(Paths.DataDir, true); }
            catch { }
            // This program is still running from that folder and can't delete itself, but it can move itself into
            // the temp folder (Windows' disk cleanup empties that), and then the folder can go.
            if (IsInstalled)
            {
                try { File.Move(Paths.Exe, Path.Combine(Path.GetTempPath(), RemovedPrefix + Guid.NewGuid().ToString("N") + ".exe")); }
                catch { }
            }
            try { Directory.Delete(Dir, true); }
            catch { }
        }

        public const string RemovedPrefix = "Nevatch-removed-";

        static void CreateShortcut(string path)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(Exe);
                link.SetWorkingDirectory(Dir);
                link.SetDescription("Nevatch VPN");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    // Nevatch updates itself from the server it's connected to. Through the tunnel it asks
    // http://update.nevatch/check for the SHA-256 of the server's current Nevatch.exe (without anyone's key);
    // if that differs from its own, it downloads its fresh copy (with its key at the end) and restarts into it.
    // Only the real server can answer: the tunnel itself proves that (Reality checks the server's key).
    public static class Updater
    {
        public const string Host = "update.nevatch";
        const string Base = "http://" + Host + "/";
        static string ownHash;

        public static string OwnHash
        {
            get
            {
                if (ownHash == null)
                {
                    byte[] exe = File.ReadAllBytes(Paths.Exe);
                    ownHash = Sha256(exe, KeyStore.ProgramLength(exe));
                }
                return ownHash;
            }
        }

        // Returns the new Nevatch.exe, or null when this one is current. Throws when the server can't be asked.
        public static byte[] Fetch(int httpPort, string id) { return Fetch(httpPort, id, null); }

        // downloading: called (on this thread) once it's clear there is a new version, before the download starts.
        public static byte[] Fetch(int httpPort, string id, Action downloading)
        {
            string query = "?id=" + Uri.EscapeDataString(id);
            string answer = Encoding.UTF8.GetString(Get(Base + "check" + query, httpPort));
            Match m = Regex.Match(answer, "\"sha256\"\\s*:\\s*\"([0-9a-f]{64})\"");
            if (!m.Success) throw new InvalidDataException("The server doesn't offer updates.");
            if (m.Groups[1].Value == OwnHash) return null;

            if (downloading != null) downloading();
            byte[] exe = Get(Base + "download" + query, httpPort);
            if (exe.Length < 4096 || exe[0] != 'M' || exe[1] != 'Z' || Sha256(exe, KeyStore.ProgramLength(exe)) != m.Groups[1].Value)
                throw new InvalidDataException("The downloaded update was damaged.");
            return exe;
        }

        // A running .exe can be renamed but not overwritten: move this one aside, put the new one in its place, start it.
        public static void Install(byte[] exe, string args)
        {
            string current = Paths.Exe, fresh = current + ".new", old = current + ".old";
            File.WriteAllBytes(fresh, exe);
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try
            {
                File.Move(fresh, current);
            }
            catch
            {
                File.Move(old, current);
                throw;
            }
            Process.Start(new ProcessStartInfo(current, args) { UseShellExecute = false });
        }

        // The previous version, left behind by Install, and a copy moved aside by an earlier uninstall.
        public static void CleanUp()
        {
            var leftovers = new List<string> { Paths.Exe + ".old", Paths.Exe + ".new" };
            try { leftovers.AddRange(Directory.GetFiles(Path.GetTempPath(), Installer.RemovedPrefix + "*.exe")); }
            catch { }
            foreach (string leftover in leftovers)
            {
                try { if (File.Exists(leftover)) File.Delete(leftover); }
                catch { }
            }
        }

        static byte[] Get(string url, int httpPort)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = new WebProxy("127.0.0.1", httpPort);
            request.Timeout = 30000;
            request.ReadWriteTimeout = 120000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream body = response.GetResponseStream())
            {
                var buffer = new MemoryStream();
                body.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        static string Sha256(byte[] data, int length)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data, 0, length)).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class Paths
    {
        public static readonly string Exe = typeof(Paths).Assembly.Location;
        // Passed on with --data when Nevatch restarts with administrator rights, which can be another Windows account.
        public static string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nevatch");

        public static string CoreDir { get { return Path.Combine(DataDir, "core"); } }
        public static string Servers { get { return Path.Combine(DataDir, "servers.txt"); } }
        public static string OldKey { get { return Path.Combine(DataDir, "key.txt"); } }
        public static string Config { get { return Path.Combine(DataDir, "config.json"); } }
        public static string ProxyBackup { get { return Path.Combine(DataDir, "proxy-backup.txt"); } }
        public static string Settings { get { return Path.Combine(DataDir, "settings.txt"); } }
        public static string Apps { get { return Path.Combine(DataDir, "apps.txt"); } }
    }

    // Proxy: the Windows proxy setting, which browsers and most apps follow (no administrator rights needed).
    // WholePc and Apps: a virtual network adapter (Wintun) that catches everything, games and UDP included;
    // Apps then sends only the chosen programs through the VPN and everything else direct.
    public enum VpnMode { Proxy, WholePc, Apps }

    // The programs (and folders of programs) that use the VPN in "only chosen apps" mode, one per line in apps.txt.
    // A folder ends with a backslash and covers every program inside it, which suits games with launchers.
    public static class AppList
    {
        public static List<string> Load()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(Paths.Apps)) return list;
                foreach (string line in File.ReadAllLines(Paths.Apps, Encoding.UTF8))
                {
                    string path = line.Trim();
                    if (path.Length > 0 && path.IndexOfAny(Path.GetInvalidPathChars()) < 0) list.Add(path);
                }
            }
            catch { }
            return list;
        }

        public static void Add(string path)
        {
            List<string> list = Load();
            if (!list.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) list.Add(path);
            Save(list);
        }

        public static void Remove(string path)
        {
            List<string> list = Load();
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Save(list);
        }

        public static bool IsFolder(string path) { return path.EndsWith("\\"); }

        public static string Title(string path)
        {
            if (IsFolder(path)) return "Folder: " + Path.GetFileName(path.TrimEnd('\\'));
            try
            {
                string description = File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
                if (!string.IsNullOrEmpty(description) && description.Trim().Length > 0) return description.Trim();
            }
            catch { }
            return Path.GetFileName(path);
        }

        static void Save(List<string> list)
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllLines(Paths.Apps, list.ToArray(), new UTF8Encoding(false));
        }
    }

    // settings.txt: name=value lines.
    public static class Settings
    {
        public static bool AutoConnect
        {
            get { return Get("autoconnect") == "1"; }
            set { Set("autoconnect", value ? "1" : "0"); }
        }

        public static VpnMode Mode
        {
            get
            {
                switch (Get("mode"))
                {
                    case "pc": return VpnMode.WholePc;
                    case "apps": return VpnMode.Apps;
                    default: return VpnMode.Proxy;
                }
            }
            set { Set("mode", value == VpnMode.WholePc ? "pc" : value == VpnMode.Apps ? "apps" : "proxy"); }
        }

        public static string Get(string name)
        {
            string value;
            return Read().TryGetValue(name, out value) ? value : null;
        }

        public static void Set(string name, string value)
        {
            Dictionary<string, string> all = Read();
            if (value == null) all.Remove(name);
            else all[name] = value;
            var lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in all) lines.Add(kv.Key + "=" + kv.Value);
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllLines(Paths.Settings, lines.ToArray(), new UTF8Encoding(false));
        }

        static Dictionary<string, string> Read()
        {
            var result = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(Paths.Settings)) return result;
                foreach (string line in File.ReadAllLines(Paths.Settings, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return result;
        }
    }

    public class Server
    {
        public readonly string Link;
        public readonly VlessKey Key;

        public Server(string link)
        {
            Link = link.Trim();
            Key = VlessKey.Parse(Link);
        }

        // A key's user id is unique per person per server, so a newer key with the same id updates that server.
        public string Id { get { return Key.Id.ToLowerInvariant(); } }

        public string Title { get { return Key.Port == 443 ? Key.Host : Key.Host + ":" + Key.Port; } }

        public string Details
        {
            get { return (string.IsNullOrEmpty(Key.Name) ? "" : "Key for " + Key.Name + " · ") + "looks like a visit to " + Key.Sni; }
        }
    }

    // The servers your friend can connect to, one key per line in servers.txt; settings.txt remembers
    // which one is picked. "Add key" adds one. The key the server put at the end of Nevatch.exe is
    // added the first time that exe runs, so a fresh download adds (or updates) its server too.
    public static class ServerList
    {
        public static List<Server> Load()
        {
            var list = new List<Server>();
            try
            {
                if (!File.Exists(Paths.Servers)) return list;
                foreach (string line in File.ReadAllLines(Paths.Servers, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) continue;
                    try { list.Add(new Server(line)); }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        public static Server Current()
        {
            List<Server> list = Load();
            string picked = Settings.Get("server");
            foreach (Server s in list)
                if (s.Id == picked) return s;
            return list.Count > 0 ? list[0] : null;
        }

        // Adds the key (or updates the server with the same id) and picks it. Throws if the key is bad.
        public static Server Add(string link)
        {
            var server = new Server(link);
            List<Server> list = Load();
            int at = list.FindIndex(s => s.Id == server.Id);
            if (at >= 0) list[at] = server;
            else list.Add(server);
            Save(list);
            Pick(server);
            return server;
        }

        public static void Remove(Server server)
        {
            List<Server> list = Load();
            list.RemoveAll(s => s.Id == server.Id);
            Save(list);
        }

        public static void Pick(Server server)
        {
            Settings.Set("server", server.Id);
        }

        // Brings in the key built into this Nevatch.exe, and a key.txt from Nevatch 1.2, oldest first
        // so the newest ends up picked.
        public static void ImportKeys()
        {
            var found = new List<KeyValuePair<DateTime, string>>();
            string builtIn = KeyStore.Embedded();
            if (builtIn != null && Settings.Get("builtin") != Fingerprint(builtIn))
                found.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(Paths.Exe), builtIn));
            if (File.Exists(Paths.OldKey))
            {
                try { found.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(Paths.OldKey), File.ReadAllText(Paths.OldKey))); }
                catch { }
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<DateTime, string> key in found)
            {
                try { Add(key.Value); }
                catch { }
            }
            // Remember the built-in key even if it was broken, so removing its server sticks.
            if (builtIn != null) Settings.Set("builtin", Fingerprint(builtIn));
            try { File.Delete(Paths.OldKey); }
            catch { }
        }

        static string Fingerprint(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.Trim())), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        static void Save(List<Server> list)
        {
            var lines = new List<string>();
            foreach (Server s in list) lines.Add(s.Link);
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllLines(Paths.Servers, lines.ToArray(), new UTF8Encoding(false));
        }
    }

    // The server puts the person's key at the end of Nevatch.exe when you download it from the console.
    public static class KeyStore
    {
        // Layout at the end of the file: [key as UTF-8][key length: 4 bytes little-endian]["FVPNKEY1"]
        const string Magic = "FVPNKEY1";

        public static string Embedded()
        {
            try
            {
                using (var file = new FileStream(Paths.Exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (file.Length < 12) return null;
                    var tail = new byte[12];
                    file.Seek(-12, SeekOrigin.End);
                    ReadExactly(file, tail);
                    if (Encoding.ASCII.GetString(tail, 4, 8) != Magic) return null;
                    int length = BitConverter.ToInt32(tail, 0);
                    if (length <= 0 || length > 8192 || length > file.Length - 12) return null;
                    var key = new byte[length];
                    file.Seek(-12 - length, SeekOrigin.End);
                    ReadExactly(file, key);
                    return Encoding.UTF8.GetString(key);
                }
            }
            catch { return null; }
        }

        // Length of the program itself, without a key at the end (what the server's update check compares).
        public static int ProgramLength(byte[] exe)
        {
            if (exe.Length < 12 || Encoding.ASCII.GetString(exe, exe.Length - 8, 8) != Magic) return exe.Length;
            int length = BitConverter.ToInt32(exe, exe.Length - 12);
            return length > 0 && length <= exe.Length - 12 ? exe.Length - 12 - length : exe.Length;
        }

        static void ReadExactly(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
        }
    }

    // xray.exe and wintun.dll (the network adapter for whole-PC mode, which Xray loads from its own folder)
    // travel inside Nevatch.exe as gzip resources and are unpacked into %LOCALAPPDATA%\Nevatch\core\<hash>\
    // so a newer Nevatch.exe brings its own copies.
    public static class EmbeddedXray
    {
        static readonly string[] Files = { "wintun.dll", "xray.exe" };

        public static string Extract()
        {
            var packed = new List<byte[]>();
            var all = new MemoryStream();
            foreach (string name in Files)
            {
                using (Stream resource = typeof(EmbeddedXray).Assembly.GetManifestResourceStream(name + ".gz"))
                {
                    if (resource == null) throw new InvalidOperationException("This Nevatch.exe was built without " + name + " inside.");
                    var buffer = new MemoryStream();
                    resource.CopyTo(buffer);
                    packed.Add(buffer.ToArray());
                    buffer.WriteTo(all);
                }
            }

            string id;
            using (SHA256 sha = SHA256.Create())
                id = BitConverter.ToString(sha.ComputeHash(all.ToArray()), 0, 6).Replace("-", "").ToLowerInvariant();
            string dir = Path.Combine(Paths.CoreDir, id);
            string exe = Path.Combine(dir, "xray.exe");
            if (File.Exists(exe)) return exe;

            Directory.CreateDirectory(dir);
            // xray.exe last: its presence means the folder is complete.
            for (int i = 0; i < Files.Length; i++)
            {
                string target = Path.Combine(dir, Files[i]);
                string temp = target + ".tmp";
                using (var input = new GZipStream(new MemoryStream(packed[i]), CompressionMode.Decompress))
                using (FileStream output = File.Create(temp))
                    input.CopyTo(output);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
            }

            foreach (string old in Directory.GetDirectories(Paths.CoreDir))
            {
                if (string.Equals(Path.GetFileName(old), id, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(old, true); } catch { }
            }
            return exe;
        }
    }

    // A share link: vless://ID@HOST:PORT?security=reality&sni=...&pbk=...&sid=...&fp=...&flow=...#NAME
    public class VlessKey
    {
        public string Id, Host, Sni, PublicKey, ShortId, Fingerprint, Flow, Name;
        public int Port;

        public static VlessKey Parse(string text)
        {
            string s = (text ?? "").Trim();
            if (!s.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("This is not a Nevatch key. It should start with vless://");
            s = s.Substring(8);
            var key = new VlessKey();

            int hash = s.IndexOf('#');
            if (hash >= 0)
            {
                key.Name = Uri.UnescapeDataString(s.Substring(hash + 1));
                s = s.Substring(0, hash);
            }

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int q = s.IndexOf('?');
            if (q >= 0)
            {
                foreach (string pair in s.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) query[pair.Substring(0, eq)] = Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
                s = s.Substring(0, q);
            }
            s = s.TrimEnd('/');

            int at = s.LastIndexOf('@');
            if (at <= 0) throw Incomplete();
            key.Id = Uri.UnescapeDataString(s.Substring(0, at));
            string hostPort = s.Substring(at + 1);
            string portText;
            if (hostPort.StartsWith("["))
            {
                int close = hostPort.IndexOf(']');
                if (close < 0) throw Incomplete();
                key.Host = hostPort.Substring(1, close - 1);
                portText = hostPort.Substring(close + 1).TrimStart(':');
            }
            else
            {
                int colon = hostPort.LastIndexOf(':');
                if (colon < 0) throw Incomplete();
                key.Host = hostPort.Substring(0, colon);
                portText = hostPort.Substring(colon + 1);
            }
            if (!int.TryParse(portText, out key.Port) || key.Port < 1 || key.Port > 65535) throw Incomplete();

            if (!string.Equals(Get(query, "security"), "reality", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("This key does not use Reality, which Nevatch needs.");
            key.Sni = Get(query, "sni");
            key.PublicKey = Get(query, "pbk");
            key.ShortId = Get(query, "sid") ?? "";
            key.Fingerprint = Get(query, "fp") ?? "chrome";
            key.Flow = Get(query, "flow") ?? "";
            if (key.Host.Length == 0 || key.Id.Length < 8 || key.Sni == null || key.PublicKey == null) throw Incomplete();
            return key;
        }

        static Exception Incomplete()
        {
            return new FormatException("The key is incomplete. Copy the whole key again.");
        }

        static string Get(Dictionary<string, string> query, string name)
        {
            string value;
            return query.TryGetValue(name, out value) && value.Length > 0 ? value : null;
        }
    }

    public static class XrayConfig
    {
        // Russian sites go direct: many of them (banks, Gosuslugi) refuse visitors from abroad.
        static readonly string[] DirectDomains = {
            "domain:ru", "domain:su", "domain:xn--p1ai", "domain:xn--80adxhks",
            "domain:vk.com", "domain:vk.me", "domain:userapi.com", "domain:vkuser.net",
            "domain:yandex.com", "domain:yandex.net", "domain:yastatic.net", "domain:mycdn.me",
        };
        static readonly string[] LocalNets = {
            "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
            "192.168.0.0/16", "::1/128", "fc00::/7", "fe80::/10",
            // Multicast and broadcast: local discovery (printers, casting, name lookups) that the adapter also catches.
            "224.0.0.0/4", "255.255.255.255/32", "ff00::/8",
        };

        // With the adapter, Xray answers every DNS lookup itself (whichever DNS server Windows asked), resolving
        // through the VPN so nothing on the way can tamper with the answers. It hands out IPv4 addresses only:
        // the server may not have IPv6, and over the adapter a failed IPv6 connection doesn't fall back to IPv4.
        static readonly string[] TunDns = { "1.1.1.1", "8.8.8.8" };

        // apiPort: when not 0, Xray listens there (on this PC only) so its rules can be swapped while it runs.
        public static string Build(VlessKey k, int socksPort, int httpPort, VpnMode mode, List<string> apps, int apiPort = 0)
        {
            string sniff = "{\"enabled\": true, \"destOverride\": [\"http\", \"tls\"], \"routeOnly\": true}";
            var sb = new StringBuilder();
            sb.AppendLine("{");
            // No access log: it would list every site visited, and flood the Log page in whole-PC mode.
            sb.AppendLine("  \"log\": {\"loglevel\": \"warning\", \"access\": \"none\"},");
            if (apiPort != 0)
                sb.AppendLine("  \"api\": {\"tag\": \"api\", \"listen\": \"127.0.0.1:" + apiPort + "\", \"services\": [\"RoutingService\"]},");
            sb.AppendLine("  \"inbounds\": [");
            if (mode != VpnMode.Proxy)
            {
                // Xray adds the routes itself and keeps its own connection to the server off the adapter.
                sb.AppendLine("    {\"tag\": \"tun\", \"protocol\": \"tun\", \"settings\": {\"name\": \"Nevatch\", \"mtu\": 1500, " +
                              "\"gateway\": [\"172.19.77.1/30\"], \"dns\": " + JList(TunDns) + ", " +
                              "\"autoSystemRoutingTable\": [\"0.0.0.0/0\", \"::/0\"], \"autoOutboundsInterface\": \"auto\"}, " +
                              "\"sniffing\": {\"enabled\": true, \"destOverride\": [\"http\", \"tls\", \"quic\"], \"routeOnly\": true}},");
            }
            sb.AppendLine("    {\"tag\": \"socks\", \"listen\": \"127.0.0.1\", \"port\": " + socksPort +
                          ", \"protocol\": \"socks\", \"settings\": {\"udp\": true}, \"sniffing\": " + sniff + "},");
            sb.AppendLine("    {\"tag\": \"http\", \"listen\": \"127.0.0.1\", \"port\": " + httpPort +
                          ", \"protocol\": \"http\", \"sniffing\": " + sniff + "}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"outbounds\": [");
            sb.AppendLine("    {\"tag\": \"proxy\", \"protocol\": \"vless\",");
            sb.AppendLine("     \"settings\": {\"vnext\": [{\"address\": " + J(k.Host) + ", \"port\": " + k.Port +
                          ", \"users\": [{\"id\": " + J(k.Id) + ", \"encryption\": \"none\", \"flow\": " + J(k.Flow) + "}]}]},");
            sb.AppendLine("     \"streamSettings\": {\"network\": \"tcp\", \"security\": \"reality\", \"realitySettings\": {" +
                          "\"serverName\": " + J(k.Sni) + ", \"fingerprint\": " + J(k.Fingerprint) +
                          ", \"publicKey\": " + J(k.PublicKey) + ", \"shortId\": " + J(k.ShortId) + ", \"spiderX\": \"/\"}}},");
            sb.AppendLine("    {\"tag\": \"direct\", \"protocol\": \"freedom\"},");
            if (mode != VpnMode.Proxy)
            {
                // A (IPv4) lookups go to the built-in resolver below; AAAA (IPv6) get an empty answer;
                // anything else (SRV records some games use, for example) is passed on through the VPN.
                sb.AppendLine("    {\"tag\": \"dns-out\", \"protocol\": \"dns\", \"settings\": {\"rules\": [" +
                              "{\"action\": \"return\", \"qType\": 28, \"rCode\": 0}, " +
                              "{\"action\": \"direct\", \"qType\": \"2-27,29-65535\"}]}, " +
                              "\"streamSettings\": {\"sockopt\": {\"dialerProxy\": \"proxy\"}}},");
            }
            sb.AppendLine("    {\"tag\": \"block\", \"protocol\": \"blackhole\"}");
            sb.AppendLine("  ],");
            if (mode != VpnMode.Proxy)
                sb.AppendLine("  \"dns\": {\"servers\": " + JList(TunDns) + ", \"queryStrategy\": \"UseIPv4\", \"tag\": \"dns-in\"},");
            sb.AppendLine("  \"routing\": {\"domainStrategy\": \"AsIs\", \"rules\": [");
            sb.AppendLine("    " + string.Join(",\n    ", Rules(mode, apps).ToArray()));
            sb.AppendLine("  ]}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Rules for "only chosen apps" while disconnected: the adapter stays up (taking it down would cut every app's
        // open connections), but everything goes direct, lookups included, as if Nevatch weren't there.
        public static string OffRules()
        {
            return "{\"routing\": {\"rules\": [\n" +
                   "  {\"inboundTag\": [\"tun\"], \"port\": \"53\", \"outboundTag\": \"direct\"},\n" +
                   "  {\"ip\": " + JList(LocalNets) + ", \"outboundTag\": \"direct\"},\n" +
                   "  {\"inboundTag\": [\"tun\"], \"outboundTag\": \"direct\"}\n" +
                   "]}}";
        }

        // The connected rules on their own, for swapping them into a running Xray.
        public static string OnRules(VpnMode mode, List<string> apps)
        {
            return "{\"routing\": {\"rules\": [\n  " + string.Join(",\n  ", Rules(mode, apps).ToArray()) + "\n]}}";
        }

        static List<string> Rules(VpnMode mode, List<string> apps)
        {
            // Rules are tried in order; anything no rule catches goes through the VPN (the first outbound).
            var rules = new List<string>();
            if (mode != VpnMode.Proxy)
            {
                rules.Add("{\"inboundTag\": [\"tun\"], \"port\": \"53\", \"outboundTag\": \"dns-out\"}");
                rules.Add("{\"inboundTag\": [\"dns-in\"], \"outboundTag\": \"proxy\"}");
            }
            rules.Add("{\"ip\": " + JList(LocalNets) + ", \"outboundTag\": \"direct\"}");
            rules.Add("{\"domain\": " + JList(DirectDomains) + ", \"outboundTag\": \"direct\"}");
            // Programs only get IPv4 addresses, but one could still use an IPv6 address it knows; refuse that.
            if (mode == VpnMode.WholePc)
            {
                rules.Add("{\"inboundTag\": [\"tun\"], \"ip\": [\"::/0\"], \"outboundTag\": \"block\"}");
            }
            else if (mode == VpnMode.Apps)
            {
                string chosen = JList(ProcessMatchers(apps));
                if (apps.Count > 0)
                {
                    rules.Add("{\"inboundTag\": [\"tun\"], \"process\": " + chosen + ", \"ip\": [\"::/0\"], \"outboundTag\": \"block\"}");
                    rules.Add("{\"inboundTag\": [\"tun\"], \"process\": " + chosen + ", \"outboundTag\": \"proxy\"}");
                }
                rules.Add("{\"inboundTag\": [\"tun\"], \"outboundTag\": \"direct\"}");
            }
            return rules;
        }

        // Xray matches programs by full path (forward slashes, case-sensitive), by folder (ending in /),
        // or by name without ".exe". Programs get both path and name, in case Windows reports the path in other letter case.
        static string[] ProcessMatchers(List<string> apps)
        {
            var result = new List<string>();
            foreach (string app in apps)
            {
                string path = app.Replace('\\', '/');
                if (path.Length > 1 && path[1] == ':') path = char.ToUpperInvariant(path[0]) + path.Substring(1);
                result.Add(path);
                if (!AppList.IsFolder(app) && app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    result.Add(Path.GetFileNameWithoutExtension(app));
            }
            return result.ToArray();
        }

        static string J(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string JList(string[] items)
        {
            var parts = new string[items.Length];
            for (int i = 0; i < items.Length; i++) parts[i] = J(items[i]);
            return "[" + string.Join(", ", parts) + "]";
        }
    }

    // Points the Windows proxy setting at Xray, and puts the previous setting back afterwards,
    // even if Nevatch crashed last time (the backup file is checked at startup).
    // It uses a setup script (PAC) that Nevatch itself serves, not a fixed proxy address: if Nevatch is killed
    // (an antivirus, a crash) the script can't be loaded and Windows simply goes direct, and if only Xray stops
    // the script's "; DIRECT" does the same. A fixed address would leave the PC without internet until Nevatch ran again.
    public static class SystemProxy
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        static readonly string[] Names = { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL" };
        // Nevatch 1.4 and older set a fixed proxy and saved only these.
        static readonly string[] OldNames = { "ProxyEnable", "ProxyServer", "ProxyOverride" };

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);

        public static void Enable(string pacUrl)
        {
            Directory.CreateDirectory(Paths.DataDir);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                Dictionary<string, string> backup = ReadBackup();
                if (backup == null)
                {
                    backup = new Dictionary<string, string> { { "v", "2" } };
                    foreach (string name in Names)
                    {
                        object value = key.GetValue(name);
                        if (value != null) backup[name] = Convert.ToString(value);
                    }
                }
                backup["ours"] = pacUrl;
                WriteBackup(backup);

                key.SetValue("AutoConfigURL", pacUrl, RegistryValueKind.String);
                // Off, so that when the script can't be loaded Windows goes direct rather than to some old fixed proxy.
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
            Notify();
        }

        public static void Restore()
        {
            Dictionary<string, string> backup = ReadBackup();
            if (backup == null) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                string ours;
                backup.TryGetValue("ours", out ours);
                bool v2 = backup.ContainsKey("v");
                // Only undo our own change; if something else changed the proxy since, leave it alone.
                if (ours != null && ours == key.GetValue(v2 ? "AutoConfigURL" : "ProxyServer") as string)
                {
                    string value;
                    int enable;
                    key.SetValue("ProxyEnable",
                        backup.TryGetValue("ProxyEnable", out value) && int.TryParse(value, out enable) ? enable : 0,
                        RegistryValueKind.DWord);
                    foreach (string name in v2 ? Names : OldNames)
                    {
                        if (name == "ProxyEnable") continue;
                        if (backup.TryGetValue(name, out value)) key.SetValue(name, value, RegistryValueKind.String);
                        else key.DeleteValue(name, false);
                    }
                }
            }
            File.Delete(Paths.ProxyBackup);
            Notify();
        }

        public static void RecoverAfterCrash()
        {
            try { Restore(); } catch { }
        }

        static Dictionary<string, string> ReadBackup()
        {
            if (!File.Exists(Paths.ProxyBackup)) return null;
            var result = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(Paths.ProxyBackup, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) result[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            return result;
        }

        static void WriteBackup(Dictionary<string, string> values)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in values) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(Paths.ProxyBackup, lines.ToArray(), new UTF8Encoding(false));
        }

        static void Notify()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); // INTERNET_OPTION_SETTINGS_CHANGED
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); // INTERNET_OPTION_REFRESH
        }
    }

    // Serves the proxy setup script for SystemProxy on 127.0.0.1, for as long as Nevatch runs.
    public class PacServer
    {
        TcpListener listener;

        public string Url { get; private set; }

        public void Start(int httpPort)
        {
            Stop();
            byte[] body = Encoding.ASCII.GetBytes(Script(httpPort));
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\n" +
                "Cache-Control: no-store\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            TcpListener l;
            try
            {
                l = new TcpListener(IPAddress.Loopback, 10810);
                l.Start();
            }
            catch (SocketException)
            {
                l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
            }
            listener = l;
            Url = "http://127.0.0.1:" + ((IPEndPoint)l.LocalEndpoint).Port + "/nevatch.pac";
            new Thread(() =>
            {
                while (true)
                {
                    TcpClient client;
                    try { client = l.AcceptTcpClient(); }
                    catch { return; } // stopped
                    ThreadPool.QueueUserWorkItem(_ => Answer(client, head, body));
                }
            }) { IsBackground = true, Name = "PAC" }.Start();
        }

        public void Stop()
        {
            if (listener == null) return;
            listener.Stop();
            listener = null;
        }

        static void Answer(TcpClient client, byte[] head, byte[] body)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    // Every request gets the script; just read up to the end of its headers first.
                    var request = new byte[8192];
                    int length = 0, read;
                    while (length < request.Length && (read = stream.Read(request, length, request.Length - length)) > 0)
                    {
                        length += read;
                        if (Encoding.ASCII.GetString(request, 0, length).Contains("\r\n\r\n")) break;
                    }
                    stream.Write(head, 0, head.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
            catch { }
        }

        // Local addresses go direct, everything else to Xray, and direct as well if Xray doesn't answer.
        static string Script(int httpPort)
        {
            var local = new List<string> { "\"127.*\"", "\"10.*\"", "\"192.168.*\"", "\"169.254.*\"" };
            for (int i = 16; i <= 31; i++) local.Add("\"172." + i + ".*\"");
            return "function FindProxyForURL(url, host) {\n" +
                   "  if (isPlainHostName(host) || host == \"localhost\") return \"DIRECT\";\n" +
                   "  var local = [" + string.Join(", ", local.ToArray()) + "];\n" +
                   "  for (var i = 0; i < local.length; i++) if (shExpMatch(host, local[i])) return \"DIRECT\";\n" +
                   "  return \"PROXY 127.0.0.1:" + httpPort + "; DIRECT\";\n" +
                   "}\n";
        }
    }

    // Xray runs inside a Windows job tied to Nevatch, so if Nevatch is killed (an antivirus, Task Manager), Windows
    // stops Xray too. Otherwise whole-PC mode would stay on with nothing left to turn it off.
    static class ChildJob
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        static IntPtr job;

        // Never closed: Windows closes it when Nevatch exits, however that happens, which stops everything in it.
        public static bool Add(Process p)
        {
            if (job == IntPtr.Zero)
            {
                IntPtr j = CreateJobObject(IntPtr.Zero, null);
                if (j == IntPtr.Zero) return false;
                var info = new ExtendedLimits();
                info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(j, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimits)))) return false;
                job = j;
            }
            return AssignProcessToJobObject(job, p.Handle);
        }
    }

    public class XrayCore
    {
        readonly List<string> log = new List<string>();
        Process process;
        volatile bool stopping;
        // "Only chosen apps": Xray (and its adapter) keeps running while disconnected, with everything going direct,
        // because taking the adapter down or up cuts the open connections of every app on the PC.
        string xrayPath, runningKey;
        VpnMode runningMode;
        int apiPort;
        bool off; // running with OffRules: Nevatch shows Disconnected

        public int SocksPort { get; private set; }
        public int HttpPort { get; private set; }
        public event EventHandler UnexpectedExit;

        static XrayCore()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }

        public string[] LogLines
        {
            get { lock (log) return log.ToArray(); }
        }

        void AddLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (log)
            {
                log.Add(DateTime.Now.ToString("HH:mm:ss  ") + line);
                if (log.Count > 400) log.RemoveAt(0);
            }
        }

        public void Note(string line) { AddLog(line); }

        string ReportBlocked()
        {
            AddLog("The antivirus blocked xray.exe. Exclude these folders in it: " + Paths.DataDir +
                   (Installer.IsInstalled ? " and " + Installer.Dir : ""));
            return "Your antivirus blocked Nevatch's VPN part. In Windows Security, open Protection history, " +
                   "restore it, and add an exclusion for the Nevatch folders (see the Log page).";
        }

        static string KeyText(VlessKey k)
        {
            return k.Host + "|" + k.Port + "|" + k.Id + "|" + k.Flow + "|" + k.Sni + "|" + k.PublicKey + "|" + k.ShortId + "|" + k.Fingerprint;
        }

        bool RunningApps
        {
            get
            {
                Process p = process;
                try { return p != null && !p.HasExited && runningMode == VpnMode.Apps && apiPort != 0; }
                catch { return false; }
            }
        }

        // Replaces all of the running Xray's rules. Returns null when it worked.
        string SwapRules(string rulesJson)
        {
            try
            {
                string file = Path.Combine(Paths.DataDir, "rules.json");
                File.WriteAllText(file, rulesJson, new UTF8Encoding(false));
                var psi = new ProcessStartInfo(xrayPath, "api adrules -s 127.0.0.1:" + apiPort + " -t 3 \"" + file + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(xrayPath);
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return "Xray didn't answer"; }
                    if (p.ExitCode == 0) return null;
                    string text = (err.Result + " " + output).Trim();
                    return text.Length > 0 ? text : "exit code " + p.ExitCode;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Disconnect. In "only chosen apps" mode Xray keeps running with everything going direct, so other apps
        // aren't cut off; otherwise (or if that fails) it stops.
        public void Pause()
        {
            if (RunningApps && !off)
            {
                string error = SwapRules(XrayConfig.OffRules());
                if (error == null)
                {
                    off = true;
                    AddLog("Chosen apps now go direct. Nevatch's adapter stays on, so other apps keep their connections.");
                    return;
                }
                AddLog("Could not switch the chosen apps to direct (" + error + "), so Xray stops instead.");
            }
            if (!off) Stop();
        }

        // While connected in "only chosen apps" mode: uses the new list right away, without reconnecting.
        // Returns null when it worked.
        public string SetApps(List<string> apps)
        {
            if (!RunningApps || off) return "not connected";
            string error = SwapRules(XrayConfig.OnRules(VpnMode.Apps, apps));
            if (error == null) AddLog("Chosen apps: " + string.Join(", ", apps.ToArray()));
            return error;
        }

        // Returns null when Xray is up, otherwise a message for the user.
        public string Start(VlessKey key, VpnMode mode, List<string> apps)
        {
            // Already running for these apps and this server, just switched off: switch it back on in place.
            if (off && mode == VpnMode.Apps && RunningApps && runningKey == KeyText(key))
            {
                string swapError = SwapRules(XrayConfig.OnRules(mode, apps));
                if (swapError == null)
                {
                    off = false;
                    AddLog("Switched on again, server " + key.Host + ":" + key.Port + ", chosen apps: " + string.Join(", ", apps.ToArray()) +
                           ". Apps that were already open keep their direct connections until they reconnect.");
                    return null;
                }
                AddLog("Could not switch the chosen apps back on (" + swapError + "), so Xray restarts.");
            }
            Stop();

            string xray;
            try { xray = EmbeddedXray.Extract(); }
            catch (Exception ex)
            {
                int code = Marshal.GetHRForException(ex) & 0xFFFF;
                if (code == 225 || code == 226) return ReportBlocked();
                return "Could not unpack Xray: " + ex.Message;
            }

            Directory.CreateDirectory(Paths.DataDir);
            SocksPort = FreePort(10808, 0);
            HttpPort = FreePort(10809, SocksPort);
            int api = mode == VpnMode.Apps ? FreePort(10810, HttpPort) : 0;
            if (api != 0 && api == SocksPort) api = FreePort(SocksPort, SocksPort); // any free port
            File.WriteAllText(Paths.Config, XrayConfig.Build(key, SocksPort, HttpPort, mode, apps, api), new UTF8Encoding(false));
            xrayPath = xray;
            runningKey = KeyText(key);
            runningMode = mode;
            apiPort = api;
            off = false;

            var psi = new ProcessStartInfo(xray, "run -c \"" + Paths.Config + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Path.GetDirectoryName(xray);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => AddLog(e.Data);
            p.ErrorDataReceived += (s, e) => AddLog(e.Data);
            p.Exited += (s, e) =>
            {
                if (stopping) return;
                AddLog("xray stopped");
                EventHandler handler = UnexpectedExit;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            stopping = false;
            AddLog("Starting, server " + key.Host + ":" + key.Port +
                   (mode == VpnMode.WholePc ? ", whole PC" : mode == VpnMode.Apps ? ", chosen apps: " + string.Join(", ", apps.ToArray()) : ""));
            try { p.Start(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // ERROR_VIRUS_INFECTED / ERROR_VIRUS_DELETED: Windows Security (or another antivirus) blocked it.
                if (ex.NativeErrorCode == 225 || ex.NativeErrorCode == 226) return ReportBlocked();
                return "Could not start Xray: " + ex.Message;
            }
            catch (Exception ex) { return "Could not start Xray: " + ex.Message; }
            try
            {
                if (!ChildJob.Add(p)) AddLog("Note: Xray will keep running if Nevatch is killed.");
            }
            catch { }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            process = p;

            for (int i = 0; i < 60; i++)
            {
                if (p.HasExited)
                {
                    process = null;
                    return "Xray stopped right away. See Log for details.";
                }
                if (IsListening(HttpPort)) return null;
                Thread.Sleep(100);
            }
            Stop();
            return "Xray did not start in time.";
        }

        public void Stop()
        {
            stopping = true;
            off = false;
            apiPort = 0;
            Process p = process;
            process = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
            p.Dispose();
        }

        // Loads a tiny page through the tunnel; returns the time it took in ms, or throws.
        public long TestTunnel()
        {
            var request = (HttpWebRequest)WebRequest.Create("https://www.gstatic.com/generate_204");
            request.Proxy = new WebProxy("127.0.0.1", HttpPort);
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            var watch = Stopwatch.StartNew();
            using (request.GetResponse()) { }
            return watch.ElapsedMilliseconds;
        }

        // An Xray we unpacked, left running by a crash, would hold our ports.
        public static void KillLeftovers()
        {
            string ours = Path.GetFullPath(Paths.CoreDir) + Path.DirectorySeparatorChar;
            foreach (Process p in Process.GetProcessesByName("xray"))
            {
                try
                {
                    if (p.MainModule.FileName.StartsWith(ours, StringComparison.OrdinalIgnoreCase)) p.Kill();
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        static int FreePort(int preferred, int avoid)
        {
            if (preferred != avoid && CanBind(preferred)) return preferred;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        static bool CanBind(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }

        static bool IsListening(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(300)) return false;
                    client.EndConnect(result);
                    return true;
                }
            }
            catch { return false; }
        }
    }

    // Measures lag while connected, once a second: through the tunnel to the server itself (over one kept-open
    // connection, the way a game's traffic travels) and, for comparison, straight to Yandex in Russia without the VPN.
    // Comparing the two tells whether lag comes from this PC's own internet or from the way to the server.
    public class LagMeter
    {
        public const int Samples = 120; // two minutes
        public const int Lost = -1;
        const int TunnelTimeout = 3000, SetupTimeout = 10000, DirectTimeout = 1500;
        static readonly IPAddress DirectTarget = IPAddress.Parse("77.88.8.8"); // Yandex DNS, answers pings

        readonly Action<string> log;
        readonly List<int> vpn = new List<int>(), direct = new List<int>();
        volatile bool running;
        volatile int generation; // each Start gets its own; a thread from an earlier one stops by itself
        long lastAnswer; // DateTime ticks of the server's last answer through the tunnel; 0 = none since Start
        int socksPort;
        string id;
        DateTime lastSpikeNote = DateTime.MinValue;

        public LagMeter(Action<string> log) { this.log = log; }

        public bool Running { get { return running; } }

        // When the server last answered through the tunnel; DateTime.MinValue if it hasn't since Start.
        public DateTime LastAnswer { get { return new DateTime(Interlocked.Read(ref lastAnswer)); } }

        public void Start(int socksPort, string id)
        {
            Stop();
            this.socksPort = socksPort;
            this.id = id;
            Interlocked.Exchange(ref lastAnswer, 0);
            lock (vpn) { vpn.Clear(); direct.Clear(); }
            running = true;
            int gen = ++generation;
            new Thread(() => Run(gen)) { IsBackground = true, Name = "Lag meter" }.Start();
        }

        // The thread notices within a few seconds and closes its connection.
        public void Stop()
        {
            running = false;
            generation++;
        }

        // Oldest first; Lost where nothing came back.
        public void Snapshot(out int[] vpnMs, out int[] directMs)
        {
            lock (vpn)
            {
                vpnMs = vpn.ToArray();
                directMs = direct.ToArray();
            }
        }

        void Run(int gen)
        {
            IPAddress source = PhysicalAddress();
            Socket tunnel = null;
            string report = ""; // the previous measurement, sent along so the server's console can show it
            int round = 0;
            while (running && gen == generation)
            {
                var watch = Stopwatch.StartNew();
                if (++round % 30 == 0) source = PhysicalAddress(); // Wi-Fi may have changed
                IPAddress from = source;
                Task<int> d = Task.Run(() => Icmp.Ping(from, DirectTarget, DirectTimeout));
                int v = PingTunnel(ref tunnel, report);
                int dm = d.Result;
                lock (vpn)
                {
                    if (!running || gen != generation) break;
                    if (v != Lost) Interlocked.Exchange(ref lastAnswer, DateTime.Now.Ticks);
                    vpn.Add(v);
                    direct.Add(dm);
                    if (vpn.Count > Samples) { vpn.RemoveAt(0); direct.RemoveAt(0); }
                }
                NoteSpike(v, dm);
                report = "&vpn=" + v + "&direct=" + dm;
                int wait = 1000 - (int)watch.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
            }
            Close(ref tunnel);
        }

        // Writes noticeable lag to the Log (at most every 15 s), so the times can be sent along with "Copy log".
        void NoteSpike(int v, int d)
        {
            int[] recent, unused;
            Snapshot(out recent, out unused);
            int usual = Median(recent);
            bool spike = v == Lost || (usual > 0 && v > usual * 2 && v > usual + 150);
            if (!spike || DateTime.Now - lastSpikeNote < TimeSpan.FromSeconds(15)) return;
            lastSpikeNote = DateTime.Now;
            log("Lag: " + (v == Lost ? "no answer through the VPN for " + TunnelTimeout / 1000 + " s" : v + " ms through the VPN") +
                (usual > 0 ? " (usually " + usual + " ms)" : "") +
                ", without the VPN: " + (d == Lost ? "no answer" : d + " ms") + ".");
        }

        // One request over the kept-open connection; reconnects (untimed) after a failure.
        int PingTunnel(ref Socket tunnel, string report)
        {
            try
            {
                if (tunnel == null)
                {
                    tunnel = OpenTunnel();
                    Request(tunnel, ""); // the first answer includes setting up the connection, so it isn't counted
                    tunnel.ReceiveTimeout = tunnel.SendTimeout = TunnelTimeout;
                }
                var watch = Stopwatch.StartNew();
                Request(tunnel, report);
                return (int)watch.ElapsedMilliseconds;
            }
            catch
            {
                Close(ref tunnel);
                return Lost;
            }
        }

        // A SOCKS5 connection through Xray to update.nevatch:80, which the server answers itself.
        Socket OpenTunnel()
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                s.NoDelay = true;
                s.ReceiveTimeout = s.SendTimeout = SetupTimeout;
                s.Connect(IPAddress.Loopback, socksPort);
                s.Send(new byte[] { 5, 1, 0 });
                byte[] hello = ReadExactly(s, 2);
                if (hello[0] != 5 || hello[1] != 0) throw new IOException("SOCKS refused");
                byte[] host = Encoding.ASCII.GetBytes(Updater.Host);
                var connect = new List<byte> { 5, 1, 0, 3, (byte)host.Length };
                connect.AddRange(host);
                connect.Add(0);
                connect.Add(80);
                s.Send(connect.ToArray());
                byte[] reply = ReadExactly(s, 4);
                if (reply[1] != 0) throw new IOException("SOCKS connect failed");
                int rest = reply[3] == 1 ? 4 : reply[3] == 4 ? 16 : ReadExactly(s, 1)[0];
                ReadExactly(s, rest + 2);
                return s;
            }
            catch
            {
                s.Close();
                throw;
            }
        }

        void Request(Socket s, string report)
        {
            s.Send(Encoding.ASCII.GetBytes("GET /ping?id=" + Uri.EscapeDataString(id) + report + " HTTP/1.1\r\nHost: " + Updater.Host + "\r\n\r\n"));
            // Headers up to the blank line, then Content-Length bytes of body.
            var head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n"))
            {
                head.Append((char)ReadExactly(s, 1)[0]);
                if (head.Length > 4096) throw new IOException("bad answer");
            }
            Match m = Regex.Match(head.ToString(), @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (!head.ToString().StartsWith("HTTP/1.1 200") || !m.Success) throw new IOException("the server doesn't answer lag checks");
            ReadExactly(s, int.Parse(m.Groups[1].Value));
        }

        static byte[] ReadExactly(Socket s, int n)
        {
            var buffer = new byte[n];
            for (int got = 0; got < n; )
            {
                int read = s.Receive(buffer, got, n - got, SocketFlags.None);
                if (read == 0) throw new IOException("closed");
                got += read;
            }
            return buffer;
        }

        static void Close(ref Socket s)
        {
            if (s != null) try { s.Close(); } catch { }
            s = null;
        }

        // The address of the real network adapter (not Nevatch's own), so the direct check stays outside the VPN
        // in whole-PC mode too: Windows sends from that address only through that adapter.
        static IPAddress PhysicalAddress()
        {
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface nic in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || nic.Name == "Nevatch") continue;
                    System.Net.NetworkInformation.IPInterfaceProperties props = nic.GetIPProperties();
                    bool gateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gateway = true;
                    if (!gateway) continue;
                    foreach (var a in props.UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address;
                }
            }
            catch { }
            return IPAddress.Any;
        }

        public static int Median(int[] samples)
        {
            var ok = new List<int>();
            foreach (int s in samples) if (s != Lost) ok.Add(s);
            if (ok.Count == 0) return 0;
            ok.Sort();
            return ok[ok.Count / 2];
        }

        // What the numbers mean, for the Lag page. Looks at the last minute.
        public static void Describe(int[] vpnMs, int[] directMs, out string title, out string body, out bool bad)
        {
            int n = Math.Min(60, vpnMs.Length);
            var v = new int[n];
            var d = new int[n];
            Array.Copy(vpnMs, vpnMs.Length - n, v, 0, n);
            Array.Copy(directMs, directMs.Length - n, d, 0, n);
            bad = false;
            if (n == 0)
            {
                title = "Measuring…";
                body = "The first numbers appear in a few seconds.";
                return;
            }
            int last = v[n - 1];
            title = last == Lost ? "Through the VPN: no answer" : "Through the VPN: " + last + " ms";

            int vUsual = Median(v), dUsual = Median(d);
            int vWorst = 0, vLost = 0, vSpikes = 0, dLost = 0, dSpikes = 0;
            for (int i = 0; i < n; i++)
            {
                if (v[i] == Lost) vLost++;
                else { vWorst = Math.Max(vWorst, v[i]); if (v[i] > vUsual + 200) vSpikes++; }
                if (d[i] == Lost) dLost++;
                else if (d[i] > dUsual + 200) dSpikes++;
            }
            bool directWorks = dLost < n;
            body = "Last minute: usually " + vUsual + " ms, worst " + vWorst + " ms" +
                   (vLost > 0 ? ", " + vLost + " s without an answer" : "") + ". " +
                   (directWorks ? "Without the VPN: usually " + dUsual + " ms" + (dLost > 0 ? ", " + dLost + " s without an answer" : "") + ".\n"
                                : "(Yandex didn't answer the check without the VPN, so only the VPN is measured.)\n");
            if (n < 10)
            {
                body += "Still measuring…";
                return;
            }
            bool vpnLags = vLost > 1 || vSpikes >= 3;
            // Yandex is close to anyone in Russia, so even a steady 150 ms without the VPN means a slow connection.
            bool ownLags = directWorks && (dLost > 2 || dSpikes >= 3 || dUsual > 150);
            bad = vpnLags || ownLags || vUsual > 300;
            if (ownLags)
                body += "Your own internet is lagging too, even without the VPN: that's your Wi-Fi or your internet provider, not the VPN.";
            else if (vpnLags)
                body += "Your own internet is steady, but the way to the server lags: its home connection may be busy, " +
                        "or your provider is slowing down foreign connections.";
            else if (vUsual > 300)
                body += "Steady, but slow: the way to the server is long. Games will feel it; browsing is fine.";
            else
                body += "Looks good.";
        }
    }

    // Pings from a chosen local address (IcmpSendEcho2Ex); .NET's own Ping can't pick the address.
    static class Icmp
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern IntPtr IcmpCreateFile();
        [DllImport("iphlpapi.dll")]
        static extern bool IcmpCloseHandle(IntPtr handle);
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern int IcmpSendEcho2Ex(IntPtr handle, IntPtr evt, IntPtr apcRoutine, IntPtr apcContext, uint source,
            uint destination, byte[] data, short dataSize, IntPtr options, byte[] reply, int replySize, int timeout);

        // Round trip in ms, or LagMeter.Lost.
        public static int Ping(IPAddress source, IPAddress target, int timeout)
        {
            IntPtr handle = IcmpCreateFile();
            if (handle == new IntPtr(-1)) return LagMeter.Lost;
            try
            {
                var data = new byte[32];
                var reply = new byte[512];
                int count = IcmpSendEcho2Ex(handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    BitConverter.ToUInt32(source.GetAddressBytes(), 0), BitConverter.ToUInt32(target.GetAddressBytes(), 0),
                    data, (short)data.Length, IntPtr.Zero, reply, reply.Length, timeout);
                // ICMP_ECHO_REPLY: Address, Status (0 = success), RoundTripTime, ...
                if (count == 0 || BitConverter.ToUInt32(reply, 4) != 0) return LagMeter.Lost;
                return (int)BitConverter.ToUInt32(reply, 8);
            }
            finally
            {
                IcmpCloseHandle(handle);
            }
        }
    }

    public class MainForm : ShellForm
    {
        // Reconnecting: the connection broke and Nevatch is waiting to try again (the internet goes direct meanwhile).
        enum State { Disconnected, Connecting, Connected, Reconnecting }

        const string AutostartName = "Nevatch";
        const int AppsPage = 1, ServersPage = 3, LogPageIndex = 4; // sidebar: Home, Apps, Lag, Servers, Log, Settings, Monitor, About
        // After a break: the first try after 2 s (most breaks are short), then every 10 s, for at most 10 minutes.
        const int FirstRetry = 2, RetryEvery = 10;
        static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);
        const int PoorMs = 250, SteadyMs = 200; // usual ping above which the connection counts as poor / steady again

        readonly XrayCore core = new XrayCore();
        readonly PacServer pac = new PacServer();
        readonly HeroView hero = new HeroView();
        readonly PillButton connect = new PillButton();
        readonly InfoCard keyCard = new InfoCard(Glyph.Globe, "Add key");
        readonly RowList serverList = new RowList(84, Glyph.Key, "No servers yet",
            "Ask the person who runs your server for a key, then click Add key and paste it.");
        readonly ChoiceRow proxyMode = new ChoiceRow("Browser and most apps",
            "Uses the Windows proxy setting. Games usually ignore it.", false, true, false);
        readonly ChoiceRow pcMode = new ChoiceRow("Whole PC",
            "Everything, games included. Windows asks for permission when you connect.", false, true, false);
        readonly ChoiceRow appsMode = new ChoiceRow("Only chosen apps",
            "Only the apps you add below. Windows asks for permission when you connect.", false, true, false);
        readonly ButtonRow appButtons;
        readonly RowList appList = new RowList(64, Glyph.Apps, "No apps chosen yet",
            "Click Add app and pick the game's .exe, or Add folder to pick the game's whole folder.");
        readonly InfoCard appsCard = new InfoCard(Glyph.Info, null);
        readonly LogPage logPage = new LogPage();
        readonly MiniLog homeLog = new MiniLog();
        readonly LagMeter lag;
        readonly Page lagPage = new Page("Lag");
        readonly InfoCard lagCard = new InfoCard(Glyph.Speed, null);
        readonly LagGraph lagGraph = new LagGraph(LagMeter.Samples) { MainName = "Through the VPN", OtherName = "Without the VPN (Yandex)" };
        readonly MonitorPage monitorPage = new MonitorPage();
        readonly ToolStripItem trayToggle;
        readonly ToolStripMenuItem trayServers = new ToolStripMenuItem("Server");
        readonly System.Windows.Forms.Timer logTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
        readonly System.Windows.Forms.Timer aliveTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        readonly System.Windows.Forms.Timer retryTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly bool startMinimized, connectNow, updated;
        State state = State.Disconnected;
        int retryAttempt; // 0 = not reconnecting; otherwise how many tries have been scheduled since the break
        DateTime retryAt; // when the next try starts
        string retryReason; // what went wrong, shown in the red bar
        DateTime brokeAt; // when the connection first broke, for "Reconnected after …"
        VpnMode connectedMode;
        int connection; // counts connects, so a check that finishes late knows whether it's still about this one
        DateTime lastAlive; // when the connection last proved it works
        bool checkingAlive;
        DateTime lastUpdateCheck = DateTime.MinValue;
        bool updating;
        bool poor; // connected, but the last seconds had lost answers or a high ping (orange)
        bool gaveUp; // reconnecting ran out of time; it starts again when the network comes back
        bool startupConnect; // the connect Nevatch starts by itself: if it fails, it keeps trying like after a break

        public MainForm(bool startMinimized, bool connectNow, bool updated) : base("Nevatch", "Nevatch", "")
        {
            this.startMinimized = startMinimized;
            this.connectNow = connectNow;
            this.updated = updated;
            lag = new LagMeter(core.Note);
            if (startMinimized) Opacity = 0;

            connect.Click += (s, e) => Toggle();
            keyCard.Button.Click += (s, e) => AddKey();
            keyCard.Cursor = Cursors.Hand;
            keyCard.Click += (s, e) => ShowPage(ServersPage);
            keyCard.Button.Primary = true;
            homeLog.OpenLog = () => ShowPage(LogPageIndex);
            var home = new HomePage(hero, connect, keyCard, homeLog);

            var servers = new Page("Servers");
            var add = new OutlineButton { Text = "Add key" };
            add.Click += (s, e) => AddKey();
            servers.Add(add, 44, 150);
            servers.Add(serverList, -1);

            var apps = new Page("What uses the VPN");
            proxyMode.Click += (s, e) => SetMode(VpnMode.Proxy);
            pcMode.Click += (s, e) => SetMode(VpnMode.WholePc);
            appsMode.Click += (s, e) => SetMode(VpnMode.Apps);
            apps.Add(proxyMode, 70);
            apps.Add(pcMode, 70);
            apps.Add(appsMode, 70);
            var addApp = new OutlineButton { Text = "Add app" };
            addApp.Click += (s, e) => AddApp();
            var addFolder = new OutlineButton { Text = "Add folder" };
            addFolder.Click += (s, e) => AddFolder();
            appButtons = apps.Add(new ButtonRow(addApp, addFolder), 44);
            apps.Add(appList, -1);

            var settings = new Page("Settings");
            var autostart = new Toggle("Start with Windows", "Nevatch starts next to the clock when you sign in.");
            autostart.Checked = StartWithWindows.IsOn(AutostartName);
            autostart.CheckedChanged += (s, e) =>
            {
                try { StartWithWindows.Set(AutostartName, autostart.Checked); }
                catch (Exception ex) { core.Note("Could not change start with Windows: " + ex.Message); }
            };
            settings.Add(autostart, 84);
            var autoconnect = new Toggle("Connect automatically", "Connect as soon as Nevatch starts.");
            autoconnect.Checked = Settings.AutoConnect;
            autoconnect.CheckedChanged += (s, e) =>
            {
                try { Settings.AutoConnect = autoconnect.Checked; }
                catch (Exception ex) { core.Note("Could not save the setting: " + ex.Message); }
            };
            settings.Add(autoconnect, 84);
            settings.Add(appsCard, 110);
            var direct = new InfoCard(Glyph.Info, null);
            direct.Set("Local sites go direct",
                "Russian sites (.ru, VK, Yandex) don't go through the VPN, so local banks and services keep working.");
            settings.Add(direct, 100);

            lagPage.Add(lagCard, 150);
            lagPage.Add(lagGraph, -1);
            // The monitor only measures while someone can see it.
            VisibleChanged += (s, e) => { if (!Visible) monitorPage.Stop(); };

            AddPage(Glyph.Home, "Home", home);
            AddPage(Glyph.Apps, "Apps", apps);
            AddPage(Glyph.Speed, "Lag", lagPage);
            AddPage(Glyph.Globe, "Servers", servers);
            AddPage(Glyph.Log, "Log", logPage);
            AddPage(Glyph.Settings, "Settings", settings);
            AddPage(MonitorPage.Icon, "Monitor", monitorPage);
            AddPage(Glyph.Info, "About", new AboutPage("Nevatch", "A private VPN, run by a friend you trust."));

            logTimer.Tick += (s, e) =>
            {
                if (logPage.Visible) logPage.SetLines(core.LogLines);
                if (homeLog.Visible) homeLog.SetLines(core.LogLines);
                if (lagPage.Visible) ShowLag();
                ShowQuality();
            };
            lagPage.VisibleChanged += (s, e) => { if (lagPage.Visible) ShowLag(); };
            logPage.VisibleChanged += (s, e) => { if (logPage.Visible) logPage.SetLines(core.LogLines); };
            logTimer.Start();
            updateTimer.Tick += (s, e) => { if (DateTime.Now - lastUpdateCheck > TimeSpan.FromHours(6)) CheckForUpdate(); };
            updateTimer.Start();
            aliveTimer.Tick += (s, e) => CheckAlive();
            aliveTimer.Start();
            retryTimer.Tick += (s, e) => RetryTick();
            // After sleep the network needs a moment to come back before the connection can prove itself again.
            SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode != PowerModes.Resume) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    lastAlive = DateTime.Now;
                    if (state == State.Reconnecting) RetrySoon(5);
                    else if (gaveUp && state == State.Disconnected) ResumeRetrying("The PC woke from sleep.", 5);
                });
            };
            // When Wi-Fi or the cable comes back, try again right away instead of waiting out the countdown.
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) =>
            {
                if (!e.IsAvailable) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (state == State.Reconnecting) RetrySoon(2);
                        else if (gaveUp && state == State.Disconnected) ResumeRetrying("The network came back.", 2);
                    });
                }
                catch { }
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Nevatch", null, (s, e) => ShowFromTray());
            trayToggle = menu.Items.Add("", null, (s, e) => Toggle());
            menu.Items.Add(trayServers);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => Quit());
            menu.Opening += (s, e) => FillTrayServers();
            Tray.ContextMenuStrip = menu;
            TrayHint = "It keeps running next to the clock. Right-click the shield there and choose Quit to close it.";

            // While connecting, Start itself reports Xray stopping, so only a running connection counts here.
            core.UnexpectedExit += (s, e) => BeginInvoke((MethodInvoker)delegate
            {
                if (state == State.Connected) ConnectionLost("The VPN part (Xray) stopped unexpectedly.");
            });
            SystemEvents.SessionEnding += (s, e) => Shutdown();

            SetState(State.Disconnected, null, false);
            ShowServers();
            ShowMode();
            ShowApps();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startMinimized)
            {
                HideToTray(false);
                Opacity = 1;
            }
            if (updated)
            {
                Version v = typeof(MainForm).Assembly.GetName().Version;
                core.Note("Updated to Nevatch " + v.Major + "." + v.Minor + "." + v.Build + ".");
                Notify("Nevatch was updated", "You now have version " + v.Major + "." + v.Minor + "." + v.Build + ".", ToastKind.Success, 6000);
            }
            if (connectNow || (Settings.AutoConnect && ServerList.Current() != null))
            {
                startupConnect = true;
                Toggle();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) Shutdown();
        }

        void Shutdown()
        {
            retryTimer.Stop();
            monitorPage.Stop();
            lag.Stop();
            core.Stop();
            try { SystemProxy.Restore(); } catch { }
            pac.Stop();
        }

        // message: shown under the status; null means the normal text for that state.
        void SetState(State next, string message, bool isError)
        {
            state = next;
            if (next != State.Connected)
            {
                poor = false;
                hero.Degraded = false;
                hero.SetBadge(Color.Empty, null);
            }
            switch (next)
            {
                case State.Connected:
                    hero.Set(HeroMode.On, "Connected", message ?? "Your connection is protected.", isError);
                    connect.SetLook(Glyph.Stop, "Disconnect", Theme.SlateLight, Theme.Slate);
                    ShowPresence(isError ? Theme.Yellow : poor ? Theme.Orange : Theme.Green,
                        isError ? "Connected, with problems" : poor ? "Connected, unstable" : "Connected");
                    break;
                case State.Connecting:
                    hero.Set(HeroMode.Busy, retryAttempt > 0 ? "Reconnecting…" : "Connecting…", message ?? "One moment…", isError);
                    connect.SetLook(null, "Please wait…", Theme.AccentLight, Theme.AccentDark);
                    ShowPresence(Theme.Yellow, retryAttempt > 0 ? "Reconnecting…" : "Connecting…");
                    break;
                case State.Reconnecting:
                    hero.Set(HeroMode.Busy, "Reconnecting…", message, isError);
                    connect.SetLook(Glyph.Stop, "Stop trying", Theme.SlateLight, Theme.Slate);
                    ShowPresence(Theme.Yellow, "Reconnecting…");
                    break;
                default:
                    hero.Set(isError ? HeroMode.Error : HeroMode.Off, "Disconnected", message ?? "Your connection is not protected.", isError);
                    connect.SetLook(Glyph.Play, "Connect", Theme.AccentLight, Theme.AccentDark);
                    ShowPresence(isError ? Theme.Red : Theme.SlateLight, "Not connected");
                    break;
            }
            connect.Enabled = next != State.Connecting;
            serverList.Enabled = next != State.Connecting;
            trayToggle.Text = next == State.Connected ? "Disconnect" : next == State.Reconnecting ? "Stop reconnecting" : "Connect";
            trayToggle.Enabled = next != State.Connecting;
            Tray.Text = next == State.Connected ? "Nevatch: connected" :
                        next == State.Reconnecting || (next == State.Connecting && retryAttempt > 0) ? "Nevatch: reconnecting…" : "Nevatch: not connected";
            TrayGreen = next == State.Connected;
            appsCard.Set("Other apps",
                "Browsers and most apps use Nevatch automatically while it's connected. Apps with their own proxy " +
                "setting (like Telegram) can use SOCKS5, server 127.0.0.1, port " +
                (state == State.Connected ? core.SocksPort.ToString() : "10808") + ".");
        }

        Color presenceDot = Theme.SlateLight;
        string presenceText = "Not connected", presenceName = "Nevatch";

        // The status panel at the bottom of the sidebar: the server's name and how the connection is.
        void ShowPresence(Color dot, string text)
        {
            presenceDot = dot;
            presenceText = text;
            SetPresence(presenceDot, presenceName, presenceText);
        }

        // Refreshes the Servers list and the card on Home after the list or the pick changed.
        void ShowServers()
        {
            List<Server> list = ServerList.Load();
            Server current = ServerList.Current();
            var rows = new List<Control>();
            foreach (Server server in list)
            {
                Server s = server;
                var row = new ChoiceRow(s.Title, s.Details, current != null && s.Id == current.Id);
                // Later, not inside the click: picking or removing rebuilds the rows, this one included.
                row.Click += (o, e) => BeginInvoke((MethodInvoker)delegate { PickServer(s); });
                row.RemoveClicked += (o, e) => BeginInvoke((MethodInvoker)delegate { RemoveServer(s); });
                rows.Add(row);
            }
            serverList.SetRows(rows);
            presenceName = current != null ? current.Title : "Nevatch";
            SetPresence(presenceDot, presenceName, presenceText);

            if (current == null)
                keyCard.Set("No server yet.", "Click Add key and paste the key you received.");
            else if (list.Count == 1)
                keyCard.Set("Server: " + current.Title, "Got a key for another server? Click Add key.");
            else
                keyCard.Set("Server: " + current.Title, list.Count + " servers. Click here to pick another one.");
        }

        void ShowLag()
        {
            int[] vpn, direct;
            lag.Snapshot(out vpn, out direct);
            lagGraph.SetData(vpn, direct);
            if (!lag.Running)
            {
                lagCard.Set("Not connected", "Connect, then come back here to see live how much lag there is, " +
                    "and whether it comes from your own internet or from the way to the server.");
                return;
            }
            string title, body;
            bool bad;
            LagMeter.Describe(vpn, direct, out title, out body, out bad);
            lagCard.Set(title, body);
        }

        void ShowMode()
        {
            VpnMode mode = Settings.Mode;
            proxyMode.Selected = mode == VpnMode.Proxy;
            pcMode.Selected = mode == VpnMode.WholePc;
            appsMode.Selected = mode == VpnMode.Apps;
            appButtons.Visible = appList.Visible = mode == VpnMode.Apps;
        }

        void ShowApps()
        {
            var rows = new List<Control>();
            foreach (string app in AppList.Load())
            {
                string path = app;
                var row = new ChoiceRow(AppList.Title(path), path, false, false, true);
                row.RemoveClicked += (o, e) => BeginInvoke((MethodInvoker)delegate { RemoveApp(path); });
                rows.Add(row);
            }
            appList.SetRows(rows);
        }

        void FillTrayServers()
        {
            trayServers.DropDownItems.Clear();
            Server current = ServerList.Current();
            foreach (Server server in ServerList.Load())
            {
                Server s = server;
                var item = new ToolStripMenuItem(s.Title) { Checked = current != null && s.Id == current.Id };
                item.Click += (o, e) => PickServer(s);
                trayServers.DropDownItems.Add(item);
            }
            if (trayServers.DropDownItems.Count > 0) trayServers.DropDownItems.Add(new ToolStripSeparator());
            trayServers.DropDownItems.Add("Add key…", null, (o, e) => AddKey());
            trayServers.Enabled = state != State.Connecting;
        }

        void Toggle()
        {
            if (state == State.Connecting) return;
            gaveUp = false;
            if (state == State.Reconnecting)
            {
                core.Note("Stopped reconnecting.");
                StopRetrying();
                SetState(State.Disconnected, null, false);
                return;
            }
            StopRetrying();
            if (state == State.Connected)
            {
                core.Note("Disconnected.");
                Disconnect(null);
                return;
            }
            Connect(false);
        }

        // retry: an automatic try after the connection broke, so no dialogs, and failures schedule the next try.
        async void Connect(bool retry)
        {
            bool keepTrying = retry || startupConnect; // failures schedule another try instead of stopping
            startupConnect = false;
            Server server = ServerList.Current();
            if (server == null)
            {
                if (retry) { GiveUp("There is no server to connect to. Add a key on the Servers page."); return; }
                server = AskForKey();
                if (server == null) return;
            }
            VlessKey key = server.Key;
            VpnMode mode = Settings.Mode;
            List<string> apps = AppList.Load();
            if (mode == VpnMode.Apps && apps.Count == 0)
            {
                StopRetrying();
                ShowFromTray();
                ShowPage(AppsPage);
                SetState(State.Disconnected, "Choose at least one app on the Apps page first.", true);
                return;
            }
            // The network adapter needs administrator rights, so Nevatch restarts with them and connects.
            if (mode != VpnMode.Proxy && !Admin.IsElevated)
            {
                if (retry) { GiveUp("This mode needs administrator permission. Click Connect to give it."); return; }
                if (startMinimized && !Visible) ShowFromTray();
                if (MessageBox.Show(this, (mode == VpnMode.WholePc ? "Whole PC" : "Only chosen apps") +
                        " mode needs administrator permission to set up Nevatch's network adapter.\n\n" +
                        "Windows will ask you now. Nevatch then restarts with that permission and connects.",
                        "Nevatch", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return;
                bool restarted;
                try { restarted = Admin.Relaunch(!Visible); }
                catch (Exception ex)
                {
                    SetState(State.Disconnected, "Could not restart Nevatch: " + ex.Message, true);
                    return;
                }
                if (restarted) Quit();
                else SetState(State.Disconnected, "Windows didn't give permission. Pick \"Browser and most apps\" on the Apps page to connect without it.", true);
                return;
            }

            SetState(State.Connecting, (retry ? "Reconnecting to " : "Connecting to ") + key.Host +
                (retry ? "… (try " + retryAttempt + ")" : "…"), false);
            string error = await Task.Run(() => core.Start(key, mode, apps));
            if (error != null)
            {
                core.Note("Could not connect: " + error);
                if (keepTrying) ScheduleRetry(error);
                else Failed(error);
                return;
            }
            if (mode == VpnMode.Proxy)
            {
                try
                {
                    pac.Start(core.HttpPort);
                    SystemProxy.Enable(pac.Url);
                }
                catch (Exception ex)
                {
                    core.Stop();
                    pac.Stop();
                    string problem = "Could not change the Windows proxy setting: " + ex.Message;
                    core.Note(problem);
                    if (keepTrying) ScheduleRetry(problem);
                    else Failed(problem);
                    return;
                }
            }
            connectedMode = mode;
            int conn = ++connection;
            lastAlive = DateTime.Now;
            SetState(State.Connected, "Checking the connection…", false);
            lag.Start(core.SocksPort, key.Id);

            try
            {
                long ms = await Task.Run(() => core.TestTunnel());
                lastAlive = DateTime.Now;
                if (state != State.Connected || conn != connection) return;
                string what = mode == VpnMode.WholePc ? "Your whole PC is protected" :
                              mode == VpnMode.Apps ? "Your chosen apps are protected" : "Your connection is protected";
                core.Note("Connected to " + key.Host + ". The test page loaded in " + ms + " ms.");
                Reconnected();
                // The ping is shown live in the badge under this, so it isn't repeated here.
                SetState(State.Connected, what + ".", false);
                // Every connect asks the server whether it has a newer Nevatch, so a new server build reaches
                // everyone the next time they connect.
                CheckForUpdate();
            }
            catch (Exception ex)
            {
                core.Note("Connection test failed: " + Describe(ex));
                if (state != State.Connected || conn != connection) return;
                // Nothing at all came back through the tunnel: drop it (so the internet works) and try again.
                if (lag.LastAnswer == DateTime.MinValue)
                    ConnectionLost("Could not reach the server (" + Describe(ex) + ").");
                else
                {
                    Reconnected();
                    SetState(State.Connected, "Connected, but the test page didn't load (" + Describe(ex) + ").", true);
                    ShowNotice(Color.FromArgb(176, 118, 18), "Connected, but the test page didn't load: " + Describe(ex), "Show log", () => ShowPage(LogPageIndex));
                }
            }
        }

        // A short, readable reason from a failed web request or socket.
        static string Describe(Exception ex)
        {
            var web = ex as WebException;
            if (web != null)
            {
                switch (web.Status)
                {
                    case WebExceptionStatus.Timeout: return "no answer within 15 s";
                    case WebExceptionStatus.ConnectFailure: return "the connection was refused";
                    case WebExceptionStatus.ConnectionClosed:
                    case WebExceptionStatus.KeepAliveFailure:
                    case WebExceptionStatus.ReceiveFailure: return "the connection was cut off";
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.ProxyNameResolutionFailure: return "the address could not be looked up";
                    case WebExceptionStatus.SecureChannelFailure:
                    case WebExceptionStatus.TrustFailure: return "the secure connection failed";
                }
            }
            return ex.Message.TrimEnd('.');
        }

        // A manual connect failed in a way retrying won't fix (antivirus, Xray not starting, proxy setting).
        void Failed(string error)
        {
            SetState(State.Disconnected, error, true);
            ShowNotice(Theme.RedDark, "Could not connect: " + error, "Show log", () => ShowPage(LogPageIndex));
        }

        // While connected, makes sure the connection still works. The server answers the lag check once a second;
        // when those answers stop (or a server never gives them), a test page is loaded through the tunnel, and if
        // that fails too, Nevatch drops the connection (so the internet works without the VPN) and reconnects.
        async void CheckAlive()
        {
            if (state != State.Connected || checkingAlive) return;
            if (lag.LastAnswer > lastAlive) lastAlive = lag.LastAnswer;
            if (DateTime.Now - lastAlive < TimeSpan.FromSeconds(15)) return;
            int conn = connection;
            checkingAlive = true;
            try
            {
                await Task.Run(() => core.TestTunnel());
                lastAlive = DateTime.Now;
            }
            catch (Exception ex)
            {
                if (state != State.Connected || conn != connection) return;
                if (DateTime.Now - lag.LastAnswer < TimeSpan.FromSeconds(15)) return; // the server answered meanwhile
                int quiet = (int)(DateTime.Now - lastAlive).TotalSeconds;
                core.Note("Lost the connection to the server: no answer for " + quiet + " s, and the test page failed (" + Describe(ex) + ").");
                ConnectionLost("No answer from the server for " + quiet + " s (" + Describe(ex) + ").");
            }
            finally
            {
                checkingAlive = false;
            }
        }

        // The connection broke (not someone clicking): drops it so the internet works directly, and reconnects.
        void ConnectionLost(string reason)
        {
            ScheduleRetry(reason);
        }

        // Tears the connection down and tries again: after 2 s, then every 10 s. After 10 minutes without success
        // it stops (Disconnected), and starts over by itself when the network comes back or the PC wakes up.
        void ScheduleRetry(string reason)
        {
            lag.Stop();
            core.Pause();
            try { SystemProxy.Restore(); } catch { }
            pac.Stop();
            if (retryAttempt == 0)
            {
                brokeAt = DateTime.Now;
                if (!Visible)
                    Notify("Connection lost", reason + " Reconnecting automatically.", ToastKind.Error, 8000);
            }
            else if (DateTime.Now - brokeAt >= GiveUpAfter)
            {
                RanOutOfTime(reason);
                return;
            }
            retryAttempt++;
            retryReason = reason;
            int delay = retryAttempt == 1 ? FirstRetry : RetryEvery;
            retryAt = DateTime.Now.AddSeconds(delay);
            core.Note("Connection lost: " + reason + " Trying again in " + delay + " s (try " + retryAttempt + ").");
            ShowNotice(Theme.RedDark, "Connection lost: " + reason, "Reconnect now", () => RetrySoon(0));
            ShowRetry();
            retryTimer.Start();
        }

        void ShowRetry()
        {
            int left = Math.Max(0, (int)Math.Ceiling((retryAt - DateTime.Now).TotalSeconds));
            int minutes = Math.Max(1, (int)Math.Ceiling((brokeAt + GiveUpAfter - DateTime.Now).TotalMinutes));
            SetState(State.Reconnecting, "Trying again in " + left + " s" + (retryAttempt > 1 ? " (try " + retryAttempt + ")" : "") +
                " · stops in " + minutes + " min. Internet works without the VPN meanwhile.", false);
        }

        // Reconnecting went on for 10 minutes without success: stop, and wait for the network to come back.
        void RanOutOfTime(string reason)
        {
            StopRetrying();
            gaveUp = true;
            core.Note("Stopped reconnecting after " + (int)GiveUpAfter.TotalMinutes + " minutes (last problem: " + reason.TrimEnd('.') +
                "). Nevatch tries again by itself when the network comes back.");
            SetState(State.Disconnected, "Could not reconnect for " + (int)GiveUpAfter.TotalMinutes + " minutes. Click Connect to try again.", true);
            ShowNotice(Theme.RedDark, "Stopped reconnecting: " + reason, "Try again", () => { if (state == State.Disconnected) Toggle(); });
            if (!Visible)
                Notify("Nevatch stopped reconnecting", "The server didn't answer for " + (int)GiveUpAfter.TotalMinutes +
                    " minutes. Open Nevatch and click Connect to try again.", ToastKind.Error, 10000);
        }

        // After giving up: the network came back or the PC woke up, so start a new round of tries.
        void ResumeRetrying(string why, int seconds)
        {
            gaveUp = false;
            core.Note(why + " Trying to reconnect.");
            HideNotice();
            brokeAt = DateTime.Now;
            retryAttempt = 1;
            retryReason = why;
            retryAt = DateTime.Now.AddSeconds(seconds);
            ShowRetry();
            retryTimer.Start();
        }

        // Once a second while connected: the live ping badge on Home, and orange when the connection is poor.
        // Looks at the last 10 s of the lag meter: 2+ lost answers, the latest lost, or a high usual ping = poor.
        void ShowQuality()
        {
            if (state != State.Connected || !lag.Running) return;
            int[] vpn, direct;
            lag.Snapshot(out vpn, out direct);
            if (vpn.Length == 0)
            {
                hero.SetBadge(Theme.SlateLight, "Measuring ping…");
                return;
            }
            int n = Math.Min(10, vpn.Length), lost = 0;
            var recent = new int[n];
            Array.Copy(vpn, vpn.Length - n, recent, 0, n);
            foreach (int v in recent) if (v == LagMeter.Lost) lost++;
            int usual = LagMeter.Median(recent);
            int m = Math.Min(3, n);
            var lastFew = new int[m];
            Array.Copy(recent, n - m, lastFew, 0, m);
            int now = LagMeter.Median(lastFew);
            bool dropping = recent[n - 1] == LagMeter.Lost || lost >= 2;
            bool slow = usual > (poor ? SteadyMs : PoorMs);
            bool bad = dropping || slow;
            string text = (now == 0 ? "No answer" : now + " ms") +
                          (dropping ? " · unstable" : slow ? " · slow" : usual <= 120 ? " · good" : " · okay");
            hero.SetBadge(bad ? Theme.Orange : Theme.GreenLight, text);
            if (bad == poor) return;
            poor = bad;
            hero.Degraded = bad;
            ShowPresence(bad ? Theme.Orange : Theme.Green, bad ? "Connected, unstable" : "Connected");
            core.Note(bad ? "The connection got " + (dropping ? "unstable (" + lost + " of the last " + n + " checks lost)." : "slow (usually " + usual + " ms).")
                          : "The connection is steady again (" + usual + " ms).");
        }

        void RetryTick()
        {
            if (state != State.Reconnecting) { retryTimer.Stop(); return; }
            if (DateTime.Now < retryAt) { ShowRetry(); return; }
            retryTimer.Stop();
            Connect(true);
        }

        // Moves the next try closer (network came back, woke from sleep, "Reconnect now").
        void RetrySoon(int seconds)
        {
            if (state != State.Reconnecting) return;
            DateTime soon = DateTime.Now.AddSeconds(seconds);
            if (soon < retryAt) retryAt = soon;
            RetryTick();
        }

        void StopRetrying()
        {
            retryTimer.Stop();
            retryAttempt = 0;
            HideNotice();
        }

        // Automatic reconnecting can't go on (no server, needs permission): stop, and say why.
        void GiveUp(string reason)
        {
            StopRetrying();
            core.Note("Stopped reconnecting: " + reason);
            SetState(State.Disconnected, reason, true);
            ShowNotice(Theme.RedDark, reason, null, null);
        }

        // The connection works again (or worked the first time): clears the reconnecting state and the red bar.
        void Reconnected()
        {
            gaveUp = false;
            if (retryAttempt > 0)
            {
                int took = (int)(DateTime.Now - brokeAt).TotalSeconds;
                core.Note("Reconnected after " + took + " s (" + retryAttempt + (retryAttempt == 1 ? " try" : " tries") + ").");
                if (!Visible) Notify("Reconnected", "The connection works again.", ToastKind.Success, 5000);
            }
            StopRetrying();
        }

        // Asks the connected server for a newer Nevatch; if there is one, swaps it in and restarts into it,
        // reconnecting the same way (with administrator rights carried over for the adapter modes).
        async void CheckForUpdate()
        {
            Server server = ServerList.Current();
            if (updating || state != State.Connected || server == null) return;
            updating = true;
            lastUpdateCheck = DateTime.Now;
            int port = core.HttpPort;
            string id = server.Key.Id;
            bool downloading = false;
            try
            {
                // First the whole download (checked against the server's hash), then install, then restart into it
                // with --connect so it connects again by itself.
                byte[] exe = await Task.Run(() => Updater.Fetch(port, id, () =>
                {
                    downloading = true;
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        core.Note("The server has a new version of Nevatch. Downloading it…");
                        if (state == State.Connected) SetState(State.Connected, "Downloading the new Nevatch from your server…", false);
                    }));
                }));
                if (exe == null) return;
                core.Note("The new Nevatch is downloaded. Installing it and reconnecting.");
                if (state == State.Connected) SetState(State.Connected, "Installing the new Nevatch and reconnecting…", false);
                // Reconnect after the restart unless they pressed Disconnect while it downloaded (a dropped
                // connection that's being retried still counts as connected).
                Updater.Install(exe, Admin.RestartArgs(state != State.Disconnected, !Visible, " --updated"));
                Quit();
            }
            catch (Exception ex)
            {
                core.Note("Update check: " + ex.Message);
                // Stay connected on this version; the next connect (or the 6-hourly check) tries again.
                if (downloading && state == State.Connected) SetState(State.Connected, "Connected. The update didn't download; it tries again next time.", false);
            }
            finally
            {
                updating = false;
            }
        }

        void Disconnect(string problem)
        {
            lag.Stop();
            core.Pause();
            try { SystemProxy.Restore(); } catch { }
            pac.Stop();
            SetState(State.Disconnected, problem, problem != null);
        }

        // While connected, switching servers or modes reconnects with the new choice.
        void Reconnect()
        {
            if (state != State.Connected) return;
            Disconnect(null);
            Toggle();
        }

        void SetMode(VpnMode mode)
        {
            if (state == State.Connecting || mode == Settings.Mode) return;
            try { Settings.Mode = mode; }
            catch (Exception ex) { core.Note("Could not save the setting: " + ex.Message); return; }
            ShowMode();
            // Leaving "only chosen apps" while disconnected: its adapter isn't needed any more.
            if (state == State.Disconnected) core.Stop();
            Reconnect();
        }

        void AddApp()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Pick the game or app that should use the VPN",
                Filter = "Programs (*.exe)|*.exe",
                Multiselect = true,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ChangeApps(() => { foreach (string file in dialog.FileNames) AppList.Add(Path.GetFullPath(file)); });
            }
        }

        void AddFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Pick the game's folder. Every program inside it, launchers included, will use the VPN.",
                ShowNewFolderButton = false,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string folder = Path.GetFullPath(dialog.SelectedPath).TrimEnd('\\') + "\\";
                if (Path.GetPathRoot(folder) == folder)
                {
                    MessageBox.Show(this, "Pick the game's own folder, not a whole drive.", "Nevatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ChangeApps(() => AppList.Add(folder));
            }
        }

        void RemoveApp(string path)
        {
            ChangeApps(() => AppList.Remove(path));
        }

        void ChangeApps(Action change)
        {
            try { change(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the list: " + ex.Message, "Nevatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowApps();
            if (state != State.Connected || connectedMode != VpnMode.Apps) return;
            // Swap the list into the running connection, so nothing (other apps included) gets cut off.
            List<string> apps = AppList.Load();
            string error = apps.Count > 0 ? core.SetApps(apps) : "no apps left";
            if (error == null) return;
            core.Note("Could not update the chosen apps in place (" + error + "), so Nevatch reconnects.");
            Reconnect();
        }

        void AddKey()
        {
            if (state == State.Connecting) return;
            if (AskForKey() != null) Reconnect();
        }

        void PickServer(Server server)
        {
            Server current = ServerList.Current();
            if (state == State.Connecting || (current != null && current.Id == server.Id)) return;
            try { ServerList.Pick(server); }
            catch (Exception ex) { core.Note("Could not save the setting: " + ex.Message); return; }
            ShowServers();
            Reconnect();
        }

        void RemoveServer(Server server)
        {
            if (state == State.Connecting) return;
            if (MessageBox.Show(this, "Remove " + server.Title + "?\n\nYou'll need its key again to add it back.", "Nevatch",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Server current = ServerList.Current();
            bool wasCurrent = current != null && current.Id == server.Id;
            try { ServerList.Remove(server); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not remove it: " + ex.Message, "Nevatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowServers();
            if (wasCurrent && state == State.Connected)
            {
                if (ServerList.Current() != null) Reconnect();
                else Disconnect(null);
            }
        }

        // Returns the added server, or null if cancelled.
        Server AskForKey()
        {
            ShowFromTray();
            using (var dialog = new KeyDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                Server server;
                try { server = ServerList.Add(dialog.KeyText); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the key: " + ex.Message, "Nevatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                core.Note("Added server " + server.Title);
                ShowServers();
                return server;
            }
        }
    }

    public class KeyDialog : Form
    {
        readonly TextBox box = new TextBox();
        public string KeyText { get { return box.Text.Trim(); } }

        public KeyDialog()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Add key";
            Icon = Art.ShieldIcon(Theme.S(32));
            Font = new Font("Segoe UI", 9.75F);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 214);

            var label = new Label
            {
                Text = "Paste the key you received (it starts with vless://)",
                Location = new Point(16, 16), AutoSize = true, Font = new Font("Segoe UI Semibold", 10F),
            };
            box.Multiline = true;
            box.ScrollBars = ScrollBars.None; // the wheel still scrolls; no grey Windows scroll bar
            box.Font = new Font("Consolas", 9F);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.SetBounds(16, 46, 408, 104);
            try
            {
                string clip = Clipboard.GetText().Trim();
                if (clip.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) box.Text = clip;
            }
            catch { }

            var ok = new Button { Text = "Add", Size = new Size(96, 34), Location = new Point(224, 164) };
            var cancel = new Button { Text = "Cancel", Size = new Size(96, 34), Location = new Point(328, 164), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                try
                {
                    VlessKey.Parse(KeyText);
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Nevatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            Controls.AddRange(new Control[] { label, box, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
            label.ForeColor = Theme.Text;
            Dark.Dialog(this);
        }
    }

    // Yes = install (or replace the installed copy), No = just run this copy, Cancel (closing it) = the same, but ask again next time.
    public class InstallDialog : Form
    {
        public InstallDialog(bool replace)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Nevatch";
            Icon = Art.ShieldIcon(Theme.S(32));
            Font = new Font("Segoe UI", 9.75F);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 196);

            var shield = new PictureBox { Image = Art.ShieldBitmap(48), SizeMode = PictureBoxSizeMode.CenterImage, Bounds = new Rectangle(16, 18, 48, 56) };
            var title = new Label
            {
                Text = replace ? "Update the installed Nevatch?" : "Install Nevatch on this PC?",
                Location = new Point(80, 18), AutoSize = true, Font = new Font("Segoe UI Semibold", 12F),
            };
            var text = new Label
            {
                Text = replace
                    ? "Nevatch is already installed. Replace it with this copy? Your servers and settings stay."
                    : "It gets its own folder, a Start menu and desktop shortcut, and keeps itself up to date. " +
                      "You can delete the downloaded file afterwards.",
                Bounds = new Rectangle(80, 50, 344, 76),
            };
            var install = new Button { Text = replace ? "Update" : "Install", Size = new Size(104, 34), Location = new Point(208, 146), DialogResult = DialogResult.Yes };
            var run = new Button { Text = "Just run it", Size = new Size(104, 34), Location = new Point(320, 146), DialogResult = DialogResult.No };
            Controls.AddRange(new Control[] { shield, title, text, install, run });
            AcceptButton = install;
            Dark.Dialog(this);
        }
    }
}
