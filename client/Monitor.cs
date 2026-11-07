// The Monitor page: which programs on this PC use the internet, live, and how much since Start was clicked. It helps
// tell whether slowness comes from this PC (another program using the line) or from the connection or the server.
//   - Nothing runs until Start. Then a second copy of Nevatch starts with administrator rights ("--monitor <pipe>",
//     Windows asks first), reads Windows' own network events (the Microsoft-Windows-Kernel-Network provider, the same
//     source Resource Monitor uses) and sends the counts here through a pipe once a second. Stop, hiding the window to
//     the tray or quitting Nevatch closes the pipe, and that copy then stops too.
//   - Traffic an app sends through Nevatch's local proxy is counted for that app. Xray's own row is greyed, kept at the
//     bottom and left out of the totals, so nothing counts twice.
//   - Traffic inside the home network (printer, TV, NAS) isn't counted: it doesn't use internet data.
//   - svchost.exe is split by the Windows services it runs (Windows Update, Delivery Optimization, ...).
// C# 5 only, like the rest: built with the compiler that ships with Windows.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nevatch.Ui;

namespace Nevatch
{
    public class MonitorPage : Page
    {
        public const string Icon = "";      // "Diagnostic" in Windows' icon font
        const double Busy = 1024 * 1024;          // bytes/s on this PC that can make the VPN slow

        readonly NetMonitor monitor = new NetMonitor();
        readonly NetTally tally = new NetTally();
        readonly InfoCard card = new InfoCard(Icon, "Start");
        readonly OutlineButton copy = new OutlineButton { Text = "Copy list" };
        readonly ListView list = new SmoothListView();
        readonly Stopwatch tick = new Stopwatch();
        static readonly int[] Widths = { 190, 76, 76, 92, 92 }; // and Note takes the rest
        DateTime since, until;

        public MonitorPage() : base("What uses the internet")
        {
            card.Button.Click += (s, e) =>
            {
                if (monitor.Running) Stop();
                else Start();
            };
            copy.Click += (s, e) => CopyList();
            VisibleChanged += (s, e) => copy.Text = "Copy list";

            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Font = Fonts.CardText;
            list.BackColor = Theme.CardBg;
            list.ForeColor = Theme.Text;
            Dark.Controls(list);
            list.BorderStyle = BorderStyle.None;
            string[] names = { "Program", "↓ Now", "↑ Now", "Downloaded", "Uploaded", "Note" };
            for (int i = 0; i < names.Length; i++)
                list.Columns.Add(names[i], i < Widths.Length ? Theme.S(Widths[i]) : Theme.S(120),
                    i >= 1 && i <= 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            list.ListViewItemSorter = new ByTotal();
            list.Resize += (s, e) => FitNote();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open file location", null, (s, e) => OpenLocation());
            list.ContextMenuStrip = menu;

            Add(card, 130);
            Add(new ButtonRow(copy), 44);
            Add(new NativeScrollHost(list), -1);

            monitor.Counts += (internet, local) => BeginLater(delegate { Count(internet, local); });
            monitor.Stopped += error => BeginLater(delegate
            {
                until = DateTime.Now;
                ShowOff(error ?? "The monitor stopped by itself. Click Start to measure again.");
            });
            ShowOff(null);
        }

        // Stops measuring (Stop button, window hidden to the tray, Nevatch quitting). Keeps the list.
        public void Stop()
        {
            if (!monitor.Running) return;
            monitor.Stop();
            until = DateTime.Now;
            ShowOff(null);
        }

        void Start()
        {
            bool started;
            try { started = monitor.Start(); }
            catch (Exception ex)
            {
                ShowOff("Could not start the monitor: " + ex.Message);
                return;
            }
            if (!started)
            {
                ShowOff("The monitor needs Windows' permission to measure. Click Start and choose Yes.");
                return;
            }
            tally.Reset();
            list.Items.Clear();
            since = DateTime.Now;
            tick.Restart();
            card.Button.Text = "Stop";
            card.Set("Starting…", "One moment…");
        }

        void ShowOff(string error)
        {
            card.Button.Text = "Start";
            if (error != null)
                card.Set("Monitor is off", error, true);
            else if (list.Items.Count > 0)
                card.Set("Monitor is off", "The list shows what was measured from " + since.ToString("t") + " to " +
                    until.ToString("t") + ". Click Start to measure again.");
            else
                card.Set("Monitor is off", "Click Start to see, live, which programs on this PC use the internet. " +
                    "It only measures while it's on. Windows asks for permission first.");
        }

        void Count(Dictionary<int, long[]> internet, Dictionary<long, long[]> local)
        {
            if (!monitor.Running) return;
            double seconds = Math.Max(0.2, tick.Elapsed.TotalSeconds);
            tick.Restart();
            tally.Add(internet, local, seconds);

            list.BeginUpdate();
            foreach (NetUsage app in tally.Apps)
            {
                if (app.Total == 0) continue;
                if (app.Item == null)
                {
                    app.Item = new ListViewItem(new[] { "", "", "", "", "", "" });
                    app.Item.Tag = app;
                    if (app.IsVpn) app.Item.ForeColor = Theme.Muted;
                    list.Items.Add(app.Item);
                }
                Set(app.Item, 0, app.Name);
                Set(app.Item, 1, NetTally.Speed(app.DownNow));
                Set(app.Item, 2, NetTally.Speed(app.UpNow));
                Set(app.Item, 3, NetTally.Size(app.Down));
                Set(app.Item, 4, NetTally.Size(app.Up));
                Set(app.Item, 5, app.Note);
            }
            list.Sort();
            list.EndUpdate();

            double down = 0, up = 0;
            NetUsage top = null;
            foreach (NetUsage app in tally.Apps)
            {
                if (app.IsVpn) continue;
                down += app.DownNow;
                up += app.UpNow;
                if (top == null || app.DownNow + app.UpNow > top.DownNow + top.UpNow) top = app;
            }
            string title = "Right now: ↓ " + NetTally.Speed(down, "0 KB/s") + "    ↑ " + NetTally.Speed(up, "0 KB/s");
            if (top != null && down + up >= Busy)
                card.Set(title, top.Name + " uses the most: " + NetTally.Speed(top.DownNow + top.UpNow) +
                    ". Programs on this PC share your internet with the VPN, so this can slow it down.");
            else
                card.Set(title, "Programs on this PC use little internet, so they aren't slowing the VPN down. " +
                    "Lag shows whether it's your own internet or the way to the server.");
        }

        static void Set(ListViewItem item, int column, string text)
        {
            if (item.SubItems[column].Text != text) item.SubItems[column].Text = text;
        }

        void FitNote()
        {
            int used = 0;
            for (int i = 0; i < Widths.Length; i++) used += list.Columns[i].Width;
            list.Columns[Widths.Length].Width = Math.Max(Theme.S(120), list.ClientSize.Width - used);
        }

        void CopyList()
        {
            List<NetUsage> apps = tally.Sorted();
            if (apps.Count == 0) return;
            long down = 0, up = 0;
            foreach (NetUsage app in apps)
                if (!app.IsVpn)
                {
                    down += app.Down;
                    up += app.Up;
                }
            var sb = new StringBuilder();
            sb.AppendLine("Nevatch monitor, " + since.ToString("g") + " to " + (monitor.Running ? DateTime.Now : until).ToString("t") +
                          ": downloaded " + NetTally.Size(down) + ", uploaded " + NetTally.Size(up));
            foreach (NetUsage app in apps) sb.AppendLine(app.Report());
            try
            {
                Clipboard.SetText(sb.ToString());
                copy.Text = "Copied";
            }
            catch { }
        }

        void OpenLocation()
        {
            if (list.SelectedItems.Count == 0) return;
            var app = (NetUsage)list.SelectedItems[0].Tag;
            if (app.Path == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + app.Path + "\""); }
            catch { }
        }

        void BeginLater(MethodInvoker action)
        {
            try { if (IsHandleCreated) BeginInvoke(action); }
            catch (InvalidOperationException) { } // closing
        }

        // The VPN's own row last, the rest by total, biggest first.
        sealed class ByTotal : System.Collections.IComparer
        {
            public int Compare(object x, object y)
            {
                var a = (NetUsage)((ListViewItem)x).Tag;
                var b = (NetUsage)((ListViewItem)y).Tag;
                if (a.IsVpn != b.IsVpn) return a.IsVpn ? 1 : -1;
                int c = b.Total.CompareTo(a.Total);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        sealed class SmoothListView : ListView
        {
            // Rows are drawn by Windows; the header is drawn here, since the dark theme leaves its text dark on dark.
            public SmoothListView()
            {
                DoubleBuffered = true;
                OwnerDraw = true;
            }

            protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
            {
                using (var b = new SolidBrush(Theme.Rail)) e.Graphics.FillRectangle(b, e.Bounds);
                var text = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, Math.Max(0, e.Bounds.Width - Theme.S(16)), e.Bounds.Height);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                    (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Fonts.Category, text, Theme.NavText, flags);
                using (var pen = new Pen(Theme.CardBorder)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = true; }
            protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { e.DrawDefault = true; }
        }
    }

    // Starts the reader with administrator rights and receives its counts. Its events come on a background thread.
    public class NetMonitor
    {
        NamedPipeServerStream pipe;

        // Bytes per process since the last batch: pid -> {received, sent} with the internet, and
        // (pid << 16) + remote port -> {received, sent} with this PC itself.
        public event Action<Dictionary<int, long[]>, Dictionary<long, long[]>> Counts;
        // The reader stopped by itself; the text says why, or is null.
        public event Action<string> Stopped;

        public bool Running { get { return pipe != null; } }

        // False when the person said no in the Windows prompt.
        public bool Start()
        {
            string name = "Nevatch.Monitor." + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                Process.Start(new ProcessStartInfo(Paths.Exe, "--monitor " + name) { UseShellExecute = true, Verb = "runas" });
            }
            catch (Win32Exception ex)
            {
                server.Dispose();
                if (ex.NativeErrorCode == 1223) return false; // ERROR_CANCELLED: "No" in the Windows prompt
                throw;
            }
            catch
            {
                server.Dispose();
                throw;
            }
            pipe = server;
            var thread = new Thread(() => Read(server));
            thread.IsBackground = true;
            thread.Start();
            return true;
        }

        public void Stop()
        {
            NamedPipeServerStream server = Interlocked.Exchange(ref pipe, null);
            if (server != null) server.Dispose();
        }

        void Read(NamedPipeServerStream server)
        {
            string error = null;
            try
            {
                IAsyncResult connect = server.BeginWaitForConnection(null, null);
                if (!connect.AsyncWaitHandle.WaitOne(30000)) throw new TimeoutException("The monitor didn't start in time.");
                server.EndWaitForConnection(connect);
                var reader = new BinaryReader(server);
                while (true)
                {
                    if (reader.ReadByte() == 1)
                    {
                        error = "Could not start the monitor: " + reader.ReadString();
                        break;
                    }
                    var internet = new Dictionary<int, long[]>();
                    for (int n = reader.ReadInt32(); n > 0; n--)
                        internet[reader.ReadInt32()] = new[] { reader.ReadInt64(), reader.ReadInt64() };
                    var local = new Dictionary<long, long[]>();
                    for (int n = reader.ReadInt32(); n > 0; n--)
                        local[reader.ReadInt64()] = new[] { reader.ReadInt64(), reader.ReadInt64() };
                    var handler = Counts;
                    if (handler != null) handler(internet, local);
                }
            }
            catch (ObjectDisposedException) { } // Stop()
            catch (IOException) { }             // the reader ended
            catch (Exception ex) { error = ex.Message; }

            // Only tell when it wasn't Stop() that ended it.
            if (Interlocked.CompareExchange(ref pipe, null, server) != server) return;
            server.Dispose();
            var stopped = Stopped;
            if (stopped != null) stopped(error);
        }

        // The copy with administrator rights ("--monitor <pipe>"): counts until the pipe closes.
        public static int Feed(string name)
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out))
            using (var trace = new NetTrace())
            {
                try { pipe.Connect(10000); }
                catch { return 1; }
                var writer = new BinaryWriter(pipe);
                try
                {
                    try { trace.Start(); }
                    catch (Exception ex)
                    {
                        writer.Write((byte)1);
                        writer.Write(ex.Message);
                        writer.Flush();
                        return 1;
                    }
                    while (true)
                    {
                        Thread.Sleep(1000);
                        Dictionary<int, long[]> internet;
                        Dictionary<long, long[]> local;
                        trace.Take(out internet, out local);
                        writer.Write((byte)0);
                        writer.Write(internet.Count);
                        foreach (var pair in internet)
                        {
                            writer.Write(pair.Key);
                            writer.Write(pair.Value[0]);
                            writer.Write(pair.Value[1]);
                        }
                        writer.Write(local.Count);
                        foreach (var pair in local)
                        {
                            writer.Write(pair.Key);
                            writer.Write(pair.Value[0]);
                            writer.Write(pair.Value[1]);
                        }
                        writer.Flush();
                    }
                }
                catch (IOException) { return 0; } // the page stopped listening, or Nevatch closed
            }
        }

        // For testing ("--monitor-test <file> <seconds>"): runs the monitor like the page does and writes the list.
        public static int Test(string file, int seconds)
        {
            var monitor = new NetMonitor();
            var tally = new NetTally();
            var clock = Stopwatch.StartNew();
            var stopped = new ManualResetEvent(false);
            string error = null;
            monitor.Counts += (internet, local) =>
            {
                lock (tally)
                {
                    tally.Add(internet, local, Math.Max(0.2, clock.Elapsed.TotalSeconds));
                    clock.Restart();
                }
            };
            monitor.Stopped += e =>
            {
                error = e ?? "stopped by itself";
                stopped.Set();
            };
            var sb = new StringBuilder();
            try
            {
                if (!monitor.Start()) error = "no permission";
                else
                {
                    stopped.WaitOne(seconds * 1000);
                    monitor.Stop();
                    lock (tally)
                        foreach (NetUsage app in tally.Sorted()) sb.AppendLine(app.Report());
                }
            }
            catch (Exception ex) { error = ex.Message; }
            if (error != null) sb.AppendLine("error: " + error);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return 0;
        }
    }

    // A real-time ETW session on Microsoft-Windows-Kernel-Network that adds up bytes per process.
    public sealed class NetTrace : IDisposable
    {
        const string SessionName = "Nevatch Monitor";
        static readonly Guid KernelNetwork = new Guid("7DD42A49-5329-4832-8DFD-43D979153A88");
        const ulong NoTrace = ulong.MaxValue;
        const int Internet = 0, ThisPc = 1, Home = 2;

        readonly object sync = new object();
        Dictionary<int, long[]> internet = new Dictionary<int, long[]>();
        Dictionary<long, long[]> local = new Dictionary<long, long[]>();
        ulong session, trace = NoTrace;
        IntPtr sessionName;
        EventRecordCallback callback;
        Thread thread;

        public void Start()
        {
            int error = StartSession();
            if (error == 183) // ERROR_ALREADY_EXISTS: left over from a monitor that was killed
            {
                StopSession(0, SessionName);
                error = StartSession();
            }
            if (error != 0) throw new Win32Exception(error);

            // Keywords 0x10 and 0x20: IPv4 and IPv6.
            Guid provider = KernelNetwork;
            error = EnableTraceEx2(session, ref provider, 1, 5, 0x30, 0, 0, IntPtr.Zero);
            if (error != 0) throw new Win32Exception(error);

            sessionName = Marshal.StringToHGlobalUni(SessionName);
            callback = OnEvent;
            var log = new EventTraceLogfile();
            log.LoggerName = sessionName;
            log.ProcessTraceMode = 0x10000100; // PROCESS_TRACE_MODE_EVENT_RECORD | PROCESS_TRACE_MODE_REAL_TIME
            log.LogfileHeader.TimeZone = new byte[172];
            log.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(callback);
            trace = OpenTrace(ref log);
            if (trace == NoTrace || trace == 0xFFFFFFFF)
            {
                trace = NoTrace;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            thread = new Thread(() => ProcessTrace(new[] { trace }, 1, IntPtr.Zero, IntPtr.Zero));
            thread.IsBackground = true;
            thread.Start();
        }

        // Hands over what was counted since the last call, and starts counting afresh.
        public void Take(out Dictionary<int, long[]> internetBytes, out Dictionary<long, long[]> localBytes)
        {
            lock (sync)
            {
                internetBytes = internet;
                localBytes = local;
                internet = new Dictionary<int, long[]>();
                local = new Dictionary<long, long[]>();
            }
        }

        void OnEvent(IntPtr record)
        {
            bool received, v6;
            switch (Marshal.ReadInt16(record, 40)) // EVENT_HEADER.EventDescriptor.Id
            {
                case 10: case 42: received = false; v6 = false; break; // TCP, UDP data sent over IPv4
                case 11: case 43: received = true; v6 = false; break;  // ... received over IPv4
                case 26: case 58: received = false; v6 = true; break;  // ... sent over IPv6
                case 27: case 59: received = true; v6 = true; break;   // ... received over IPv6
                default: return;
            }
            // UserData: PID, size, daddr, saddr, dport, sport, ... The "d" side is the other end, both ways.
            int addr = v6 ? 16 : 4;
            int length = Marshal.ReadInt16(record, 86) & 0xFFFF;
            if (length < 12 + 2 * addr) return;
            IntPtr data = Marshal.ReadIntPtr(record, 88 + IntPtr.Size);
            int pid = Marshal.ReadInt32(data, 0);
            int size = Marshal.ReadInt32(data, 4);
            int where = Where(data + 8, v6);
            if (where == Home) return;
            int port = Marshal.ReadByte(data, 8 + 2 * addr) << 8 | Marshal.ReadByte(data, 9 + 2 * addr);

            lock (sync)
            {
                long[] bytes;
                if (where == Internet)
                {
                    if (!internet.TryGetValue(pid, out bytes)) internet[pid] = bytes = new long[2];
                }
                else
                {
                    long key = ((long)pid << 16) + port;
                    if (!local.TryGetValue(key, out bytes)) local[key] = bytes = new long[2];
                }
                bytes[received ? 0 : 1] += size;
            }
        }

        // Where the other end is: the internet, this PC itself, or the home network (incl. multicast and broadcast).
        static int Where(IntPtr a, bool v6)
        {
            int v4 = 0;
            if (v6)
            {
                byte b0 = Marshal.ReadByte(a), b1 = Marshal.ReadByte(a, 1);
                if (b0 == 0xFF || (b0 == 0xFE && (b1 & 0xC0) == 0x80) || (b0 & 0xFE) == 0xFC) return Home;
                for (int i = 0; i < 10; i++) if (Marshal.ReadByte(a, i) != 0) return Internet;
                if (Marshal.ReadByte(a, 10) == 0xFF && Marshal.ReadByte(a, 11) == 0xFF) v4 = 12; // IPv4 written as IPv6
                else
                {
                    for (int i = 10; i < 15; i++) if (Marshal.ReadByte(a, i) != 0) return Internet;
                    return Marshal.ReadByte(a, 15) <= 1 ? ThisPc : Internet; // ::1 (and ::)
                }
            }
            byte x = Marshal.ReadByte(a, v4), y = Marshal.ReadByte(a, v4 + 1);
            if (x == 127 || x == 0) return ThisPc;
            if (x == 10 || (x == 172 && (y & 0xF0) == 16) || (x == 192 && y == 168) || (x == 169 && y == 254) || x >= 224) return Home;
            return Internet;
        }

        public void Dispose()
        {
            if (session != 0)
            {
                StopSession(session, null);
                session = 0;
            }
            if (trace != NoTrace)
            {
                CloseTrace(trace);
                trace = NoTrace;
            }
            if (thread != null) thread.Join(2000);
            if (sessionName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sessionName);
                sessionName = IntPtr.Zero;
            }
        }

        int StartSession()
        {
            IntPtr props = Properties(false);
            try { return StartTrace(out session, SessionName, props); }
            finally { Marshal.FreeHGlobal(props); }
        }

        static void StopSession(ulong handle, string name)
        {
            IntPtr props = Properties(true);
            try { ControlTrace(handle, name, props, 1); } // EVENT_TRACE_CONTROL_STOP
            finally { Marshal.FreeHGlobal(props); }
        }

        // EVENT_TRACE_PROPERTIES plus room behind it for the names Windows writes back.
        static IntPtr Properties(bool control)
        {
            int size = Marshal.SizeOf(typeof(EventTraceProperties));
            int total = size + 2048;
            IntPtr p = Marshal.AllocHGlobal(total);
            for (int i = 0; i < total; i++) Marshal.WriteByte(p, i, 0);
            var props = new EventTraceProperties();
            props.WnodeBufferSize = (uint)total;
            props.WnodeFlags = 0x20000; // WNODE_FLAG_TRACED_GUID
            props.ClientContext = 1;    // timestamps from QueryPerformanceCounter
            props.BufferSize = 64;      // KB
            props.MinimumBuffers = 8;
            props.MaximumBuffers = 64;
            props.LogFileMode = 0x100;  // EVENT_TRACE_REAL_TIME_MODE
            props.FlushTimer = 1;       // seconds
            props.LoggerNameOffset = (uint)size;
            if (control) props.LogFileNameOffset = (uint)(size + 1024);
            Marshal.StructureToPtr(props, p, false);
            return p;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void EventRecordCallback(IntPtr record);

        [StructLayout(LayoutKind.Sequential)]
        struct EventTraceProperties
        {
            public uint WnodeBufferSize, ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext, WnodeFlags;
            public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset, LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct EventTrace
        {
            public ushort Size, FieldTypeFlags;
            public uint Version, ThreadId, ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public long ProcessorTime;
            public uint InstanceId, ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength, ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TraceLogfileHeader
        {
            public uint BufferSize, Version, ProviderVersion, NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName, LogFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 172)] public byte[] TimeZone;
            public long BootTime, PerfFreq, StartTime;
            public uint ReservedFlags, BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct EventTraceLogfile
        {
            public IntPtr LogFileName, LoggerName;
            public long CurrentTime;
            public uint BuffersRead, ProcessTraceMode;
            public EventTrace CurrentEvent;
            public TraceLogfileHeader LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize, Filled, EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")]
        static extern int StartTrace(out ulong handle, string name, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
        static extern int ControlTrace(ulong handle, string name, IntPtr properties, uint code);

        [DllImport("advapi32.dll")]
        static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level,
                                         ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr parameters);

        [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "OpenTraceW")]
        static extern ulong OpenTrace(ref EventTraceLogfile logfile);

        [DllImport("advapi32.dll")]
        static extern int ProcessTrace(ulong[] handles, uint count, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        static extern int CloseTrace(ulong handle);
    }

    // One program (all its processes together), or one group of Windows services.
    public class NetUsage
    {
        public string Name, File, Path;
        public bool IsVpn;
        public long Down, Up, ViaVpn, TickDown, TickUp;
        public double DownNow, UpNow;
        public readonly HashSet<int> Pids = new HashSet<int>();
        public ListViewItem Item;

        public long Total { get { return Down + Up; } }

        public string Note
        {
            get
            {
                if (IsVpn) return "The VPN itself: it carries the apps marked \"through VPN\", so it isn't counted again";
                var parts = new List<string>();
                if (File != "") parts.Add(File);
                if (Pids.Count > 1) parts.Add(Pids.Count + " processes");
                if (ViaVpn > 0)
                {
                    long share = ViaVpn * 100 / Math.Max(1, Total);
                    parts.Add(share >= 99 ? "through VPN" : share + "% through VPN");
                }
                return string.Join(", ", parts.ToArray());
            }
        }

        public string Report()
        {
            return Name + ": downloaded " + NetTally.Size(Down) + ", uploaded " + NetTally.Size(Up) + " (" + Note + ")";
        }
    }

    // Adds up the reader's counts per program.
    public class NetTally
    {
        static readonly HashSet<string> VpnEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "xray", "v2ray", "sing-box", "mihomo", "clash", "clash-meta" };

        readonly Dictionary<int, NetUsage> byPid = new Dictionary<int, NetUsage>();
        readonly Dictionary<string, NetUsage> apps = new Dictionary<string, NetUsage>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<NetUsage> Apps { get { return apps.Values; } }

        public void Add(Dictionary<int, long[]> internet, Dictionary<long, long[]> local, double seconds)
        {
            foreach (var pair in internet) Count(AppFor(pair.Key), pair.Key, pair.Value, false);
            if (local.Count > 0)
            {
                // Traffic inside this PC counts only when it's an app talking to a VPN's local proxy: that's the
                // app's internet traffic, which the VPN then sends on.
                Dictionary<int, int> listeners = Listeners();
                foreach (var pair in local)
                {
                    int pid = (int)(pair.Key >> 16), owner;
                    if (!listeners.TryGetValue((int)(pair.Key & 0xFFFF), out owner) || owner == pid) continue;
                    NetUsage app = AppFor(pid);
                    if (!app.IsVpn && AppFor(owner).IsVpn) Count(app, pid, pair.Value, true);
                }
            }
            foreach (NetUsage app in apps.Values)
            {
                app.DownNow = (app.DownNow + app.TickDown / seconds) / 2;
                app.UpNow = (app.UpNow + app.TickUp / seconds) / 2;
                app.TickDown = app.TickUp = 0;
            }
        }

        static void Count(NetUsage app, int pid, long[] bytes, bool viaVpn)
        {
            app.Down += bytes[0];
            app.Up += bytes[1];
            app.TickDown += bytes[0];
            app.TickUp += bytes[1];
            if (viaVpn) app.ViaVpn += bytes[0] + bytes[1];
            app.Pids.Add(pid);
        }

        public void Reset()
        {
            byPid.Clear(); // process ids get reused
            apps.Clear();
        }

        public List<NetUsage> Sorted()
        {
            var list = new List<NetUsage>();
            foreach (NetUsage app in apps.Values) if (app.Total > 0) list.Add(app);
            list.Sort((a, b) => a.IsVpn != b.IsVpn ? (a.IsVpn ? 1 : -1) : b.Total.CompareTo(a.Total));
            return list;
        }

        NetUsage AppFor(int pid)
        {
            NetUsage app;
            if (byPid.TryGetValue(pid, out app)) return app;

            string key, name, file, path = null;
            if (pid == 0 || pid == 4)
            {
                key = "system";
                name = "Windows itself";
                file = "System";
            }
            else
            {
                path = ProcessPath(pid);
                file = path != null ? System.IO.Path.GetFileName(path) : ProcessName(pid);
                if (file == null)
                {
                    key = "closed";
                    name = "Programs that closed before they could be named";
                    file = "";
                }
                else if (file.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> names, shown;
                    Services(pid, out names, out shown);
                    key = "svchost:" + string.Join(",", names.ToArray());
                    name = shown.Count == 0 ? "Windows service" : shown.Count <= 2
                        ? string.Join(", ", shown.ToArray())
                        : shown[0] + ", " + shown[1] + " and " + (shown.Count - 2) + " more";
                    file = "svchost.exe" + (names.Count == 0 ? "" : " (" + string.Join(", ", names.ToArray()) + ")");
                }
                else
                {
                    key = path ?? file;
                    name = Describe(path) ?? System.IO.Path.GetFileNameWithoutExtension(file);
                }
            }

            if (!apps.TryGetValue(key, out app))
            {
                app = new NetUsage { Name = name, File = file, Path = path };
                app.IsVpn = file != "" && VpnEngines.Contains(System.IO.Path.GetFileNameWithoutExtension(file));
                if (app.IsVpn && path != null && path.StartsWith(Paths.DataDir, StringComparison.OrdinalIgnoreCase))
                    app.Name = "Nevatch VPN (Xray)";
                apps[key] = app;
            }
            byPid[pid] = app;
            return app;
        }

        public static string Size(double bytes)
        {
            if (bytes < 1024) return bytes.ToString("0") + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024).ToString("0") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).ToString(bytes < 10 * 1024 * 1024 ? "0.0" : "0") + " MB";
            return (bytes / (1024 * 1024 * 1024)).ToString("0.00") + " GB";
        }

        public static string Speed(double bytesPerSecond) { return Speed(bytesPerSecond, ""); }

        // Below 1 KB/s shows the given text instead.
        public static string Speed(double bytesPerSecond, string none)
        {
            return bytesPerSecond < 1024 ? none : Size(bytesPerSecond) + "/s";
        }

        static string Describe(string path)
        {
            if (path == null) return null;
            try
            {
                string text = FileVersionInfo.GetVersionInfo(path).FileDescription;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch { return null; }
        }

        static void Services(int pid, out List<string> names, out List<string> shown)
        {
            names = new List<string>();
            shown = new List<string>();
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT Name, DisplayName FROM Win32_Service WHERE ProcessId = " + pid))
                    foreach (ManagementBaseObject service in search.Get())
                    {
                        names.Add((string)service["Name"]);
                        shown.Add((string)service["DisplayName"]);
                    }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
        }

        static string ProcessName(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) return p.ProcessName + ".exe";
            }
            catch { return null; }
        }

        static string ProcessPath(int pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) return null;
            try
            {
                var name = new StringBuilder(1024);
                int size = name.Capacity;
                return QueryFullProcessImageName(process, 0, name, ref size) ? name.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        // Which process listens on each TCP port of this PC (IPv4 and IPv6), to see where loopback traffic goes.
        static Dictionary<int, int> Listeners()
        {
            var owners = new Dictionary<int, int>();
            AddListeners(owners, 2, 24, 8, 20);   // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid
            AddListeners(owners, 23, 56, 20, 52); // MIB_TCP6ROW_OWNER_PID: local addr[16], scope, local port, remote addr[16], scope, remote port, state, pid
            return owners;
        }

        static void AddListeners(Dictionary<int, int> owners, int family, int rowSize, int portAt, int pidAt)
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0); // TCP_TABLE_OWNER_PID_LISTENER
            size += 4096;
            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(table, ref size, false, family, 3, 0) != 0) return;
                int count = Marshal.ReadInt32(table);
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = table + 4 + i * rowSize;
                    int port = Marshal.ReadByte(row, portAt) << 8 | Marshal.ReadByte(row, portAt + 1);
                    owners[port] = Marshal.ReadInt32(row, pidAt);
                }
            }
            finally { Marshal.FreeHGlobal(table); }
        }

        [DllImport("iphlpapi.dll")]
        static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, int reserved);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
    }
}
