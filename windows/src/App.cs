using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Dropline
{
    internal sealed class Settings
    {
        private readonly string file;
        public bool AnyApp;
        public bool KeepAfterDragOut;

        public Settings(string root)
        {
            file = Path.Combine(root, "settings.ini");
            if (!File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] kv = line.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                bool v = kv[1].Trim() == "1";
                if (kv[0] == "AnyApp") AnyApp = v;
                else if (kv[0] == "KeepAfterDragOut") KeepAfterDragOut = v;
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(file, new[] { "AnyApp=" + (AnyApp ? "1" : "0"), "KeepAfterDragOut=" + (KeepAfterDragOut ? "1" : "0") });
            }
            catch (IOException) { }
        }
    }

    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue("Dropline") != null;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue("Dropline", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    else key.DeleteValue("Dropline", false);
                }
            }
        }
    }

    internal sealed class Controller : IDisposable
    {
        private const int HotkeyId = 0xD10;
        private readonly ShelfStore store = new ShelfStore();
        private readonly Settings settings;
        private readonly ShelfWindow shelf;
        private readonly DragWatcher watcher = new DragWatcher();
        private readonly Forms.NotifyIcon tray;

        public Controller()
        {
            settings = new Settings(store.Root);
            store.Load();
            shelf = new ShelfWindow(store) { KeepAfterDragOut = settings.KeepAfterDragOut };

            // Create the window handle without showing it, so the hotkey can be registered.
            var helper = new WindowInteropHelper(shelf);
            helper.EnsureHandle();
            HwndSource.FromHwnd(helper.Handle).AddHook(WndProc);
            Native.MakeToolWindow(helper.Handle);
            bool hotkey = Native.RegisterHotKey(helper.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x44 /* D */);

            watcher.AnyApp = settings.AnyApp;
            watcher.IsOwnWindow = h => h == helper.Handle;
            watcher.DragStarted += () => shelf.Dispatcher.BeginInvoke(new Action(shelf.ExternalDragStarted));
            watcher.DragEnded += () => shelf.Dispatcher.BeginInvoke(new Action(shelf.ExternalDragEnded));
            watcher.Start();

            tray = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Dropline 暂存架",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) shelf.Toggle(); };

            shelf.Restore();
            if (store.Items.Count == 0)
            {
                tray.ShowBalloonTip(4000, "Dropline 已在后台运行",
                    "从资源管理器拖动文件，屏幕右侧会出现暂存架。" + (hotkey ? "按 Ctrl+Alt+D 随时打开。" : ""), Forms.ToolTipIcon.None);
            }
        }

        private Forms.ContextMenuStrip BuildMenu()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开暂存架\tCtrl+Alt+D", null, delegate { shelf.Toggle(); });
            menu.Items.Add("清空暂存架", null, delegate { store.Clear(); });
            menu.Items.Add(new Forms.ToolStripSeparator());

            var anyApp = new Forms.ToolStripMenuItem("在任何应用中拖动时都弹出") { Checked = settings.AnyApp, CheckOnClick = true };
            anyApp.CheckedChanged += delegate { settings.AnyApp = anyApp.Checked; watcher.AnyApp = anyApp.Checked; settings.Save(); };
            menu.Items.Add(anyApp);

            var keep = new Forms.ToolStripMenuItem("拖出后仍保留在暂存架") { Checked = settings.KeepAfterDragOut, CheckOnClick = true };
            keep.CheckedChanged += delegate { settings.KeepAfterDragOut = keep.Checked; shelf.KeepAfterDragOut = keep.Checked; settings.Save(); };
            menu.Items.Add(keep);

            var startup = new Forms.ToolStripMenuItem("开机自动启动") { CheckOnClick = true };
            try { startup.Checked = Startup.Enabled; } catch { }
            startup.CheckedChanged += delegate { try { Startup.Enabled = startup.Checked; } catch { } };
            menu.Items.Add(startup);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("打开剪藏文件夹", null, delegate { System.Diagnostics.Process.Start("explorer.exe", "\"" + store.Clippings + "\""); });
            menu.Items.Add("关于 Dropline", null, delegate
            {
                MessageBox.Show("Dropline " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) +
                    "\n\nWindows 上的拖拽暂存架。灵感来自 Mac 应用 Yoink（Eternal Storms Software），本项目与其无关联，未使用其任何代码。\n\ngithub.com/jiuyang5354/Dropline-",
                    "关于 Dropline");
            });
            menu.Items.Add("退出", null, delegate { Application.Current.Shutdown(); });
            return menu;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                shelf.Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static Icon LoadIcon()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dropline.ico");
            return stream != null ? new Icon(stream) : SystemIcons.Application;
        }

        public void Dispose()
        {
            watcher.Dispose();
            store.Save();
            tray.Visible = false;
            tray.Dispose();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\Dropline.SingleInstance", out created))
            {
                if (!created) return;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Controller controller = null;
                app.Startup += delegate { controller = new Controller(); };
                app.Exit += delegate { if (controller != null) controller.Dispose(); };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dropline", "error.log"),
                            DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine);
                    }
                    catch { }
                    e.Handled = true;
                };
                app.Run();
            }
        }
    }
}
