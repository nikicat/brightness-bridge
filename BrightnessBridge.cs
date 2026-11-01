// BrightnessBridge: forwards the Windows brightness setting (slider / brightness keys)
// to external monitors over DDC/CI. Event-driven via RegisterPowerSettingNotification.
// Run with --log to write BrightnessBridge.log next to the exe.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program {
    static readonly Guid SUB_VIDEO = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
    static readonly Guid VIDEONORMALLEVEL = new Guid("aded5e82-b909-4619-9949-f5d71dac0bcb");

    static int target = -1;
    static readonly AutoResetEvent pending = new AutoResetEvent(false);
    static string logPath;

    [STAThread]
    static void Main(string[] args) {
        bool created;
        using (var mutex = new Mutex(true, @"Local\BrightnessBridge", out created)) {
            if (!created) return;
            if (Array.IndexOf(args, "--log") >= 0)
                logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrightnessBridge.log");

            var worker = new Thread(ApplyLoop) { IsBackground = true };
            worker.Start();

            int initial = ReadWindowsBrightness();
            Log("start, windows brightness = " + initial);
            if (initial >= 0) Request(initial);

            var window = new PowerWindow();
            Application.Run();
            GC.KeepAlive(window);
        }
    }

    static void Request(int value) {
        Interlocked.Exchange(ref target, value);
        pending.Set();
    }

    // Applies only the latest requested value, so key-repeat bursts collapse into one DDC write.
    static void ApplyLoop() {
        int applied = -1;
        while (true) {
            pending.WaitOne();
            int value = Interlocked.CompareExchange(ref target, 0, 0);
            if (value == applied) continue;
            if (Ddc.SetBrightness(value)) {
                applied = value;
                Log("applied " + value);
            } else {
                Log("DDC write failed for " + value + ", retrying");
                Thread.Sleep(500);
                pending.Set();
            }
        }
    }

    static int ReadWindowsBrightness() {
        IntPtr schemePtr;
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out schemePtr) != 0) return -1;
        try {
            Guid scheme = (Guid)Marshal.PtrToStructure(schemePtr, typeof(Guid));
            Guid sub = SUB_VIDEO, setting = VIDEONORMALLEVEL;
            uint value;
            return Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value) == 0 ? (int)value : -1;
        } finally {
            Native.LocalFree(schemePtr);
        }
    }

    static void Log(string msg) {
        if (logPath == null) return;
        try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + Environment.NewLine); } catch { }
    }

    // Invisible top-level window that receives WM_POWERBROADCAST for the brightness setting.
    // Top-level rather than message-only so installers and Restart Manager can close it with WM_CLOSE.
    class PowerWindow : NativeWindow {
        const int WM_CLOSE = 0x0010;
        const int WM_QUERYENDSESSION = 0x0011;
        const int WM_ENDSESSION = 0x0016;
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_POWERSETTINGCHANGE = 0x8013;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        IntPtr notifyHandle;

        public PowerWindow() {
            CreateHandle(new CreateParams { Caption = "BrightnessBridge", ExStyle = WS_EX_TOOLWINDOW });
            Guid setting = VIDEONORMALLEVEL;
            notifyHandle = Native.RegisterPowerSettingNotification(Handle, ref setting, 0); // DEVICE_NOTIFY_WINDOW_HANDLE
            Log(notifyHandle == IntPtr.Zero ? "RegisterPowerSettingNotification FAILED" : "subscribed to brightness changes");
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE) {
                // POWERBROADCAST_SETTING: GUID (16 bytes), DWORD DataLength, then data
                Guid g = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
                if (g == VIDEONORMALLEVEL && Marshal.ReadInt32(m.LParam, 16) >= 4) {
                    int value = Marshal.ReadInt32(m.LParam, 20);
                    Log("event: windows brightness = " + value);
                    Request(value);
                }
                m.Result = new IntPtr(1);
                return;
            }
            if (m.Msg == WM_QUERYENDSESSION) {
                m.Result = new IntPtr(1);
                return;
            }
            if (m.Msg == WM_CLOSE || (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)) {
                Log("closing (message 0x" + m.Msg.ToString("X") + ")");
                Application.ExitThread();
                return;
            }
            base.WndProc(ref m);
        }
    }
}

static class Ddc {
    const byte VCP_BRIGHTNESS = 0x10;

    // Enumerates monitors on every call so handles stay valid across sleep, hotplug and mode changes.
    public static bool SetBrightness(int percent) {
        bool anyOk = false;
        Native.MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref Native.RECT rc, IntPtr d) => {
            uint n;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) return true;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return true;
            foreach (var pm in arr) {
                uint cur, max;
                if (!Native.GetVCPFeatureAndVCPFeatureReply(pm.h, VCP_BRIGHTNESS, IntPtr.Zero, out cur, out max) || max == 0) continue;
                uint value = (uint)Math.Round(Math.Max(0, Math.Min(100, percent)) * max / 100.0);
                if (cur == value || Native.SetVCPFeature(pm.h, VCP_BRIGHTNESS, value)) anyOk = true;
            }
            Native.DestroyPhysicalMonitors(n, arr);
            return true;
        };
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return anyOk;
    }
}

static class Native {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);
}
