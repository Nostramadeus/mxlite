// MxLite - minimal replacement for Logitech Options+ (MX Master 3/3S, Windows 10)
// Gesture button (under scroll wheel): hold+move = switch virtual desktops, click = Task View.
// SmartShift wheel threshold control. Tray icon + settings GUI. Zero dependencies.
// Speaks Logitech HID++ 2.0 directly over HID (USB receiver or Bluetooth).
// Build: build.cmd (uses the csc.exe that ships with Windows/.NET Framework 4.x)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class Native
{
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_RW = 0x3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr min, IntPtr max, uint flags);

    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid gClass, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll")]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData, ref Guid gClass, int idx, ref SP_DEVICE_INTERFACE_DATA ifData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA ifData, IntPtr detail, int detailSize, out int required, IntPtr devInfoData);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, IntPtr read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int n, IntPtr written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] public static extern bool AllocConsole();

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    // x64 INPUT: 8-byte union offset, total size 40 (union sized by MOUSEINPUT).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint n, INPUT[] inputs, int size);
}

class HidppDevice : IDisposable
{
    public SafeFileHandle Handle;
    public string Path;
    public int InLen, OutLen;
    public byte DevIdx;

    public void Dispose() { if (Handle != null && !Handle.IsInvalid) Handle.Dispose(); }

    public bool Write(byte[] report)
    {
        byte[] buf = new byte[OutLen];
        Array.Copy(report, buf, Math.Min(report.Length, OutLen));
        IntPtr ev = Native.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        IntPtr ov = Marshal.AllocHGlobal(IntPtr.Size * 4);
        try
        {
            for (int i = 0; i < 4; i++) Marshal.WriteIntPtr(ov, i * IntPtr.Size, IntPtr.Zero);
            Marshal.WriteIntPtr(ov, IntPtr.Size * 3, ev);
            if (!Native.WriteFile(Handle, buf, OutLen, IntPtr.Zero, ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 997) return false; // 997 = ERROR_IO_PENDING
            }
            if (Native.WaitForSingleObject(ev, 3000) != 0) { Native.CancelIoEx(Handle, ov); int b0; Native.GetOverlappedResult(Handle, ov, out b0, true); return false; }
            int b;
            return Native.GetOverlappedResult(Handle, ov, out b, true);
        }
        finally { Marshal.FreeHGlobal(ov); Native.CloseHandle(ev); }
    }

    // >0 = bytes, 0 = timeout, -1 = device gone.
    public int Read(byte[] buf, int timeoutMs)
    {
        IntPtr ev = Native.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        IntPtr ov = Marshal.AllocHGlobal(IntPtr.Size * 4);
        try
        {
            for (int i = 0; i < 4; i++) Marshal.WriteIntPtr(ov, i * IntPtr.Size, IntPtr.Zero);
            Marshal.WriteIntPtr(ov, IntPtr.Size * 3, ev);
            if (!Native.ReadFile(Handle, buf, InLen, IntPtr.Zero, ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 997) return -1;
            }
            if (Native.WaitForSingleObject(ev, (uint)timeoutMs) != 0)
            {
                Native.CancelIoEx(Handle, ov);
                int b0; Native.GetOverlappedResult(Handle, ov, out b0, true);
                return 0;
            }
            int b;
            if (!Native.GetOverlappedResult(Handle, ov, out b, true)) return -1;
            return b;
        }
        finally { Marshal.FreeHGlobal(ov); Native.CloseHandle(ev); }
    }
}

class Config
{
    public string SmartShift = "auto";   // "auto" or 1..254
    public string Click = "Win+Tab";
    public string Left = "Ctrl+Win+Left";
    public string Right = "Ctrl+Win+Right";
    public string Up = "";
    public string Down = "";
    public int GestureDistance = 300;
    public int ClickMaxMove = 245;
    public bool Repeat = false;
    public int ExtraDistance = 450;      // movement after the first jump needed for each extra desktop
    public int MultiHoldMs = 400;        // extra jumps (and turn-back) only after the button has been held this long
    public bool SideButtons = false;     // true = thumb buttons send Back/Forward below; false = normal browser back/forward
    public string Back = "Ctrl+Win+Left";
    public string Forward = "Ctrl+Win+Right";

    public static Config Load(string path)
    {
        Config c = new Config();
        if (!File.Exists(path)) return c;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw;
            int sc = line.IndexOf(';');
            if (sc >= 0) line = line.Substring(0, sc);
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string k = line.Substring(0, eq).Trim().ToLowerInvariant();
            string v = line.Substring(eq + 1).Trim();
            switch (k)
            {
                case "smartshift": c.SmartShift = v.ToLowerInvariant(); break;
                case "click": c.Click = v; break;
                case "left": c.Left = v; break;
                case "right": c.Right = v; break;
                case "up": c.Up = v; break;
                case "down": c.Down = v; break;
                case "gesture_distance": int.TryParse(v, out c.GestureDistance); break;
                case "click_max_move": int.TryParse(v, out c.ClickMaxMove); break;
                case "repeat": c.Repeat = v != "0" && v.ToLowerInvariant() != "false"; break;
                case "extra_distance": int.TryParse(v, out c.ExtraDistance); break;
                case "multi_hold_ms": int.TryParse(v, out c.MultiHoldMs); break;
                case "side_buttons": c.SideButtons = v != "0" && v.ToLowerInvariant() != "false"; break;
                case "back": c.Back = v; break;
                case "forward": c.Forward = v; break;
            }
        }
        if (c.GestureDistance < 100) c.GestureDistance = 100;
        if (c.ExtraDistance < 100) c.ExtraDistance = 100;
        if (c.ClickMaxMove < 50) c.ClickMaxMove = 50;
        if (c.MultiHoldMs < 0) c.MultiHoldMs = 0;
        return c;
    }

    public void Save(string path)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("; MxLite config - edited by the settings GUI; safe to edit by hand too.");
        sb.AppendLine("; smartshift: \"auto\" = leave the wheel as the mouse has it.");
        sb.AppendLine(";   Or 1-50: LOWER = free-spins on a gentle flick, HIGHER = needs a hard flick.");
        sb.AppendLine("smartshift = " + SmartShift);
        sb.AppendLine();
        sb.AppendLine("; Button under the scroll wheel. Empty = do nothing.");
        sb.AppendLine("; Keys: Ctrl Shift Alt Win A-Z 0-9 F1-F24 Left Right Up Down Tab Space Enter Esc PgUp PgDn Home End");
        sb.AppendLine(";       PlayPause NextTrack PrevTrack VolUp VolDown Mute BrowserBack BrowserFwd PrtSc");
        sb.AppendLine("click = " + Click);
        sb.AppendLine("left = " + Left);
        sb.AppendLine("right = " + Right);
        sb.AppendLine("up = " + Up);
        sb.AppendLine("down = " + Down);
        sb.AppendLine();
        sb.AppendLine("; Mouse movement (in counts) needed per gesture step.");
        sb.AppendLine("gesture_distance = " + GestureDistance);
        sb.AppendLine("; Maximum wiggle for a press to still count as a click.");
        sb.AppendLine("click_max_move = " + ClickMaxMove);
        sb.AppendLine("; 1 = keep moving while holding to jump several desktops in one hold. 0 = one per hold (default).");
        sb.AppendLine("repeat = " + (Repeat ? "1" : "0"));
        sb.AppendLine("; With repeat = 1: extra movement (in counts) after the first jump for EACH further desktop.");
        sb.AppendLine(";   Example: gesture_distance 300 + extra_distance 450 -> 300 = 1 desktop, 750 = 2, 1200 = 3.");
        sb.AppendLine("extra_distance = " + ExtraDistance);
        sb.AppendLine("; With repeat = 1: a quick sweep (released sooner than this many milliseconds after the press)");
        sb.AppendLine(";   moves only 1 desktop. Hold at least this long to jump more. 0 = no wait.");
        sb.AppendLine("multi_hold_ms = " + MultiHoldMs);
        sb.AppendLine();
        sb.AppendLine("; Thumb side buttons. 1 = they send back/forward below (default: switch desktops).");
        sb.AppendLine(";   0 = leave them alone (normal browser back/forward).");
        sb.AppendLine("side_buttons = " + (SideButtons ? "1" : "0"));
        sb.AppendLine("; back = rear thumb button, forward = front thumb button. Same key names as above.");
        sb.AppendLine("back = " + Back);
        sb.AppendLine("forward = " + Forward);
        File.WriteAllText(path, sb.ToString());
    }
}

class MxLite
{
    const byte SWID = 0x0A;
    const ushort GESTURE_CID = 0x00C4; // button below the scroll wheel
    const ushort BACK_CID = 0x0053;    // rear thumb button (browser back)
    const ushort FWD_CID = 0x0056;     // front thumb button (browser forward)

    static bool debug, probe;
    static string baseDir, logPath, iniPath;
    public static volatile Config Cfg;
    public static volatile bool CfgDirty;
    public static volatile bool Connected;
    public static string DeviceName = "";
    public static string Status = "searching for mouse...";
    public static int WheelModeOnMouse = -1, WheelThreshOnMouse = -1;
    static int origThresh = -1;          // wheel value before we ever touched it (this session)
    static bool wroteCustomShift;
    static readonly object logLock = new object();

    public static void Log(string msg)
    {
        string line = DateTime.Now.ToString("HH:mm:ss ") + msg;
        if (debug || probe) Console.WriteLine(line);
        lock (logLock) { try { File.AppendAllText(logPath, line + "\r\n"); } catch { } }
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool openSettings = false;
        foreach (string a in args)
        {
            if (a == "--debug") debug = true;
            if (a == "--probe") probe = true;
            if (a == "--settings") openSettings = true;
        }
        if (debug || probe) Native.AllocConsole();

        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        logPath = Path.Combine(baseDir, "mxlite.log");
        iniPath = Path.Combine(baseDir, "mxlite.ini");
        try { if (File.Exists(logPath) && new FileInfo(logPath).Length > 512 * 1024) File.Delete(logPath); } catch { }
        Cfg = Config.Load(iniPath);
        InitVkMap();

        if (probe)
        {
            Log("MxLite probe");
            try { DeviceCycle(); } catch (Exception ex) { Log("ERROR: " + ex.Message); }
            Console.WriteLine("Press Enter to close."); Console.ReadLine();
            return;
        }

        bool createdNew;
        Mutex mtx = new Mutex(true, "MxLiteSingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MxLite is already running.\n\nLook for its icon in the tray, next to the clock\n(you may need to click the ^ arrow). Double-click it for settings.",
                "MxLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        GC.KeepAlive(mtx);

        Log("MxLite starting");
        try
        {
            // Stay responsive under memory pressure: high CPU priority + pin the working
            // set in RAM (hard min 32 MB, soft max) so paging storms can't stall input.
            Process p = Process.GetCurrentProcess();
            p.PriorityClass = ProcessPriorityClass.High;
            const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x1, QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x8;
            if (Native.SetProcessWorkingSetSizeEx(p.Handle, (IntPtr)(32L * 1024 * 1024), (IntPtr)(96L * 1024 * 1024),
                QUOTA_LIMITS_HARDWS_MIN_ENABLE | QUOTA_LIMITS_HARDWS_MAX_DISABLE))
                Log("priority=High, working set pinned (32 MB hard min)");
            else
                Log("priority=High, working set pin FAILED (err " + Marshal.GetLastWin32Error() + ")");
        }
        catch (Exception ex) { Log("priority setup failed: " + ex.Message); }

        Thread dev = new Thread(DeviceThread);
        dev.IsBackground = true;
        dev.Priority = ThreadPriority.Highest;
        dev.Start();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext(openSettings));
    }

    static void DeviceThread()
    {
        while (true)
        {
            try { DeviceCycle(); }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
            Connected = false;
            Status = "searching for mouse...";
            Thread.Sleep(3000);
        }
    }

    // ---- HID++ plumbing -------------------------------------------------

    // Button/move events that arrive while we are mid-request (e.g. during the
    // periodic divert refresh) are queued here and replayed by the event loop,
    // so a press during that window is never lost.
    static readonly List<byte[]> pendingEvents = new List<byte[]>();

    static byte[] Request(HidppDevice d, byte featIdx, byte fn, byte[] p, int timeoutMs)
    {
        byte[] rep = new byte[20];
        rep[0] = 0x11; rep[1] = d.DevIdx; rep[2] = featIdx; rep[3] = (byte)((fn << 4) | SWID);
        if (p != null) Array.Copy(p, 0, rep, 4, Math.Min(p.Length, 16));
        if (!d.Write(rep)) return null;
        byte fnByte = (byte)((fn << 4) | SWID);
        byte[] buf = new byte[d.InLen];
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            int n = d.Read(buf, Math.Max(1, timeoutMs - (int)sw.ElapsedMilliseconds));
            if (n <= 0) return null;
            if (buf[0] != 0x11 && buf[0] != 0x10) continue;
            // buttons events (fn 0) are never dropped - losing a release leaves the
            // gesture state stuck "held" with stale motion; only cap the rawXY flood
            if (buf[0] == 0x11 && (buf[3] & 0x0F) == 0 && buf[2] != 0xFF && buf[2] != 0x8F
                && (buf[3] == 0x00 || pendingEvents.Count < 64))
            {
                byte[] copy = new byte[20];
                Array.Copy(buf, copy, Math.Min(20, n));
                pendingEvents.Add(copy);
                continue;
            }
            if (buf[2] == featIdx && buf[3] == fnByte)
            {
                byte[] outp = new byte[16];
                Array.Copy(buf, 4, outp, 0, Math.Min(16, n - 4));
                return outp;
            }
            if ((buf[2] == 0xFF || buf[2] == 0x8F) && buf[3] == featIdx && buf[4] == fnByte)
            {
                Log("HID++ error for feat=" + featIdx + " fn=" + fn + " code=" + buf[5]);
                return null;
            }
        }
        return null;
    }

    static int GetFeatureIndex(HidppDevice d, ushort featureId)
    {
        byte[] r = Request(d, 0x00, 0x0, new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) }, 1500);
        return r == null ? 0 : r[0];
    }

    static string ReadDeviceName(HidppDevice d)
    {
        int idx = GetFeatureIndex(d, 0x0005);
        if (idx == 0) return null;
        byte[] cnt = Request(d, (byte)idx, 0x0, null, 1500);
        if (cnt == null) return null;
        int len = cnt[0];
        StringBuilder sb = new StringBuilder();
        while (sb.Length < len && sb.Length < 64)
        {
            byte[] part = Request(d, (byte)idx, 0x1, new byte[] { (byte)sb.Length }, 1500);
            if (part == null) break;
            for (int i = 0; i < 16 && sb.Length < len; i++)
            {
                if (part[i] == 0) break;
                sb.Append((char)part[i]);
            }
            if (part[0] == 0) break;
        }
        string s = sb.ToString().Trim();
        return s.Length > 0 ? s : null;
    }

    // ---- discovery ------------------------------------------------------

    static List<string> EnumerateCandidatePaths()
    {
        List<string> paths = new List<string>();
        Guid g; Native.HidD_GetHidGuid(out g);
        IntPtr devs = Native.SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (devs == IntPtr.Zero || devs.ToInt64() == -1) return paths;
        try
        {
            for (int i = 0; ; i++)
            {
                Native.SP_DEVICE_INTERFACE_DATA ifd = new Native.SP_DEVICE_INTERFACE_DATA();
                ifd.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                if (!Native.SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref g, i, ref ifd)) break;
                int required;
                Native.SetupDiGetDeviceInterfaceDetail(devs, ref ifd, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (Native.SetupDiGetDeviceInterfaceDetail(devs, ref ifd, detail, required, out required, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(devs); }
        return paths;
    }

    static HidppDevice OpenIfHidpp(string path)
    {
        SafeFileHandle h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) return null;
        Native.HIDD_ATTRIBUTES attr = new Native.HIDD_ATTRIBUTES();
        attr.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
        if (!Native.HidD_GetAttributes(h, ref attr) || attr.VendorID != 0x046D) { h.Dispose(); return null; }
        IntPtr pp;
        if (!Native.HidD_GetPreparsedData(h, out pp)) { h.Dispose(); return null; }
        Native.HIDP_CAPS caps = new Native.HIDP_CAPS();
        Native.HidP_GetCaps(pp, ref caps);
        Native.HidD_FreePreparsedData(pp);
        if (caps.UsagePage < 0xFF00 || caps.OutputReportByteLength < 20 || caps.InputReportByteLength < 20)
        { h.Dispose(); return null; }
        HidppDevice d = new HidppDevice();
        d.Handle = h; d.Path = path; d.InLen = caps.InputReportByteLength; d.OutLen = caps.OutputReportByteLength;
        return d;
    }

    static HidppDevice FindMouse(out int idxReprog, out int idxSmart, out bool smartEnhanced)
    {
        idxReprog = 0; idxSmart = 0; smartEnhanced = false;
        byte[] devIdxCandidates = new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 };
        foreach (string path in EnumerateCandidatePaths())
        {
            HidppDevice d = OpenIfHidpp(path);
            if (d == null) continue;
            bool keep = false;
            foreach (byte idx in devIdxCandidates)
            {
                d.DevIdx = idx;
                byte[] pong = Request(d, 0x00, 0x1, new byte[] { 0, 0, 0xAA }, 1200);
                if (pong == null) continue;
                int ir = GetFeatureIndex(d, 0x1B04);
                if (ir == 0) continue;
                int ism = GetFeatureIndex(d, 0x2110);
                bool enh = false;
                if (ism == 0) { ism = GetFeatureIndex(d, 0x2111); enh = ism != 0; }
                if (ism == 0) continue;
                idxReprog = ir; idxSmart = ism; smartEnhanced = enh; keep = true;
                Log("Mouse found: devIdx=0x" + idx.ToString("X2") + " reprogIdx=" + ir + " smartIdx=" + ism +
                    (enh ? " (0x2111)" : " (0x2110)"));
                break;
            }
            if (keep) return d;
            d.Dispose();
        }
        return null;
    }

    // ---- device setup ---------------------------------------------------

    static int sideDivertState = -1; // last side-button divert we applied: -1 unknown, 0 off, 1 on

    static bool ApplyDivert(HidppDevice d, int idxReprog)
    {
        byte[] p = new byte[] { GESTURE_CID >> 8, GESTURE_CID & 0xFF, 0x33, 0x00, 0x00 };
        if (Request(d, (byte)idxReprog, 0x3, p, 2000) == null) return false;
        ApplySideDivert(d, idxReprog);
        return true;
    }

    // Thumb buttons: flags 0x03 = divert to MxLite, 0x02 = give back to Windows
    // (browser back/forward). A failure here never blocks the wheel button.
    static void ApplySideDivert(HidppDevice d, int idxReprog)
    {
        bool on = Cfg.SideButtons;
        byte flags = (byte)(on ? 0x03 : 0x02);
        bool okB = Request(d, (byte)idxReprog, 0x3, new byte[] { BACK_CID >> 8, BACK_CID & 0xFF, flags, 0x00, 0x00 }, 2000) != null;
        bool okF = Request(d, (byte)idxReprog, 0x3, new byte[] { FWD_CID >> 8, FWD_CID & 0xFF, flags, 0x00, 0x00 }, 2000) != null;
        int state = on ? 1 : 0;
        if (!okB || !okF)
        {
            Log("Side buttons: " + (on ? "divert" : "release") + " FAILED (back=" + okB + " forward=" + okF + ")");
            sideDivertState = -1;
        }
        else if (state != sideDivertState)
        {
            Log(on ? "Side buttons active: back -> " + Cfg.Back + ", forward -> " + Cfg.Forward
                   : "Side buttons left as browser back/forward");
            sideDivertState = state;
        }
        if (!on) { sideBackDown = false; sideFwdDown = false; }
    }

    static void ApplySmartShift(HidppDevice d, int idxSmart, bool enhanced)
    {
        Config c = Cfg;
        byte readFn = (byte)(enhanced ? 0x1 : 0x0);
        byte writeFn = (byte)(enhanced ? 0x2 : 0x1);
        byte[] cur = Request(d, (byte)idxSmart, readFn, null, 2000);
        if (cur != null)
        {
            WheelModeOnMouse = cur[0]; WheelThreshOnMouse = cur[1];
            if (origThresh < 0) origThresh = cur[1];
            Log("SmartShift on mouse: mode=" + cur[0] + " threshold=" + cur[1]);
        }
        if (c.SmartShift == "auto")
        {
            // user went back to "leave it alone": restore what the mouse had before we touched it
            if (wroteCustomShift && origThresh >= 0)
            {
                if (Request(d, (byte)idxSmart, writeFn, new byte[] { 2, (byte)origThresh }, 2000) != null)
                { Log("SmartShift restored to original " + origThresh); wroteCustomShift = false; WheelThreshOnMouse = origThresh; }
            }
            return;
        }
        int t;
        if (!int.TryParse(c.SmartShift, out t)) { Log("Bad smartshift value: " + c.SmartShift); return; }
        if (t < 1) t = 1; if (t > 254) t = 254;
        // mode 2 = ratchet/SmartShift, 1 = permanent free-spin. Check both: a mouse
        // left in free-spin at the same threshold would otherwise look "already set"
        // and never get its notches back.
        if (WheelThreshOnMouse == t && WheelModeOnMouse == 2) return;
        if (Request(d, (byte)idxSmart, writeFn, new byte[] { 2, (byte)t }, 2000) != null)
        { Log("SmartShift threshold set to " + t); wroteCustomShift = true; WheelThreshOnMouse = t; }
        else
            Log("SmartShift write FAILED");
    }

    // ---- key sending ----------------------------------------------------

    static Dictionary<string, ushort> vkMap;
    static void InitVkMap()
    {
        vkMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            {"ctrl",0x11},{"control",0x11},{"shift",0x10},{"alt",0x12},{"win",0x5B},
            {"tab",0x09},{"enter",0x0D},{"esc",0x1B},{"escape",0x1B},{"space",0x20},
            {"left",0x25},{"up",0x26},{"right",0x27},{"down",0x28},
            {"pgup",0x21},{"pgdn",0x22},{"end",0x23},{"home",0x24},{"ins",0x2D},{"del",0x2E},
            {"playpause",0xB3},{"nexttrack",0xB0},{"prevtrack",0xB1},
            {"volup",0xAF},{"voldown",0xAE},{"mute",0xAD},
            {"browserback",0xA6},{"browserfwd",0xA7},{"prtsc",0x2C},
        };
        for (char ch = 'a'; ch <= 'z'; ch++) vkMap[ch.ToString()] = (ushort)char.ToUpperInvariant(ch);
        for (char ch = '0'; ch <= '9'; ch++) vkMap[ch.ToString()] = (ushort)ch;
        for (int f = 1; f <= 24; f++) vkMap["f" + f] = (ushort)(0x6F + f);
    }

    static bool IsExtended(ushort vk)
    {
        return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C
            || (vk >= 0xA6 && vk <= 0xB3) || vk == 0x2C;
    }

    public static bool ValidCombo(string combo, out string badToken)
    {
        badToken = null;
        if (combo == null || combo.Trim().Length == 0) return true;
        foreach (string tok in combo.Split('+'))
        {
            string t = tok.Trim();
            if (t.Length == 0) continue;
            ushort vk;
            if (!vkMap.TryGetValue(t, out vk)) { badToken = t; return false; }
        }
        return true;
    }

    static void SendCombo(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return;
        List<ushort> vks = new List<ushort>();
        foreach (string tok in combo.Split('+'))
        {
            string t = tok.Trim();
            if (t.Length == 0) continue;
            ushort vk;
            if (!vkMap.TryGetValue(t, out vk)) { Log("Unknown key in combo '" + combo + "': " + t); return; }
            vks.Add(vk);
        }
        if (vks.Count == 0) return;
        Native.INPUT[] inputs = new Native.INPUT[vks.Count * 2];
        for (int i = 0; i < vks.Count; i++)
        {
            inputs[i].type = 1;
            inputs[i].ki.wVk = vks[i];
            inputs[i].ki.dwFlags = IsExtended(vks[i]) ? 1u : 0u;
        }
        for (int i = 0; i < vks.Count; i++)
        {
            int j = vks.Count + i;
            ushort vk = vks[vks.Count - 1 - i];
            inputs[j].type = 1;
            inputs[j].ki.wVk = vk;
            inputs[j].ki.dwFlags = 2u | (IsExtended(vk) ? 1u : 0u);
        }
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        if (sent != (uint)inputs.Length)
            Log("SendInput FAILED for '" + combo + "': sent=" + sent + " err=" + Marshal.GetLastWin32Error());
        else
            Log("Sent: " + combo);
    }

    // ---- main device loop -------------------------------------------------

    static void DeviceCycle()
    {
        int idxReprog, idxSmart; bool enh;
        HidppDevice d = FindMouse(out idxReprog, out idxSmart, out enh);
        if (d == null) { if (debug) Log("No MX mouse found, retrying..."); return; }
        using (d)
        {
            string name = ReadDeviceName(d);
            DeviceName = name != null ? name : "Logitech mouse";
            ApplySmartShift(d, idxSmart, enh);
            if (probe) { Log("Probe: skipping button divert."); return; }
            sideDivertState = -1; sideBackDown = false; sideFwdDown = false;
            if (!ApplyDivert(d, idxReprog)) { Log("Divert failed."); return; }
            Log("Gesture button active on " + DeviceName);
            Connected = true;
            Status = DeviceName + " - connected";

            gHeld = false; gFired = false; gGestureDone = false;
            gAccX = 0; gAccY = 0; gTotalMove = 0;
            pendingEvents.Clear();
            byte[] buf = new byte[d.InLen];
            byte reprogB = (byte)idxReprog;
            long lastApply = Environment.TickCount;
            int heldIdle = 0; // consecutive 2s read timeouts while gHeld

            while (true)
            {
                // replay events that arrived while a request was in flight
                while (pendingEvents.Count > 0)
                {
                    byte[] ev = pendingEvents[0];
                    pendingEvents.RemoveAt(0);
                    HandleEvent(ev, reprogB);
                }

                int n = d.Read(buf, 2000);
                if (n < 0) { Log("Device lost, reconnecting..."); return; }

                if (n > 0) heldIdle = 0;
                else if (gHeld && ++heldIdle >= 5)
                { gHeld = false; Log("held but silent 10s - assuming missed release"); }
                if (gHeld) { if (n > 0) HandleEvent(buf, reprogB); continue; } // no HID++ chatter mid-gesture
                if (CfgDirty)
                {
                    CfgDirty = false;
                    ApplySmartShift(d, idxSmart, enh);
                    ApplySideDivert(d, idxReprog);
                }
                if (Environment.TickCount - lastApply > 60000)
                {
                    lastApply = Environment.TickCount;
                    if (!ApplyDivert(d, idxReprog)) { Log("Reapply failed, reconnecting..."); return; }
                }
                if (n == 0) continue;
                HandleEvent(buf, reprogB);
            }
        }
    }

    // gesture state (device thread only)
    static bool gHeld, gFired, gGestureDone;
    static int gAccX, gAccY, gTotalMove;
    static int gDir; // 0 = undecided, 1 = right, -1 = left, 2 = down, -2 = up
    static int gEvt; // rawXY events seen this hold
    static int gJumps; // desktops switched mid-hold this hold
    static int gPeak;  // furthest progress along gDir since the last jump
    static int gPressTick; // Environment.TickCount at the press
    static bool sideBackDown, sideFwdDown; // thumb buttons currently held (device thread only)

    static string DirAction(Config c, int dir)
    {
        return dir == 1 ? c.Right : dir == -1 ? c.Left : dir == 2 ? c.Down : dir == -2 ? c.Up : "";
    }

    static void HandleEvent(byte[] buf, byte reprogB)
    {
        if (buf[0] != 0x11 || buf[2] != reprogB) return;
        Config c = Cfg;
        if (buf[3] == 0x00) // divertedButtonsEvent
        {
            // the event lists every diverted button held right now (up to 4)
            bool pressed = false, back = false, fwd = false;
            for (int i = 0; i < 4; i++)
            {
                ushort cid = (ushort)((buf[4 + 2 * i] << 8) | buf[5 + 2 * i]);
                if (cid == GESTURE_CID) pressed = true;
                else if (cid == BACK_CID) back = true;
                else if (cid == FWD_CID) fwd = true;
            }
            // thumb buttons fire on the press edge (instant), nothing on release
            bool sideChanged = back != sideBackDown || fwd != sideFwdDown;
            if (back && !sideBackDown && c.SideButtons) SendCombo(c.Back);
            if (fwd && !sideFwdDown && c.SideButtons) SendCombo(c.Forward);
            sideBackDown = back; sideFwdDown = fwd;

            if (pressed)
            {
                // a thumb button changed while the wheel button stays held: not a new press
                if (gHeld && sideChanged) return;
                // reset on EVERY press: if gHeld is still true here we missed a
                // release event, and carrying the old hold's motion into this press
                // turns a plain click into a phantom swipe
                if (gHeld) Log("press while already held - missed a release, resetting state");
                gHeld = true; gFired = false; gGestureDone = false;
                gAccX = 0; gAccY = 0; gTotalMove = 0; gDir = 0; gEvt = 0; gJumps = 0; gPeak = 0;
                gPressTick = Environment.TickCount;
            }
            else if (!pressed && gHeld)
            {
                gHeld = false;
                string decided = gFired ? "fired earlier" : "none";
                if (!gFired)
                {
                    // little total movement = click, no matter which way it wiggled
                    if (gTotalMove <= c.ClickMaxMove) { decided = "click"; SendCombo(c.Click); }
                    else
                    {
                        // quick flick: judge by NET movement over the whole hold, so a
                        // stray jerk while pressing can't brand the gesture wrong
                        int flick = Math.Max(80, c.GestureDistance / 3);
                        int ax = Math.Abs(gAccX), ay = Math.Abs(gAccY);
                        string h = gAccX > 0 ? c.Right : c.Left, v = gAccY > 0 ? c.Down : c.Up;
                        string action = null;
                        if (ax >= flick && ax >= ay) action = h;
                        else if (ay >= flick && ay > ax) action = v;
                        // the winning axis may have nothing bound (diagonal drift onto
                        // up/down): fall back to the other axis rather than eat the swipe
                        if (string.IsNullOrEmpty(action))
                        {
                            if (ax >= flick && !string.IsNullOrEmpty(h)) action = h;
                            else if (ay >= flick && !string.IsNullOrEmpty(v)) action = v;
                        }
                        if (!string.IsNullOrEmpty(action)) { decided = action; SendCombo(action); }
                    }
                }
                Log("release: totalMove=" + gTotalMove + " accX=" + gAccX + " accY=" + gAccY + " dir=" + gDir + " fired=" + gFired + " jumps=" + gJumps + " -> " + decided);
            }
        }
        else if (buf[3] == 0x10 && gHeld) // divertedRawXYEvent
        {
            int dx = (short)((buf[4] << 8) | buf[5]);
            int dy = (short)((buf[6] << 8) | buf[7]);
            // first report(s) of a hold can carry motion the sensor accumulated while
            // idle: a giant stale delta that latches (and can even fire) the wrong
            // direction before the real swipe registers. Real deltas this early in a
            // hold are tiny - the hand is only starting to move.
            gEvt++;
            if (gEvt <= 2 && Math.Abs(dx) + Math.Abs(dy) > 150)
            { Log("ignored stale wake delta: dx=" + dx + " dy=" + dy); return; }
            gAccX += dx; gAccY += dy;
            gTotalMove += Math.Abs(dx) + Math.Abs(dy);
            if (gDir == 0)
            {
                // latch a direction at 40% of the full swipe distance: MID-HOLD fires
                // only go this way (blocks return-movement misfires; a wrong latch just
                // defers the gesture to the net-based decision at release)
                int flick = Math.Max(80, c.GestureDistance * 2 / 5);
                int ax = Math.Abs(gAccX), ay = Math.Abs(gAccY);
                if (ax >= flick && ax >= ay) gDir = gAccX > 0 ? 1 : -1;
                else if (ay >= flick && ay > ax) gDir = gAccY > 0 ? 2 : -2;
                // an axis with no action bound must not steal the gesture
                if (gDir != 0 && string.IsNullOrEmpty(DirAction(c, gDir))) gDir = 0;
            }
            if (gGestureDone || gDir == 0) return;
            // quick sweep: after the first jump, movement before multi_hold_ms is thrown
            // away, so a fast flick moves one desktop and nothing piles up to fire the
            // moment the wait ends
            if (gJumps > 0 && Environment.TickCount - gPressTick < c.MultiHoldMs)
            { gAccX = 0; gAccY = 0; gPeak = 0; return; }
            // progress = movement along the latched direction since the last jump
            int progress = gDir == 1 ? gAccX : gDir == -1 ? -gAccX : gDir == 2 ? gAccY : -gAccY;
            if (progress > gPeak) gPeak = progress;
            // turn back (repeat on): moving a full swipe length back from the furthest
            // point reverses the direction and jumps that way. Measured from the peak,
            // so overshoot before turning does not have to be undone first.
            if (c.Repeat && gPeak - progress >= c.GestureDistance && !string.IsNullOrEmpty(DirAction(c, -gDir)))
            {
                gDir = -gDir;
                gFired = true;
                gJumps++;
                gAccX = 0; gAccY = 0; gPeak = 0;
                SendCombo(DirAction(c, gDir));
                return;
            }
            // first jump needs the swipe length; each further jump (repeat on) needs extra_distance more
            int need = gJumps == 0 ? c.GestureDistance : c.ExtraDistance;
            if (progress >= need)
            {
                gFired = true;
                gJumps++;
                gAccX = 0; gAccY = 0; gPeak = 0;
                SendCombo(DirAction(c, gDir));
                if (!c.Repeat) gGestureDone = true;
            }
        }
    }

    public static string IniPath { get { return iniPath; } }
    public static string LogFilePath { get { return logPath; } }

    // ---- start with Windows ----------------------------------------------

    static string StartupLnk
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MxLite.lnk"); }
    }

    public static bool StartupEnabled() { return File.Exists(StartupLnk); }

    public static void SetStartup(bool on)
    {
        try
        {
            if (!on) { if (File.Exists(StartupLnk)) File.Delete(StartupLnk); return; }
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(StartupLnk);
            sc.TargetPath = Application.ExecutablePath;
            sc.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            sc.Description = "MxLite - lightweight Logitech mouse driver";
            sc.Save();
        }
        catch (Exception ex) { Log("Startup shortcut failed: " + ex.Message); }
    }
}

// ---- tray -----------------------------------------------------------------

class TrayContext : ApplicationContext
{
    NotifyIcon icon;
    SettingsForm form;
    System.Windows.Forms.Timer tipTimer;

    public static Icon AppIcon;

    public TrayContext(bool openSettings)
    {
        AppIcon = MakeIcon();
        icon = new NotifyIcon();
        icon.Icon = AppIcon;
        icon.Text = "MxLite";
        ContextMenu menu = new ContextMenu();
        menu.MenuItems.Add("Settings...", delegate { ShowSettings(); });
        menu.MenuItems.Add("Open log", delegate { try { Process.Start("notepad.exe", MxLite.LogFilePath); } catch { } });
        menu.MenuItems.Add("-");
        menu.MenuItems.Add("Exit MxLite", delegate { icon.Visible = false; Environment.Exit(0); });
        icon.ContextMenu = menu;
        icon.DoubleClick += delegate { ShowSettings(); };
        icon.Visible = true;

        tipTimer = new System.Windows.Forms.Timer();
        tipTimer.Interval = 2000;
        tipTimer.Tick += delegate
        {
            string t = "MxLite - " + (MxLite.Connected ? MxLite.Status : "searching for mouse...");
            if (t.Length > 63) t = t.Substring(0, 63);
            icon.Text = t;
        };
        tipTimer.Start();

        if (openSettings) ShowSettings();
    }

    void ShowSettings()
    {
        if (form != null && !form.IsDisposed) { form.Activate(); return; }
        form = new SettingsForm();
        form.Show();
    }

    static Icon MakeIcon()
    {
        Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (GraphicsPath body = new GraphicsPath())
            {
                body.AddEllipse(7, 3, 18, 26);
                using (Brush b = new SolidBrush(Color.White)) g.FillPath(b, body);
            }
            using (Brush w = new SolidBrush(Color.FromArgb(0, 120, 215)))
                g.FillRectangle(w, 14, 7, 4, 9);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

// ---- settings window --------------------------------------------------------

class SettingsForm : Form
{
    static readonly string[,] PRESETS = {
        {"Do nothing",""},
        {"Task View (all windows)","Win+Tab"},
        {"Next desktop","Ctrl+Win+Right"},
        {"Previous desktop","Ctrl+Win+Left"},
        {"Show desktop","Win+D"},
        {"Switch app (Alt+Tab)","Alt+Tab"},
        {"Maximize window","Win+Up"},
        {"Minimize window","Win+Down"},
        {"Snap window left","Win+Left"},
        {"Snap window right","Win+Right"},
        {"Play / Pause","PlayPause"},
        {"Next track","NextTrack"},
        {"Previous track","PrevTrack"},
        {"Volume up","VolUp"},
        {"Volume down","VolDown"},
        {"Mute","Mute"},
        {"Browser back","BrowserBack"},
        {"Browser forward","BrowserFwd"},
    };
    const string CUSTOM = "Custom keys...";

    static readonly Color ACCENT = Color.FromArgb(0, 103, 192);
    static readonly Color GOOD = Color.FromArgb(35, 134, 54);
    static readonly Color MUTED = Color.FromArgb(110, 110, 110);
    static readonly Color LINE = Color.FromArgb(229, 229, 229);
    static readonly Color CARDBG = Color.FromArgb(246, 248, 250);
    static readonly Color BADBG = Color.FromArgb(255, 235, 235);

    const int ROWS = 7; // 0 click, 1-4 hold+move left/right/up/down, 5 back, 6 forward
    ComboBox[] combos = new ComboBox[ROWS];
    TextBox[] customs = new TextBox[ROWS];
    Label[] rowLabels = new Label[ROWS];
    Panel card;
    Label statusLabel, hint;
    TrackBar tbDist, tbShift, tbExtra, tbHold;
    Label distValue, extraValue, extraLabel, holdValue, holdLabel;
    RadioButton rbAuto, rbCustom;
    CheckBox cbRepeat, cbStartup, cbSide;
    Label savedLabel;
    System.Windows.Forms.Timer statusTimer, applyTimer, savedTimer;
    bool loading = true;
    bool dotOk;

    public SettingsForm()
    {
        Text = "MxLite";
        if (TrayContext.AppIcon != null) Icon = TrayContext.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(500, 1028);

        int L = 24, W = 452;
        int y = 16;

        // status card with painted dot
        card = new Panel();
        card.SetBounds(L, y, W, 44);
        card.BackColor = CARDBG;
        card.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen p = new Pen(LINE)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush b = new SolidBrush(dotOk ? GOOD : Color.FromArgb(180, 180, 180)))
                e.Graphics.FillEllipse(b, 14, 17, 10, 10);
        };
        statusLabel = new Label(); statusLabel.AutoSize = true; statusLabel.BackColor = CARDBG;
        statusLabel.Location = new Point(34, 13);
        card.Controls.Add(statusLabel);
        Controls.Add(card);
        y += 64;

        y = Section("BUTTON UNDER THE SCROLL WHEEL", L, W, y);
        string[] rowNames = { "Click", "Hold + move left", "Hold + move right", "Hold + move up", "Hold + move down" };
        for (int i = 0; i < 5; i++) y = AddRow(i, rowNames[i], L, y);
        hint = new Label();
        hint.Text = "Custom keys example: Ctrl+Shift+P   (keys: Ctrl Shift Alt Win, A-Z, F1-F24, arrows...)";
        hint.ForeColor = MUTED; hint.Font = new Font("Segoe UI", 8.25f);
        hint.SetBounds(L, y, W, 18);
        hint.Visible = false;
        Controls.Add(hint);
        y += 26;

        y = Section("GESTURE FEEL", L, W, y);
        Label dl = new Label(); dl.Text = "Swipe length"; dl.SetBounds(L, y, 138, 26);
        dl.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(dl);
        tbDist = new TrackBar();
        tbDist.Minimum = 100; tbDist.Maximum = 800; tbDist.TickFrequency = 100; tbDist.SmallChange = 50; tbDist.LargeChange = 100;
        tbDist.SetBounds(L + 140, y - 2, 226, 40);
        Controls.Add(tbDist);
        distValue = new Label(); distValue.SetBounds(L + 372, y + 3, 80, 20); distValue.ForeColor = MUTED;
        distValue.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(distValue);
        tbDist.ValueChanged += delegate
        {
            distValue.Text = ShortLabel(tbDist.Value);
            ScheduleApply();
        };
        y += 44;
        Label dh = new Label(); dh.Text = "How far you move the mouse (while holding the button) to switch one desktop.";
        dh.ForeColor = MUTED; dh.Font = new Font("Segoe UI", 8.25f);
        dh.SetBounds(L, y, W, 18); Controls.Add(dh);
        y += 24;
        cbRepeat = new CheckBox();
        cbRepeat.Text = "Move further to jump 2 or more desktops in one hold";
        cbRepeat.SetBounds(L, y, W, 24); Controls.Add(cbRepeat);
        cbRepeat.CheckedChanged += delegate { UpdateEnabled(); ScheduleApply(); };
        y += 30;
        extraLabel = new Label(); extraLabel.Text = "Each extra desktop"; extraLabel.SetBounds(L, y, 138, 26);
        extraLabel.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(extraLabel);
        tbExtra = new TrackBar();
        tbExtra.Minimum = 150; tbExtra.Maximum = 1000; tbExtra.TickFrequency = 100; tbExtra.SmallChange = 50; tbExtra.LargeChange = 100;
        tbExtra.SetBounds(L + 140, y - 2, 226, 40);
        Controls.Add(tbExtra);
        extraValue = new Label(); extraValue.SetBounds(L + 372, y + 3, 80, 20); extraValue.ForeColor = MUTED;
        extraValue.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(extraValue);
        tbExtra.ValueChanged += delegate { extraValue.Text = ShortLabel(tbExtra.Value); ScheduleApply(); };
        y += 44;
        Label eh = new Label(); eh.Text = "How much further you move after the first jump for each next desktop.";
        eh.ForeColor = MUTED; eh.Font = new Font("Segoe UI", 8.25f);
        eh.SetBounds(L, y, W, 18); Controls.Add(eh);
        y += 30;
        holdLabel = new Label(); holdLabel.Text = "Hold before 2nd jump"; holdLabel.SetBounds(L, y, 138, 26);
        holdLabel.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(holdLabel);
        tbHold = new TrackBar();
        tbHold.Minimum = 0; tbHold.Maximum = 1000; tbHold.TickFrequency = 100; tbHold.SmallChange = 50; tbHold.LargeChange = 100;
        tbHold.SetBounds(L + 140, y - 2, 226, 40);
        Controls.Add(tbHold);
        holdValue = new Label(); holdValue.SetBounds(L + 372, y + 3, 80, 20); holdValue.ForeColor = MUTED;
        holdValue.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(holdValue);
        tbHold.ValueChanged += delegate { holdValue.Text = tbHold.Value + " ms"; ScheduleApply(); };
        y += 44;
        Label hh = new Label(); hh.Text = "A quick sweep moves 1 desktop. Hold at least this long to jump more. 0 = no wait.";
        hh.ForeColor = MUTED; hh.Font = new Font("Segoe UI", 8.25f);
        hh.SetBounds(L, y, W, 18); Controls.Add(hh);
        y += 30;

        y = Section("SIDE BUTTONS (THUMB)", L, W, y);
        cbSide = new CheckBox();
        cbSide.Text = "Use the side buttons for these keys (unticked = browser back/forward)";
        cbSide.SetBounds(L, y, W, 24); Controls.Add(cbSide);
        cbSide.CheckedChanged += delegate { UpdateEnabled(); ScheduleApply(); };
        y += 32;
        y = AddRow(5, "Back (rear)", L, y);
        y = AddRow(6, "Forward (front)", L, y);
        y += 6;

        y = Section("SCROLL WHEEL FREE-SPIN", L, W, y);
        rbAuto = new RadioButton(); rbAuto.Text = "Use the mouse's own setting (recommended)";
        rbAuto.SetBounds(L, y, 340, 24); Controls.Add(rbAuto);
        y += 28;
        rbCustom = new RadioButton(); rbCustom.Text = "Custom:";
        rbCustom.SetBounds(L, y + 4, 110, 24); Controls.Add(rbCustom);
        tbShift = new TrackBar();
        tbShift.Minimum = 1; tbShift.Maximum = 50; tbShift.TickFrequency = 5;
        tbShift.SetBounds(L + 140, y - 2, 226, 40);   // same column as the swipe slider
        Controls.Add(tbShift);
        Label sh1 = new Label(); sh1.Text = "spins free easily"; sh1.ForeColor = MUTED; sh1.Font = new Font("Segoe UI", 7.5f);
        sh1.SetBounds(L + 144, y + 40, 110, 16); Controls.Add(sh1);   // below the track, no overlap
        Label sh2 = new Label(); sh2.Text = "needs a hard flick"; sh2.ForeColor = MUTED; sh2.Font = new Font("Segoe UI", 7.5f);
        sh2.TextAlign = ContentAlignment.TopRight;
        sh2.SetBounds(L + 252, y + 40, 110, 16); Controls.Add(sh2);
        y += 60;
        Label sh3 = new Label();
        sh3.Text = "Wheel lost its click-click? Choose Custom - it also ends permanent free-spin.";
        sh3.ForeColor = MUTED; sh3.Font = new Font("Segoe UI", 7.5f);
        sh3.SetBounds(L, y, W, 16); Controls.Add(sh3);
        y += 20;
        EventHandler wheelChanged = delegate { tbShift.Enabled = rbCustom.Checked; UpdateCustomText(); ScheduleApply(); };
        rbAuto.CheckedChanged += wheelChanged;
        rbCustom.CheckedChanged += wheelChanged;
        tbShift.ValueChanged += delegate { UpdateCustomText(); ScheduleApply(); };

        Panel sep = new Panel(); sep.BackColor = LINE; sep.SetBounds(L, y, W, 1); Controls.Add(sep);
        y += 14;
        cbStartup = new CheckBox(); cbStartup.Text = "Start MxLite with Windows";
        cbStartup.SetBounds(L, y + 2, 220, 24); Controls.Add(cbStartup);
        cbStartup.CheckedChanged += delegate { if (!loading) MxLite.SetStartup(cbStartup.Checked); };
        savedLabel = new Label(); savedLabel.Text = ""; savedLabel.ForeColor = GOOD;
        savedLabel.TextAlign = ContentAlignment.MiddleRight;
        savedLabel.SetBounds(L + 226, y + 6, 130, 20); Controls.Add(savedLabel);
        y += 30;
        Label adminNote = new Label();
        adminNote.Text =
            "If you use the admin start below, leave this checkbox unticked.\r\n" +
            "It's optional - most people never need it: only if desktop\r\n" +
            "switching silently stops while an \"as administrator\" app has\r\n" +
            "focus (a Windows security rule) and that annoys you. Then run\r\n" +
            "install-elevated.cmd in MxLite's folder: MxLite starts as admin\r\n" +
            "at logon via Task Scheduler (one UAC click, none ever at boot).\r\n" +
            "Undo: uninstall-elevated.cmd.";
        adminNote.ForeColor = MUTED; adminNote.Font = new Font("Segoe UI", 8.25f);
        adminNote.SetBounds(L, y, W, 92); Controls.Add(adminNote);
        y += 98;
        Button btnClose = new Button(); btnClose.Text = "Close";
        btnClose.SetBounds(L + 364, y, 88, 30);
        btnClose.Click += delegate { Close(); };
        Controls.Add(btnClose);

        LoadFromConfig();
        UpdateHint();
        UpdateCustomText();
        loading = false;

        statusTimer = new System.Windows.Forms.Timer();
        statusTimer.Interval = 1000;
        statusTimer.Tick += delegate { RefreshStatus(); };
        statusTimer.Start();
        RefreshStatus();

        applyTimer = new System.Windows.Forms.Timer();
        applyTimer.Interval = 450;
        applyTimer.Tick += delegate { applyTimer.Stop(); ApplyNow(); };

        savedTimer = new System.Windows.Forms.Timer();
        savedTimer.Interval = 2000;
        savedTimer.Tick += delegate { savedTimer.Stop(); savedLabel.Text = ""; };
    }

    // one "label + preset dropdown + custom keys box" row; returns the next y
    int AddRow(int i, string name, int L, int y)
    {
        Label l = new Label(); l.Text = name; l.AutoSize = false;
        l.SetBounds(L, y, 138, 26); l.TextAlign = ContentAlignment.MiddleLeft;
        l.ForeColor = Color.FromArgb(40, 40, 40);
        Controls.Add(l);
        ComboBox cb = new ComboBox();
        cb.DropDownStyle = ComboBoxStyle.DropDownList;
        cb.FlatStyle = FlatStyle.System;
        cb.SetBounds(L + 140, y, 200, 24);
        for (int p = 0; p < PRESETS.GetLength(0); p++) cb.Items.Add(PRESETS[p, 0]);
        cb.Items.Add(CUSTOM);
        Controls.Add(cb);
        TextBox tb = new TextBox();
        tb.SetBounds(L + 348, y, 104, 24);
        tb.Visible = false;
        Controls.Add(tb);
        combos[i] = cb; customs[i] = tb; rowLabels[i] = l;
        cb.SelectedIndexChanged += delegate
        {
            customs[i].Visible = (string)combos[i].SelectedItem == CUSTOM;
            UpdateHint();
            ScheduleApply();
        };
        tb.TextChanged += delegate
        {
            string bad;
            tb.BackColor = MxLite.ValidCombo(tb.Text, out bad) ? Color.White : BADBG;
            ScheduleApply();
        };
        return y + 34;
    }

    void UpdateEnabled()
    {
        tbExtra.Enabled = cbRepeat.Checked;
        extraLabel.Enabled = cbRepeat.Checked;
        tbHold.Enabled = cbRepeat.Checked;
        holdLabel.Enabled = cbRepeat.Checked;
        for (int i = 5; i < ROWS; i++)
        {
            combos[i].Enabled = cbSide.Checked; customs[i].Enabled = cbSide.Checked;
            rowLabels[i].Enabled = cbSide.Checked;
        }
    }

    int Section(string title, int L, int W, int y)
    {
        Label h = new Label(); h.Text = title;
        h.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        h.ForeColor = MUTED;
        h.AutoSize = true;
        h.Location = new Point(L, y);
        Controls.Add(h);
        Panel line = new Panel(); line.BackColor = LINE;
        int lw = TextRenderer.MeasureText(title, h.Font).Width;
        line.SetBounds(L + lw + 12, y + 6, W - lw - 12, 1);
        Controls.Add(line);
        return y + 26;
    }

    static string ShortLabel(int v)
    {
        if (v <= 250) return v + " short";
        if (v <= 450) return v + " medium";
        return v + " long";
    }

    void UpdateHint()
    {
        bool anyCustom = false;
        for (int i = 0; i < ROWS; i++) if (customs[i].Visible) anyCustom = true;
        hint.Visible = anyCustom;
    }

    void UpdateCustomText()
    {
        rbCustom.Text = rbCustom.Checked ? "Custom (" + tbShift.Value + "):" : "Custom:";
    }

    void RefreshStatus()
    {
        bool ok = MxLite.Connected;
        if (ok != dotOk) { dotOk = ok; card.Invalidate(); }
        statusLabel.Text = ok ? MxLite.Status : "Searching for the mouse... (is it switched on?)";
        statusLabel.ForeColor = ok ? Color.FromArgb(40, 40, 40) : MUTED;
    }

    void SetCombo(int i, string combo)
    {
        for (int p = 0; p < PRESETS.GetLength(0); p++)
        {
            if (string.Equals(PRESETS[p, 1], combo, StringComparison.OrdinalIgnoreCase))
            { combos[i].SelectedIndex = p; customs[i].Visible = false; return; }
        }
        combos[i].SelectedItem = CUSTOM;
        customs[i].Text = combo;
        customs[i].Visible = true;
    }

    string GetCombo(int i)
    {
        string sel = (string)combos[i].SelectedItem;
        if (sel == CUSTOM) return customs[i].Text.Trim();
        for (int p = 0; p < PRESETS.GetLength(0); p++)
            if (PRESETS[p, 0] == sel) return PRESETS[p, 1];
        return "";
    }

    void LoadFromConfig()
    {
        Config c = MxLite.Cfg;
        SetCombo(0, c.Click); SetCombo(1, c.Left); SetCombo(2, c.Right); SetCombo(3, c.Up); SetCombo(4, c.Down);
        tbDist.Value = Math.Min(tbDist.Maximum, Math.Max(tbDist.Minimum, c.GestureDistance));
        distValue.Text = ShortLabel(tbDist.Value);
        cbRepeat.Checked = c.Repeat;
        tbExtra.Value = Math.Min(tbExtra.Maximum, Math.Max(tbExtra.Minimum, c.ExtraDistance));
        extraValue.Text = ShortLabel(tbExtra.Value);
        tbHold.Value = Math.Min(tbHold.Maximum, Math.Max(tbHold.Minimum, c.MultiHoldMs));
        holdValue.Text = tbHold.Value + " ms";
        cbSide.Checked = c.SideButtons;
        SetCombo(5, c.Back); SetCombo(6, c.Forward);
        UpdateEnabled();
        int t;
        bool auto = c.SmartShift == "auto" || !int.TryParse(c.SmartShift, out t);
        rbAuto.Checked = auto; rbCustom.Checked = !auto;
        tbShift.Enabled = !auto;
        if (!auto) { int v; int.TryParse(c.SmartShift, out v); tbShift.Value = Math.Min(50, Math.Max(1, v)); }
        else tbShift.Value = 25;
        cbStartup.Checked = MxLite.StartupEnabled();
    }

    void ScheduleApply()
    {
        if (loading || applyTimer == null) return;
        applyTimer.Stop();
        applyTimer.Start();
    }

    void ApplyNow()
    {
        for (int i = 0; i < ROWS; i++)
        {
            string bad;
            if (!MxLite.ValidCombo(GetCombo(i), out bad))
            { savedLabel.ForeColor = Color.Firebrick; savedLabel.Text = "unknown key: " + bad; savedTimer.Stop(); return; }
        }
        Config c = new Config();
        c.Click = GetCombo(0); c.Left = GetCombo(1); c.Right = GetCombo(2); c.Up = GetCombo(3); c.Down = GetCombo(4);
        c.GestureDistance = tbDist.Value;
        c.ClickMaxMove = Math.Max(150, Math.Min(400, tbDist.Value * 7 / 10));
        c.Repeat = cbRepeat.Checked;
        c.ExtraDistance = tbExtra.Value;
        c.MultiHoldMs = tbHold.Value;
        c.SideButtons = cbSide.Checked;
        c.Back = GetCombo(5); c.Forward = GetCombo(6);
        c.SmartShift = rbAuto.Checked ? "auto" : tbShift.Value.ToString();
        try { c.Save(MxLite.IniPath); }
        catch (Exception ex) { savedLabel.ForeColor = Color.Firebrick; savedLabel.Text = "save failed: " + ex.Message; return; }
        MxLite.Cfg = c;
        MxLite.CfgDirty = true;
        savedLabel.ForeColor = GOOD;
        savedLabel.Text = "✓ applied";
        savedTimer.Stop(); savedTimer.Start();
    }
}
