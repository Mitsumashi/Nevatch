// The web console built into Nevatch Server.exe: the People and Lag pages, and the server settings and setup
// checklist on the Settings page. They talk to server.js over http://127.0.0.1 with the random token
// NevatchServer.cs hands it at start (FVPN_LAUNCHER_TOKEN), so they need no password.
// C# 5 only, like the rest: built with the compiler that ships with Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Nevatch.Ui;

namespace Nevatch.Server
{
    // Owns the console pages and keeps them up to date. NevatchServer.cs adds the pages and says when the server runs.
    public class ServerConsole
    {
        readonly ShellForm form;
        readonly ConsoleApi api;
        readonly int port, peoplePage, settingsPage;
        public readonly InfoCard HomeCard = new InfoCard(Glyph.People, "People");
        public readonly Page People = new Page("People");
        public readonly Page Lag = new Page("Lag");
        public readonly ScrollStack Settings = new ScrollStack();
        readonly Field newName = new Field("Name, e.g. Ivan");
        readonly OutlineButton addPerson = new OutlineButton { Text = "Add person" };
        readonly FieldRow addBar;
        readonly RowList peopleList = new RowList(132, Glyph.People, "", "");
        readonly List<PersonRow> personRows = new List<PersonRow>();
        readonly LagRow selfLag = new LagRow(60, "Router", "Internet");
        readonly RowList lagList = new RowList(232, Glyph.Speed, "", "");
        readonly List<LagRow> lagRows = new List<LagRow>();
        readonly List<string> lagIds = new List<string>();
        readonly ChecklistCard checklist = new ChecklistCard();
        readonly FormCard serverCard = new FormCard("Server settings", "Save",
            "Changing the port or camouflage site means everyone needs a new key.");
        readonly Field publicHost = new Field("e.g. 85.123.45.67"), portField = new Field(null), sni = new Field(null),
            downLimit = new Field(null), upLimit = new Field(null);
        readonly OutlineButton detect = new OutlineButton { Text = "Detect" };
        readonly InfoCard webCard = new InfoCard(Glyph.Globe, "Open in browser");
        readonly InfoCard restartCard = new InfoCard(Glyph.Refresh, "Restart Xray");
        readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        bool running, settingsLoaded, polling;
        Status status; // the console's last answer; null while the server isn't running
        int busy, generation, ticks;
        int homeTarget;

        public ServerConsole(ShellForm form, int port, string token, int peoplePage, int settingsPage)
        {
            this.form = form;
            this.port = port;
            this.peoplePage = homeTarget = peoplePage;
            this.settingsPage = settingsPage;
            api = new ConsoleApi(port, token);

            HomeCard.Button.Click += (s, e) => form.ShowPage(homeTarget);

            addBar = new FieldRow(newName, addPerson, 170);
            newName.Box.MaxLength = 40;
            newName.Submit += (s, e) => AddPerson();
            addPerson.Click += (s, e) => AddPerson();
            People.Add(addBar, 46);
            People.Add(peopleList, -1);

            Lag.Add(selfLag, 232);
            Lag.Add(lagList, -1);
            Lag.VisibleChanged += (s, e) => { if (Lag.Visible) Poll(); };

            Settings.Add(checklist, 0);
            serverCard.AddField("Public address", "Your home's internet IP, or a dynamic DNS name like myhome.duckdns.org",
                new FieldRow(publicHost, detect, 110), false);
            serverCard.AddField("Port", "Forward this TCP port on your router", portField, true);
            serverCard.AddField("Camouflage site", "Connections look like visits to this site", sni, true);
            serverCard.AddField("Download limit per person", "Mbit/s; 0 = no limit", downLimit, true);
            serverCard.AddField("Upload limit per person", "Mbit/s; 0 = no limit", upLimit, true);
            serverCard.Button.Click += (s, e) => SaveSettings();
            detect.Click += (s, e) => DetectAddress();
            foreach (Field field in new[] { publicHost, portField, sni, downLimit, upLimit })
            {
                field.Submit += (s, e) => SaveSettings();
                field.Box.TextChanged += (s, e) => serverCard.SetMessage(null, Theme.Muted);
            }
            Settings.Add(serverCard, 0);
            webCard.Button.Click += (s, e) => { try { Process.Start("http://localhost:" + port); } catch { } };
            Settings.Add(webCard, 120);
            restartCard.Set("Restart Xray", "Everyone's connection drops for a moment; Nevatch reconnects by itself. Rarely needed.");
            restartCard.Button.Click += (s, e) => RestartXray();
            Settings.Add(restartCard, 120);

            // The Lag page follows along every 2 seconds, the rest every 5. Asking for the status also makes the
            // server ping the router for a while (for the Lag page), so nothing is asked while the window is away.
            pollTimer.Tick += (s, e) => { ticks++; if (ticks % (Lag.Visible ? 2 : 5) == 0) Poll(); };
            pollTimer.Start();
            form.VisibleChanged += (s, e) => Poll();
            SetRunning(false);
        }

        // Called when the console can be reached (server.js printed "Console:") and when the server stops.
        public void SetRunning(bool on)
        {
            running = on;
            if (on)
            {
                Poll();
                return;
            }
            generation++;
            status = null;
            settingsLoaded = false;
            personRows.Clear();
            peopleList.SetEmpty("The server isn't running", "Start it on the Home page to add people and copy their keys.");
            peopleList.SetRows(new Control[0]);
            lagRows.Clear();
            lagIds.Clear();
            lagList.SetEmpty("The server isn't running", "Start it on the Home page to see the lag.");
            lagList.SetRows(new Control[0]);
            selfLag.Set("This PC (the server)", new int[0], new int[0], "Measured while the server is running.", Theme.Muted);
            checklist.Set(new List<CheckItem> { new CheckItem(Check.Todo, "The server isn't running. Start it on the Home page.", null) });
            webCard.Set("Web console", "Available while the server is running.");
            serverCard.SetMessage(null, Theme.Muted);
            ShowHomeCard();
            SetEditable();
        }

        bool Watching { get { return form.Visible && form.WindowState != FormWindowState.Minimized; } }

        async void Poll()
        {
            if (polling || busy > 0 || !running || !Watching) return;
            polling = true;
            int seen = generation;
            try
            {
                Status s = await Task.Run(() => api.GetStatus());
                if (seen == generation && busy == 0 && running) Render(s);
            }
            catch (Exception ex)
            {
                if (seen == generation && running && status == null) HomeCard.Set("People", "Can't reach the server: " + ex.Message, true);
            }
            finally
            {
                polling = false;
            }
        }

        // Sends one change to the server and shows the status it answers with. Returns what went wrong, or null.
        async Task<string> Act(Func<Status> change)
        {
            busy++;
            generation++;
            SetEditable();
            try
            {
                Status s = await Task.Run(change);
                if (running) Render(s);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                busy--;
                SetEditable();
            }
        }

        void Render(Status s)
        {
            status = s;
            ShowPeople(s);
            ShowLag(s);
            checklist.Set(Checklist(s));
            if (!settingsLoaded) LoadSettings();
            webCard.Set("Web console", "From other devices at home: http://" + (s.LocalIps.Length > 0 ? s.LocalIps[0] : "this-pc") + ":" +
                port + ". It asks for the console password.");
            ShowHomeCard();
            SetEditable();
        }

        // Changes go one at a time, and only while the server runs.
        void SetEditable()
        {
            bool on = running && status != null && busy == 0;
            addBar.Enabled = on;
            foreach (PersonRow row in personRows) row.Enabled = on;
            serverCard.Enabled = on;
            restartCard.Button.Enabled = on;
            webCard.Button.Enabled = running;
            HomeCard.Button.Enabled = running && status != null;
        }

        void ShowHomeCard()
        {
            homeTarget = peoplePage;
            HomeCard.Button.Text = "People";
            if (!running)
            {
                HomeCard.Set("People", "Add people and copy their keys while the server is running.");
                return;
            }
            if (status == null)
            {
                HomeCard.Set("People", "Loading…");
                return;
            }
            CheckItem todo = Checklist(status).FirstOrDefault(c => c.State == Check.Todo);
            if (todo != null)
            {
                homeTarget = settingsPage;
                HomeCard.Button.Text = "Settings";
                HomeCard.Set("Setup isn't finished", todo.Text);
                return;
            }
            int total = status.People.Count, on = status.People.Count(p => p.Connected);
            HomeCard.Set(total == 0 ? "No one added yet" : Pretty.Count(total, "person", "people") + ", " + on + " connected",
                "Add people and copy their keys.");
        }

        // The setup checklist from the web console.
        static List<CheckItem> Checklist(Status s)
        {
            var items = new List<CheckItem>();
            if (!s.XrayInstalled) items.Add(new CheckItem(Check.Todo, "Xray is not installed. Stop the server and start it again to run setup.", null));
            else if (!s.XrayRunning) items.Add(new CheckItem(Check.Todo, "Xray is not running. The Log page says why.", null));
            else items.Add(new CheckItem(Check.Ok, "Xray is running (" + (s.XrayVersion.Length > 0 ? s.XrayVersion : "version unknown") + ")", null));
            items.Add(s.PublicHost.Length > 0
                ? new CheckItem(Check.Ok, "Public address: " + s.PublicHost, null)
                : new CheckItem(Check.Todo, "Set your public address in Server settings: click Detect, then Save.", null));
            items.Add(new CheckItem(Check.Info, "Router: forward TCP port " + s.Port + " to " + (s.LocalIps.Length > 0 ? s.LocalIps[0] : "this PC"),
                "Give this PC a fixed local IP in the router (DHCP reservation) so the rule keeps working."));
            items.Add(new CheckItem(Check.Info, "Windows Firewall: right-click allow-firewall.cmd in the server folder → Run as administrator",
                "Only needed once, and again if you change the port."));
            items.Add(s.PackageReady
                ? new CheckItem(Check.Ok, "Nevatch.exe is built, so your friends get updates", null)
                : new CheckItem(Check.Todo, "Nevatch.exe is not built yet. Stop the server and start it again to run setup.", null));
            return items;
        }

        void ShowPeople(Status s)
        {
            peopleList.SetEmpty("No one added yet", "Type a name above and click Add person. Then click Copy key and send it to them.");
            bool same = personRows.Count == s.People.Count;
            for (int i = 0; same && i < personRows.Count; i++) same = personRows[i].Person.Id == s.People[i].Id;
            if (same)
            {
                for (int i = 0; i < personRows.Count; i++) personRows[i].Set(s.People[i]);
                return;
            }
            personRows.Clear();
            foreach (Person person in s.People)
            {
                var row = new PersonRow(person);
                // Later, not inside the click: the answer rebuilds the rows, this one included.
                row.CopyKey.Click += (o, e) => CopyKeyOf(row);
                row.Toggle.Click += (o, e) => { Person p = row.Person; form.BeginInvoke((MethodInvoker)delegate { SetEnabled(p); }); };
                row.Remove.Click += (o, e) => { Person p = row.Person; form.BeginInvoke((MethodInvoker)delegate { RemovePerson(p); }); };
                personRows.Add(row);
            }
            peopleList.SetRows(personRows);
        }

        void ShowLag(Status s)
        {
            lagList.SetEmpty("No one connected", "Each person's lag shows up here while they're connected.");
            bool selfSlow = LagReport.ForThisPc(s, selfLag);
            bool same = lagIds.Count == s.Lag.Count;
            for (int i = 0; same && i < lagIds.Count; i++) same = lagIds[i] == s.Lag[i].Id;
            if (!same)
            {
                lagRows.Clear();
                lagIds.Clear();
                foreach (PersonLag lag in s.Lag)
                {
                    lagRows.Add(new LagRow(120, "VPN", "Without VPN"));
                    lagIds.Add(lag.Id);
                }
                lagList.SetRows(lagRows);
            }
            for (int i = 0; i < lagRows.Count; i++)
            {
                Person person = s.People.FirstOrDefault(p => p.Id == lagIds[i]);
                LagReport.ForPerson(person != null ? person.Name : "Someone", s.Lag[i], selfSlow, lagRows[i]);
            }
        }

        void LoadSettings()
        {
            publicHost.Box.Text = status.PublicHost;
            portField.Box.Text = status.Port.ToString(CultureInfo.InvariantCulture);
            sni.Box.Text = status.Sni;
            downLimit.Box.Text = status.DownLimit.ToString(CultureInfo.InvariantCulture);
            upLimit.Box.Text = status.UpLimit.ToString(CultureInfo.InvariantCulture);
            settingsLoaded = true;
        }

        async void AddPerson()
        {
            string name = newName.Box.Text.Trim();
            if (name.Length == 0 || busy > 0 || status == null)
            {
                newName.Box.Focus();
                return;
            }
            string error = await Act(() => api.Post("/api/users", Body("name", name)));
            if (error != null)
            {
                Warn(error);
                return;
            }
            newName.Box.Text = "";
            newName.Box.Focus();
        }

        void CopyKeyOf(PersonRow row)
        {
            if (status == null) return;
            if (string.IsNullOrEmpty(row.Person.Link))
            {
                NeedPublicAddress();
                return;
            }
            try
            {
                Clipboard.SetText(row.Person.Link);
                row.ShowCopied();
            }
            catch (Exception ex)
            {
                Warn("Could not copy the key: " + ex.Message);
            }
        }

        async void SetEnabled(Person person)
        {
            if (busy > 0) return;
            string error = await Act(() => api.Post("/api/users/" + person.Id, Body("enabled", !person.Enabled)));
            if (error != null) Warn(error);
        }

        async void RemovePerson(Person person)
        {
            if (busy > 0) return;
            if (MessageBox.Show(form, "Remove " + person.Name + "?\n\nTheir key stops working immediately.", "Nevatch Server",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            string error = await Act(() => api.Delete("/api/users/" + person.Id));
            if (error != null) Warn(error);
        }

        // The server checks the values and says what's wrong.
        async void SaveSettings()
        {
            if (busy > 0 || status == null) return;
            var body = Body("publicHost", publicHost.Box.Text.Trim(), "port", portField.Box.Text.Trim(), "sni", sni.Box.Text.Trim(),
                "downLimit", Decimal(downLimit), "upLimit", Decimal(upLimit));
            serverCard.SetMessage("Saving…", Theme.Muted);
            string error = await Act(() => api.Post("/api/settings", body));
            if (error != null)
            {
                serverCard.SetMessage(error, Theme.Red);
                return;
            }
            LoadSettings();
            serverCard.SetMessage("Saved.", Theme.Green);
        }

        async void DetectAddress()
        {
            if (busy > 0 || status == null) return;
            detect.Enabled = false;
            serverCard.SetMessage("Asking the internet for your address…", Theme.Muted);
            try
            {
                publicHost.Box.Text = await Task.Run(() => api.DetectIp());
                serverCard.SetMessage("Detected. Click Save to keep it.", Theme.Green);
            }
            catch (Exception ex)
            {
                serverCard.SetMessage(ex.Message, Theme.Red);
            }
            finally
            {
                detect.Enabled = true;
            }
        }

        async void RestartXray()
        {
            if (busy > 0) return;
            string error = await Act(() => api.Post("/api/restart", Body()));
            if (error != null) Warn(error);
        }

        void NeedPublicAddress()
        {
            MessageBox.Show(form, "Set your public address first: it's the address every downloaded Nevatch connects to.\n\n" +
                "In Server settings, click Detect, then Save.", "Nevatch Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
            form.ShowPage(settingsPage);
        }

        void Warn(string text)
        {
            MessageBox.Show(form, text, "Nevatch Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        static Dictionary<string, object> Body(params object[] pairs)
        {
            var body = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) body[(string)pairs[i]] = pairs[i + 1];
            return body;
        }

        // "2,5" works too.
        static string Decimal(Field field) { return field.Box.Text.Trim().Replace(',', '.'); }
    }

    // ---------- talking to server.js ----------

    // The console API in server.js. The token server.js was started with stands in for the password; a website
    // can't send it along (browsers only add such a header when the console allows it, which it never does).
    class ConsoleApi
    {
        readonly string root, token;

        public ConsoleApi(int port, string token)
        {
            root = "http://127.0.0.1:" + port;
            this.token = token;
        }

        public Status GetStatus() { return Status.From(Parse(Send("GET", "/api/status", null))); }
        public Status Post(string path, Dictionary<string, object> body) { return Status.From(Parse(Send("POST", path, body))); }
        public Status Delete(string path) { return Status.From(Parse(Send("DELETE", path, null))); }

        public string DetectIp()
        {
            return Json.Str(Json.Get(Json.Obj(Parse(Send("POST", "/api/detect-ip", new Dictionary<string, object>()))), "ip")) ?? "";
        }

        static object Parse(byte[] data)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Encoding.UTF8.GetString(data));
        }

        // Adding someone or changing settings restarts Xray, which can take a few seconds.
        byte[] Send(string method, string path, Dictionary<string, object> body)
        {
            var request = (HttpWebRequest)WebRequest.Create(root + path);
            request.Method = method;
            request.Proxy = null;
            request.Timeout = request.ReadWriteTimeout = 60000;
            request.ServicePoint.Expect100Continue = false;
            request.Headers["X-Nevatch-Token"] = token;
            try
            {
                if (body != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
                    request.ContentType = "application/json";
                    request.ContentLength = data.Length;
                    using (Stream s = request.GetRequestStream()) s.Write(data, 0, data.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse()) return ReadAll(response);
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null) throw new Exception("The server didn't answer (" + ex.Message + ")");
                string error = null;
                int code;
                using (response)
                {
                    code = (int)response.StatusCode;
                    try { error = Json.Str(Json.Get(Json.Obj(Parse(ReadAll(response))), "error")); } catch { }
                }
                throw new Exception(error ?? "The server answered with error " + code + ".");
            }
        }

        static byte[] ReadAll(WebResponse response)
        {
            using (Stream s = response.GetResponseStream())
            using (var copy = new MemoryStream())
            {
                s.CopyTo(copy);
                return copy.ToArray();
            }
        }
    }

    // What JavaScriptSerializer gives back: dictionaries, lists, strings, numbers, bools and nulls.
    static class Json
    {
        public static Dictionary<string, object> Obj(object v) { return v as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(Dictionary<string, object> o, string key) { object v; return o.TryGetValue(key, out v) ? v : null; }
        public static string Str(object v) { return v as string; }
        public static bool Bool(object v) { return v is bool && (bool)v; }
        public static System.Collections.IList List(object v) { return v as System.Collections.IList ?? new object[0]; }

        public static double Num(object v)
        {
            return v is int || v is long || v is decimal || v is double ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
        }

        public static int[] Ints(object v)
        {
            System.Collections.IList list = List(v);
            var result = new int[list.Count];
            for (int i = 0; i < result.Length; i++) result[i] = (int)Json.Num(list[i]);
            return result;
        }

        public static DateTime? Time(object v)
        {
            DateTime t;
            return DateTime.TryParse(Str(v), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)
                ? t : (DateTime?)null;
        }
    }

    public class Person
    {
        public string Id, Name, Link;
        public bool Enabled, Connected;
        public long Up, Down;
        public DateTime? LastActive, ConnectedSince; // UTC
        public List<Device> Devices = new List<Device>(); // connected ones first
        public bool OtherApp; // connected through a phone app like v2rayNG, which doesn't say which device
    }

    // One of a person's devices, as its Nevatch reported it with its pings.
    public class Device
    {
        public string Name, Os, App;
        public bool Connected;
        public DateTime? ConnectedSince, LastSeen; // UTC
    }

    // Round trips in ms, oldest first; -1 = no answer.
    public class PersonLag
    {
        public string Id;
        public int[] Vpn, Direct;
    }

    // GET /api/status, which every change also answers with.
    public class Status
    {
        public bool XrayInstalled, XrayRunning, PackageReady;
        public string XrayVersion, PublicHost, Sni, Gateway;
        public int Port;
        public double DownLimit, UpLimit;
        public string[] LocalIps;
        public List<Person> People = new List<Person>();
        public int[] SelfRouter, SelfInternet;
        public List<PersonLag> Lag = new List<PersonLag>();

        public static Status From(object json)
        {
            Dictionary<string, object> o = Json.Obj(json);
            Dictionary<string, object> xray = Json.Obj(Json.Get(o, "xray")), settings = Json.Obj(Json.Get(o, "settings"));
            Dictionary<string, object> lag = Json.Obj(Json.Get(o, "lag")), self = Json.Obj(Json.Get(lag, "self"));
            var s = new Status
            {
                XrayInstalled = Json.Bool(Json.Get(xray, "installed")),
                XrayRunning = Json.Bool(Json.Get(xray, "running")),
                XrayVersion = Json.Str(Json.Get(xray, "version")) ?? "",
                PackageReady = Json.Bool(Json.Get(o, "packageReady")),
                PublicHost = Json.Str(Json.Get(settings, "publicHost")) ?? "",
                Port = (int)Json.Num(Json.Get(settings, "port")),
                Sni = Json.Str(Json.Get(settings, "sni")) ?? "",
                DownLimit = Json.Num(Json.Get(settings, "downLimit")),
                UpLimit = Json.Num(Json.Get(settings, "upLimit")),
                LocalIps = Json.List(Json.Get(o, "localIps")).OfType<string>().ToArray(),
                SelfRouter = Json.Ints(Json.Get(self, "router")),
                SelfInternet = Json.Ints(Json.Get(self, "internet")),
                Gateway = Json.Str(Json.Get(self, "gateway")),
            };
            foreach (object item in Json.List(Json.Get(o, "users")))
            {
                Dictionary<string, object> u = Json.Obj(item);
                var devices = new List<Device>();
                foreach (object d in Json.List(Json.Get(u, "devices")))
                {
                    Dictionary<string, object> o2 = Json.Obj(d);
                    devices.Add(new Device
                    {
                        Name = Json.Str(Json.Get(o2, "name")) ?? "Unknown device",
                        Os = Json.Str(Json.Get(o2, "os")) ?? "",
                        App = Json.Str(Json.Get(o2, "app")) ?? "",
                        Connected = Json.Bool(Json.Get(o2, "connected")),
                        ConnectedSince = Json.Time(Json.Get(o2, "connectedSince")),
                        LastSeen = Json.Time(Json.Get(o2, "lastSeen")) ?? Json.Time(Json.Get(o2, "firstSeen")),
                    });
                }
                s.People.Add(new Person
                {
                    Devices = devices,
                    OtherApp = Json.Bool(Json.Get(u, "otherApp")),
                    Id = Json.Str(Json.Get(u, "id")) ?? "",
                    Name = Json.Str(Json.Get(u, "name")) ?? "",
                    Link = Json.Str(Json.Get(u, "link")),
                    Enabled = Json.Bool(Json.Get(u, "enabled")),
                    Connected = Json.Bool(Json.Get(u, "connected")),
                    Up = (long)Json.Num(Json.Get(u, "up")),
                    Down = (long)Json.Num(Json.Get(u, "down")),
                    LastActive = Json.Time(Json.Get(u, "lastActive")),
                    ConnectedSince = Json.Time(Json.Get(u, "connectedSince")),
                });
            }
            foreach (KeyValuePair<string, object> pair in Json.Obj(Json.Get(lag, "people")))
            {
                Dictionary<string, object> p = Json.Obj(pair.Value);
                s.Lag.Add(new PersonLag { Id = pair.Key, Vpn = Json.Ints(Json.Get(p, "vpn")), Direct = Json.Ints(Json.Get(p, "direct")) });
            }
            return s;
        }
    }

    static class Pretty
    {
        public static string Bytes(long n)
        {
            if (n <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = Math.Min(units.Length - 1, (int)Math.Floor(Math.Log(n) / Math.Log(1024)));
            return (n / Math.Pow(1024, i)).ToString(i > 1 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[i];
        }

        public static string Ago(DateTime utc)
        {
            double s = (DateTime.UtcNow - utc).TotalSeconds;
            if (s < 90) return "a minute ago";
            if (s < 3600) return Math.Round(s / 60) + " min ago";
            if (s < 86400) return Math.Round(s / 3600) + " h ago";
            return Math.Round(s / 86400) + " days ago";
        }

        public static string ConnectedFor(DateTime utc)
        {
            int m = (int)Math.Floor((DateTime.UtcNow - utc).TotalMinutes);
            return m < 1 ? "just now" : m < 60 ? "for " + m + " min" : "for " + m / 60 + " h " + m % 60 + " min";
        }

        public static string Count(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
    }

    // Lag, as the web console explains it: comparing each person's lag through the VPN with their own internet
    // and with this PC's own ping to the router shows whose side it comes from.
    static class LagReport
    {
        struct Stats { public int Count, Usual, Worst, Lost, Spikes; public bool Answers; }

        // spike: how far above usual counts as a lag spike.
        static Stats Measure(int[] list, int spike)
        {
            int[] ok = list.Where(v => v >= 0).OrderBy(v => v).ToArray();
            int usual = ok.Length > 0 ? ok[ok.Length / 2] : 0;
            return new Stats
            {
                Count = list.Length,
                Usual = usual,
                Worst = ok.Length > 0 ? ok[ok.Length - 1] : 0,
                Lost = list.Length - ok.Length,
                Spikes = list.Count(v => v > usual + spike),
                Answers = ok.Length > 0,
            };
        }

        // A home router normally answers in a few ms, so anything slower is this PC's own connection (Wi-Fi).
        // Returns whether it is slow.
        public static bool ForThisPc(Status s, LagRow row)
        {
            Stats router = Measure(s.SelfRouter, 100), internet = Measure(s.SelfInternet, 150);
            bool slow = router.Count >= 5 && (router.Usual > 20 || router.Spikes >= 3 || router.Lost > 2);
            string verdict;
            Color color = Look.Amber;
            if (router.Count < 5) { verdict = "Measuring…"; color = Theme.Muted; }
            else if (s.Gateway == null) { verdict = "Router not found, so only the internet is checked."; color = Theme.Muted; }
            else if (slow) verdict = "Slow Wi-Fi on this PC. A network cable fixes it.";
            else if (internet.Usual > 80 || internet.Lost > 2) verdict = "Your internet is slow right now. Something big downloading?";
            else { verdict = "All good."; color = Theme.Green; }
            row.Set("This PC (the server)", s.SelfRouter, s.SelfInternet, verdict, color);
            return slow;
        }

        public static void ForPerson(string name, PersonLag lag, bool selfSlow, LagRow row)
        {
            Stats vpn = Measure(lag.Vpn, 200), direct = Measure(lag.Direct, 200);
            // Yandex is close to anyone in Russia, so even a steady 150 ms without the VPN means their connection is slow.
            bool ownLags = direct.Answers && (direct.Usual > 150 || direct.Lost > 2 || direct.Spikes >= 3);
            bool vpnLags = vpn.Lost > 1 || vpn.Spikes >= 3;
            string verdict;
            Color color = Look.Amber;
            if (vpn.Count < 10) { verdict = "Measuring…"; color = Theme.Muted; }
            else if (ownLags) verdict = "Their own internet is slow, even without the VPN.";
            else if (vpnLags && selfSlow) verdict = "Lagging. This PC's connection is slow too: fix that first.";
            else if (vpnLags) verdict = "Lagging between you. Try a lower download limit.";
            else if (vpn.Usual > 300) verdict = "Steady, but slow. Fine for browsing.";
            else { verdict = "All good."; color = Theme.Green; }
            row.Set(name, lag.Vpn, lag.Direct, verdict, color);
        }
    }

    // ---------- controls for the console pages ----------

    static class Look
    {
        public static readonly Font Label = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Hint = new Font("Segoe UI", 9.25f);
        public static readonly Font Field = new Font("Segoe UI", 11f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Struck = new Font("Segoe UI Semibold", 11.5f, FontStyle.Strikeout);
        public static readonly Color Amber = Theme.Yellow;

        public static int LineHeight(Font font)
        {
            return TextRenderer.MeasureText("Ag", font, new Size(1000, 1000), TextFormatFlags.NoPadding).Height;
        }

        public static int TextHeight(string text, Font font, int width)
        {
            return TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        }
    }

    // A white rounded text box, like the cards around it. Enter raises Submit.
    public class Field : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        const int EM_SETCUEBANNER = 0x1501;

        public readonly TextBox Box = new TextBox();
        public event EventHandler Submit;

        public Field(string placeholder)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.InputBg;
            Box.ForeColor = Theme.Text;
            Box.Font = Look.Field;
            if (placeholder != null) Box.HandleCreated += (s, e) => SendMessage(Box.Handle, EM_SETCUEBANNER, (IntPtr)1, placeholder);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                EventHandler handler = Submit;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(Box);
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int inset = Theme.S(12);
            Box.SetBounds(inset, (Height - Box.Height) / 2, Math.Max(10, Width - inset * 2), Box.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.ContentBg);
            Art.Smooth(g);
            bool focused = Box.Focused;
            float line = Math.Max(1f, Theme.Scale) * (focused ? 2 : 1);
            var r = new RectangleF(line / 2, line / 2, Width - line - 1, Height - line - 1);
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(10)))
            {
                using (var b = new SolidBrush(Theme.InputBg)) g.FillPath(b, p);
                using (var pen = new Pen(focused ? Theme.Accent : Theme.Border, line)) g.DrawPath(pen, p);
            }
        }
    }

    // A field with a button at its right end (Add person, Detect).
    public class FieldRow : Panel
    {
        readonly Field field;
        readonly OutlineButton button;
        readonly int buttonWidth;

        public FieldRow(Field field, OutlineButton button, int buttonWidth)
        {
            this.field = field;
            this.button = button;
            this.buttonWidth = buttonWidth;
            Controls.Add(field);
            Controls.Add(button);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w = Theme.S(buttonWidth), gap = Theme.S(10);
            field.SetBounds(0, 0, Math.Max(Theme.S(60), Width - w - gap), Height);
            button.SetBounds(Width - w, 0, w, Height);
        }
    }

    // For a card whose height depends on how wide it is.
    public interface IFitsWidth
    {
        int HeightFor(int width);
    }

    // A scrolling column of controls with gaps between them.
    public class ScrollStack : ScrollPanel
    {
        readonly List<Control> items = new List<Control>();
        readonly List<int> heights = new List<int>();

        public ScrollStack() { }

        // height: at 96 DPI; 0 asks the control (IFitsWidth).
        public T Add<T>(T control, int height) where T : Control
        {
            items.Add(control);
            heights.Add(height);
            Controls.Add(control);
            PerformLayout();
            return control;
        }

        // Rows first, then the scroll area: the other way round, rows still at the old width bring up a sideways scroll bar.
        protected override void OnLayout(LayoutEventArgs e)
        {
            int gap = Theme.S(16), width = ClientSize.Width - SlimScrollBar.BarWidth;
            var h = new int[items.Count];
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var fits = items[i] as IFitsWidth;
                h[i] = heights[i] > 0 || fits == null ? Theme.S(heights[i]) : fits.HeightFor(width);
                total += h[i] + (i > 0 ? gap : 0);
            }
            SetContentHeight(total);
            int y = -ScrollOffset;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].SetBounds(0, y, width, h[i]);
                y += h[i] + gap;
            }
            base.OnLayout(e);
        }
    }

    // A card with a heading, labelled fields (two side by side where they fit), and a button with a message next to it.
    public class FormCard : Card, IFitsWidth
    {
        class Item
        {
            public string Title, Hint;
            public Control Input;
            public bool Half;
            public Rectangle TitleBox, HintBox;
        }

        readonly string heading, note;
        readonly List<Item> items = new List<Item>();
        public readonly OutlineButton Button;
        string message;
        Color messageColor = Theme.Muted;
        Rectangle messageBox;

        public FormCard(string heading, string buttonText, string note)
        {
            this.heading = heading;
            this.note = note;
            Button = new OutlineButton { Text = buttonText };
            Controls.Add(Button);
        }

        // half: may sit next to the field after it.
        public void AddField(string title, string hint, Control input, bool half)
        {
            items.Add(new Item { Title = title, Hint = hint, Input = input, Half = half });
            Controls.Add(input);
        }

        // Shown next to the button; null shows the note again.
        public void SetMessage(string text, Color color)
        {
            message = text;
            messageColor = color;
            Invalidate();
        }

        public int HeightFor(int width) { return Arrange(width, false); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Arrange(Width, true);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        // Places everything for this width (when apply is set) and returns the height it all takes.
        int Arrange(int width, bool apply)
        {
            int pad = Theme.S(24), gap = Theme.S(20), inner = Math.Max(Theme.S(100), width - pad * 2);
            int labelH = Look.LineHeight(Look.Label), inputH = Theme.S(42);
            bool pairs = inner >= Theme.S(440);
            int y = pad + Look.LineHeight(Fonts.CardTitle) + Theme.S(16);
            for (int i = 0; i < items.Count; i++)
            {
                var row = new List<Item> { items[i] };
                if (pairs && items[i].Half && i + 1 < items.Count && items[i + 1].Half) row.Add(items[++i]);
                int colW = (inner - gap * (row.Count - 1)) / row.Count;
                int textH = 0;
                foreach (Item item in row)
                    textH = Math.Max(textH, labelH + (item.Hint != null ? Theme.S(2) + Look.TextHeight(item.Hint, Look.Hint, colW) : 0));
                int inputY = y + textH + Theme.S(8);
                for (int c = 0; apply && c < row.Count; c++)
                {
                    int x = pad + c * (colW + gap);
                    row[c].TitleBox = new Rectangle(x, y, colW, labelH);
                    row[c].HintBox = new Rectangle(x, y + labelH + Theme.S(2), colW, textH - labelH - Theme.S(2));
                    row[c].Input.SetBounds(x, inputY, colW, inputH);
                }
                y = inputY + inputH + Theme.S(18);
            }
            int buttonW = Theme.S(150);
            if (apply)
            {
                Button.SetBounds(pad, y + Theme.S(4), buttonW, inputH);
                messageBox = new Rectangle(pad + buttonW + Theme.S(16), y, Math.Max(10, inner - buttonW - Theme.S(16)), inputH + Theme.S(8));
            }
            return y + Theme.S(4) + inputH + pad;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int pad = Theme.S(24);
            const TextFormatFlags line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            const TextFormatFlags wrap = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, heading, Fonts.CardTitle, new Point(pad, pad), Theme.Text, TextFormatFlags.NoPadding);
            foreach (Item item in items)
            {
                TextRenderer.DrawText(g, item.Title, Look.Label, item.TitleBox, Enabled ? Theme.Text : Theme.Muted, line);
                if (item.Hint != null) TextRenderer.DrawText(g, item.Hint, Look.Hint, item.HintBox, Theme.Muted, wrap);
            }
            string text = string.IsNullOrEmpty(message) ? note : message;
            Color color = string.IsNullOrEmpty(message) ? Theme.Muted : messageColor;
            int h = Math.Min(messageBox.Height, Look.TextHeight(text, Look.Hint, messageBox.Width));
            TextRenderer.DrawText(g, text, Look.Hint,
                new Rectangle(messageBox.X, messageBox.Y + (messageBox.Height - h) / 2, messageBox.Width, h), color, wrap);
        }
    }

    public enum Check { Ok, Todo, Info }

    public class CheckItem
    {
        public readonly Check State;
        public readonly string Text, Note;

        public CheckItem(Check state, string text, string note)
        {
            State = state;
            Text = text;
            Note = note;
        }

        public bool Same(CheckItem other) { return other.State == State && other.Text == Text && other.Note == Note; }
    }

    // "Setup checklist": a round mark (done, to do, or just so you know) and a line or two for each step.
    public class ChecklistCard : Card, IFitsWidth
    {
        List<CheckItem> items = new List<CheckItem>();

        public void Set(List<CheckItem> next)
        {
            if (next.Count == items.Count && next.Zip(items, (a, b) => a.Same(b)).All(same => same)) return;
            items = next;
            Invalidate();
            if (Parent != null) Parent.PerformLayout();
        }

        public int HeightFor(int width) { return Arrange(null, width); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Art.Smooth(e.Graphics);
            Arrange(e.Graphics, Width);
        }

        // Draws the list (unless g is null) and returns the height it takes at this width.
        int Arrange(Graphics g, int width)
        {
            int pad = Theme.S(24), mark = Theme.S(24), left = pad + mark + Theme.S(12);
            int textW = Math.Max(Theme.S(60), width - left - pad), lineH = Look.LineHeight(Fonts.CardText);
            const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int y = pad;
            if (g != null) TextRenderer.DrawText(g, "Setup checklist", Fonts.CardTitle, new Point(pad, y), Theme.Text, TextFormatFlags.NoPadding);
            y += Look.LineHeight(Fonts.CardTitle) + Theme.S(14);
            foreach (CheckItem item in items)
            {
                int textY = y + (mark - lineH) / 2;
                int textH = Look.TextHeight(item.Text, Fonts.CardText, textW);
                int noteH = item.Note != null ? Look.TextHeight(item.Note, Look.Hint, textW) : 0;
                if (g != null)
                {
                    DrawMark(g, item.State, new Rectangle(pad, y, mark, mark));
                    TextRenderer.DrawText(g, item.Text, Fonts.CardText, new Rectangle(left, textY, textW, textH), Theme.Text, wrap);
                    if (item.Note != null)
                        TextRenderer.DrawText(g, item.Note, Look.Hint, new Rectangle(left, textY + textH + Theme.S(2), textW, noteH), Theme.Muted, wrap);
                }
                y = Math.Max(y + mark, textY + textH + (noteH > 0 ? Theme.S(2) + noteH : 0)) + Theme.S(12);
            }
            return y - Theme.S(12) + pad;
        }

        static void DrawMark(Graphics g, Check state, Rectangle r)
        {
            Color fill = state == Check.Ok ? Color.FromArgb(40, 35, 165, 90) : state == Check.Todo ? Color.FromArgb(45, 240, 178, 50) : Theme.NavSelected;
            Color ink = state == Check.Ok ? Theme.Green : state == Check.Todo ? Look.Amber : Theme.SlateLight;
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            if (state == Check.Ok)
            {
                using (var pen = new Pen(ink, Theme.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] {
                        new PointF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.52f),
                        new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.66f),
                        new PointF(r.X + r.Width * 0.71f, r.Y + r.Height * 0.36f) });
                return;
            }
            TextRenderer.DrawText(g, state == Check.Todo ? "!" : "i", Look.Label, r, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // One person on the People page: whether they're connected, how much they used, and what you can do.
    public class PersonRow : Card, ISizedRow
    {
        // People whose device list is open; kept while the rows are rebuilt.
        static readonly HashSet<string> openIds = new HashSet<string>();
        const int BaseHeight = 132, LinkHeight = 26, DeviceHeight = 26;
        Rectangle link; // the "2 devices connected ▸" line, which opens and closes the list
        static readonly string[] WidestTexts = { "Copy key", "Disable", "Remove" };
        public readonly OutlineButton CopyKey = new OutlineButton { Text = "Copy key" };
        public readonly OutlineButton Toggle = new OutlineButton();
        public readonly OutlineButton Remove = new OutlineButton { Text = "Remove" };
        readonly System.Windows.Forms.Timer copied = new System.Windows.Forms.Timer { Interval = 2000 };
        public Person Person { get; private set; }

        public PersonRow(Person person)
        {
            Controls.AddRange(new Control[] { CopyKey, Toggle, Remove });
            copied.Tick += (s, e) => { copied.Stop(); CopyKey.Text = "Copy key"; };
            Set(person);
        }

        public void Set(Person person)
        {
            int before = Person != null ? RowHeight : -1;
            Person = person;
            Toggle.Text = person.Enabled ? "Disable" : "Enable";
            if (before != -1 && before != RowHeight && Parent != null) Parent.PerformLayout();
            Invalidate();
        }

        int DeviceCount { get { return Person.Devices.Count + (Person.OtherApp ? 1 : 0); } }
        bool HasDevices { get { return Person.Enabled && DeviceCount > 0; } }
        bool Open { get { return HasDevices && openIds.Contains(Person.Id); } }

        public int RowHeight
        {
            get { return BaseHeight + (HasDevices ? LinkHeight : 0) + (Open ? DeviceCount * DeviceHeight + 6 : 0); }
        }

        string DevicesText()
        {
            Person p = Person;
            int live = p.Devices.Count(d => d.Connected) + (p.OtherApp ? 1 : 0);
            string text = live > 0
                ? live + (p.OtherApp ? "+" : "") + (live == 1 && !p.OtherApp ? " device" : " devices") + " connected"
                : Pretty.Count(p.Devices.Count, "device", "devices");
            return text + (Open ? "  ▾" : "  ▸");
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!HasDevices || !link.Contains(e.Location)) return;
            if (!openIds.Remove(Person.Id)) openIds.Add(Person.Id);
            if (Parent != null) Parent.PerformLayout();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HasDevices && link.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        public void ShowCopied()
        {
            CopyKey.Text = "Copied";
            copied.Stop();
            copied.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) copied.Dispose();
            base.Dispose(disposing);
        }

        // Buttons along the bottom, each as wide as its text, all a bit narrower if the row is.
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            OutlineButton[] buttons = { CopyKey, Toggle, Remove };
            int pad = Theme.S(22), gap = Theme.S(10), h = Theme.S(40);
            var widths = new int[buttons.Length];
            int sum = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                widths[i] = TextRenderer.MeasureText(WidestTexts[i], Fonts.Outline).Width + Theme.S(28);
                sum += widths[i];
            }
            float fit = Math.Min(1f, (Width - pad * 2 - gap * (buttons.Length - 1)) / (float)sum);
            int x = pad, y = Height - Theme.S(18) - h;
            for (int i = 0; i < buttons.Length; i++)
            {
                int w = (int)(widths[i] * fit);
                buttons[i].SetBounds(x, y, w, h);
                x += w + gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Art.Smooth(g);
            Person p = Person;
            int pad = Theme.S(22), top = Theme.S(18), nameH = Look.LineHeight(Fonts.CardTitle);
            float dot = Theme.S(9);
            using (var b = new SolidBrush(p.Connected ? Theme.GreenLight : Theme.Faint))
                g.FillEllipse(b, pad, top + (nameH - dot) / 2f, dot, dot);
            int left = pad + Theme.S(18), width = Math.Max(10, Width - left - pad);
            const TextFormatFlags line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, p.Name, p.Enabled ? Fonts.CardTitle : Look.Struck, new Rectangle(left, top, width, nameH),
                p.Enabled ? Theme.Text : Theme.Muted, line);
            string state = !p.Enabled ? "Disabled"
                : p.Connected ? "Connected " + (p.ConnectedSince.HasValue ? Pretty.ConnectedFor(p.ConnectedSince.Value) : "now")
                : p.LastActive.HasValue ? "Not connected · last active " + Pretty.Ago(p.LastActive.Value)
                : "Never connected";
            int textH = Look.LineHeight(Fonts.CardText), y = top + nameH + Theme.S(4);
            TextRenderer.DrawText(g, state + "  ·  ↓ " + Pretty.Bytes(p.Down) + "  ·  ↑ " + Pretty.Bytes(p.Up), Fonts.CardText,
                new Rectangle(left, y, width, textH), Theme.Muted, line);
            if (!HasDevices)
            {
                link = Rectangle.Empty;
                return;
            }

            // The device line, and below it the devices when it's open.
            y += textH + Theme.S(8);
            string text = DevicesText();
            Size size = TextRenderer.MeasureText(g, text, Fonts.CardText, new Size(width, textH), line);
            link = new Rectangle(left, y, Math.Min(width, size.Width), textH);
            TextRenderer.DrawText(g, text, Fonts.CardText, link, Theme.AccentLight, line);
            if (!Open) return;
            y += Theme.S(LinkHeight) + Theme.S(2);
            float small = Theme.S(7);
            foreach (Device d in p.Devices)
            {
                using (var b = new SolidBrush(d.Connected ? Theme.GreenLight : Theme.Faint))
                    g.FillEllipse(b, left + Theme.S(2), y + (textH - small) / 2f, small, small);
                string when = d.Connected
                    ? "connected " + (d.ConnectedSince.HasValue ? Pretty.ConnectedFor(d.ConnectedSince.Value) : "now")
                    : d.LastSeen.HasValue ? "last seen " + Pretty.Ago(d.LastSeen.Value) : "";
                DeviceLine(g, left, y, width, textH, d.Name, new[] { d.Os, d.App, when });
                y += Theme.S(DeviceHeight);
            }
            if (p.OtherApp)
            {
                using (var b = new SolidBrush(Theme.GreenLight))
                    g.FillEllipse(b, left + Theme.S(2), y + (textH - small) / 2f, small, small);
                DeviceLine(g, left, y, width, textH, "Another app", new[] { "v2rayNG, Hiddify or similar: they don't say which device, or how many" });
            }
        }

        // "Name  ·  Windows 11 · Nevatch 1.5.0 · connected for 5 min", the name brighter.
        static void DeviceLine(Graphics g, int left, int y, int width, int h, string name, string[] details)
        {
            const TextFormatFlags line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int x = left + Theme.S(18);
            int nameW = Math.Min(width - Theme.S(18), TextRenderer.MeasureText(g, name, Fonts.CardText, new Size(width, h), line).Width);
            TextRenderer.DrawText(g, name, Fonts.CardText, new Rectangle(x, y, nameW, h), Theme.Text, line);
            string rest = string.Join(" · ", details.Where(s => !string.IsNullOrEmpty(s)).ToArray());
            int restX = x + nameW + Theme.S(4);
            if (rest.Length > 0 && restX < left + width)
                TextRenderer.DrawText(g, "·  " + rest, Fonts.CardText, new Rectangle(restX, y, left + width - restX, h), Theme.Muted, line);
        }
    }

    // One row on the Lag page: a name with what it means, two lines of round trips over the last two minutes,
    // and each line's latest value.
    public class LagRow : Card
    {
        // Fine steps at the low end so a 1-12 ms line isn't flat at the bottom of a 50 ms graph.
        static readonly int[] Scales = { 5, 10, 20, 30, 50, 75, 100, 150, 200, 300, 500, 750, 1000, 1500, 2000, 3000 };
        static readonly Color OtherColor = Color.FromArgb(150, 156, 186);
        readonly int slots;
        readonly string mainName, otherName;
        string title = "", verdict = "";
        Color verdictColor = Theme.Muted;
        int[] main = new int[0], other = new int[0];

        // slots: how many values fill the graph (newest on the right); both kinds of row cover two minutes.
        // The main line is blue, the other gray.
        public LagRow(int slots, string mainName, string otherName)
        {
            this.slots = slots;
            this.mainName = mainName;
            this.otherName = otherName;
        }

        public void Set(string title, int[] main, int[] other, string verdict, Color verdictColor)
        {
            this.title = title;
            this.main = main;
            this.other = other;
            this.verdict = verdict;
            this.verdictColor = verdictColor;
            Invalidate();
        }

        // The newest value: "12 ms", "no answer", or "–" before anything came in.
        static string Now(int[] values)
        {
            if (values.Length == 0) return "–";
            int last = values[values.Length - 1];
            return last >= 0 ? last + " ms" : "no answer";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Art.Smooth(g);
            int pad = Theme.S(22), width = Math.Max(10, Width - pad * 2), y = Theme.S(18);
            const TextFormatFlags line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int titleH = Look.LineHeight(Fonts.CardTitle), smallH = Look.LineHeight(Look.Small);

            // The name on the left, what it means on the right, on one line.
            int titleW = Math.Min(width / 2, TextRenderer.MeasureText(g, title, Fonts.CardTitle, new Size(width, titleH), TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(g, title, Fonts.CardTitle, new Rectangle(pad, y, titleW, titleH), Theme.Text, line);
            int verdictX = pad + titleW + Theme.S(16);
            TextRenderer.DrawText(g, verdict, Fonts.CardText, new Rectangle(verdictX, y, Math.Max(0, pad + width - verdictX), titleH), verdictColor,
                line | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            y += titleH + Theme.S(12);

            // Graph: ms labels in a gutter on the left, gridlines every quarter, time labels underneath.
            int gutter = Theme.S(52);
            var graph = new Rectangle(pad + gutter, y, Math.Max(10, width - gutter), Theme.S(120));
            using (GraphicsPath p = Art.RoundRect(graph, Theme.S(8)))
            using (var b = new SolidBrush(Theme.ContentBg))
                g.FillPath(b, p);
            int top = 0;
            foreach (int v in main.Concat(other)) top = Math.Max(top, v);
            int scale = Scales[Scales.Length - 1];
            foreach (int s in Scales) if (top * 1.1 <= s) { scale = s; break; }
            var plot = new RectangleF(graph.X + Theme.S(6), graph.Y + Theme.S(8), graph.Width - Theme.S(12), graph.Height - Theme.S(14));

            const TextFormatFlags rightMid = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            using (var grid = new Pen(Theme.CardBorder, Math.Max(1f, Theme.Scale)) { DashStyle = DashStyle.Dot })
            {
                for (int i = 0; i <= 4; i++)
                {
                    float gy = plot.Bottom - plot.Height * i / 4f;
                    if (i > 0) g.DrawLine(grid, plot.Left, gy, plot.Right, gy);
                    if (i % 2 == 0)
                    {
                        double ms = scale * i / 4.0;
                        string label = ms.ToString("0.#", CultureInfo.InvariantCulture) + " ms";
                        TextRenderer.DrawText(g, label, Look.Small, new Rectangle(pad, (int)gy - smallH / 2, gutter - Theme.S(8), smallH), Theme.Muted, rightMid);
                    }
                }
                for (int i = 1; i < 4; i++)
                {
                    float gx = plot.Left + plot.Width * i / 4f;
                    g.DrawLine(grid, gx, plot.Top, gx, plot.Bottom);
                }
            }

            DrawSeries(g, plot, other, scale, OtherColor, 1.5f, false, false);
            DrawSeries(g, plot, main, scale, Theme.Accent, 2.25f, true, true);
            y = graph.Bottom + Theme.S(4);
            var timeRow = new Rectangle(graph.X, y, graph.Width, smallH);
            TextRenderer.DrawText(g, "2 min ago", Look.Small, timeRow, Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.Left);
            TextRenderer.DrawText(g, "now", Look.Small, timeRow, Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.Right);
            y += smallH + Theme.S(10);

            // One legend line: each series with its latest value.
            int x = pad + gutter;
            foreach (var key in new[] { Tuple.Create(Theme.Accent, mainName, main), Tuple.Create(OtherColor, otherName, other) })
            {
                using (var b = new SolidBrush(key.Item1)) g.FillRectangle(b, x, y + smallH / 2 - Theme.S(1), Theme.S(14), Theme.S(3));
                x += Theme.S(20);
                TextRenderer.DrawText(g, key.Item2, Look.Small, new Point(x, y), Theme.Muted, TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(key.Item2, Look.Small, new Size(1000, 100), TextFormatFlags.NoPadding).Width + Theme.S(6);
                string now = Now(key.Item3);
                TextRenderer.DrawText(g, now, Fonts.CardTitle, new Rectangle(x, y - Theme.S(3), Theme.S(120), smallH + Theme.S(6)), Theme.Text,
                    line | TextFormatFlags.VerticalCenter);
                x += TextRenderer.MeasureText(now, Fonts.CardTitle, new Size(1000, 100), TextFormatFlags.NoPadding).Width + Theme.S(28);
            }
        }

        // A gap where nothing came back; for the main line also a red mark at the top, a soft fill under it
        // and a dot on the newest value.
        void DrawSeries(Graphics g, RectangleF plot, int[] values, int scale, Color color, float width, bool markLost, bool fill)
        {
            float step = plot.Width / Math.Max(1, slots - 1);
            float x0 = plot.X + (slots - values.Length) * step;
            var points = new List<PointF>();
            PointF? newest = null;
            using (var pen = new Pen(color, width * Theme.Scale) { LineJoin = LineJoin.Round })
            using (var area = new SolidBrush(Color.FromArgb(38, color)))
            using (var lost = new SolidBrush(Theme.Red))
            {
                for (int i = 0; i <= values.Length; i++)
                {
                    if (i == values.Length || values[i] < 0)
                    {
                        if (points.Count > 1)
                        {
                            if (fill)
                            {
                                var poly = new List<PointF>(points);
                                poly.Add(new PointF(points[points.Count - 1].X, plot.Bottom));
                                poly.Add(new PointF(points[0].X, plot.Bottom));
                                g.FillPolygon(area, poly.ToArray());
                            }
                            g.DrawLines(pen, points.ToArray());
                        }
                        if (i == values.Length && points.Count > 0) newest = points[points.Count - 1];
                        points.Clear();
                        if (i < values.Length && markLost)
                            g.FillRectangle(lost, x0 + i * step - Math.Max(1f, step * 0.4f), plot.Y, Math.Max(2f, step * 0.8f), plot.Height * 0.22f);
                        continue;
                    }
                    points.Add(new PointF(x0 + i * step, plot.Bottom - Math.Min(values[i], scale) / (float)scale * plot.Height));
                }
            }
            if (fill && newest.HasValue)
            {
                float r = Theme.S(4);
                using (var b = new SolidBrush(color)) g.FillEllipse(b, newest.Value.X - r, newest.Value.Y - r, r * 2, r * 2);
            }
        }
    }
}
