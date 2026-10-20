using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dropline
{
    // Watches the global mouse with a low-level hook and reports when a left-button drag
    // starts and ends. Windows has no public "a file drag started" event, so this is a heuristic:
    // a press followed by movement beyond a small threshold, optionally limited to drags that
    // begin inside Explorer or on the desktop (the common case for files).
    internal sealed class DragWatcher : IDisposable
    {
        private const int Threshold = 10;
        private readonly Native.LowLevelMouseProc proc;
        private IntPtr hook = IntPtr.Zero;
        private bool pressed;
        private bool dragging;
        private bool eligible;
        private Native.POINT downPoint;

        public event Action DragStarted;
        public event Action DragEnded;

        public bool AnyApp { get; set; }
        public Func<IntPtr, bool> IsOwnWindow { get; set; }

        public DragWatcher()
        {
            proc = HookProc;
        }

        public void Start()
        {
            if (hook != IntPtr.Zero) return;
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(module.ModuleName), 0);
            }
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if (msg == Native.WM_LBUTTONDOWN)
                {
                    pressed = true;
                    dragging = false;
                    downPoint = data.pt;
                    eligible = IsEligibleSource(data.pt);
                }
                else if (msg == Native.WM_MOUSEMOVE && pressed && !dragging && eligible)
                {
                    if (Math.Abs(data.pt.X - downPoint.X) > Threshold || Math.Abs(data.pt.Y - downPoint.Y) > Threshold)
                    {
                        dragging = true;
                        var handler = DragStarted;
                        if (handler != null) handler();
                    }
                }
                else if (msg == Native.WM_LBUTTONUP)
                {
                    pressed = false;
                    if (dragging)
                    {
                        dragging = false;
                        var handler = DragEnded;
                        if (handler != null) handler();
                    }
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private bool IsEligibleSource(Native.POINT pt)
        {
            try
            {
                IntPtr hwnd = Native.WindowFromPoint(pt);
                if (hwnd == IntPtr.Zero) return false;
                IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
                if (IsOwnWindow != null && IsOwnWindow(root)) return false;

                // Skip presses on a title bar or a resize border: those move or size a window.
                IntPtr hit;
                int lp = (pt.Y << 16) | (pt.X & 0xFFFF);
                if (Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero, new IntPtr(lp), 0x2, 50, out hit) != IntPtr.Zero)
                {
                    long h = hit.ToInt64();
                    if (h == 2 || (h >= 10 && h <= 17) || h == 20 || h == 8 || h == 9) return false;
                }

                if (AnyApp) return true;

                string cls = Native.ClassOf(root);
                if (cls == "Progman" || cls == "WorkerW" || cls == "CabinetWClass" || cls == "ExploreWClass") return true;

                uint pid;
                Native.GetWindowThreadProcessId(root, out pid);
                using (var p = Process.GetProcessById((int)pid))
                {
                    return string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }
}
