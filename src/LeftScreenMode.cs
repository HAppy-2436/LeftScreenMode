// LeftScreenMode - 让屏幕只使用左半边、右侧变黑，并可一键/热键切换
// 编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LeftScreenMode
{
    internal static class Program
    {
        public const string Version = "1.2.4";
        public static int CmdMessage;
        public static string ExeDir = "";
        public static string DataDir = "";
        public static string ConfigPath = "";
        public static string StatePath = "";
        public static string RuntimePath = "";
        public static string LogPath = "";
        public static string CmdFilePath = "";
        private static readonly object LogLock = new object();

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                ExeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(ExeDir)) ExeDir = Environment.CurrentDirectory;
                InitPaths();
                CmdMessage = Native.RegisterWindowMessage("LeftScreenMode.Command");

                string cmd = NormalizeCommand(args);
                if (cmd == "help" || cmd == "?")
                {
                    CliWrite(HelpText());
                    return 0;
                }
                if (cmd == "info" || cmd == "status")
                {
                    PrintInfo(cmd);
                    return 0;
                }

                int reqPercent;
                string reqSide;
                bool hasPreset = ParsePresetArgs(args, out reqPercent, out reqSide);
                if (!hasPreset && (cmd == "preset" || cmd == "percent" || cmd == "side"))
                {
                    CliWrite("参数无效。用法: --preset left:70 | --preset right:50 | --percent 60 | --side left|right\r\n");
                    return 2;
                }

                bool createdNew;
                System.Threading.Mutex mutex = new System.Threading.Mutex(true, "Local\\LeftScreenMode.SingleInstance", out createdNew);
                try
                {
                    if (!createdNew)
                    {
                        if (hasPreset)
                        {
                            WritePresetCommandFile(reqSide, reqPercent);
                            bool sentPreset = SendCommandToRunningInstance("apply");
                            if (!sentPreset) CliWrite("未能通知正在运行的程序，请重试。\r\n");
                            return 0;
                        }
                        bool sent = SendCommandToRunningInstance(cmd);
                        if (!sent && (cmd == "off" || cmd == "restore" || cmd == "full"))
                            RestoreStaleState(true);
                        return 0;
                    }

                    if (hasPreset)
                    {
                        SavePresetToIni(reqPercent, reqSide);
                        cmd = "on";
                    }

                    if (cmd == "off" || cmd == "restore" || cmd == "full")
                    {
                        RestoreStaleState(true);
                        return 0;
                    }
                    if (cmd == "quit" || cmd == "exit")
                        return 0;

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { Log("界面线程异常: " + e.Exception); };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log("未处理异常: " + e.ExceptionObject); };

                    MessageForm form = new MessageForm();
                    App app = new App(form, cmd);
                    App.Instance = app;
                    Application.Run(form);
                    return 0;
                }
                finally
                {
                    try { mutex.ReleaseMutex(); }
                    catch { }
                    try { mutex.Dispose(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log("主程序异常: " + ex);
                CliWrite("发生异常: " + ex.Message + "\r\n详细信息见日志: " + LogPath + "\r\n");
                return 1;
            }
        }

        private static void InitPaths()
        {
            DataDir = ExeDir;
            try
            {
                string test = Path.Combine(ExeDir, ".lsm_write_test");
                File.WriteAllText(test, "x");
                File.Delete(test);
            }
            catch
            {
                DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeftScreenMode");
                try { Directory.CreateDirectory(DataDir); }
                catch { }
            }
            ConfigPath = Path.Combine(DataDir, "LeftScreenMode.ini");
            StatePath = Path.Combine(DataDir, "LeftScreenMode.state");
            RuntimePath = Path.Combine(DataDir, "LeftScreenMode.runtime");
            LogPath = Path.Combine(DataDir, "LeftScreenMode.log");
            CmdFilePath = Path.Combine(DataDir, "LeftScreenMode.cmd");
        }

        private static string NormalizeCommand(string[] args)
        {
            if (args == null || args.Length == 0) return "";
            string a = args[0].Trim().ToLowerInvariant();
            while (a.StartsWith("-")) a = a.Substring(1);
            if (a.StartsWith("/")) a = a.Substring(1);
            if (a == "enable" || a == "left" || a == "on") return "on";
            if (a == "disable" || a == "full" || a == "off" || a == "restore") return "off";
            if (a == "quit" || a == "exit") return "quit";
            return a;
        }

        public static void StartApplication()
        {
            if (App.Instance != null) App.Instance.StartApp();
        }

        private static int CommandCode(string cmd)
        {
            if (cmd == "on") return 1;
            if (cmd == "off") return 2;
            if (cmd == "toggle") return 3;
            if (cmd == "quit") return 4;
            if (cmd == "refresh") return 5;
            if (cmd == "apply") return 6;
            return 0;
        }

        private static bool SendCommandToRunningInstance(string cmd)
        {
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 25; i++)
            {
                hwnd = Native.FindWindow(null, MessageForm.WindowTitle);
                if (hwnd != IntPtr.Zero) break;
                System.Threading.Thread.Sleep(100);
            }
            if (hwnd == IntPtr.Zero) return false;
            if (CmdMessage == 0) return false;
            Native.SendMessage(hwnd, CmdMessage, new IntPtr(CommandCode(cmd)), IntPtr.Zero);
            return true;
        }

        private static void RestoreStaleState(bool print)
        {
            SavedState st = ReadState();
            if (st == null)
            {
                RecoverTaskbarStale();
                if (print) CliWrite("没有需要恢复的状态。\r\n");
                return;
            }
            if (st.Enabled && st.WorkArea != Rectangle.Empty)
            {
                bool ok = Native.SetWorkArea(st.WorkArea);
                if (print) CliWrite("已恢复工作区: " + RectText(st.WorkArea) + (ok ? " (成功)" : " (失败)") + "\r\n");
            }
            if (st.TaskbarAl >= 0)
            {
                WriteTaskbarAl(st.TaskbarAl);
                BroadcastTaskbarSetting();
                if (print) CliWrite("已恢复任务栏图标对齐 (TaskbarAl=" + st.TaskbarAl + ")\r\n");
            }
            DeleteState();
            RecoverTaskbarStale();
        }

        // ---------- console ----------
        public static void CliWrite(string text)
        {
            try
            {
                IntPtr h = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                    Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
                using (Stream stdout = Console.OpenStandardOutput())
                {
                    if (stdout != null)
                    {
                        using (StreamWriter w = new StreamWriter(stdout, new UTF8Encoding(false)))
                        {
                            w.AutoFlush = true;
                            w.Write(text);
                        }
                    }
                }
            }
            catch
            {
                try { Console.Out.Write(text); }
                catch { }
            }
        }

        // ---------- logging ----------
        public static void Log(string msg)
        {
            try
            {
                lock (LogLock)
                {
                    FileInfo fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 262144) fi.Delete();
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n", new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        // ---------- key/value helpers ----------
        public static Dictionary<string, string> ParseKeyValues(string[] lines)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null) return kv;
            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                    val = val.Substring(1, val.Length - 2);
                kv[key] = val;
            }
            return kv;
        }

        public static string GetString(Dictionary<string, string> kv, string key, string def)
        {
            string v;
            if (kv != null && kv.TryGetValue(key, out v)) return v;
            return def;
        }

        public static bool GetBool(Dictionary<string, string> kv, string key, bool def)
        {
            string v = GetString(kv, key, null);
            if (v == null) return def;
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on" || v == "是";
        }

        public static int GetInt(Dictionary<string, string> kv, string key, int def)
        {
            string v = GetString(kv, key, null);
            int n;
            if (v != null && int.TryParse(v.Trim(), out n)) return n;
            return def;
        }

        public static Rectangle ParseRect(string text)
        {
            if (string.IsNullOrEmpty(text)) return Rectangle.Empty;
            string[] p = text.Split(',');
            if (p.Length != 4) return Rectangle.Empty;
            int[] n = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(p[i].Trim(), out n[i])) return Rectangle.Empty;
            }
            return Rectangle.FromLTRB(n[0], n[1], n[2], n[3]);
        }

        public static string RectText(Rectangle r)
        {
            if (r == Rectangle.Empty) return "";
            return r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom;
        }

        public static Color ParseColor(string text, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return fallback;
                string s = text.Trim();
                if (s.StartsWith("#")) s = s.Substring(1);
                if (s.Length == 6)
                {
                    int v = int.Parse(s, NumberStyles.HexNumber);
                    return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
                }
            }
            catch
            {
            }
            return fallback;
        }

        // ---------- 任务栏图标对齐（半屏模式左对齐，退出恢复） ----------
        public const string TaskbarAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        public static int ReadTaskbarAl()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(TaskbarAdvancedKey, false))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("TaskbarAl");
                        if (v is int) return (int)v;
                        if (v is long) return (int)(long)v;
                    }
                }
            }
            catch
            {
            }
            return 1;
        }

        public static bool WriteTaskbarAl(int value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(TaskbarAdvancedKey, true))
                {
                    if (k == null) return false;
                    k.SetValue("TaskbarAl", value, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("写入 TaskbarAl 失败: " + ex.Message);
                return false;
            }
        }

        public static void BroadcastTaskbarSetting()
        {
            Native.BroadcastSettingChange("TraySettings");
            Native.BroadcastSettingChange("TaskbarAl");
            System.Threading.Thread.Sleep(120);
        }
        // ---------- saved state ----------
        public static SavedState ReadState()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                Dictionary<string, string> kv = ParseKeyValues(File.ReadAllLines(StatePath, Encoding.UTF8));
                SavedState st = new SavedState();
                st.Enabled = GetBool(kv, "enabled", false);
                st.WorkArea = ParseRect(GetString(kv, "workarea", ""));
                st.Monitor = GetString(kv, "monitor", "");
                st.TaskbarAl = GetInt(kv, "taskbaral", -1);
                return st;
            }
            catch
            {
                return null;
            }
        }

        public static void WriteState(SavedState st)
        {
            try
            {
                SavedState old = ReadState();
                if (old != null)
                {
                    if (st.WorkArea == Rectangle.Empty && old.WorkArea != Rectangle.Empty)
                        st.WorkArea = old.WorkArea;
                    if (st.TaskbarAl < 0 && old.TaskbarAl >= 0)
                        st.TaskbarAl = old.TaskbarAl;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("enabled=" + (st.Enabled ? "1" : "0"));
                sb.AppendLine("workarea=" + RectText(st.WorkArea));
                sb.AppendLine("monitor=" + (st.Monitor == null ? "" : st.Monitor));
                if (st.TaskbarAl >= 0) sb.AppendLine("taskbaral=" + st.TaskbarAl);
                sb.AppendLine("pid=" + Process.GetCurrentProcess().Id);
                sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                File.WriteAllText(StatePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log("写入状态失败: " + ex.Message);
            }
        }
        public static void DeleteState()
        {
            try
            {
                if (File.Exists(StatePath)) File.Delete(StatePath);
            }
            catch
            {
            }
        }

        // 若上次被强制结束，任务栏可能停留在半宽状态：启动/--off 时自动恢复
        public static void RecoverTaskbarStale()
        {
            try
            {
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return;
                Rectangle trayRect = Native.GetWindowRectangle(tray);
                if (trayRect.Width < 100) return;
                IntPtr bridge = IntPtr.Zero;
                try
                {
                    Native.EnumChildWindows(tray, delegate(IntPtr h, IntPtr l)
                    {
                        if (Native.GetClassNameSafe(h) == "Windows.UI.Composition.DesktopWindowContentBridge")
                        {
                            bridge = h;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                catch
                {
                    return;
                }
                if (bridge == IntPtr.Zero) return;
                Rectangle br = Native.GetWindowRectangle(bridge);
                if (br.Width > 0 && br.Width < trayRect.Width - 50)
                {
                    Native.SetWindowPos(bridge, IntPtr.Zero, 0, 0, trayRect.Width, trayRect.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                    try { Native.SetWindowRgn(tray, IntPtr.Zero, true); }
                    catch { }
                    Log("已恢复上次异常退出的任务栏（全宽）");
                }
            }
            catch (Exception ex)
            {
                Log("恢复任务栏状态失败: " + ex.Message);
            }
        }
        // ---------- 显示区域预设（百分比 / 左右侧） ----------
        public static bool ParsePresetArgs(string[] args, out int percent, out string side)
        {
            percent = 0;
            side = null;
            bool found = false;
            if (args == null) return false;
            int i = 0;
            while (i < args.Length)
            {
                string a = args[i] == null ? "" : args[i].Trim();
                if (a.Length == 0) { i++; continue; }
                string key = a.TrimStart('-', '/').ToLowerInvariant();
                string val = null;
                int eq = key.IndexOf('=');
                if (eq > 0)
                {
                    val = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }
                else if (i + 1 < args.Length)
                {
                    val = args[i + 1];
                }
                if (key == "preset" && !string.IsNullOrEmpty(val))
                {
                    string[] parts = val.Trim().ToLowerInvariant().Split(':', '：');
                    foreach (string part in parts)
                    {
                        string p = part.Trim();
                        if (p.Length == 0) continue;
                        if (p == "left" || p == "左" || p == "左半") { side = "right"; found = true; }
                        else if (p == "right" || p == "右" || p == "右半") { side = "left"; found = true; }
                        else
                        {
                            int n;
                            if (int.TryParse(p.Replace("%", "").Trim(), out n)) { percent = n; found = true; }
                        }
                    }
                    i += (eq > 0) ? 1 : 2;
                    continue;
                }
                if (key == "percent" && !string.IsNullOrEmpty(val))
                {
                    int n;
                    if (int.TryParse(val.Replace("%", "").Trim(), out n)) { percent = n; found = true; }
                    i += (eq > 0) ? 1 : 2;
                    continue;
                }
                if (key == "side" && !string.IsNullOrEmpty(val))
                {
                    string s = val.Trim().ToLowerInvariant();
                    if (s == "left" || s == "左") { side = "right"; found = true; }
                    else if (s == "right" || s == "右") { side = "left"; found = true; }
                    i += (eq > 0) ? 1 : 2;
                    continue;
                }
                i++;
            }
            if (percent > 0)
            {
                if (percent < 10) percent = 10;
                if (percent > 100) percent = 100;
            }
            return found;
        }

        public static void WritePresetCommandFile(string side, int percent)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                if (percent > 0) sb.AppendLine("percent=" + percent);
                if (!string.IsNullOrEmpty(side)) sb.AppendLine("side=" + side);
                File.WriteAllText(CmdFilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log("写入预设命令失败: " + ex.Message);
            }
        }

        public static void SavePresetToIni(int percent, string side)
        {
            try
            {
                Dictionary<string, string> vals = new Dictionary<string, string>();
                if (percent > 0)
                {
                    int p = percent;
                    if (p < 10) p = 10;
                    if (p > 100) p = 100;
                    vals.Add("LeftAreaPercent", p.ToString());
                }
                if (!string.IsNullOrEmpty(side))
                    vals.Add("CoverSide", side == "left" ? "left" : "right");
                Config.UpdateKeys(ConfigPath, vals);
            }
            catch (Exception ex)
            {
                Log("保存显示区域预设失败: " + ex.Message);
            }
        }
        private static void PrintInfo(string cmd)
        {
            bool running = Native.FindWindow(null, MessageForm.WindowTitle) != IntPtr.Zero;
            Dictionary<string, string> rt = null;
            try
            {
                if (File.Exists(RuntimePath))
                    rt = ParseKeyValues(File.ReadAllLines(RuntimePath, Encoding.UTF8));
            }
            catch
            {
            }
            bool enabled = rt != null && GetBool(rt, "enabled", false);

            if (cmd == "status")
            {
                if (!running) CliWrite("stopped\r\n");
                else CliWrite(enabled ? "enabled\r\n" : "disabled\r\n");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LeftScreenMode " + Version);
            sb.AppendLine("运行状态: " + (running ? "正在运行" : "未运行"));
            sb.AppendLine("当前模式: " + (running ? (enabled ? "左侧模式（右半屏黑）" : "全屏模式") : "未运行"));
            sb.AppendLine("配置文件: " + ConfigPath + (File.Exists(ConfigPath) ? "" : "（不存在，将使用默认值）"));
            sb.AppendLine("数据目录: " + DataDir);
            sb.AppendLine("显示器列表:");
            List<MonitorDesc> mons = MonitorUtil.All();
            for (int i = 0; i < mons.Count; i++)
            {
                MonitorDesc m = mons[i];
                sb.AppendLine("  [" + i + "] " + m.DeviceName + (m.Primary ? " [主]" : "") + "  " +
                              m.Bounds.Width + "x" + m.Bounds.Height + " @ (" + m.Bounds.X + "," + m.Bounds.Y + ")  " +
                              "工作区=" + RectText(m.WorkArea) + "  ID=" + m.MonitorId);
            }
            sb.AppendLine("当前系统工作区: " + RectText(Native.GetWorkArea()));
            try
            {
                Config cfg = Config.Load(ConfigPath);
                MonitorDesc target = MonitorUtil.Select(cfg.Monitor);
                sb.AppendLine("显示区域: " + (cfg.CoverSide == "left" ? "右侧" : "左侧") + "可用 " + cfg.LeftAreaPercent + "%（Presets=" + string.Join(",", Array.ConvertAll(cfg.Presets, delegate(int v) { return v.ToString(); })) + "）");
                sb.AppendLine("配置: Monitor=" + cfg.Monitor + "  LeftAreaPercent=" + cfg.LeftAreaPercent +
                              "  CoverSide=" + cfg.CoverSide +
                              "  MoveWindows=" + (cfg.MoveWindows ? "1" : "0") +
                              "  ConstrainWorkArea=" + (cfg.ConstrainWorkArea ? "1" : "0"));
                sb.AppendLine("目标显示器: " + (target == null ? "未找到" : target.DeviceName + " (" + target.MonitorId + ")"));
            }
            catch
            {
            }
            if (rt != null)
            {
                sb.AppendLine("运行信息: enabled=" + GetString(rt, "enabled", "?") + " appBar=" + GetString(rt, "appBar", "-") + " taskbarHalf=" + GetString(rt, "taskbarHalf", "-") +
                              " overlay=" + GetString(rt, "overlay", "-") +
                              " hotkey=" + GetString(rt, "hotkey", "-") +
                              " hotkeyRegistered=" + GetString(rt, "hotkeyRegistered", "-") +
                              " updated=" + GetString(rt, "updated", "-"));
            }
            CliWrite(sb.ToString());
        }

        private static string HelpText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LeftScreenMode " + Version + " — 让显示器只使用左半边、右侧遮黑，并可快速切换");
            sb.AppendLine("");
            sb.AppendLine("用法:");
            sb.AppendLine("  LeftScreenMode.exe             启动托盘程序（默认全屏正常显示）");
            sb.AppendLine("  LeftScreenMode.exe --on        启动并切换到「左侧模式」(右半屏黑)");
            sb.AppendLine("  LeftScreenMode.exe --off       关闭「左侧模式」，恢复正常全屏显示");
            sb.AppendLine("  LeftScreenMode.exe --toggle    切换两种模式（未运行时启动并进入左侧模式）");
            sb.AppendLine("  LeftScreenMode.exe --quit      退出托盘程序并恢复所有设置");
            sb.AppendLine("  LeftScreenMode.exe --info      查看显示器、工作区与运行状态");
            sb.AppendLine("  LeftScreenMode.exe --status    只输出 enabled / disabled / stopped");
            sb.AppendLine("  LeftScreenMode.exe --preset left:70    切换到“左侧可用 70%”（右侧 30% 黑）");
            sb.AppendLine("  LeftScreenMode.exe --preset right:50   切换到“右侧可用 50%”（左侧一半黑）");
            sb.AppendLine("  LeftScreenMode.exe --percent 60        只改可用宽度百分比（左右侧保持当前）");
            sb.AppendLine("  LeftScreenMode.exe --side left         只改可用侧（left=左侧可用, right=右侧可用）");
            sb.AppendLine("");
            sb.AppendLine("比例热键（默认）: Win+Ctrl+L + 数字 = 左侧可用；Win+Ctrl+R + 数字 = 右侧可用");
            sb.AppendLine("  数字即百分比（7=70%，0=100%），不按数字 = 50%，Esc 取消；超过 100% 按 100% 计算。");
            sb.AppendLine("  切回全屏：Win+Ctrl+0（鼠标可改 ini 的 HotkeyFullScreen）");
            sb.AppendLine("  LeftScreenMode.exe --help      显示本帮助");
            sb.AppendLine("");
            sb.AppendLine("日常使用: 托盘图标双击可切换；Win+Ctrl+L/R + 数字 设置显示区域，Win+Ctrl+0 切回全屏。");
            sb.AppendLine("配置文件: " + ConfigPath);
            return sb.ToString();
        }
    }

    internal sealed class SavedState
    {
        public bool Enabled;
        public Rectangle WorkArea = Rectangle.Empty;
        public string Monitor = "";
        public int TaskbarAl = -1;
    }

    // =====================================================================
    internal sealed class Config
    {
        public string Monitor = "auto";
        public int LeftAreaPercent = 50;
        public string CoverSide = "right";

        public string HotkeyLeftLayout = "Win+Ctrl+L";
        public string HotkeyRightLayout = "Win+Ctrl+R";
        public string HotkeyFullScreen = "Win+Ctrl+0";
        public int LayoutDigitTimeoutMs = 1500;
        public bool UseKeyboardHook = true;
        public bool RepositionFlyouts = true;
        public bool MoveWindows = true;
        public bool ResizeFullscreenWindows = false;
        public bool ConstrainWorkArea = true;
        public int OverlayOpacity = 100;
        public string OverlayColor = "#000000";
        public bool StartInLeftMode = false;
        public bool BalloonTips = true;
        public bool HalfTaskbar = true;
        public bool LeftAlignTaskbarIcons = true;
        public int[] Presets = new int[] { 30, 40, 50, 60, 70, 80 };

        public const string DefaultIni =
            "; ============================================================\r\n" +
            "; LeftScreenMode 配置文件\r\n" +
            "; 修改后请退出程序（托盘菜单“退出”）再重新启动，配置才会生效。\r\n" +
            "; 首次运行会自动生成此文件。\r\n" +
            "; ============================================================\r\n" +
            "\r\n" +
            "; 目标显示器。auto = 主显示器（默认）；也可填 \"\\\\.\\DISPLAY1\"、\r\n" +
            "; 或者显示器硬件 ID 中的关键字，例如 SKG2526（本机外接屏）。\r\n" +
            "Monitor=auto\r\n" +
            "\r\n" +
            "; 左侧可用区域占屏幕宽度的百分比（10-100），默认 50 即左半屏。\r\n" +
            "LeftAreaPercent=50\r\n" +
            "\r\n" +
            "; 遮盖哪一侧：right = 遮住右侧（默认，适合右侧被遮挡）；left = 遮住左侧。\r\n" +
            "CoverSide=right\r\n" +
            "\r\n" +

            "; 比例热键：先按组合键，再按数字选择百分比（7=70%，0=100%，无数字=50%）。\r\n" +
            "; Win+Ctrl+L 被 Windows 11「实时字幕」占用，程序会用键盘钩子接管（会覆盖系统的实时字幕快捷键）；\r\n" +
            "; 若不想接管，可把左侧改成 Win+Alt+L，并把 UseKeyboardHook 设为 0。\r\n" +
            "HotkeyLeftLayout=Win+Ctrl+L\r\n" +
            "HotkeyRightLayout=Win+Ctrl+R\r\n" +
            "\r\n" +
            "; 数字输入等待/确认时间（毫秒，500-5000）。\r\n" +
            "LayoutDigitTimeoutMs=1500\r\n" +
            "\r\n" +
            "; 1=用低级键盘钩子识别组合键并接管数字输入（默认 1）。\r\n" +
            "UseKeyboardHook=1\r\n" +
            "\r\n" +
            "; 1=把 Win+A 快速设置 / Win+N 通知中心等系统浮出面板移入可用区域（默认 1）。\r\n" +
            "RepositionFlyouts=1\r\n" +
            "\r\n" +
            "; 切回全屏的快捷键（默认 Win+Ctrl+0，被其它程序占用时用键盘钩子接管）。\r\n" +
            "HotkeyFullScreen=Win+Ctrl+0\r\n" +
            "\r\n" +
            "; 1 = 进入左侧模式时把窗口移进左侧可用区域（默认 1）。\r\n" +
            "MoveWindows=1\r\n" +
            "\r\n" +
            "; 1 = 尝试把无边框全屏窗口缩到左侧（默认 0，可能影响部分游戏）。\r\n" +
            "ResizeFullscreenWindows=0\r\n" +
            "\r\n" +
            "; 1 = 把 Windows 工作区（窗口最大化区域）限制在左侧（默认 1，仅主显示器有效）。\r\n" +
            "ConstrainWorkArea=1\r\n" +
            "\r\n" +
            "; 遮罩不透明度 1-100，默认 100（纯黑）。\r\n" +
            "OverlayOpacity=100\r\n" +
            "\r\n" +
            "; 遮罩颜色，默认纯黑。\r\n" +
            "OverlayColor=#000000\r\n" +
            "\r\n" +
            "; 1 = 程序启动时直接进入左侧模式（默认 0）。\r\n" +
            "StartInLeftMode=0\r\n" +
            "\r\n" +
            "; 1 = 让任务栏也只占左半（默认 1；Windows 11 实测有效）。\r\n" +
            "HalfTaskbar=1\r\n" +
            "\r\n" +
            "; 1 = 半屏模式下把任务栏图标改为左对齐（默认 1）。\r\n" +
            "; 若你用 Windhawk 的“徽标左对齐+图标居中”，窄任务栏里居中会算错位，\r\n" +
            "; 所以半屏模式会临时改成左对齐，退出后自动恢复你原来的对齐设置。\r\n" +
            "LeftAlignTaskbarIcons=1\r\n" +
            "\r\n" +
            "; 托盘菜单“显示区域”里列出的可用宽度百分比（10-100），逗号分隔。\r\n" +
            "Presets=30,40,50,60,70,80\r\n" +
            "\r\n" +
            "; 1 = 切换时显示气泡提示（默认 1）。\r\n" +
            "BalloonTips=1\r\n";

        public static Config Load(string path)
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(path))
                {
                    WriteDefault(path);
                    return c;
                }
                Dictionary<string, string> kv = Program.ParseKeyValues(File.ReadAllLines(path, Encoding.UTF8));
                c.Monitor = Program.GetString(kv, "Monitor", c.Monitor);
                c.LeftAreaPercent = Program.GetInt(kv, "LeftAreaPercent", c.LeftAreaPercent);
                if (c.LeftAreaPercent < 10) c.LeftAreaPercent = 10;
                if (c.LeftAreaPercent > 100) c.LeftAreaPercent = 100;
                c.CoverSide = Program.GetString(kv, "CoverSide", c.CoverSide).Trim().ToLowerInvariant();
                if (c.CoverSide != "left") c.CoverSide = "right";

                c.HotkeyLeftLayout = Program.GetString(kv, "HotkeyLeftLayout", c.HotkeyLeftLayout).Trim();
                c.HotkeyRightLayout = Program.GetString(kv, "HotkeyRightLayout", c.HotkeyRightLayout).Trim();
                c.LayoutDigitTimeoutMs = Program.GetInt(kv, "LayoutDigitTimeoutMs", c.LayoutDigitTimeoutMs);
                if (c.LayoutDigitTimeoutMs < 500) c.LayoutDigitTimeoutMs = 500;
                if (c.LayoutDigitTimeoutMs > 5000) c.LayoutDigitTimeoutMs = 5000;
                c.UseKeyboardHook = Program.GetBool(kv, "UseKeyboardHook", c.UseKeyboardHook);
                c.RepositionFlyouts = Program.GetBool(kv, "RepositionFlyouts", c.RepositionFlyouts);
                c.HotkeyFullScreen = Program.GetString(kv, "HotkeyFullScreen", c.HotkeyFullScreen).Trim();
                c.MoveWindows = Program.GetBool(kv, "MoveWindows", c.MoveWindows);
                c.ResizeFullscreenWindows = Program.GetBool(kv, "ResizeFullscreenWindows", c.ResizeFullscreenWindows);
                c.ConstrainWorkArea = Program.GetBool(kv, "ConstrainWorkArea", c.ConstrainWorkArea);
                c.OverlayOpacity = Program.GetInt(kv, "OverlayOpacity", c.OverlayOpacity);
                if (c.OverlayOpacity < 1) c.OverlayOpacity = 1;
                if (c.OverlayOpacity > 100) c.OverlayOpacity = 100;
                c.OverlayColor = Program.GetString(kv, "OverlayColor", c.OverlayColor).Trim();
                c.StartInLeftMode = Program.GetBool(kv, "StartInLeftMode", c.StartInLeftMode);
                c.BalloonTips = Program.GetBool(kv, "BalloonTips", c.BalloonTips);
                c.HalfTaskbar = Program.GetBool(kv, "HalfTaskbar", c.HalfTaskbar);
                c.LeftAlignTaskbarIcons = Program.GetBool(kv, "LeftAlignTaskbarIcons", c.LeftAlignTaskbarIcons);
                string presetsText = Program.GetString(kv, "Presets", null);
                if (!string.IsNullOrEmpty(presetsText))
                {
                    List<int> presetList = new List<int>();
                    foreach (string part in presetsText.Split(',', '，'))
                    {
                        int n;
                        if (int.TryParse(part.Trim(), out n) && n >= 10 && n <= 100 && !presetList.Contains(n))
                            presetList.Add(n);
                    }
                    if (presetList.Count > 0) c.Presets = presetList.ToArray();
                }
            }
            catch (Exception ex)
            {
                Program.Log("读取配置失败: " + ex.Message);
            }
            return c;
        }

        public static void WriteDefault(string path)
        {
            try
            {
                if (!File.Exists(path))
                    File.WriteAllText(path, DefaultIni, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Program.Log("写入默认配置失败: " + ex.Message);
            }
        }
        public static void UpdateKeys(string path, Dictionary<string, string> values)
        {
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
                HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = t.Substring(0, eq).Trim();
                    foreach (KeyValuePair<string, string> kv in values)
                    {
                        if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = key + "=" + kv.Value;
                            done.Add(kv.Key);
                            break;
                        }
                    }
                }
                foreach (KeyValuePair<string, string> kv in values)
                {
                    if (!done.Contains(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
                }
                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Program.Log("更新配置失败: " + ex.Message);
            }
        }
        // ---------- hotkey parsing ----------
        public static bool TryParseHotkey(string text, out int mods, out int vk, out string normalized)
        {
            mods = 0;
            vk = 0;
            normalized = text;
            if (text == null) return false;
            string[] parts = text.Split('+');
            if (parts.Length < 2) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) return false;
                string pl = p.ToLowerInvariant();
                if (i < parts.Length - 1)
                {
                    if (pl == "ctrl" || pl == "control") mods |= Native.MOD_CONTROL;
                    else if (pl == "alt") mods |= Native.MOD_ALT;
                    else if (pl == "shift") mods |= Native.MOD_SHIFT;
                    else if (pl == "win" || pl == "windows" || pl == "super") mods |= Native.MOD_WIN;
                    else return false;
                }
                else
                {
                    vk = KeyNameToVk(pl);
                    if (vk == 0) return false;
                }
            }
            if (mods == 0 || vk == 0) return false;
            StringBuilder sb = new StringBuilder();
            if ((mods & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & Native.MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(VkToName(vk));
            normalized = sb.ToString();
            return true;
        }

        private static int KeyNameToVk(string lower)
        {
            if (lower.Length == 1)
            {
                char c = lower[0];
                if (c >= 'a' && c <= 'z') return (int)char.ToUpperInvariant(c);
                if (c >= '0' && c <= '9') return (int)c;
                return 0;
            }
            switch (lower)
            {
                case "space": return 0x20;
                case "tab": return 0x09;
                case "enter":
                case "return": return 0x0D;
                case "esc":
                case "escape": return 0x1B;
                case "backspace": return 0x08;
                case "delete":
                case "del": return 0x2E;
                case "insert":
                case "ins": return 0x2D;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pageup":
                case "pgup": return 0x21;
                case "pagedown":
                case "pgdn": return 0x22;
                case "left": return 0x25;
                case "up": return 0x26;
                case "right": return 0x27;
                case "down": return 0x28;
                case "printscreen":
                case "prtsc": return 0x2C;
                case "pause": return 0x13;
                case "capslock": return 0x14;
                case "numlock": return 0x90;
                case "scrolllock": return 0x91;
                case "`":
                case "tilde": return 0xC0;
            }
            if (lower.Length >= 2 && lower[0] == 'f')
            {
                int n;
                if (int.TryParse(lower.Substring(1), out n) && n >= 1 && n <= 24)
                    return 0x70 + (n - 1);
            }
            return 0;
        }

        private static string VkToName(int vk)
        {
            if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
            if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            switch (vk)
            {
                case 0x20: return "Space";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x08: return "Backspace";
                case 0x2E: return "Delete";
                case 0x2D: return "Insert";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "PrintScreen";
                case 0x13: return "Pause";
                case 0xC0: return "`";
            }
            return "VK" + vk;
        }
    }

    // =====================================================================
    internal static class IconFactory
    {
        public static Icon Create(bool leftMode)
        {
            int size = 32;
            Bitmap bmp = new Bitmap(size, size);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Color frame = Color.FromArgb(110, 118, 132);
                    Color blue = Color.FromArgb(64, 140, 240);
                    Color blueLight = Color.FromArgb(150, 190, 245);
                    Color dark = Color.FromArgb(24, 24, 28);
                    Rectangle outer = new Rectangle(1, 4, size - 3, size - 9);
                    using (Pen pen = new Pen(frame, 1.6f))
                        g.DrawRectangle(pen, outer);
                    int pad = 3;
                    int mid = size / 2;
                    Rectangle left = new Rectangle(outer.Left + pad, outer.Top + pad, mid - outer.Left - pad, outer.Height - pad * 2);
                    Rectangle right = new Rectangle(mid, outer.Top + pad, outer.Right - pad - mid, outer.Height - pad * 2);
                    using (SolidBrush b1 = new SolidBrush(blue))
                        g.FillRectangle(b1, left);
                    using (SolidBrush b2 = new SolidBrush(leftMode ? dark : blueLight))
                        g.FillRectangle(b2, right);
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
            finally
            {
                bmp.Dispose();
            }
        }
    }

    // =====================================================================
    internal sealed class OverlayForm : Form
    {
        private readonly byte _alpha;
        private readonly Color _color;
        private bool _appBarRegistered;
        private uint _appBarEdge;

        public OverlayForm(int opacityPercent, Color color)
        {
            _color = color;
            int a = opacityPercent * 255 / 100;
            if (a < 1) a = 1;
            if (a > 255) a = 255;
            _alpha = (byte)a;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = color;
            TopMost = true;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = "";
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                Native.SetLayeredWindowAttributes(Handle, 0, _alpha, Native.LWA_ALPHA);
            }
            catch
            {
            }
        }

        public void ApplyBounds(Rectangle r)
        {
            if (!Visible)
            {
                Bounds = r;
                Show();
            }
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
            if (_appBarRegistered)
                Native.SetAppBarPosition(Handle, _appBarEdge, r);
        }

        public bool AppBarRegistered
        {
            get { return _appBarRegistered; }
        }

        public bool EnsureAppBar(uint edge, Rectangle r)
        {
            _appBarEdge = edge;
            if (!_appBarRegistered)
            {
                if (!Native.CreateAppBar(Handle, edge, r, (uint)Native.WM_APPBAR_CALLBACK))
                {
                    Program.Log("注册 AppBar 失败（将回退到其它方式）");
                    return false;
                }
                _appBarRegistered = true;
                Program.Log("已注册 AppBar: edge=" + edge + " rect=" + Program.RectText(r));
                return true;
            }
            Native.SetAppBarPosition(Handle, edge, r);
            return true;
        }

        public void RemoveAppBar()
        {
            if (!_appBarRegistered) return;
            try
            {
                Native.RemoveAppBar(Handle);
                Program.Log("已移除 AppBar");
            }
            catch
            {
            }
            _appBarRegistered = false;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            }
            if (m.Msg == Native.WM_NCHITTEST)
            {
                m.Result = new IntPtr(Native.HTTRANSPARENT);
                return;
            }
            if (m.Msg == Native.WM_APPBAR_CALLBACK)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_color);
        }
    }

    // =====================================================================
    internal sealed class MessageForm : Form
    {
        public const string WindowTitle = "LeftScreenMode.Hidden.Window";

        public MessageForm()
        {
            Text = WindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-32000, -32000, 8, 8);
            Opacity = 0;
            AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false;
            MinimizeBox = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BeginInvoke(new MethodInvoker(Program.StartApplication));
        }

        protected override void WndProc(ref Message m)
        {
            if (App.Instance != null) App.Instance.HandleMessage(ref m);
            base.WndProc(ref m);
        }
    }

    // =====================================================================
    internal sealed class HintForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;

        public HintForm(string text, Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(22, 24, 30);
            Bounds = bounds;
            DoubleBuffered = true;

            Label label = new Label();
            label.Text = text;
            label.ForeColor = Color.White;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Dock = DockStyle.Fill;
            try { label.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }
            Controls.Add(label);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 6500;
            _timer.Tick += delegate
            {
                _timer.Stop();
                try { Close(); }
                catch { }
            };
            _timer.Start();
        }

        public void StopTimer()
        {
            try { _timer.Stop(); }
            catch { }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(90, 160, 255), 2))
                e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }
    // =====================================================================
    internal sealed class LayoutDialog : Form
    {
        private readonly RadioButton _rbLeft;
        private readonly RadioButton _rbRight;
        private readonly NumericUpDown _num;
        public string ResultSide = "right";
        public int ResultPercent = 50;

        public LayoutDialog(string side, int percent)
        {
            Text = "自定义显示区域";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(380, 170);
            TopMost = true;
            try { Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }

            Label lblTip = new Label();
            lblTip.Text = "选择可用区域（另一侧会被纯黑遮罩）：";
            lblTip.AutoSize = true;
            lblTip.Location = new Point(16, 14);
            Controls.Add(lblTip);

            _rbLeft = new RadioButton();
            _rbLeft.Text = "左侧可用（遮住右侧）";
            _rbLeft.AutoSize = true;
            _rbLeft.Location = new Point(24, 46);
            Controls.Add(_rbLeft);

            _rbRight = new RadioButton();
            _rbRight.Text = "右侧可用（遮住左侧）";
            _rbRight.AutoSize = true;
            _rbRight.Location = new Point(24, 72);
            Controls.Add(_rbRight);

            Label lblPct = new Label();
            lblPct.Text = "可用宽度百分比：";
            lblPct.AutoSize = true;
            lblPct.Location = new Point(16, 106);
            Controls.Add(lblPct);

            _num = new NumericUpDown();
            _num.Minimum = 10;
            _num.Maximum = 90;
            _num.Increment = 1;
            _num.Value = Math.Max(10, Math.Min(90, percent));
            _num.Location = new Point(130, 102);
            _num.Width = 80;
            Controls.Add(_num);

            Label lblUnit = new Label();
            lblUnit.Text = "%（10-90，50=一半，70=70%）";
            lblUnit.AutoSize = true;
            lblUnit.Location = new Point(216, 106);
            Controls.Add(lblUnit);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(200, 132);
            ok.Size = new Size(74, 26);
            ok.Click += delegate { Apply(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(284, 132);
            cancel.Size = new Size(74, 26);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            if (side == "left") _rbRight.Checked = true;
            else _rbLeft.Checked = true;
        }

        private void Apply()
        {
            ResultSide = _rbRight.Checked ? "left" : "right";
            ResultPercent = (int)_num.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
    // =====================================================================
    internal sealed class WinRecord
    {
        public IntPtr Hwnd;
        public bool WasMaximized;
        public Rectangle Orig = Rectangle.Empty;
        public Rectangle Placed = Rectangle.Empty;
        public bool RestoreRect;
        public Native.WINDOWPLACEMENT OriginalPlacement;
        public bool HasPlacement;
    }
    // =====================================================================
    internal sealed class App
    {
        public static App Instance;

        private readonly MessageForm _form;
        private readonly string _pendingCommand;
        private readonly List<WinRecord> _records = new List<WinRecord>();

        private Config _cfg = new Config();
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miLeft;
        private ToolStripMenuItem _miAutoStart;
        private ToolStripMenuItem _layoutMenu;
        private OverlayForm _overlay;
        private HintForm _hint;
        private IntPtr _trayBridge = IntPtr.Zero;
        private Rectangle _trayBridgeOrig = Rectangle.Empty;
        private bool _taskbarHalf;
        private int _savedTaskbarAl = 1;
        private bool _taskbarAlChanged;
        private MonitorDesc _target;
        private bool _enabled;
        private bool _started;
        private bool _busy;
        private bool _appBarActive;
        private uint _appBarEdge;
        private bool _spiFallbackActive;
        private Rectangle _savedWorkArea = Rectangle.Empty;
        private Rectangle _expectedWorkArea = Rectangle.Empty;        private Rectangle _overlayRect = Rectangle.Empty;
        private Rectangle _leftWorkArea = Rectangle.Empty;
        private System.Windows.Forms.Timer _timer;

        private string _leftHotkeyText = "";
        private string _rightHotkeyText = "";
        private int _leftHotkeyId = 0x4C54;
        private int _rightHotkeyId = 0x4C55;
        private bool _leftHotkeyRegistered;
        private bool _rightHotkeyRegistered;
        private IntPtr _kbHook = IntPtr.Zero;
        private Native.LowLevelKeyboardProc _kbProc;
        private System.Windows.Forms.Timer _layoutTimer;
        private bool _layoutPending;
        private string _pendingSide = "right";
        private readonly StringBuilder _pendingDigits = new StringBuilder();
        private DateTime _pendingDeadline;
        private bool _pendingChanged;
        private bool _pendingCancel;
        private int _fullHotkeyId = 0x4C56;
        private string _fullHotkeyText = "";
        private bool _fullHotkeyRegistered;
        private DateTime _lastFullHotkeyAt = DateTime.MinValue;
        private int _enableRetryLeft;
        private IntPtr _winEventHook = IntPtr.Zero;
        private Native.WinEventDelegate _winEventProc;
        private Icon _iconOn;
        private Icon _iconOff;

        public App(MessageForm form, string pendingCommand)
        {
            _form = form;
            _pendingCommand = pendingCommand == null ? "" : pendingCommand;
        }

        // ---------- lifecycle ----------
        public void StartApp()
        {
            if (_started) return;
            _started = true;
            try
            {
                _cfg = Config.Load(Program.ConfigPath);
                Config.WriteDefault(Program.ConfigPath);
                _iconOn = IconFactory.Create(true);
                _iconOff = IconFactory.Create(false);
                CreateTray();
                RegisterLayoutHotkeys();
                InstallForegroundHook();
                RecoverFromCrash();
                Program.RecoverTaskbarStale();

                bool enableNow = _cfg.StartInLeftMode;
                if (_pendingCommand == "on")
                    enableNow = true;
                else if (_pendingCommand == "toggle")
                    enableNow = !_cfg.StartInLeftMode;

                if (enableNow)
                {
                    SetEnabled(true, _cfg.BalloonTips);
                    if (!_enabled)
                    {
                        _enableRetryLeft = 3;
                        Program.Log("启动时未能进入左侧模式，将自动重试");
                    }
                }
                else UpdateTray();

                if (_enabled)
                    ShowHint("已进入左侧模式（右侧黑屏）\n" + LayoutHotkeyHelp());
                else
                    ShowHint("LeftScreenMode 已启动\n" + LayoutHotkeyHelp());

                _layoutTimer = new System.Windows.Forms.Timer();
                _layoutTimer.Interval = 80;
                _layoutTimer.Tick += delegate { LayoutTimerTick(); };

                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 500;
                _timer.Tick += delegate { WatchTick(); };
                _timer.Start();
                Program.Log("启动完成: command=" + _pendingCommand + " enabled=" + _enabled + " appbar=" + (_appBarActive ? "1" : "0"));
                WriteRuntime();
            }
            catch (Exception ex)
            {
                Program.Log("启动失败: " + ex);
                try { Balloon("启动异常", ex.Message, ToolTipIcon.Error); }
                catch { }
            }
        }

        private void RecoverFromCrash()
        {
            try
            {
                SavedState st = Program.ReadState();
                if (st == null) return;
                if (st.Enabled && st.WorkArea != Rectangle.Empty)
                {
                    Native.SetWorkArea(st.WorkArea);
                    Program.Log("检测到上次未正常退出（SPI 回退模式），已尝试恢复工作区: " + Program.RectText(st.WorkArea));
                }
                if (st.TaskbarAl >= 0)
                {
                    Program.WriteTaskbarAl(st.TaskbarAl);
                    Program.BroadcastTaskbarSetting();
                    Program.Log("恢复上次异常退出的任务栏对齐: TaskbarAl=" + st.TaskbarAl);
                }
                Program.DeleteState();
            }
            catch (Exception ex)
            {
                Program.Log("恢复上次状态失败: " + ex.Message);
            }
        }

        private void CreateTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = _iconOff;
            _tray.Text = "LeftScreenMode";
            _tray.Visible = true;

            _menu = new ContextMenuStrip();
            _miLeft = new ToolStripMenuItem("左侧模式（右半屏黑）");
            _miLeft.Click += delegate { SetEnabled(!_enabled, true); };
            _menu.Items.Add(_miLeft);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miRefresh = new ToolStripMenuItem("重新应用当前模式");
            miRefresh.Click += delegate
            {
                if (_enabled)
                {
                    Disable();
                    SetEnabled(true, false);
                }
                else
                {
                    UpdateTray();
                }
            };
            _menu.Items.Add(miRefresh);

            _layoutMenu = new ToolStripMenuItem("显示区域");
            _menu.Items.Add(_layoutMenu);

            ToolStripMenuItem miConfig = new ToolStripMenuItem("打开配置文件");
            miConfig.Click += delegate { OpenFile(Program.ConfigPath); };
            _menu.Items.Add(miConfig);

            ToolStripMenuItem miFolder = new ToolStripMenuItem("打开程序文件夹");
            miFolder.Click += delegate { OpenFolder(Program.DataDir); };
            _menu.Items.Add(miFolder);

            _menu.Items.Add(new ToolStripSeparator());
            _miAutoStart = new ToolStripMenuItem("开机自动启动");
            _miAutoStart.Click += delegate
            {
                SetAutoStart(!AutoStartEnabled());
                UpdateTray();
            };
            _menu.Items.Add(_miAutoStart);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miQuit = new ToolStripMenuItem("退出");
            miQuit.Click += delegate { Quit(); };
            _menu.Items.Add(miQuit);

            _tray.ContextMenuStrip = _menu;
            _menu.Opening += delegate { RebuildLayoutMenu(); };
            _tray.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Toggle();
            };
        }

        public void HandleMessage(ref Message m)
        {
            if (Program.CmdMessage != 0 && m.Msg == Program.CmdMessage)
            {
                int code = m.WParam.ToInt32();
                m.Result = IntPtr.Zero;
                try { _form.BeginInvoke(new MethodInvoker(delegate { HandleCommand(code); })); }
                catch { }
                return;
            }
            if (m.Msg == Native.WM_HOTKEY)
            {
                int hid = m.WParam.ToInt32();
                if (hid == _leftHotkeyId)
                {
                    BeginLayoutPending(true);
                }
                else if (hid == _rightHotkeyId)
                {
                    BeginLayoutPending(false);
                }
                else if (hid == _fullHotkeyId)
                {
                    ApplyFullScreenHotkey();
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == Native.WM_DISPLAYCHANGE)
            {
                try { _form.BeginInvoke(new MethodInvoker(OnDisplayChanged)); }
                catch { }
                return;
            }
        }

        private void HandleCommand(int code)
        {
            switch (code)
            {
                case 0:
                    ShowHint("LeftScreenMode 已在运行\n" + LayoutHotkeyHelp());
                    break;
                case 1:
                    SetEnabled(true, true);
                    break;
                case 2:
                    SetEnabled(false, true);
                    break;
                case 3:
                    Toggle();
                    break;
                case 4:
                    Quit();
                    break;
                case 5:
                    if (_enabled)
                    {
                        Disable();
                        SetEnabled(true, false);
                    }
                    break;
                case 6:
                    ApplyPendingCommand();
                    break;
            }
        }

        private void Toggle()
        {
            SetEnabled(!_enabled, true);
        }

        private bool SetEnabled(bool enabled, bool showBalloon)
        {
            if (_busy) return _enabled;
            if (!enabled) _enableRetryLeft = 0;
            if (enabled && _cfg.LeftAreaPercent >= 100)
            {
                if (_enabled) Disable();
                ShowHint("可用区域为 100%：当前为全屏模式");
                return _enabled;
            }
            _busy = true;
            CloseHint();
            bool before = _enabled;
            try
            {
                if (enabled == _enabled)
                {
                    UpdateTray();
                    return _enabled;
                }
                if (enabled) Enable();
                else Disable();
                WriteRuntime();
            }
            catch (Exception ex)
            {
                Program.Log("切换失败: " + ex);
                try { Balloon("切换失败", ex.Message, ToolTipIcon.Error); }
                catch { }
            }
            finally
            {
                _busy = false;
                UpdateTray();
            }
            if (showBalloon && _cfg.BalloonTips && _enabled != before)
            {
                if (_enabled)
                {
                    Balloon("已切换到左侧模式",
                        "屏幕右侧已遮黑，窗口与最大化区域限制在左侧。\n按 " + _fullHotkeyText + " 可切回全屏。",
                        ToolTipIcon.Info);
                }
                else
                {
                    Balloon("已切换到全屏模式",
                        "已恢复正常显示。\n按 " + _leftHotkeyText + " / " + _rightHotkeyText + " + 数字可设置显示区域。",
                        ToolTipIcon.Info);
                }
            }
            return _enabled;
        }

        // ---------- enable / disable ----------
        private bool Enable()
        {
            Config cfg = _cfg;
            MonitorDesc mon = MonitorUtil.Select(cfg.Monitor);
            if (mon == null)
            {
                Program.Log("未找到目标显示器: " + cfg.Monitor);
                Balloon("未找到目标显示器", "请检查 LeftScreenMode.ini 中的 Monitor 设置。", ToolTipIcon.Warning);
                return false;
            }
            Rectangle mb = mon.Bounds;
            if (mb.Width < 200 || mb.Height < 100)
            {
                Program.Log("显示器尺寸异常: " + mb.Width + "x" + mb.Height);
                return false;
            }

            int usable = mb.Width * cfg.LeftAreaPercent / 100;
            if (usable < 100) usable = 100;
            if (usable > mb.Width - 100) usable = mb.Width - 100;
            if (usable <= 0 || usable >= mb.Width)
            {
                Program.Log("可用宽度异常: percent=" + cfg.LeftAreaPercent + " usable=" + usable + " width=" + mb.Width);
                return false;
            }

            bool coverLeft = string.Equals(cfg.CoverSide, "left", StringComparison.OrdinalIgnoreCase);
            Rectangle overlayRect;
            Rectangle leftArea;
            uint edge;
            if (coverLeft)
            {
                overlayRect = new Rectangle(mb.Left, mb.Top, mb.Width - usable, mb.Height);
                leftArea = new Rectangle(mb.Left + overlayRect.Width, mb.Top, usable, mb.Height);
                edge = Native.ABE_LEFT;
            }
            else
            {
                overlayRect = new Rectangle(mb.Left + usable, mb.Top, mb.Width - usable, mb.Height);
                leftArea = new Rectangle(mb.Left, mb.Top, usable, mb.Height);
                edge = Native.ABE_RIGHT;
            }

            _target = mon;
            _overlayRect = overlayRect;
            _appBarEdge = edge;
            _appBarActive = false;
            _spiFallbackActive = false;
            _expectedWorkArea = Rectangle.Empty;
            _leftWorkArea = leftArea;

            try
            {
                ShowOverlay(overlayRect);
            }
            catch (Exception ex)
            {
                Program.Log("创建遮罩失败: " + ex);
                _target = null;
                return false;
            }

            // 1) 首选：把遮罩窗口注册为 AppBar（Shell 停靠栏），Windows 会自动收缩工作区
            if (_overlay != null)
            {
                try
                {
                    _appBarActive = _overlay.EnsureAppBar(edge, overlayRect);
                }
                catch (Exception ex)
                {
                    Program.Log("AppBar 异常: " + ex.Message);
                    _appBarActive = false;
                }
            }

            // 2) 回退：SPI_SETWORKAREA（部分系统上可用；本机 Win11 24H2 实测被忽略）
            if (!_appBarActive && cfg.ConstrainWorkArea && mon.Primary)
            {
                Rectangle curWA = Native.GetWorkArea();
                Rectangle newWA;
                if (coverLeft) newWA = Rectangle.FromLTRB(leftArea.Left, curWA.Top, curWA.Right, curWA.Bottom);
                else newWA = Rectangle.FromLTRB(curWA.Left, curWA.Top, leftArea.Right, curWA.Bottom);
                if (newWA.Width > 100 && newWA.Height > 100)
                {
                    SavedState st = new SavedState();
                    st.Enabled = true;
                    st.WorkArea = curWA;
                    st.Monitor = mon.DeviceName;
                    Program.WriteState(st);
                    _savedWorkArea = curWA;
                    if (Native.SetWorkArea(newWA))
                    {
                        Rectangle after = Native.GetWorkArea();
                        if (after == newWA)
                        {
                            _spiFallbackActive = true;
                        }
                        else
                        {
                            Program.Log("SPI_SETWORKAREA 被系统忽略，仅使用 AppBar / 窗口级约束");
                            Program.DeleteState();
                        }
                    }
                    else
                    {
                        Program.DeleteState();
                    }
                }
            }

            // 3) 计算左侧可用工作区（以 AppBar 生效后的显示器工作区为准）
            MonitorDesc monNow = MonitorUtil.Select(cfg.Monitor);
            Rectangle mwa = monNow != null ? monNow.WorkArea : (mon.Primary ? Native.GetWorkArea() : mb);
            Rectangle lb = Rectangle.FromLTRB(leftArea.Left, mwa.Top, Math.Min(mwa.Right, leftArea.Right), mwa.Bottom);
            if (lb.Width < 100 || lb.Height < 50) lb = leftArea;
            _leftWorkArea = lb;
            _expectedWorkArea = lb;

            ApplyTaskbarAlignmentForLeftMode();
            ApplyHalfTaskbar();

            if (cfg.MoveWindows)
            {
                try { MoveWindowsIntoLeft(); }
                catch (Exception ex) { Program.Log("移动窗口失败: " + ex.Message); }
            }

            _enabled = true;
            Program.Log("启用左侧模式: monitor=" + mb.Width + "x" + mb.Height +
                        " overlay=" + Program.RectText(overlayRect) +
                        " appbar=" + (_appBarActive ? "1" : "0") +
                        " spiFallback=" + (_spiFallbackActive ? "1" : "0") +
                        " leftWork=" + Program.RectText(_leftWorkArea));
            return true;
        }
        private bool Disable()
        {
            _enabled = false;
            RestoreTaskbarAlignment();
            RestoreTaskbar();
            try
            {
                if (_overlay != null) _overlay.RemoveAppBar();
            }
            catch (Exception ex)
            {
                Program.Log("移除 AppBar 失败: " + ex.Message);
            }
            _appBarActive = false;
            if (_spiFallbackActive)
            {
                try { Native.SetWorkArea(_savedWorkArea); }
                catch { }
                _spiFallbackActive = false;
                Program.DeleteState();
            }
            _expectedWorkArea = Rectangle.Empty;
            Program.DeleteState();
            try { RestoreWindows(); }
            catch (Exception ex) { Program.Log("恢复窗口失败: " + ex.Message); }
            HideOverlay();
            _target = null;
            Program.Log("关闭左侧模式");
            return true;
        }
        private static IntPtr FindTaskbarBridge()
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            try
            {
                Native.EnumChildWindows(tray, delegate(IntPtr h, IntPtr l)
                {
                    if (Native.GetClassNameSafe(h) == "Windows.UI.Composition.DesktopWindowContentBridge")
                    {
                        result = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
            return result;
        }

        // 把 Windows 11 任务栏的 XAML 内容窗口缩到左半宽；任务栏内容（开始/图标/托盘/时钟）会跟着重排
        private void ApplyHalfTaskbar()
        {
            if (!_cfg.HalfTaskbar || _target == null) return;
            try
            {
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return;
                IntPtr bridge = FindTaskbarBridge();
                if (bridge == IntPtr.Zero) return;

                Rectangle trayRect = Native.GetWindowRectangle(tray);
                if (trayRect.Width < 100 || trayRect.Height < 10) return;

                if (_trayBridge != bridge)
                {
                    _trayBridge = bridge;
                    _trayBridgeOrig = Native.GetWindowRectangle(bridge);
                }

                int wantWidth = _leftWorkArea.Width;
                int relX = _leftWorkArea.Left - trayRect.Left;
                if (relX < 0) relX = 0;
                if (relX + wantWidth > trayRect.Width) wantWidth = trayRect.Width - relX;
                if (wantWidth < 200) return;
                int h = trayRect.Height;

                Rectangle cur = Native.GetWindowRectangle(bridge);
                int wantAbsX = trayRect.Left + relX;
                if (cur.Left != wantAbsX || cur.Top != trayRect.Top || cur.Width != wantWidth || cur.Height != h)
                {
                    Native.SetWindowPos(bridge, IntPtr.Zero, relX, 0, wantWidth, h,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                }

                try
                {
                    IntPtr rgn = Native.CreateRectRgn(relX, 0, relX + wantWidth, h);
                    if (rgn != IntPtr.Zero) Native.SetWindowRgn(tray, rgn, true);
                }
                catch
                {
                }

                _taskbarHalf = true;
            }
            catch (Exception ex)
            {
                Program.Log("任务栏半边失败: " + ex.Message);
            }
        }

        private void RestoreTaskbar()
        {
            try
            {
                if (_trayBridge != IntPtr.Zero && Native.IsWindow(_trayBridge) && _trayBridgeOrig.Width > 100)
                {
                    IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                    if (tray != IntPtr.Zero)
                    {
                        Rectangle trayRect = Native.GetWindowRectangle(tray);
                        Native.SetWindowPos(_trayBridge, IntPtr.Zero,
                            _trayBridgeOrig.Left - trayRect.Left, _trayBridgeOrig.Top - trayRect.Top,
                            _trayBridgeOrig.Width, _trayBridgeOrig.Height,
                            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                        try { Native.SetWindowRgn(tray, IntPtr.Zero, true); }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log("恢复任务栏失败: " + ex.Message);
            }
            _trayBridge = IntPtr.Zero;
            _trayBridgeOrig = Rectangle.Empty;
            _taskbarHalf = false;
        }
        private void ApplyTaskbarAlignmentForLeftMode()
        {
            if (!_cfg.LeftAlignTaskbarIcons) return;
            try
            {
                int cur = Program.ReadTaskbarAl();
                _savedTaskbarAl = cur;
                if (cur == 0)
                {
                    _taskbarAlChanged = false;
                    return;
                }
                SavedState st = Program.ReadState();
                if (st == null) st = new SavedState();
                st.Enabled = true;
                st.TaskbarAl = cur;
                if (_target != null) st.Monitor = _target.DeviceName;
                Program.WriteState(st);
                if (Program.WriteTaskbarAl(0))
                {
                    _taskbarAlChanged = true;
                    Program.BroadcastTaskbarSetting();
                    Program.Log("任务栏图标改为左对齐（原 TaskbarAl=" + cur + "）");
                }
            }
            catch (Exception ex)
            {
                Program.Log("设置任务栏对齐失败: " + ex.Message);
            }
        }

        private void RestoreTaskbarAlignment()
        {
            if (!_taskbarAlChanged) return;
            try
            {
                Program.WriteTaskbarAl(_savedTaskbarAl);
                Program.BroadcastTaskbarSetting();
                Program.Log("恢复任务栏图标对齐 TaskbarAl=" + _savedTaskbarAl);
            }
            catch (Exception ex)
            {
                Program.Log("恢复任务栏对齐失败: " + ex.Message);
            }
            finally
            {
                _taskbarAlChanged = false;
            }
        }
        private void ShowOverlay(Rectangle rect)
        {
            if (_overlay == null || _overlay.IsDisposed)
            {
                Color color = Program.ParseColor(_cfg.OverlayColor, Color.Black);
                _overlay = new OverlayForm(_cfg.OverlayOpacity, color);
            }
            _overlay.ApplyBounds(rect);
        }

        private void HideOverlay()
        {
            if (_overlay == null) return;
            try
            {
                _overlay.Hide();
                _overlay.Close();
                _overlay.Dispose();
            }
            catch
            {
            }
            _overlay = null;
        }

        // ---------- window moving ----------
        private void MoveWindowsIntoLeft()
        {
            _records.Clear();
            if (_target == null) return;
            Rectangle lb = _leftWorkArea;
            Rectangle mb = _target.Bounds;
            int myPid = Process.GetCurrentProcess().Id;
            IntPtr overlayHandle = (_overlay != null && !_overlay.IsDisposed) ? _overlay.Handle : IntPtr.Zero;
            IntPtr fg = Native.GetForegroundWindow();

            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lp)
            {
                try { ProcessOneWindow(hwnd, myPid, overlayHandle, lb, mb); }
                catch { }
                return true;
            }, IntPtr.Zero);

            try
            {
                if (fg != IntPtr.Zero && Native.IsWindow(fg)) Native.SetForegroundWindow(fg);
            }
            catch
            {
            }
        }

        private static bool CapturePlacement(IntPtr hwnd, out Native.WINDOWPLACEMENT wp)
        {
            wp = new Native.WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
            try
            {
                return Native.GetWindowPlacement(hwnd, ref wp);
            }
            catch
            {
                return false;
            }
        }

        private static bool NearRectangle(Rectangle a, Rectangle b, int tolerance)
        {
            return Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
                   Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;
        }

        private void ProcessOneWindow(IntPtr hwnd, int myPid, IntPtr overlayHandle, Rectangle lb, Rectangle mb)
        {
            if (hwnd == overlayHandle) return;
            if (!Native.IsWindowVisible(hwnd)) return;
            if (Native.IsIconic(hwnd)) return;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)myPid) return;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return;
            if (IsExcludedWindowClass(Native.GetClassNameSafe(hwnd))) return;
            if (Native.GetWindowTextLength(hwnd) == 0) return;
            try
            {
                int cloaked;
                if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                    return;
            }
            catch
            {
            }
            if (_target == null) return;
            if (Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) != _target.Handle) return;

            Rectangle r = Native.GetWindowRectangle(hwnd);
            if (r.Width <= 0 || r.Height <= 0) return;

            if (Native.IsZoomed(hwnd))
            {
                WinRecord rec = new WinRecord();
                rec.Hwnd = hwnd;
                rec.WasMaximized = true;
                Native.WINDOWPLACEMENT wp;
                rec.HasPlacement = CapturePlacement(hwnd, out wp);
                rec.OriginalPlacement = wp;
                _records.Add(rec);
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
                Native.SetWindowPos(hwnd, IntPtr.Zero, lb.X, lb.Y, lb.Width, lb.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
                return;
            }

            bool full = r.Left <= mb.Left + 2 && r.Top <= mb.Top + 2 &&
                        r.Right >= mb.Right - 2 && r.Bottom >= mb.Bottom - 2;
            if (full)
            {
                if (!_cfg.ResizeFullscreenWindows) return;
                WinRecord rec = new WinRecord();
                rec.Hwnd = hwnd;
                rec.WasMaximized = false;
                rec.Orig = r;
                rec.Placed = lb;
                rec.RestoreRect = true;
                Native.WINDOWPLACEMENT wp2;
                rec.HasPlacement = CapturePlacement(hwnd, out wp2);
                rec.OriginalPlacement = wp2;
                _records.Add(rec);
                Native.SetWindowPos(hwnd, IntPtr.Zero, lb.X, lb.Y, lb.Width, lb.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                return;
            }

            if (r.Right <= lb.Right && r.Left >= lb.Left && r.Top >= lb.Top && r.Bottom <= lb.Bottom)
                return;

            int w = Math.Min(r.Width, lb.Width);
            int h = Math.Min(r.Height, lb.Height);
            int x = r.Left;
            int y = r.Top;
            if (x + w > lb.Right) x = lb.Right - w;
            if (x < lb.Left) x = lb.Left;
            if (y < lb.Top) y = lb.Top;
            if (y + h > lb.Bottom) y = lb.Bottom - h;
            if (x == r.Left && y == r.Top && w == r.Width && h == r.Height) return;

            WinRecord rec2 = new WinRecord();
            rec2.Hwnd = hwnd;
            rec2.WasMaximized = false;
            rec2.Orig = r;
            rec2.Placed = new Rectangle(x, y, w, h);
            rec2.RestoreRect = true;
            Native.WINDOWPLACEMENT wp3;
            rec2.HasPlacement = CapturePlacement(hwnd, out wp3);
            rec2.OriginalPlacement = wp3;
            _records.Add(rec2);
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        private static bool IsExcludedWindowClass(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return true;
            string[] excl = new string[]
            {
                "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
                "Windows.UI.Core.CoreWindow", "Windows.UI.Input.InputSite.WindowClass",
                "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
                "DV2ControlHost", "MsgrIMEWindowClass", "SysShadow", "ForegroundStaging"
            };
            foreach (string e in excl)
            {
                if (string.Equals(cls, e, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void RestoreWindows()
        {
            IntPtr fg = Native.GetForegroundWindow();
            List<WinRecord> recs = new List<WinRecord>(_records);
            _records.Clear();

            List<IntPtr> needMax = new List<IntPtr>();
            List<WinRecord> needRect = new List<WinRecord>();

            foreach (WinRecord rec in recs)
            {
                if (!Native.IsWindow(rec.Hwnd)) continue;
                try
                {
                    if (Native.IsIconic(rec.Hwnd)) continue;
                    if (rec.WasMaximized)
                    {
                        Native.ShowWindow(rec.Hwnd, Native.SW_RESTORE);
                        Native.ShowWindow(rec.Hwnd, Native.SW_MAXIMIZE);
                        needMax.Add(rec.Hwnd);
                    }
                    else if (rec.RestoreRect && rec.Orig != Rectangle.Empty)
                    {
                        Rectangle cur = Native.GetWindowRectangle(rec.Hwnd);
                        if (NearRectangle(cur, rec.Placed, 16))
                            needRect.Add(rec);
                    }
                }
                catch
                {
                }
            }

            // 启用期间才最大化的窗口：工作区恢复后也应重新铺满全屏
            if (_target != null)
            {
                IntPtr targetH = _target.Handle;
                int myPid = Process.GetCurrentProcess().Id;
                Native.EnumWindows(delegate(IntPtr hwnd, IntPtr lp)
                {
                    try
                    {
                        if (!Native.IsWindowVisible(hwnd) || !Native.IsZoomed(hwnd)) return true;
                        uint pid;
                        Native.GetWindowThreadProcessId(hwnd, out pid);
                        if (pid == (uint)myPid) return true;
                        if (Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) != targetH) return true;
                        if (!needMax.Contains(hwnd)) needMax.Add(hwnd);
                    }
                    catch
                    {
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // 普通窗口：用 SetWindowPlacement 精确还原原始位置
            foreach (WinRecord rec in needRect)
            {
                try
                {
                    if (!Native.IsWindow(rec.Hwnd)) continue;
                    if (rec.HasPlacement)
                    {
                        Native.WINDOWPLACEMENT wp = rec.OriginalPlacement;
                        wp.showCmd = Native.SW_SHOWNORMAL;
                        Native.SetWindowPlacement(rec.Hwnd, ref wp);
                    }
                    else
                    {
                        Native.SetWindowPos(rec.Hwnd, IntPtr.Zero, rec.Orig.X, rec.Orig.Y, rec.Orig.Width, rec.Orig.Height,
                            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    }
                }
                catch
                {
                }
            }

            // SW_MAXIMIZE 是异步的：稍等后校验，失败则补一次（必要时用 SetWindowPlacement）
            if (needMax.Count > 0)
            {
                System.Threading.Thread.Sleep(160);
                foreach (IntPtr h in needMax)
                {
                    try
                    {
                        if (!Native.IsWindow(h) || Native.IsIconic(h)) continue;
                        if (!Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                    }
                    catch
                    {
                    }
                }
                System.Threading.Thread.Sleep(130);
                foreach (IntPtr h in needMax)
                {
                    try
                    {
                        if (!Native.IsWindow(h) || Native.IsIconic(h)) continue;
                        if (Native.IsZoomed(h)) continue;
                        Native.WINDOWPLACEMENT wp;
                        if (CapturePlacement(h, out wp))
                        {
                            wp.showCmd = Native.SW_SHOWMAXIMIZED;
                            Native.SetWindowPlacement(h, ref wp);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                if (fg != IntPtr.Zero && Native.IsWindow(fg)) Native.SetForegroundWindow(fg);
            }
            catch
            {
            }
        }
        // ---------- display change / watchdog ----------
        private void OnDisplayChanged()
        {
            if (_busy) return;
            bool wasEnabled = _enabled;
            _busy = true;
            try
            {
                if (wasEnabled)
                {
                    Disable();
                    if (MonitorUtil.Select(_cfg.Monitor) != null)
                        Enable();
                }
            }
            catch (Exception ex)
            {
                Program.Log("显示配置变化处理失败: " + ex);
            }
            finally
            {
                _busy = false;
                UpdateTray();
                WriteRuntime();
            }
        }

        private void WatchTick()
        {
            if (_enableRetryLeft > 0 && !_enabled && _started)
            {
                _enableRetryLeft--;
                Program.Log("重试进入左侧模式（剩余 " + _enableRetryLeft + " 次）");
                SetEnabled(true, false);
                if (_enabled) _enableRetryLeft = 0;
            }
            if (_busy || !_enabled || _overlay == null || _overlay.IsDisposed) return;
            FixShellFlyoutPosition();
            try
            {
                Rectangle cur = Native.GetWindowRectangle(_overlay.Handle);
                if (cur != _overlayRect) _overlay.ApplyBounds(_overlayRect);

                MonitorDesc mon = MonitorUtil.Select(_cfg.Monitor);
                if (mon == null || _target == null || mon.DeviceName != _target.DeviceName)
                {
                    Program.Log("目标显示器变化，重新应用");
                    OnDisplayChanged();
                    return;
                }

                if (_cfg.HalfTaskbar) ApplyHalfTaskbar();

                if (_appBarActive)
                {
                    // 任务栏自动隐藏/弹出会让工作区底部在 1080/1032 之间变化，只检查右侧是否被"放开"
                    Rectangle wa = mon.WorkArea;
                    if (!_overlay.AppBarRegistered || wa.Right > _leftWorkArea.Right + 2 || wa.Left < _leftWorkArea.Left - 2)
                        _overlay.EnsureAppBar(_appBarEdge, _overlayRect);
                }
                else if (_spiFallbackActive)
                {
                    if (Native.GetWorkArea() != _expectedWorkArea)
                        Native.SetWorkArea(_expectedWorkArea);
                }
            }
            catch (Exception ex)
            {
                Program.Log("定时检查异常: " + ex.Message);
            }
        }
        // ---------- UI helpers ----------
        private void UpdateTray()
        {
            if (_tray == null) return;
            _miLeft.Checked = _enabled;
            _tray.Icon = _enabled ? _iconOn : _iconOff;
            string layout = (_cfg.CoverSide == "left" ? "右" : "左") + _cfg.LeftAreaPercent + "%";
            string text = _enabled
                ? ("LeftScreenMode · 左侧模式 " + layout)
                : "LeftScreenMode · 全屏模式";
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
            if (_miAutoStart != null) _miAutoStart.Checked = AutoStartEnabled();
        }

        private void ShowHint(string text)
        {
            try
            {
                CloseHint();
                Rectangle area;
                MonitorDesc mon = MonitorUtil.Select(_cfg.Monitor);
                area = mon != null ? mon.Bounds : Screen.PrimaryScreen.Bounds;
                if (_enabled && _leftWorkArea.Width > 200) area = _leftWorkArea;
                int w = Math.Min(620, area.Width - 48);
                if (w < 260) w = Math.Min(area.Width, 620);
                int h = 108;
                int x = area.Left + 24;
                int y = area.Top + 24;
                if (x + w > area.Right - 16) x = area.Right - w - 16;
                if (y + h > area.Bottom - 16) y = area.Bottom - h - 16;
                if (x < area.Left) x = area.Left;
                if (y < area.Top) y = area.Top;
                _hint = new HintForm(text, new Rectangle(x, y, w, h));
                _hint.Show();
            }
            catch (Exception ex)
            {
                Program.Log("显示提示窗失败: " + ex.Message);
            }
        }

        private void CloseHint()
        {
            if (_hint == null) return;
            try
            {
                _hint.StopTimer();
                _hint.Close();
                _hint.Dispose();
            }
            catch
            {
            }
            _hint = null;
        }
        private void RebuildLayoutMenu()
        {
            if (_layoutMenu == null) return;
            _layoutMenu.DropDownItems.Clear();
            string sideName = _cfg.CoverSide == "left" ? "右侧" : "左侧";
            _layoutMenu.Text = "显示区域（当前：" + sideName + "可用 " + _cfg.LeftAreaPercent + "%）";
            int[] presets = _cfg.Presets;
            if (presets == null || presets.Length == 0) presets = new int[] { 50 };
            ToolStripMenuItem leftMenu = new ToolStripMenuItem("左侧可用（遮住右侧）");
            ToolStripMenuItem rightMenu = new ToolStripMenuItem("右侧可用（遮住左侧）");
            for (int i = 0; i < presets.Length; i++)
            {
                int pct = presets[i];
                ToolStripMenuItem li = new ToolStripMenuItem(pct + "%");
                li.Checked = _cfg.CoverSide == "right" && _cfg.LeftAreaPercent == pct;
                li.Click += delegate { SetPreset("right", pct); };
                leftMenu.DropDownItems.Add(li);
                ToolStripMenuItem ri = new ToolStripMenuItem(pct + "%");
                ri.Checked = _cfg.CoverSide == "left" && _cfg.LeftAreaPercent == pct;
                ri.Click += delegate { SetPreset("left", pct); };
                rightMenu.DropDownItems.Add(ri);
            }
            _layoutMenu.DropDownItems.Add(leftMenu);
            _layoutMenu.DropDownItems.Add(rightMenu);
            _layoutMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem custom = new ToolStripMenuItem("自定义百分比…");
            custom.Click += delegate { ShowLayoutDialog(); };
            _layoutMenu.DropDownItems.Add(custom);
        }

        private void ShowLayoutDialog()
        {
            try
            {
                using (LayoutDialog dlg = new LayoutDialog(_cfg.CoverSide, _cfg.LeftAreaPercent))
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                        SetPreset(dlg.ResultSide, dlg.ResultPercent);
                }
            }
            catch (Exception ex)
            {
                Program.Log("显示自定义对话框失败: " + ex.Message);
            }
        }

        private void SetPreset(string side, int percent)
        {
            if (percent < 10) percent = 10;
            if (percent > 100) percent = 100;
            side = side == "left" ? "left" : "right";
            if (percent >= 100)
            {
                _cfg.LeftAreaPercent = 100;
                PersistCurrentPreset();
                if (_enabled) SetEnabled(false, false);
                ShowHint("可用区域 100%：已切换为全屏模式");
                UpdateTray();
                WriteRuntime();
                return;
            }
            if (side == _cfg.CoverSide && percent == _cfg.LeftAreaPercent && _enabled)
            {
                UpdateTray();
                return;
            }
            if (_busy) return;
            CloseHint();
            _cfg.CoverSide = side;
            _cfg.LeftAreaPercent = percent;
            PersistCurrentPreset();
            if (_enabled) SetEnabled(false, false);
            SetEnabled(true, false);
            if (_cfg.BalloonTips)
                ShowHint("显示区域：" + (side == "left" ? "右侧" : "左侧") + "可用 " + percent + "%\n按 " + _fullHotkeyText + " 切换回全屏");
            UpdateTray();
            WriteRuntime();
        }

        private void PersistCurrentPreset()
        {
            try
            {
                Dictionary<string, string> vals = new Dictionary<string, string>();
                vals["LeftAreaPercent"] = _cfg.LeftAreaPercent.ToString();
                vals["CoverSide"] = _cfg.CoverSide;
                Config.UpdateKeys(Program.ConfigPath, vals);
            }
            catch (Exception ex)
            {
                Program.Log("保存显示区域失败: " + ex.Message);
            }
        }

        private void ApplyPendingCommand()
        {
            try
            {
                string path = Program.CmdFilePath;
                if (!File.Exists(path)) return;
                Dictionary<string, string> kv = Program.ParseKeyValues(File.ReadAllLines(path, Encoding.UTF8));
                try { File.Delete(path); }
                catch { }
                int percent = Program.GetInt(kv, "percent", _cfg.LeftAreaPercent);
                string side = Program.GetString(kv, "side", _cfg.CoverSide);
                SetPreset(side, percent);
            }
            catch (Exception ex)
            {
                Program.Log("应用预设命令失败: " + ex.Message);
            }
        }
        private void RegisterLayoutHotkeys()
        {
            int mods;
            int vk;
            string norm;
            if (Config.TryParseHotkey(_cfg.HotkeyLeftLayout, out mods, out vk, out norm)) _leftHotkeyText = norm;
            else _leftHotkeyText = _cfg.HotkeyLeftLayout;
            if (Config.TryParseHotkey(_cfg.HotkeyRightLayout, out mods, out vk, out norm)) _rightHotkeyText = norm;
            else _rightHotkeyText = _cfg.HotkeyRightLayout;
            if (Config.TryParseHotkey(_cfg.HotkeyFullScreen, out mods, out vk, out norm)) _fullHotkeyText = norm;
            else _fullHotkeyText = _cfg.HotkeyFullScreen;

            if (_cfg.UseKeyboardHook)
            {
                InstallKeyboardHook();
                if (_kbHook != IntPtr.Zero)
                {
                    Program.Log("已启用键盘钩子：比例热键 " + _leftHotkeyText + " / " + _rightHotkeyText + " + 数字；全屏热键 " + _fullHotkeyText);
                    return;
                }
                Program.Log("键盘钩子安装失败，改用 RegisterHotKey 注册比例热键");
            }

            _leftHotkeyText = RegisterSingleHotkey(_cfg.HotkeyLeftLayout,
                new string[] { "Win+Alt+L", "Win+Ctrl+Alt+L", "Win+Shift+L", "Ctrl+Alt+Shift+L" }, _leftHotkeyId);
            _leftHotkeyRegistered = _leftHotkeyText.Length > 0;
            _rightHotkeyText = RegisterSingleHotkey(_cfg.HotkeyRightLayout,
                new string[] { "Win+Ctrl+Alt+R", "Win+Alt+F10", "Ctrl+Alt+Shift+R" }, _rightHotkeyId);
            _rightHotkeyRegistered = _rightHotkeyText.Length > 0;
            _fullHotkeyText = RegisterSingleHotkey(_cfg.HotkeyFullScreen,
                new string[] { "Win+Ctrl+F12", "Ctrl+Alt+0", "Win+Ctrl+F11" }, _fullHotkeyId);
            _fullHotkeyRegistered = _fullHotkeyText.Length > 0;
            if (_leftHotkeyRegistered || _rightHotkeyRegistered || _fullHotkeyRegistered)
                Balloon("比例热键已启用",
                    "左侧：" + _leftHotkeyText + " + 数字\n右侧：" + _rightHotkeyText + " + 数字\n全屏：" + _fullHotkeyText,
                    ToolTipIcon.Info);
        }
        private string RegisterSingleHotkey(string primary, string[] fallbacks, int id)
        {
            int mods;
            int vk;
            string norm;
            if (Config.TryParseHotkey(primary, out mods, out vk, out norm))
            {
                if (Native.RegisterHotKey(_form.Handle, id, mods | Native.MOD_NOREPEAT, vk))
                    return norm;
                Program.Log("比例热键注册失败: " + norm + "，尝试备用组合");
            }
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++)
                {
                    if (!Config.TryParseHotkey(fallbacks[i], out mods, out vk, out norm)) continue;
                    if (Native.RegisterHotKey(_form.Handle, id, mods | Native.MOD_NOREPEAT, vk))
                    {
                        Program.Log("已改用备用比例热键: " + norm);
                        return norm;
                    }
                }
            }
            return "";
        }

        private void InstallKeyboardHook()
        {
            if (_kbHook != IntPtr.Zero) return;
            try
            {
                _kbProc = new Native.LowLevelKeyboardProc(KeyboardHookProc);
                _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, Native.GetModuleHandle(null), 0);
                if (_kbHook == IntPtr.Zero)
                    Program.Log("安装键盘钩子失败: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Program.Log("安装键盘钩子异常: " + ex.Message);
            }
        }

        private void UninstallKeyboardHook()
        {
            if (_kbHook == IntPtr.Zero) return;
            try { Native.UnhookWindowsHookEx(_kbHook); }
            catch { }
            _kbHook = IntPtr.Zero;
            _kbProc = null;
        }

        private static bool ComboMatches(int mods, int vk, int pressedVk)
        {
            if (pressedVk != vk) return false;
            bool needCtrl = (mods & Native.MOD_CONTROL) != 0;
            bool needAlt = (mods & Native.MOD_ALT) != 0;
            bool needShift = (mods & Native.MOD_SHIFT) != 0;
            bool needWin = (mods & Native.MOD_WIN) != 0;
            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool win = ((Native.GetAsyncKeyState(0x5B) & 0x8000) != 0) || ((Native.GetAsyncKeyState(0x5C) & 0x8000) != 0);
            return ctrl == needCtrl && alt == needAlt && shift == needShift && win == needWin;
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        int vk = (int)k.vkCode;
                        int lmods;
                        int lvk;
                        string lnorm;
                        if (_layoutPending)
                        {
                            if (vk == Native.VK_ESCAPE)
                            {
                                _pendingCancel = true;
                                return (IntPtr)1;
                            }
                            int digit = -1;
                            if (vk >= 0x30 && vk <= 0x39) digit = vk - 0x30;
                            else if (vk >= 0x60 && vk <= 0x69) digit = vk - 0x60;
                            if (digit >= 0)
                            {
                                if (_pendingDigits.Length < 4) _pendingDigits.Append((char)('0' + digit));
                                _pendingDeadline = DateTime.Now.AddMilliseconds(_cfg.LayoutDigitTimeoutMs);
                                _pendingChanged = true;
                                return (IntPtr)1;
                            }
                            if (Config.TryParseHotkey(_cfg.HotkeyLeftLayout, out lmods, out lvk, out lnorm) && ComboMatches(lmods, lvk, vk))
                            {
                                try { _form.BeginInvoke(new MethodInvoker(delegate { BeginLayoutPending(true); })); }
                                catch { }
                                return (IntPtr)1;
                            }
                            if (Config.TryParseHotkey(_cfg.HotkeyRightLayout, out lmods, out lvk, out lnorm) && ComboMatches(lmods, lvk, vk))
                            {
                                try { _form.BeginInvoke(new MethodInvoker(delegate { BeginLayoutPending(false); })); }
                                catch { }
                                return (IntPtr)1;
                            }
                        }
                        else
                        {
                            if (Config.TryParseHotkey(_cfg.HotkeyLeftLayout, out lmods, out lvk, out lnorm) && ComboMatches(lmods, lvk, vk))
                            {
                                try { _form.BeginInvoke(new MethodInvoker(delegate { BeginLayoutPending(true); })); }
                                catch { }
                                return (IntPtr)1;
                            }
                            if (Config.TryParseHotkey(_cfg.HotkeyRightLayout, out lmods, out lvk, out lnorm) && ComboMatches(lmods, lvk, vk))
                            {
                                try { _form.BeginInvoke(new MethodInvoker(delegate { BeginLayoutPending(false); })); }
                                catch { }
                                return (IntPtr)1;
                            }
                            if (Config.TryParseHotkey(_cfg.HotkeyFullScreen, out lmods, out lvk, out lnorm) && ComboMatches(lmods, lvk, vk))
                            {
                                try { _form.BeginInvoke(new MethodInvoker(delegate { ApplyFullScreenHotkey(); })); }
                                catch { }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }
        private void BeginLayoutPending(bool leftUsable)
        {
            try
            {
                CloseHint();
                _pendingSide = leftUsable ? "right" : "left";
                _pendingDigits.Length = 0;
                _pendingCancel = false;
                _pendingChanged = false;
                _layoutPending = true;
                _pendingDeadline = DateTime.Now.AddMilliseconds(_cfg.LayoutDigitTimeoutMs);
                if (_layoutTimer != null) _layoutTimer.Start();
                UpdatePendingHint();
            }
            catch (Exception ex)
            {
                Program.Log("开始比例热键失败: " + ex.Message);
            }
        }

        private int ComputePendingPercent()
        {
            if (_pendingDigits.Length == 0) return -1;
            if (_pendingDigits.Length == 1)
            {
                int d = _pendingDigits[0] - '0';
                return d == 0 ? 100 : d * 10;
            }
            int n = 0;
            for (int i = 0; i < _pendingDigits.Length; i++) n = n * 10 + (_pendingDigits[i] - '0');
            if (n > 100) n = 100;
            if (n < 10) n = 10;
            return n;
        }

        private void UpdatePendingHint()
        {
            string sideName = _pendingSide == "left" ? "右侧" : "左侧";
            string t;
            if (_pendingDigits.Length > 0)
                t = sideName + "可用：" + ComputePendingPercent() + "%\n继续按数字修正，等待生效；Esc 取消";
            else
                t = sideName + "可用：按数字选择（7=70%，0=100%）\n不按 = 50%；Esc 取消";
            ShowHint(t);
        }

        private void LayoutTimerTick()
        {
            if (!_layoutPending)
            {
                if (_layoutTimer != null) _layoutTimer.Stop();
                return;
            }
            if (_pendingCancel)
            {
                CancelLayoutPending(true);
                return;
            }
            if (_pendingChanged)
            {
                _pendingChanged = false;
                UpdatePendingHint();
            }
            if (DateTime.Now >= _pendingDeadline || _pendingDigits.Length >= 3)
                ApplyPendingLayout();
        }

        private void ApplyPendingLayout()
        {
            int percent = ComputePendingPercent();
            if (percent < 0) percent = 50;
            string side = _pendingSide;
            CancelLayoutPending(false);
            SetPreset(side, percent);
        }

        private void CancelLayoutPending(bool closeHint)
        {
            _layoutPending = false;
            _pendingDigits.Length = 0;
            _pendingCancel = false;
            _pendingChanged = false;
            if (_layoutTimer != null) _layoutTimer.Stop();
            if (closeHint) CloseHint();
        }

        private void ApplyFullScreenHotkey()
        {
            if ((DateTime.Now - _lastFullHotkeyAt).TotalMilliseconds < 500) return;
            _lastFullHotkeyAt = DateTime.Now;
            CancelLayoutPending(false);
            if (_enabled) SetEnabled(false, true);
            else UpdateTray();
            CloseHint();
            ShowHint("已切回全屏模式\n按 " + _leftHotkeyText + " / " + _rightHotkeyText + " + 数字设置显示区域");
        }

        private string LayoutHotkeyHelp()
        {
            return "比例热键：" + _leftHotkeyText + " / " + _rightHotkeyText + " + 数字（如 7=70%）；" + _fullHotkeyText + " = 全屏";
        }
        private void InstallForegroundHook()
        {
            if (!_cfg.RepositionFlyouts) return;
            try
            {
                _winEventProc = new Native.WinEventDelegate(WinEventProc);
                _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
                if (_winEventHook == IntPtr.Zero)
                    Program.Log("安装前台窗口事件钩子失败: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Program.Log("安装前台窗口事件钩子异常: " + ex.Message);
            }
        }

        private void UninstallForegroundHook()
        {
            if (_winEventHook == IntPtr.Zero) return;
            try { Native.UnhookWinEvent(_winEventHook); }
            catch { }
            _winEventHook = IntPtr.Zero;
            _winEventProc = null;
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try { FixShellFlyoutPosition(); }
            catch { }
        }

        private void FixShellFlyoutPosition()
        {
            if (!_cfg.RepositionFlyouts || !_enabled) return;
            if (_leftWorkArea == Rectangle.Empty) return;
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return;
                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return;
                int w = r.Right - r.Left;
                int ht = r.Bottom - r.Top;
                if (w < 220 || w > 760 || ht < 380) return;
                string cls = Native.GetClassNameSafe(h);
                if (cls != "ControlCenterWindow" && cls != "Windows.UI.Core.CoreWindow") return;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                string proc = "";
                try { proc = Process.GetProcessById((int)pid).ProcessName; }
                catch { return; }
                if (proc != "ShellHost" && proc != "ShellExperienceHost") return;
                Rectangle usable = _leftWorkArea;
                if (r.Left >= usable.Left - 4 && r.Right <= usable.Right + 4) return;
                int x = usable.Right - w;
                if (x < usable.Left + 4) x = usable.Left + 4;
                if (Native.SetWindowPos(h, IntPtr.Zero, x, r.Top, w, ht, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE))
                    Program.Log("系统浮出面板已移入可用区: " + proc + " " + cls + " x=" + x + " w=" + w);
            }
            catch
            {
            }
        }
        private void Balloon(string title, string text, ToolTipIcon icon)
        {
            if (_tray == null || !_tray.Visible) return;
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = icon;
                _tray.ShowBalloonTip(2000);
            }
            catch
            {
            }
        }

        private static void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Program.Log("文件不存在: " + path);
                    return;
                }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\""));
            }
            catch (Exception ex)
            {
                Program.Log("打开文件失败: " + ex.Message);
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch
            {
            }
        }

        // ---------- autostart ----------
        private static string StartupShortcutPath()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startup, "LeftScreenMode.lnk");
        }

        private static bool AutoStartEnabled()
        {
            try { return File.Exists(StartupShortcutPath()); }
            catch { return false; }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string path = StartupShortcutPath();
                if (enable)
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null)
                    {
                        Balloon("创建快捷方式失败", "系统未提供 WScript.Shell 组件。", ToolTipIcon.Error);
                        return;
                    }
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    Type scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Program.ExeDir });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "LeftScreenMode 托盘程序" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                }
                else
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                Program.Log("开机自启: " + (enable ? "启用" : "关闭"));
            }
            catch (Exception ex)
            {
                Program.Log("设置开机启动失败: " + ex.Message);
                Balloon("设置开机启动失败", ex.Message, ToolTipIcon.Error);
            }
        }

        // ---------- runtime info / quit ----------
        private void WriteRuntime()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("version=" + Program.Version);
                sb.AppendLine("running=1");
                sb.AppendLine("enabled=" + (_enabled ? "1" : "0"));
                sb.AppendLine("mode=" + (_enabled ? "left" : "full"));
                if (_target != null)
                {
                    sb.AppendLine("monitor=" + _target.DeviceName);
                    sb.AppendLine("monitorId=" + _target.MonitorId);
                    sb.AppendLine("monitorBounds=" + Program.RectText(_target.Bounds));
                }
                if (_enabled)
                {
                    sb.AppendLine("appBar=" + (_appBarActive ? "1" : "0"));
                    sb.AppendLine("appBarEdge=" + _appBarEdge);
                    sb.AppendLine("spiFallback=" + (_spiFallbackActive ? "1" : "0"));
                    sb.AppendLine("taskbarHalf=" + (_taskbarHalf ? "1" : "0"));
                    sb.AppendLine("taskbarAl=" + Program.ReadTaskbarAl());
                    sb.AppendLine("leftAlignTaskbarIcons=" + (_taskbarAlChanged ? "1" : "0"));
                    sb.AppendLine("layout=" + (_cfg.CoverSide == "left" ? "right" : "left") + ":" + _cfg.LeftAreaPercent);
                    sb.AppendLine("hotkeyLeftLayout=" + _leftHotkeyText + " hotkeyRightLayout=" + _rightHotkeyText + " hotkeyFullScreen=" + _fullHotkeyText + " keyboardHook=" + (_kbHook != IntPtr.Zero ? "1" : "0"));
                    if (_trayBridge != IntPtr.Zero && Native.IsWindow(_trayBridge))
                        sb.AppendLine("taskbarBridge=" + Program.RectText(Native.GetWindowRectangle(_trayBridge)));
                    sb.AppendLine("overlay=" + Program.RectText(_overlayRect));
                    sb.AppendLine("leftWorkArea=" + Program.RectText(_leftWorkArea));
                    sb.AppendLine("expectedWorkArea=" + Program.RectText(_expectedWorkArea));
                    sb.AppendLine("originalWorkArea=" + Program.RectText(_savedWorkArea));
                }
                sb.AppendLine("workarea=" + Program.RectText(Native.GetWorkArea()));

                sb.AppendLine("pid=" + Process.GetCurrentProcess().Id);
                sb.AppendLine("updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                File.WriteAllText(Program.RuntimePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Program.Log("写运行信息失败: " + ex.Message);
            }
        }

        private void WriteRuntimeStopped()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("version=" + Program.Version);
                sb.AppendLine("running=0");
                sb.AppendLine("mode=unknown");
                sb.AppendLine("updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                File.WriteAllText(Program.RuntimePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public void Quit()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (_enabled) Disable();
                CancelLayoutPending(false);
                UninstallKeyboardHook();
                UninstallForegroundHook();
                CloseHint();
                if (_layoutTimer != null)
                {
                    _layoutTimer.Stop();
                    _layoutTimer.Dispose();
                    _layoutTimer = null;
                }
                if (_leftHotkeyRegistered)
                {
                    try { Native.UnregisterHotKey(_form.Handle, _leftHotkeyId); }
                    catch { }
                    _leftHotkeyRegistered = false;
                }
                if (_rightHotkeyRegistered)
                {
                    try { Native.UnregisterHotKey(_form.Handle, _rightHotkeyId); }
                    catch { }
                    _rightHotkeyRegistered = false;
                }
                if (_fullHotkeyRegistered)
                {
                    try { Native.UnregisterHotKey(_form.Handle, _fullHotkeyId); }
                    catch { }
                    _fullHotkeyRegistered = false;
                }
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }

                Program.DeleteState();
                WriteRuntimeStopped();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                if (_menu != null)
                {
                    _menu.Dispose();
                    _menu = null;
                }
                Program.Log("退出程序");
            }
            catch (Exception ex)
            {
                Program.Log("退出异常: " + ex);
            }
            finally
            {
                _busy = false;
            }
            try { _form.Close(); }
            catch { }
            Application.Exit();
        }
    }
}
