// 自动定位「游戏 exe 所在的目录」。
//
// 为什么要定位：ReShade 只认【目标 exe 所在目录】下的 ReShade.ini，
// 缺了它会 DllMain 返回 FALSE，注入器这边只会看到 "LoadLibraryW 返回 0"。
// 所以注入前必须知道游戏装在哪，才能把 ini 放对地方。
//
// 三级回退（越靠前越准）：
//   1. 进程正在运行 —— 直接读 Process.MainModule.FileName，绝对准确；
//   2. 没在运行 —— 查 HoYoPlay 启动器写在注册表里的安装路径
//      HKCU\Software\miHoYo\HYP\<版本>\<biz>\GameInstallPath，
//      再在那些目录里按 exe 名反查（不依赖 biz 命名规则，国际服/崩铁/崩3 都能认）；
//   3. 还是没有 —— 用上次用户手动指定的路径（存在程序目录的 game-paths.ini 里）。
// 三级都失败返回 null，由调用方弹文件选择框让用户自己指。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace HoYoInjector
{
    internal static class GameLocator
    {
        /// <summary>记录用户手动指定过的 exe 路径，下次直接复用。</summary>
        private const string SettingsFile = "game-paths.ini";

        /// <summary>注册表里 HoYoPlay 记录安装位置的根路径。</summary>
        private const string HypRegistryRoot = @"Software\miHoYo\HYP";

        /// <summary>这些 exe 不是游戏本体，反查时跳过。</summary>
        private static readonly string[] NotGameExes = new string[]
        {
            "UnityCrashHandler64", "UnityCrashHandler32", "uninstall", "launcher",
            "vc_redist.x64", "vc_redist.x86", "crashreport", "elevation_service",
            "InstallHelper", "HYP", "HoyoPlay", "launcher_installer",
        };

        private static Dictionary<string, string> _remembered;

        /// <summary>
        /// 找出 processName（不含 .exe，忽略大小写）对应游戏的安装目录。
        /// 找不到返回 null。source 用来说明是走哪一级回退命中的，便于写日志。
        /// </summary>
        public static string Resolve(string processName, out string source)
        {
            source = null;
            if (string.IsNullOrWhiteSpace(processName)) return null;

            string name = StripExe(processName);

            #if !SHIPPING
            // 第 0 级：内置测试靶子 —— 程序目录下 测试\<名字>.exe
            // 发布版（-Shipping）不认这个目录，免得万一有人放个同名的 exe 进去被误当游戏。
            string local = FromAppTestFolder(name);
            if (local != null) { source = "程序目录下的测试靶子"; return local; }
#endif

            // 第 1 级：进程正在跑，读它的真实路径
            string dir = FromRunningProcess(name);
            if (dir != null) { source = "运行中的进程"; return dir; }

            // 第 2 级：HoYoPlay 注册表里记的安装路径
            Dictionary<string, string> map = FromRegistry();
            if (map != null && map.TryGetValue(name.ToLowerInvariant(), out dir) && Directory.Exists(dir))
            {
                source = "HoYoPlay 注册表";
                return dir;
            }

            // 第 3 级：上次用户手动指过的路径
            dir = FromRemembered(name);
            if (dir != null) { source = "上次手动指定"; return dir; }

            return null;
        }

        #if !SHIPPING
        /// <summary>
        /// 内置测试靶子：程序目录下 \测试\<名字>.exe。
        /// 有就返回它所在的目录（ReShade 认 ini 是认目标 exe 目录的）。
        /// </summary>
        private static string FromAppTestFolder(string name)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "测试");
                if (File.Exists(Path.Combine(dir, name + ".exe"))) return dir;
            }
            catch { }
            return null;
        }
#endif

        /// <summary>
        /// 找游戏主程序的完整 exe 路径（不是目录）。
        /// 等待期间用户再点一次「开始注入」时，要拿这个路径去启动游戏。
        /// </summary>
        public static string ResolveExePath(string processName, out string source)
        {
            source = null;
            string dir = Resolve(processName, out source);
            if (string.IsNullOrEmpty(dir)) return null;

            string name = StripExe(processName);
            string exe = Path.Combine(dir, name + ".exe");
            if (File.Exists(exe)) return exe;

            // 目录对但 exe 名对不上（例如国际服进程名和主程序名不一致），
            // 退一步在目录里找第一个"像游戏本体"的 exe。
            try
            {
                string[] all = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < all.Length; i++)
                {
                    if (!IsNotGameExe(Path.GetFileName(all[i]))) return all[i];
                }
            }
            catch { }

            return null;
        }

        /// <summary>把用户手动选的 exe 路径记下来，下次不用再选。</summary>
        public static void Remember(string processName, string exeFullPath)
        {
            if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(exeFullPath)) return;

            try
            {
                string name = StripExe(processName);
                var all = LoadRemembered();
                all[name.ToLowerInvariant()] = Path.GetFullPath(exeFullPath);
                _remembered = all;
                SaveRemembered(all);
            }
            catch
            {
                // 程序目录只读（比如装在 Program Files）时写不进去，
                // 只影响「下次不用再选」这个便利性，不影响注入本身，静默忽略。
            }
        }

        private static string StripExe(string name)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>第 1 级：从正在运行的进程取 exe 目录。</summary>
        private static string FromRunningProcess(string name)
        {
            try
            {
                Process[] all = Process.GetProcessesByName(name);
                if (all == null || all.Length == 0) return null;

                for (int i = 0; i < all.Length; i++)
                {
                    try
                    {
                        string exe = all[i].MainModule.FileName;
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                            return Path.GetDirectoryName(exe);
                    }
                    catch
                    {
                        // 提权不够读不到 MainModule，换下一个同名进程试
                    }
                    finally
                    {
                        all[i].Dispose();
                    }
                }
            }
            catch
            {
                // 枚举进程失败就当没找到
            }
            return null;
        }

        /// <summary>
        /// 第 2 级：扫 HKCU\Software\miHoYo\HYP 下所有子键的 GameInstallPath，
        /// 在每个目录里按 exe 文件名建「进程名(小写) → 目录」的映射。
        /// 不硬编码 biz 名，所以国际服、崩铁、崩 3 一旦装过都能认出来。
        /// </summary>
        private static Dictionary<string, string> FromRegistry()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(HypRegistryRoot, false))
                {
                    if (root == null) return result;

                    string[] bizKeys = root.GetSubKeyNames();
                    for (int i = 0; i < bizKeys.Length; i++)
                    {
                        // 结构是 HYP\<版本号，如 1_1>\<biz，如 hk4e_cn>，
                        // 所以要在版本号子键下再枚举一层。
                        string[] inner;
                        using (RegistryKey ver = root.OpenSubKey(bizKeys[i], false))
                        {
                            if (ver == null) continue;
                            inner = ver.GetSubKeyNames();

                            // 也考虑 GameInstallPath 直接挂在版本号键上的情况
                            AddInstallPath(result, ver, null, bizKeys[i]);
                        }

                        for (int j = 0; j < inner.Length; j++)
                        {
                            using (RegistryKey biz = root.OpenSubKey(bizKeys[i] + "\\" + inner[j], false))
                            {
                                if (biz == null) continue;
                                AddInstallPath(result, biz, inner[j], bizKeys[i]);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 注册表读不了（权限/键不存在）就当这级没命中
            }
            return result;
        }

        /// <summary>读一个键的 GameInstallPath，把目录里的游戏 exe 登记进 map。</summary>
        private static void AddInstallPath(Dictionary<string, string> map, RegistryKey key,
                                           string bizName, string versionName)
        {
            try
            {
                object raw = key.GetValue("GameInstallPath", null);
                string dir = raw == null ? null : raw.ToString();
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

                string[] exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                for (int k = 0; k < exes.Length; k++)
                {
                    string exeName = Path.GetFileNameWithoutExtension(exes[k]);
                    if (IsNotGameExe(exeName)) continue;

                    string key1 = exeName.ToLowerInvariant();
                    // 同一个 exe 出现在多个目录时，保留先找到的（遍历顺序稳定即可）
                    if (!map.ContainsKey(key1)) map[key1] = dir;
                }
            }
            catch
            {
                // 单个目录读不了不影响其它游戏
            }
        }

        private static bool IsNotGameExe(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return true;

            for (int i = 0; i < NotGameExes.Length; i++)
            {
                if (string.Equals(exeName, NotGameExes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Unity 的 CrashHandler 各种后缀都要排除
            if (exeName.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>第 3 级：读上次记住的手动指定路径。</summary>
        private static string FromRemembered(string name)
        {
            try
            {
                Dictionary<string, string> all = LoadRemembered();
                string exe;
                if (!all.TryGetValue(name.ToLowerInvariant(), out exe)) return null;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
                return Path.GetDirectoryName(exe);
            }
            catch
            {
                return null;
            }
        }

        private static string SettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFile);
        }

        /// <summary>game-paths.ini 格式很简单：一行一个 进程名=exe 完整路径。</summary>
        private static Dictionary<string, string> LoadRemembered()
        {
            if (_remembered != null) return _remembered;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = SettingsPath();
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = (lines[i] ?? "").Trim();
                        if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") ||
                            line.StartsWith("[")) continue;

                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;

                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k.Length > 0 && v.Length > 0) map[k] = v;
                    }
                }
            }
            catch
            {
                // 读失败就给个空表，不影响其它功能
            }

            _remembered = map;
            return map;
        }

        private static void SaveRemembered(Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; 这里记录用户手动指定过的游戏 exe 路径，删掉某一行即可让它重新自动识别。");
            sb.AppendLine("; 格式：进程名(不含 .exe)=exe 的完整路径");
            foreach (KeyValuePair<string, string> kv in map)
            {
                sb.AppendLine(kv.Key + "=" + kv.Value);
            }

            // 不带 BOM，纯文本更好手改
            File.WriteAllText(SettingsPath(), sb.ToString(), new UTF8Encoding(false));
        }
    }
}