// 预设（滤镜包）管理：每个游戏一套独立预设。
//
// 目录约定：
//   ReShadeInjector\
//     Shader\
//       Default\                  ← 无游戏归属的公共预设
//         Shaders\
//         Textures\
//       YuanShen\                 ← 原神国服的游戏文件夹
//         Default\                ← 该游戏的默认预设
//           Shaders\
//           Textures\
//         MyPreset\               ← 用户为该游戏新建的预设
//           Shaders\
//           Textures\
//       ZenlessZoneZero\          ← 绝区零的游戏文件夹
//         Default\
//           Shaders\
//           Textures\
//
// 游戏文件夹名 = 进程名（不含 .exe），例如 YuanShen、ZenlessZoneZero。
// 每个游戏各自记住自己选中的预设，存在程序目录的 settings.ini：
//   CurrentPreset.YuanShen=MyPreset
//   CurrentPreset.ZenlessZoneZero=Default
//
// 注入时 IniPatcher 把选中预设的 Shaders / Textures 写进游戏目录 ReShade.ini 的
// EffectSearchPaths / TextureSearchPaths —— 这就是"应用预设"的实际效果。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    /// <summary>一个预设的概况。</summary>
    internal sealed class PresetInfo
    {
        public string Name;

        /// <summary>Shaders 目录的完整路径（不存在时为 null）。</summary>
        public string ShaderDir;

        /// <summary>Textures 目录的完整路径（不存在时为 null）。</summary>
        public string TextureDir;

        /// <summary>Shaders 目录下的 .fx 文件数量。</summary>
        public int ShaderCount;

        /// <summary>Textures 目录下的图片数量。</summary>
        public int TextureCount;

        /// <summary>列表里显示的一行文字。</summary>
        public string Display
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(Name);
                sb.Append("    ");
                sb.Append(ShaderCount);
                sb.Append(" 个效果");
                if (TextureCount > 0)
                {
                    sb.Append(" · ");
                    sb.Append(TextureCount);
                    sb.Append(" 个纹理");
                }
                if (ShaderDir == null && TextureDir == null)
                    sb.Append("（空）");
                return sb.ToString();
            }
        }

        /// <summary>右侧状态文字。</summary>
        public string Status
        {
            get
            {
                if (ShaderDir == null && TextureDir == null) return "空";
                if (ShaderCount == 0 && TextureCount == 0) return "空";
                return "可用";
            }
        }

        /// <summary>列表里文字的颜色（直接给 Brush，不靠 XAML DataTrigger）。</summary>
        public System.Windows.Media.SolidColorBrush DisplayBrush
        {
            get
            {
                var c = ShaderCount > 0
                    ? System.Windows.Media.Color.FromRgb(0xED, 0xF0, 0xF6)
                    : System.Windows.Media.Color.FromRgb(0xC0, 0xCA, 0xD9);
                var b = new System.Windows.Media.SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }

        public System.Windows.Media.SolidColorBrush StatusBrush
        {
            get
            {
                var c = (ShaderDir != null && ShaderCount > 0)
                    ? System.Windows.Media.Color.FromRgb(0x3D, 0xD6, 0x8C)
                    : System.Windows.Media.Color.FromRgb(0x83, 0x8F, 0xA3);
                var b = new System.Windows.Media.SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
    }

    internal static class PresetManager
    {
        public const string DefaultName = "Default";

        /// <summary>没有指定游戏时用的公共预设文件夹名。</summary>
        public const string GlobalName = "Default";

        private const string ShaderRootName = "Shader";
        private const string SettingsFileName = "settings.ini";
        private const string ShadersDirName = "Shaders";
        private const string TexturesDirName = "Textures";

        /// <summary>导入时往下找 Shaders / Textures 目录的最大层数。</summary>
        private const int SearchDepth = 4;

        /// <summary>纹理的文件名通配符（Directory.GetFiles 要的是 *.png 而不是 .png）。</summary>
        private static readonly string[] TextureExts = new string[]
        {
            "*.png", "*.jpg", "*.jpeg", "*.dds", "*.bmp", "*.tga",
        };

        // ---------- 路径 ----------

        /// <summary>Shader\ 目录的完整路径。</summary>
        public static string ShaderRoot()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ShaderRootName);
        }

        /// <summary>某个游戏的预设根目录：Shader\&lt;游戏&gt;\ 。gameKey 为空时是 Shader\Default\ 。</summary>
        public static string PresetRoot(string gameKey)
        {
            string g = Sanitize(gameKey);
            if (g.Length == 0) g = GlobalName;
            return Path.Combine(ShaderRoot(), g);
        }

        /// <summary>某个游戏下某个预设的目录。</summary>
        public static string PresetDir(string gameKey, string presetName)
        {
            return Path.Combine(PresetRoot(gameKey), Sanitize(presetName));
        }

        /// <summary>
        /// 进程名 → 游戏文件夹名。去空格、去 .exe 后缀，
        /// 只保留文件名允许的字符。
        /// </summary>
        public static string GameKey(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return GlobalName;

            string s = processName.Trim();
            if (s.Length > 4 && s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);

            string clean = Sanitize(s);
            return clean.Length == 0 ? GlobalName : clean;
        }

        /// <summary>
        /// 预设/游戏名合法化：去掉 Windows 文件名不允许的字符。
        /// 返回空串表示名字不能用了。
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (c < 32) continue;
                if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
                    c == '"' || c == '<' || c == '>' || c == '|') continue;
                sb.Append(c);
            }

            string s = sb.ToString().Trim().TrimEnd('.', ' ');
            if (s.Length > 40) s = s.Substring(0, 40).Trim();
            return s;
        }

        // ---------- 列出 / 检查 ----------

        /// <summary>
        /// 列出某游戏下的预设。Default 永远排第一，其余按名字排。
        /// 目录不存在时返回空列表。
        /// </summary>
        public static List<PresetInfo> List(string gameKey)
        {
            var result = new List<PresetInfo>();

            string root = PresetRoot(gameKey);
            if (!Directory.Exists(root)) return result;

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(root);
            }
            catch
            {
                return result;
            }

            foreach (string d in dirs)
            {
                string name = Path.GetFileName(d);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith("_")) continue;   // _ 开头是导入时的临时目录，不算预设

                // 老目录结构（Shader\Default\Shaders + Shaders\Textures 直接躺在游戏目录下）
                // 会被误读成名为 "Shaders" / "Textures" 的预设，这里直接跳过。
                if (string.Equals(name, ShadersDirName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, TexturesDirName, StringComparison.OrdinalIgnoreCase)) continue;

                var info = Inspect(d);
                if (info != null) result.Add(info);
            }

            result.Sort(delegate (PresetInfo a, PresetInfo b)
            {
                bool ad = string.Equals(a.Name, DefaultName, StringComparison.OrdinalIgnoreCase);
                bool bd = string.Equals(b.Name, DefaultName, StringComparison.OrdinalIgnoreCase);
                if (ad != bd) return ad ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        /// <summary>读某个预设目录的概况。</summary>
        public static PresetInfo Inspect(string presetDir)
        {
            if (string.IsNullOrEmpty(presetDir) || !Directory.Exists(presetDir)) return null;

            var info = new PresetInfo();
            info.Name = Path.GetFileName(presetDir);

            string s = Path.Combine(presetDir, ShadersDirName);
            if (Directory.Exists(s))
            {
                info.ShaderDir = s;
                info.ShaderCount = CountFiles(s, "*.fx");
            }

            string t = Path.Combine(presetDir, TexturesDirName);
            if (Directory.Exists(t))
            {
                info.TextureDir = t;
                info.TextureCount = CountFiles(t, TextureExts);
            }

            return info;
        }

        private static int CountFiles(string dir, string pattern)
        {
            try
            {
                return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountFiles(string dir, string[] patterns)
        {
            int total = 0;
            for (int i = 0; i < patterns.Length; i++) total += CountFiles(dir, patterns[i]);
            return total;
        }

        /// <summary>
        /// 把老的单层结构迁移成「游戏/预设」两层结构。
        ///
        /// 老结构：Shader\Default\Shaders\ 、Shader\Default\Textures\
        /// 新结构：Shader\<游戏>\<预设>\Shaders\ 、...\Textures\
        ///
        /// 所以当游戏目录下直接躺着 Shaders / Textures 两个文件夹时，
        /// 就说明它们属于该游戏下的 Default 预设，挪进去即可。
        /// 已经在目标位置存在的同名目录不覆盖，避免毁掉用户的东西。
        /// </summary>
        public static void MigrateLegacy(string gameKey)
        {
            try
            {
                string root = PresetRoot(gameKey);
                if (!Directory.Exists(root)) return;

                string target = Path.Combine(root, DefaultName);
                string[] legacy = new string[] { ShadersDirName, TexturesDirName };

                for (int i = 0; i < legacy.Length; i++)
                {
                    string src = Path.Combine(root, legacy[i]);
                    if (!Directory.Exists(src)) continue;

                    string dst = Path.Combine(target, legacy[i]);
                    if (Directory.Exists(dst) || File.Exists(dst)) continue;

                    try
                    {
                        Directory.CreateDirectory(target);
                        Directory.Move(src, dst);
                    }
                    catch { }   // 挪不动就算了，反正 List 已经会跳过这两个名字
                }
            }
            catch { }
        }

        /// <summary>
        /// 确保某游戏下存在 Default 预设（建两个空目录）。
        /// 程序启动和切游戏时调用，省得用户第一次进预设页是空的。
        /// </summary>
        public static void EnsureDefault(string gameKey)
        {
            MigrateLegacy(gameKey);
            try
            {
                string dir = PresetDir(gameKey, DefaultName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(Path.Combine(dir, ShadersDirName));
                    Directory.CreateDirectory(Path.Combine(dir, TexturesDirName));
                }
            }
            catch { }
        }

        /// <summary>
        /// 新建一个空预设（只建 Shaders / Textures 两个空目录）。
        /// existed 为 true 表示同名目录本来就有，没做任何改动。
        /// </summary>
        public static bool Create(string gameKey, string rawName, out string error, out bool existed)
        {
            error = null;
            existed = false;

            string name = Sanitize(rawName);
            if (name.Length == 0)
            {
                error = "预设名称不合法（不能为空，也不能只有符号）。";
                return false;
            }

            string dir = PresetDir(gameKey, name);
            if (Directory.Exists(dir))
            {
                existed = true;
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(dir, ShadersDirName));
                Directory.CreateDirectory(Path.Combine(dir, TexturesDirName));

                // 顺手放一个空的滤镜预设文件（Techniques= 一行），
                // 这样预设文件夹里不会只有两个空目录，用户一看就知道
                // 那个 ini 才是 ReShade 认的预设文件。
                string presetError;
                EnsurePresetFile(gameKey, name, Path.Combine(dir, ShadersDirName), out presetError);
                return true;
            }
            catch (Exception ex)
            {
                error = "创建预设失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 重命名预设（就是把文件夹改名）。若改的正好是当前在用的那个，settings.ini 一起改掉。
        /// </summary>
        public static bool Rename(string gameKey, string oldName, string rawNewName, out string error)
        {
            error = null;

            string name = Sanitize(rawNewName);
            if (name.Length == 0)
            {
                error = "预设名称不合法（不能为空，也不能只有符号）。";
                return false;
            }

            // 名字没变（只差大小写或符号），当成成功，不去动目录
            if (string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase)) return true;

            if (string.Equals(oldName, DefaultName, StringComparison.OrdinalIgnoreCase))
            {
                error = "Default 是内置预设，不能改名。";
                return false;
            }

            string oldDir = PresetDir(gameKey, oldName);
            string newDir = PresetDir(gameKey, name);

            if (!Directory.Exists(oldDir))
            {
                error = "预设文件夹不存在：" + oldDir;
                return false;
            }

            if (Directory.Exists(newDir))
            {
                error = "已经有一个叫「" + name + "」的预设了。";
                return false;
            }

            try
            {
                Directory.Move(oldDir, newDir);

                // 预设文件夹里的 preset ini 跟着改名，否则里面躺着的还是老名字，
                // 用户下次打开这个预设的文件夹会发现对不上。
                string oldPreset = Path.Combine(newDir, Sanitize(oldName) + PresetFileExt);
                string newPreset = Path.Combine(newDir, name + PresetFileExt);
                if (File.Exists(oldPreset))
                {
                    try { File.Move(oldPreset, newPreset); }
                    catch { }   // 改不动就算了，不影响预设本身能用
                }
            }
            catch (Exception ex)
            {
                error = "重命名失败：" + ex.Message;
                return false;
            }

            // 改的是当前在用的预设，记录一起改，否则下次注入会指向不存在的老目录
            if (string.Equals(GetCurrent(gameKey), oldName, StringComparison.OrdinalIgnoreCase))
            {
                SetCurrent(gameKey, name);
            }

            return true;
        }

        // ---------- 导入 ----------

        /// <summary>
        /// 把 sourceDir 里找到的 Shaders / Textures 复制进某游戏下名字为 name 的预设。
        /// 返回复制的文件总数；0 表示没认出滤镜内容（error 里是原因）。
        /// </summary>
        public static int ImportFromFolder(string gameKey, string rawName, string sourceDir, out string error)
        {
            error = null;

            string name = Sanitize(rawName);
            if (name.Length == 0)
            {
                error = "预设名称不合法。";
                return 0;
            }

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                error = "源文件夹不存在：" + sourceDir;
                return 0;
            }

            string foundShaders, foundTextures;
            FindResourceDirs(sourceDir, out foundShaders, out foundTextures);

            if (foundShaders == null && foundTextures == null)
            {
                error = "在导入内容里没找到名为 Shaders 或 Textures 的文件夹。";
                return 0;
            }

            string dir = PresetDir(gameKey, name);
            try
            {
                Directory.CreateDirectory(dir);

                int copied = 0;
                if (foundShaders != null)
                    copied += CopyTree(foundShaders, Path.Combine(dir, ShadersDirName));
                if (foundTextures != null)
                    copied += CopyTree(foundTextures, Path.Combine(dir, TexturesDirName));

                // 复制完再建预设文件 —— 这时 Shaders 里已经有 fx 了，
                // 生成的 ini 里 Techniques 能把它们全列上。
                string presetError;
                EnsurePresetFile(gameKey, name, Path.Combine(dir, ShadersDirName), out presetError);

                return copied;
            }
            catch (Exception ex)
            {
                error = "复制失败：" + ex.Message;
                return 0;
            }
        }

        /// <summary>
        /// 在 sourceDir 下面找 Shaders / Textures 目录（不区分大小写）。
        /// 优先看根目录的直接子目录，找不到再 BFS 往下钻（最多 SearchDepth 层），
        /// 这样 reshade-shaders\Shaders 这种嵌套结构也能认出来。
        /// </summary>
        private static void FindResourceDirs(string root, out string shaders, out string textures)
        {
            shaders = null;
            textures = null;

            // 第一轮：只看根目录的直接子目录
            try
            {
                foreach (string d in Directory.GetDirectories(root))
                {
                    string n = Path.GetFileName(d);
                    if (shaders == null && string.Equals(n, ShadersDirName, StringComparison.OrdinalIgnoreCase))
                        shaders = d;
                    if (textures == null && string.Equals(n, TexturesDirName, StringComparison.OrdinalIgnoreCase))
                        textures = d;
                }
            }
            catch { }

            if (shaders != null && textures != null) return;

            // 第二轮：BFS 递归找，层数受限，免得在巨型目录里爬太久
            var queue = new Queue<string>();
            queue.Enqueue(root);
            int level = 0;

            while (queue.Count > 0 && level < SearchDepth)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    string cur = queue.Dequeue();
                    try
                    {
                        foreach (string d in Directory.GetDirectories(cur))
                        {
                            string n = Path.GetFileName(d);
                            if (shaders == null && string.Equals(n, ShadersDirName, StringComparison.OrdinalIgnoreCase))
                                shaders = d;
                            if (textures == null && string.Equals(n, TexturesDirName, StringComparison.OrdinalIgnoreCase))
                                textures = d;
                            if (shaders != null && textures != null) return;
                            queue.Enqueue(d);
                        }
                    }
                    catch { }
                }
                level++;
            }
        }

        /// <summary>把 src 下的内容整体复制到 dst（保留子目录结构），返回文件数。</summary>
        private static int CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            int copied = 0;

            foreach (string file in Directory.GetFiles(src))
            {
                try
                {
                    File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
                    copied++;
                }
                catch { }
            }

            foreach (string sub in Directory.GetDirectories(src))
                copied += CopyTree(sub, Path.Combine(dst, Path.GetFileName(sub)));

            return copied;
        }

        // ---------- 当前预设（按游戏分别记） ----------

        private static string SettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        }

        private static string CurrentKey(string gameKey)
        {
            return "CurrentPreset." + GameKey(gameKey);
        }

        /// <summary>
        /// 读某游戏当前选中的预设名。没记录、读失败、或者那个目录已不在了，
        /// 都回退到 Default。
        /// </summary>
        public static string GetCurrent(string gameKey)
        {
            string key = CurrentKey(gameKey);
            string name = null;

            try
            {
                string p = SettingsPath();
                if (File.Exists(p))
                {
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                            continue;
                        name = line.Substring(eq + 1).Trim();
                        break;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(name)) name = DefaultName;

            if (!Directory.Exists(PresetDir(gameKey, name)))
            {
                // 记录的预设文件夹没了（用户删了、或者改名了），回落到 Default，
                // 并且把记录改回来 —— 否则 settings.ini 会一直指着一个不存在的东西，
                // 界面上显示的名字和实际用的目录长期不一致。
                name = DefaultName;
                if (Directory.Exists(PresetDir(gameKey, name))) SetCurrent(gameKey, name);
            }

            return name;
        }

        /// <summary>写入某游戏当前选中的预设。写失败静默忽略（程序目录只读时）。</summary>
        public static void SetCurrent(string gameKey, string name)
        {
            string clean = Sanitize(name);
            if (clean.Length == 0) return;

            string key = CurrentKey(gameKey);

            try
            {
                var lines = new List<string>();
                string p = SettingsPath();

                if (File.Exists(p))
                {
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key,
                                                    StringComparison.OrdinalIgnoreCase)) continue;
                        if (line.Trim().Length == 0) continue;
                        lines.Add(line);
                    }
                }

                lines.Add(key + "=" + clean);
                File.WriteAllText(p, string.Join("\r\n", lines.ToArray()) + "\r\n",
                                  new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>
        /// 取某游戏某预设的效果 / 纹理目录。预设不存在或没建对应子目录时返回 null。
        /// </summary>
        public static void Resolve(string gameKey, string presetName,
                                   out string shaderDir, out string textureDir)
        {
            shaderDir = null;
            textureDir = null;

            string clean = Sanitize(presetName);
            if (clean.Length == 0) return;

            string dir = PresetDir(gameKey, clean);
            if (!Directory.Exists(dir)) return;

            string s = Path.Combine(dir, ShadersDirName);
            string t = Path.Combine(dir, TexturesDirName);
            if (Directory.Exists(s)) shaderDir = s;
            if (Directory.Exists(t)) textureDir = t;
        }

        /// <summary>ReShade 的「滤镜预设」文件扩展名。</summary>
        private const string PresetFileExt = ".ini";

        /// <summary>ReShade.ini 本身的文件名。找预设文件时要把它排除掉。</summary>
        private const string MainIniName = "ReShade.ini";

        /// <summary>
        /// 预设目录下那个 preset ini 的路径（算出来而已，不管存不存在）。
        /// 约定名字 = 预设名 + .ini，例如 Shader\YuanShen\001\001.ini
        /// </summary>
        public static string PresetFilePath(string gameKey, string presetName)
        {
            string clean = Sanitize(presetName);
            if (clean.Length == 0) clean = DefaultName;
            return Path.Combine(PresetDir(gameKey, clean), clean + PresetFileExt);
        }

        /// <summary>
        /// 确保预设目录下有一个「滤镜预设」ini（这是 ReShade 的 preset 文件，
        /// 跟游戏目录里的 ReShade.ini 是两种完全不同的东西），返回它的完整路径。
        ///
        /// 规则：
        ///   - 目录里已经有 ini（不叫 ReShade.ini）→ 直接用那个，一个字节都不动；
        ///   - 一个都没有 → 在预设目录下新建「预设名.ini」，把 Shaders 里的 fx 列进 Techniques。
        /// 预设目录不存在时返回 null（不该在不存在的地方凭空造文件）。
        /// </summary>
        public static string EnsurePresetFile(string gameKey, string presetName, string shaderDir,
                                              out string error)
        {
            error = null;

            string clean = Sanitize(presetName);
            if (clean.Length == 0) clean = DefaultName;

            string dir = PresetDir(gameKey, clean);
            if (!Directory.Exists(dir))
            {
                error = "预设目录不存在，跳过滤镜预设文件：" + dir;
                return null;
            }

            string existing = FindPresetFile(dir, clean);
            if (existing != null) return existing;

            string path = Path.Combine(dir, clean + PresetFileExt);
            try
            {
                File.WriteAllText(path, BuildPresetText(shaderDir), new UTF8Encoding(false));
                return path;
            }
            catch (Exception ex)
            {
                error = "创建滤镜预设文件失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// 在预设目录里找现成的 preset ini。优先同名的，其次按名字排序取第一个。
        /// 排除 ReShade.ini —— 那是注入到游戏目录的主配置，不是预设。
        /// </summary>
        private static string FindPresetFile(string presetDir, string preferredName)
        {
            string preferred = Path.Combine(presetDir, preferredName + PresetFileExt);
            if (File.Exists(preferred)) return preferred;

            string[] files;
            try
            {
                files = Directory.GetFiles(presetDir, "*" + PresetFileExt, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return null;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetFileName(files[i]), MainIniName,
                                  StringComparison.OrdinalIgnoreCase)) continue;
                return files[i];
            }
            return null;
        }

        /// <summary>
        /// 新预设文件的内容。ReShade 预设里 Techniques 的格式是
        /// 「技术名@fx文件名」，多个用逗号隔开（实测从 ReShade 自己存出来的
        /// ReShadePreset.ini 里读到的就是这么写的）：
        ///     Techniques=MartysMods_ReGradePlus@MartysMods_REGRADE+.fx,...
        ///     TechniqueSorting=（同一份清单，决定覆盖层里的排列顺序）
        ///
        /// 技术名不是文件名 —— 带中文后缀的文件（MartysMods_CLARITY - 细节锐化.fx）
        /// 里面的技术名其实是 MartysMods_Clarity，所以必须扫 fx 内容把 technique 名抠出来。
        /// 写错了 ReShade 会在日志里报 unknown technique，一个效果都出不来。
        ///
        /// 这里 Techniques 故意留空 = 新建的预设里一个效果都不启用，让用户自己按需勾。
        /// TechniqueSorting 仍然写全清单，否则覆盖层左侧列表是空的，用户连勾都没得勾。
        /// </summary>
        private static string BuildPresetText(string shaderDir)
        {
            var items = new List<string>();

            if (!string.IsNullOrEmpty(shaderDir))
            {
                try
                {
                    string[] fx = Directory.GetFiles(shaderDir, "*.fx", SearchOption.AllDirectories);
                    Array.Sort(fx, StringComparer.OrdinalIgnoreCase);

                    // @ 后面写的是相对 EffectSearchPaths 的文件名（含子目录）。
                    // 这就是上面 fx 文件的名字。
                    string root = Path.GetFullPath(shaderDir).TrimEnd('\\') + "\\";

                    for (int i = 0; i < fx.Length; i++)
                    {
                        string rel = Path.GetFullPath(fx[i]);
                        if (rel.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            rel = rel.Substring(root.Length);
                        else
                            rel = Path.GetFileName(fx[i]);

                        string[] techniques = ReadTechniqueNames(fx[i]);
                        for (int j = 0; j < techniques.Length; j++)
                            items.Add(techniques[j] + "@" + rel);
                    }
                }
                catch { }   // 没权限读就算了，生成一个空 Techniques 的预设也能用
            }

            string list = string.Join(",", items.ToArray());

            var sb = new StringBuilder();
            // Techniques 留空 = 全部禁用。之前这里写的是 list，等于新建预设后
            // 一进游戏十几个效果全开，画面是十几种滤镜叠一起，还白白吃性能。
            sb.Append("Techniques=").Append("\r\n");
            // TechniqueSorting 写全清单：它只管覆盖层里的排列顺序，不影响启用状态，
            // 留空的话用户点开 ReShade 会发现列表是空的，没东西可勾。
            sb.Append("TechniqueSorting=").Append(list).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// 从一个 fx 文件里抠出所有 technique 的名字。
        /// 只认行首（允许前面有空格）的 technique 声明，跳过被 // 注释掉的那些。
        /// </summary>
        private static string[] ReadTechniqueNames(string fxPath)
        {
            var names = new List<string>();
            try
            {
                string text = File.ReadAllText(fxPath, Encoding.UTF8);
                string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("technique", StringComparison.OrdinalIgnoreCase)) continue;

                    // "techniques" / "techniquefoo" 这类不能要，必须紧跟空格
                    if (line.Length <= 9 || !char.IsWhiteSpace(line[9])) continue;

                    string rest = line.Substring(9).TrimStart();
                    int end = 0;
                    while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
                    if (end == 0) continue;

                    string name = rest.Substring(0, end);

                    // ReShade 自己会跳过 preprocessor 里的 technique，这里也忽略
                    if (string.Equals(name, "technique", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!names.Contains(name)) names.Add(name);
                }
            }
            catch { }
            return names.ToArray();
        }
    }
}