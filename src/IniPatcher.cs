// 把 ReShade.ini 放到「游戏 exe 所在目录」，并让里面的路径项指向
// 本工具（ReShadeInjector）自己的资源目录。
//
// 两条铁律：
//   1. 【路径类键一律以本工具为准】AddonPath / EffectSearchPaths /
//      TextureSearchPaths / Font / EditorFont / LatinFont 每次都重写成
//      程序目录下的实际位置。用户把滤镜包、字体丢进 ReShadeInjector 目录，
//      ReShade 就用那里的东西 —— 不需要再去游戏目录手动改一遍。
//   2. 【样式类键一个字符都不动】[INPUT] 的快捷键、[OVERLAY] 的布局、
//      [STYLE] 字号颜色、[DEPTH]、[GENERAL] 里的其它项，全部原样保留，
//      连行顺序、行尾风格（CRLF/LF）都不变。
//
// 为什么每次都重算：同盘符写相对路径（..\..\xxx），整个目录树搬走也还有效；
// 跨盘符（游戏在 F:\、工具在 C:\）相对路径表达不了，只能写绝对路径，
// 但每次注入都重算，所以工具搬家后下一次注入就自动指向新位置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class IniPatcher
    {
        private const string IniFileName = "ReShade.ini";

        /// <summary>搜索路径类键：值可能是逗号分隔的多个路径。</summary>
        private static readonly string[] PathListKeys = new string[]
        {
            "AddonPath", "EffectSearchPaths", "TextureSearchPaths",
        };

        /// <summary>单个路径类键（字体文件）。</summary>
        private static readonly string[] FontKeys = new string[]
        {
            "Font", "EditorFont", "LatinFont",
        };

        /// <summary>截图保存目录。</summary>
        private const string ScreenshotKey = "SavePath";

        /// <summary>滤镜预设文件路径（指向 预设目录\预设名.ini）。</summary>
        private const string PresetPathKey = "PresetPath";

        /// <summary>
        /// 已经不需要的键（"段名|键名"），补丁时顺便从已有的 ini 里删掉。
        /// 这几个是 ReShade 覆盖层的外观/快捷键项，模板里已经不写了；
        /// 但 ReShade 退出时会把它们连同几十个别的默认键一起写回 ini
        /// （实测：跑一次回来 ini 多出 37 个键），所以"删掉"根本不可能持久，
        /// 只能每次注入时按固定值写死，这样界面上看到的外观每次都一致。
        /// 只动这几项，其它样式键（字号、颜色、布局）依旧一个字符都不动。
        /// </summary>
        /// <summary>
        /// 这几项"删掉"是没用的 —— ReShade 退出时会把它们连同几十个别的默认键
        /// 一起写回 ini（实测：跑一次回来 ini 多出 37 个键，Alpha / StyleIndex /
        /// EditorStyleIndex / TabRounding / KeyScreenshot 全在内）。
        /// 所以改成每次注入时按固定值写死，这样界面上看到的外观每次都一致。
        /// </summary>
        private static readonly string[,] ForcedStyle = new string[,]
        {
            { "STYLE", "Alpha", "0.850000" },          // 全局透明度：0.85
            { "STYLE", "StyleIndex", "1" },            // 全局样式：1 = Light（0=Dark 1=Light 2=Default 3=Custom Simple 4=Custom Advanced 5=Solarized Dark 6=Solarized Light）
            { "STYLE", "EditorStyleIndex", "0" },      // 文本编辑器样式：0 = Dark
            // 边框圆角：TabRounding 是覆盖层窗口的圆角，其余几个一起归零才是干净外观
            { "STYLE", "TabRounding", "8.000000" },
            { "STYLE", "ChildRounding", "0.000000" },
            { "STYLE", "FrameRounding", "0.000000" },
            { "STYLE", "PopupRounding", "0.000000" },
            { "STYLE", "WindowRounding", "0.000000" },
            { "STYLE", "ScrollbarRounding", "0.000000" },
            { "STYLE", "GrabRounding", "0.000000" },
            // 注：Custom Simple / Custom Advanced 那套自定义配色刷不出来。
            // 往 [STYLE] 里写 Background / ItemBackground / Text / ActiveItem
            // 这些键，实测 ReShade 既不认也不写回（Alpha=1.0 时采样仍是内置的深色），
            // 那四个颜色框是 ReShade 内部状态，不走 ini 持久化。
            // 所以只用内置主题，Light 就是这套浅色外观。
            { "INPUT", "KeyScreenshot", "0,0,0,0" },  // 截图快捷键：0,0,0,0 = 不绑定任何键
        };

        /// <summary>取某个 section|key 的固定值，没有就返回 null。</summary>
        private static string ForcedValue(string section, string key)
        {
            string sig = section + "|" + key;
            for (int i = 0; i < ForcedStyle.GetLength(0); i++)
            {
                if (string.Equals(ForcedStyle[i, 0] + "|" + ForcedStyle[i, 1], sig, StringComparison.OrdinalIgnoreCase))
                    return ForcedStyle[i, 2];
            }
            return null;
        }

        /// <summary>文件里是否已经有 section 下这个 key。</summary>
        private static bool HasKey(string[] lines, string section, string key)
        {
            string cur = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']')
                {
                    cur = t.Substring(1, t.Length - 2).Trim().ToUpperInvariant();
                    continue;
                }
                if (!string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) continue;

                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 把 key=value 插到指定段的最后一行非空内容之后。
        /// 段不存在就原样返回（引用相等，调用方据此判断"没插"）。
        /// </summary>
        private static string[] InsertKey(string[] lines, string section, string key, string value)
        {
            var list = new List<string>(lines);

            int secIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                string t = list[i].Trim();
                if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']' &&
                    string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase))
                {
                    secIdx = i;
                    break;
                }
            }
            if (secIdx < 0) return lines;

            int insertAt = secIdx + 1;
            for (int i = secIdx + 1; i < list.Count; i++)
            {
                string t = list[i].Trim();
                if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']') break;
                if (t.Length > 0) insertAt = i + 1;
            }

            list.Insert(insertAt, key + "=" + value);
            return list.ToArray();
        }

        /// <summary>
        /// 确保 gameDir\ReShade.ini 存在且路径项指向本工具。
        /// 返回 false 表示没能准备好（error 里是原因），调用方应该中止注入。
        /// report 里是要写进日志的说明行（可能为空）。
        /// </summary>
        public static bool Ensure(string gameDir, out List<string> report, out string error)
        {
            return Ensure(gameDir, null, null, out report, out error);
        }

        /// <summary>
        /// 同上，但指定游戏与预设：Effects / Textures 指向
        /// Shader\&lt;游戏&gt;\&lt;预设&gt;\Shaders 和 ...\Textures。
        /// gameKey 为空或预设目录不存在时，退回公共的 Shader\Default。
        /// </summary>
        public static bool Ensure(string gameDir, string gameKey, string presetName,
                                  out List<string> report, out string error)
        {
            report = new List<string>();
            error = null;

            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            {
                error = "游戏目录不存在：" + gameDir;
                return false;
            }

            string iniPath = Path.Combine(gameDir, IniFileName);
            bool created = !File.Exists(iniPath);

            if (created)
            {
                string writeError;
                if (!DefaultIni.WriteTo(gameDir, out writeError))
                {
                    error = "写入 ReShade.ini 失败：" + writeError;
                    return false;
                }
                report.Add("目标目录原本没有 ReShade.ini，已用内置模板生成：" + iniPath);
            }

            string patchError;
            if (!Patch(iniPath, gameDir, gameKey, presetName, created, report, out patchError))
            {
                // 补丁失败不算致命：文件已经在，顶多路径不对导致没效果，
                // 不该因此拒绝注入。
                report.Add("路径项自动修正失败（不影响注入，可能没有滤镜效果）：" + patchError);
            }

            return true;
        }

        /// <summary>
        /// 就地改 ini：路径项每次都以本工具为准；样式项只在 created（本次刚用
        /// 模板新建出来的这份 ini）时才按固定值写，已有 ini 的样式一个字符都不碰。
        /// </summary>
        private static bool Patch(string iniPath, string gameDir, string gameKey, string presetName,
                                  bool created, List<string> report, out string error)
        {
            error = null;
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            // 插件目录固定在程序目录下的 Addons\，不按游戏/预设分。
            string addonDir = PickDir(appDir,
                new string[] { "Addons", @"Shader\Default\Addons", @"reshade-shaders\Addons" });

            // 效果 / 纹理：先看当前选中的预设，没有就退回公共 Default
            string shaderDir = null;
            string textureDir = null;
            if (!string.IsNullOrEmpty(gameKey))
            {
                // 先确保这个游戏的预设根目录存在，否则 Resolve 会返回 null，
                // 路径项就会保留旧值（老的 ini 里可能还指向搬家前的旧目录）。
                PresetManager.EnsureDefault(gameKey);

                PresetManager.Resolve(gameKey, presetName, out shaderDir, out textureDir);

                // 预设目录不存在（换过预设名、或者把文件夹删了）就回落到 Default。
                // 不这么做的话路径项会退回模板里的 .\reshade-shaders\...，
                // 那个目录现实中不存在，等于没效果。
                if (shaderDir == null && textureDir == null &&
                    !string.Equals(presetName, PresetManager.DefaultName, StringComparison.OrdinalIgnoreCase))
                {
                    PresetManager.Resolve(gameKey, PresetManager.DefaultName, out shaderDir, out textureDir);
                    if (shaderDir != null || textureDir != null)
                        report.Add("预设「" + PresetManager.Sanitize(presetName) + "」的文件夹不在，已回落到 Default。");
                }

                if (shaderDir != null || textureDir != null)
                    report.Add("按预设写入路径：" + PresetManager.GameKey(gameKey) + " / " +
                               PresetManager.Sanitize(
                                   shaderDir != null && !string.IsNullOrEmpty(presetName) &&
                                   Directory.Exists(PresetManager.PresetDir(gameKey, presetName))
                                       ? presetName
                                       : PresetManager.DefaultName));
            }

            if (shaderDir == null)
                shaderDir = PickDir(appDir,
                    new string[] { @"Shader\Default\Shaders", @"Shader\Shaders", @"reshade-shaders\Shaders" });

            if (textureDir == null)
                textureDir = PickDir(appDir,
                    new string[] { @"Shader\Default\Textures", @"Shader\Textures", @"reshade-shaders\Textures" });

            string fontFile = PickFont(appDir);

            string shotDir = Path.Combine(appDir, "ScreenShot");

            // 滤镜预设文件（ReShade 的 preset，跟这份 ReShade.ini 是两种东西）：
            // 落在「预设目录\预设名.ini」。没有就建一个，有就用现成那个。
            string presetFile = null;
            if (!string.IsNullOrEmpty(gameKey))
            {
                string presetError;
                presetFile = PresetManager.EnsurePresetFile(gameKey, presetName, shaderDir, out presetError);
                if (presetFile != null)
                    report.Add("滤镜预设文件：" + presetFile);
                else if (!string.IsNullOrEmpty(presetError))
                    report.Add(presetError);
            }

            string[] lines;
            string raw;
            try
            {
                // 用 ReadAllText 再切行，能原样保住 CRLF / LF 和末尾换行
                raw = File.ReadAllText(iniPath, Encoding.UTF8);
                lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            }
            catch (Exception ex)
            {
                error = "读不了 ReShade.ini：" + ex.Message;
                return false;
            }

            var changed = new List<string>();
            var styleFixed = new List<string>();
            bool isNewLine = raw.IndexOf("\r\n", StringComparison.Ordinal) >= 0;

            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 先记下当前段名，后面判断"这个键该不该删"要用到
                string trimmed = line.Trim();
                if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim().ToUpperInvariant();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);   // 保留原样，不去空格

                // 样式类固定值：只对"本次刚新建出来的 ini"生效。
                // 用户自己的 ini 里这些项一律保持原样 —— ReShade 会把它自己的
                // 默认值写回去，我们不该跟用户抢这份配置。
                if (created)
                {
                    string forced = ForcedValue(section, key);
                    if (forced != null)
                    {
                        if (!string.Equals(value.Trim(), forced, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = key + "=" + forced;
                            styleFixed.Add(key);
                        }
                        continue;
                    }
                }

                // 路径列表类：整条重写成本工具的实际位置
                if (Contains(PathListKeys, key))
                {
                    string target = KeyToDir(key, addonDir, shaderDir, textureDir);
                    if (string.IsNullOrEmpty(target)) continue;

                    string newValue = MakePath(gameDir, target);
                    if (newValue != value)
                    {
                        lines[i] = key + "=" + newValue;
                        changed.Add(key);
                    }
                    continue;
                }

                // 字体类：写的是文件路径，绝对不能带结尾反斜杠
                if (Contains(FontKeys, key))
                {
                    if (string.IsNullOrEmpty(fontFile)) continue;

                    string np = MakeFilePath(gameDir, fontFile);
                    if (np != value)
                    {
                        lines[i] = key + "=" + np;
                        changed.Add(key);
                    }
                    continue;
                }

                // 预设文件路径：指向预设目录下那个 ini，让 ReShade 一上来就加载它。
                // 写的是文件路径，不能带结尾反斜杠。
                if (string.Equals(key, PresetPathKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(presetFile)) continue;

                    string np = MakeFilePath(gameDir, presetFile);
                    if (np != value)
                    {
                        lines[i] = key + "=" + np;
                        changed.Add(key);
                    }
                    continue;
                }

                // 截图目录：程序目录下没有 ScreenShot 就保留原值不动，
                // 免得把一个根本不存在的路径写进去（写进去 ReShade 会照用，
                // 截图就跑到别处去了）。
                if (string.Equals(key, ScreenshotKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(shotDir)) continue;

                    string np = MakePath(gameDir, shotDir);
                    if (np != value)
                    {
                        lines[i] = key + "=" + np;
                        changed.Add(key);
                    }
                    continue;
                }
            }

            // 新建出来的这份 ini，如果模板里没有这些键（比如以后模板又精简了），
            // 主动补进去 —— 不能等 ReShade 自己保存时才补它那套默认值。
            // 已有 ini 不补：那是用户自己的文件，缺什么都是他有意删的。
            if (created)
            {
                for (int f = 0; f < ForcedStyle.GetLength(0); f++)
                {
                    string fSection = ForcedStyle[f, 0];
                    string fKey = ForcedStyle[f, 1];
                    string fValue = ForcedStyle[f, 2];
                    if (HasKey(lines, fSection, fKey)) continue;

                    string[] grown = InsertKey(lines, fSection, fKey, fValue);
                    if (grown == lines) continue;   // 连这个段都没有，就不硬塞
                    lines = grown;
                    styleFixed.Add(fKey);
                }
            }

            // 预设路径是「路径类」而不是样式类：不管 ini 是新是旧都要有。
            // 老版本生成的 ini 里 PresetPath 是空的，用户自己存过预设的则指向别处，
            // 两种都统一改成当前预设目录下的那个 ini。
            if (!string.IsNullOrEmpty(presetFile) && !HasKey(lines, "GENERAL", PresetPathKey))
            {
                string[] grown = InsertKey(lines, "GENERAL", PresetPathKey,
                                           MakeFilePath(gameDir, presetFile));
                if (grown != lines)
                {
                    lines = grown;
                    changed.Add(PresetPathKey);
                }
            }

            if (changed.Count > 0 || styleFixed.Count > 0)
            {
                try
                {
                    string text = string.Join(isNewLine ? "\r\n" : "\n", lines);
                    File.WriteAllText(iniPath, text, new UTF8Encoding(false));

                    if (changed.Count > 0)
                        report.Add("已更新 ReShade.ini 的路径项：" + string.Join("、", changed.ToArray()));

                    if (styleFixed.Count > 0)
                        report.Add("已固定样式项：" + string.Join("、", styleFixed.ToArray()));
                }
                catch (Exception ex)
                {
                    error = "写回 ReShade.ini 失败：" + ex.Message;
                    return false;
                }
            }

            return true;
        }

        /// <summary>键名 → 该键对应的资源目录。</summary>
        private static string KeyToDir(string key, string addonDir, string shaderDir, string textureDir)
        {
            if (string.Equals(key, "AddonPath", StringComparison.OrdinalIgnoreCase)) return addonDir;
            if (string.Equals(key, "EffectSearchPaths", StringComparison.OrdinalIgnoreCase)) return shaderDir;
            if (string.Equals(key, "TextureSearchPaths", StringComparison.OrdinalIgnoreCase)) return textureDir;
            return null;
        }

        /// <summary>
        /// 在候选里挑第一个真实存在的目录。存在即用，不要求里面有文件 ——
        /// 这样可以先把路径指好，用户随后往里丢滤镜包就会立即生效。
        /// 全都不存在时返回 null，调用方跳过这一项（保留 ini 里的原值）。
        /// </summary>
        private static string PickDir(string appDir, string[] relatives)
        {
            for (int i = 0; i < relatives.Length; i++)
            {
                string full = Path.Combine(appDir, relatives[i]);
                if (Directory.Exists(full)) return full;
            }
            return null;
        }

        /// <summary>挑本工具 Fonts 下的字体文件，优先中文黑体。</summary>
        private static string PickFont(string appDir)
        {
            string[] dirs = new string[]
            {
                Path.Combine(appDir, "Fonts"),
                Path.Combine(appDir, "InjectResource", "Fonts"),
            };

            for (int i = 0; i < dirs.Length; i++)
            {
                if (!Directory.Exists(dirs[i])) continue;

                // 优先 msyhbd.ttc（微软雅黑加粗，中文界面最稳）
                string msyh = Path.Combine(dirs[i], "msyhbd.ttc");
                if (File.Exists(msyh)) return msyh;

                string[] fonts = Directory.GetFiles(dirs[i], "*.tt*", SearchOption.TopDirectoryOnly);
                if (fonts.Length > 0)
                {
                    Array.Sort(fonts, StringComparer.OrdinalIgnoreCase);
                    return fonts[0];
                }
            }
            return null;
        }

        /// <summary>
        /// 生成 ini 里该写的【目录】路径。同盘符给相对路径（整个目录树搬家后依然有效），
        /// 跨盘符相对路径表达不了，只能给绝对路径（但每次注入都会重算，所以搬家后自动修正）。
        /// 返回值统一以反斜杠结尾 —— ReShade 的搜索路径需要它。
        /// </summary>
        public static string MakePath(string fromDir, string toPath)
        {
            return MakeRelative(fromDir, toPath, true);
        }

        /// <summary>
        /// 生成 ini 里该写的【文件】路径（字体之类）。逻辑同上，但不能带结尾反斜杠 ——
        /// 带上以后 ReShade 会把它当目录，字体就加载不出来了。
        /// </summary>
        private static string MakeFilePath(string fromDir, string toFile)
        {
            return MakeRelative(fromDir, toFile, false);
        }

        private static string MakeRelative(string fromDir, string toPath, bool trailingSlash)
        {
            string from = Path.GetFullPath(fromDir).TrimEnd('\\');
            string to = Path.GetFullPath(toPath).TrimEnd('\\');
            string suffix = trailingSlash ? "\\" : "";

            string fromRoot = Path.GetPathRoot(from);
            string toRoot = Path.GetPathRoot(to);

            if (!string.Equals(fromRoot, toRoot, StringComparison.OrdinalIgnoreCase))
                return to + suffix;

            string a = from.Substring(fromRoot.Length);
            string b = to.Substring(toRoot.Length);

            string[] aSeg = a.Length == 0 ? new string[0] : a.Split('\\');
            string[] bSeg = b.Length == 0 ? new string[0] : b.Split('\\');

            int i = 0;
            while (i < aSeg.Length && i < bSeg.Length &&
                   string.Equals(aSeg[i], bSeg[i], StringComparison.OrdinalIgnoreCase))
            {
                i++;
            }

            var sb = new StringBuilder();
            for (int k = i; k < aSeg.Length; k++) sb.Append("..\\");
            for (int k = i; k < bSeg.Length; k++)
            {
                sb.Append(bSeg[k]);
                if (k < bSeg.Length - 1) sb.Append('\\');
            }

            if (sb.Length == 0) return trailingSlash ? ".\\" : ".";
            return sb.ToString() + suffix;
        }

        private static bool Contains(string[] list, string key)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i], key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}