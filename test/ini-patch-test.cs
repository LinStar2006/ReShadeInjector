// IniPatcher 的行为验证：只改路径项，绝不碰用户的样式/快捷键配置。
//
// 三个场景：
//   A. 目录里没有 ini          → 用模板生成；程序目录有真滤镜包就指向它，否则保留模板默认值
//   B. 有 ini 但路径已失效     → 只改失效的路径项，[STYLE]/[OVERLAY]/[INPUT] 原样保留
//   C. 有 ini 且路径都有效     → 一个字节都不改（尊重用户已有配置）
//
// 另外会打印「程序目录里到底有什么」，方便对照断言为什么那样判。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class IniPatchTest
    {
        private static readonly string[] AddonCand = { "Addons", @"Shader\Default\Addons", @"reshade-shaders\Addons" };
        private static readonly string[] ShaderCand = { @"Shader\Default\Shaders", @"Shader\Shaders", @"reshade-shaders\Shaders" };
        private static readonly string[] TextureCand = { @"Shader\Default\Textures", @"Shader\Textures", @"reshade-shaders\Textures" };

        private static int Main(string[] args)
        {
            try
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }

                if (args.Length < 1)
                {
                    Console.WriteLine("用法: ini-patch-test.exe <测试根目录>");
                    return 2;
                }

                string root = Path.GetFullPath(args[0]);
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(root);

                Console.WriteLine("程序目录: " + AppDomain.CurrentDomain.BaseDirectory);
                Console.WriteLine("插件目录有内容: " + HasAddons());
                Console.WriteLine("效果目录有内容: " + HasShaders());
                Console.WriteLine("纹理目录有内容: " + HasTextures());

                int failed = 0;
                failed += CaseA(Path.Combine(root, "A-empty"));
                failed += CaseB(Path.Combine(root, "B-stale"));
                failed += CaseC(Path.Combine(root, "C-valid"));

                Console.WriteLine();
                Console.WriteLine(failed == 0 ? "RESULT: ALL PASS" : "RESULT: " + failed + " FAILED");
                Console.Out.Flush();
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("未处理异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.Out.Flush();
                return 3;
            }
        }

        // ---------- A：目录里没有 ini，应生成模板 ----------
        private static int CaseA(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("===== A：空目录，应自动生成 ini =====");
            Directory.CreateDirectory(dir);

            List<string> report;
            string err;
            bool ok = IniPatcher.Ensure(dir, out report, out err);
            Report(ok, err, report);

            string ini = Path.Combine(dir, "ReShade.ini");
            if (!File.Exists(ini)) { Console.WriteLine("  [FAIL] ini 没生成"); return 1; }

            string text = File.ReadAllText(ini, Encoding.UTF8);
            Console.WriteLine("  生成大小: " + text.Length + " 字符");

            int bad = 0;
            foreach (string k in new string[] { "AddonPath", "EffectSearchPaths", "TextureSearchPaths", "SavePath" })
            {
                string v = GetValue(text, k);
                Console.WriteLine("  " + k + " = " + v);
                if (string.IsNullOrEmpty(v)) { Console.WriteLine("  [FAIL] " + k + " 为空"); bad++; }
            }
            if (bad > 0) return 1;

            bad += CheckPathItem(text, dir, "AddonPath", HasAddons(), true);
            bad += CheckPathItem(text, dir, "EffectSearchPaths", HasShaders(), true);
            bad += CheckPathItem(text, dir, "TextureSearchPaths", HasTextures(), true);

            Console.WriteLine(bad == 0 ? "  [OK] 路径项都合理" : "  [FAIL] 有 " + bad + " 处不符合预期");
            return ok && bad == 0 ? 0 : 1;
        }

        // ---------- B：有 ini，路径指向别的地方（模拟用户从别处拷来的配置） ----------
        private static int CaseB(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("===== B：已有 ini，路径失效，应只改路径项 =====");
            Directory.CreateDirectory(dir);

            // 故意写一份「路径全失效、但样式配置有用户自定义值」的 ini
            string original =
                "[ADDON]\r\n" +
                "AddonPath=D:\\不存在的滤镜包\\Addons\r\n" +
                "\r\n" +
                "[GENERAL]\r\n" +
                "EffectSearchPaths=D:\\不存在的滤镜包\\Shaders\\\r\n" +
                "TextureSearchPaths=D:\\不存在的滤镜包\\Textures\\\r\n" +
                "PresetPath=D:\\不存在的滤镜包\\Mod OFF.ini\r\n" +
                "PerformanceMode=1\r\n" +
                "\r\n" +
                "[INPUT]\r\n" +
                "KeyOverlay=123,0,0,0\r\n" +
                "KeyEffects=45,0,0,0\r\n" +
                "\r\n" +
                "[OVERLAY]\r\n" +
                "ShowFPS=0\r\n" +
                "TutorialProgress=3\r\n" +
                "VariableListHeight=520.000000\r\n" +
                "\r\n" +
                "[STYLE]\r\n" +
                "StyleIndex=7\r\n" +
                "FontSize=22\r\n" +
                "Alpha=0.850000\r\n" +
                "TabRounding=9.000000\r\n";

            string ini = Path.Combine(dir, "ReShade.ini");
            File.WriteAllText(ini, original, new UTF8Encoding(false));

            List<string> report;
            string err;
            bool ok = IniPatcher.Ensure(dir, out report, out err);
            Report(ok, err, report);

            string after = File.ReadAllText(ini, Encoding.UTF8);

            // 断言 1：用户自定义的样式/快捷键/overlay 一项都不能被改
            int bad = 0;
            bad += Expect(after, "KeyOverlay=123,0,0,0", "用户快捷键 KeyOverlay");
            bad += Expect(after, "KeyEffects=45,0,0,0", "用户快捷键 KeyEffects");
            bad += Expect(after, "ShowFPS=0", "用户 overlay ShowFPS");
            bad += Expect(after, "TutorialProgress=3", "用户 overlay TutorialProgress");
            bad += Expect(after, "VariableListHeight=520.000000", "用户 overlay 布局");
            bad += Expect(after, "StyleIndex=7", "用户样式 StyleIndex");
            bad += Expect(after, "FontSize=22", "用户字号 FontSize");
            bad += Expect(after, "Alpha=0.850000", "用户 Alpha");
            bad += Expect(after, "TabRounding=9.000000", "用户圆角 TabRounding");
            bad += Expect(after, "PerformanceMode=1", "用户 PerformanceMode");

            // 断言 2：PresetPath 不在我们的路径白名单里，也必须原样保留
            bad += Expect(after, "PresetPath=D:\\不存在的滤镜包\\Mod OFF.ini", "非白名单项的 PresetPath");

            // 断言 3：失效的路径项 —— 程序目录有真滤镜包就修成本工具的实际位置，
            // 没有就保持原样（宁可不动，也不要指向另一个不存在的路径）
            bad += CheckPathItem(after, dir, "AddonPath", HasAddons(), false);
            bad += CheckPathItem(after, dir, "EffectSearchPaths", HasShaders(), false);
            bad += CheckPathItem(after, dir, "TextureSearchPaths", HasTextures(), false);

            // 断言 4：报告里要讲清楚这一项怎么处理的
            bool mentioned = false;
            if (report != null)
            {
                for (int i = 0; i < report.Count; i++)
                {
                    if (report[i].IndexOf("路径项", StringComparison.Ordinal) >= 0
                        || report[i].IndexOf("保持原样", StringComparison.Ordinal) >= 0
                        || report[i].IndexOf("已更新", StringComparison.Ordinal) >= 0)
                    { mentioned = true; break; }
                }
            }
            if (!mentioned) { Console.WriteLine("  [FAIL] 报告里没提到路径项的处理结果"); bad++; }

            Console.WriteLine(bad == 0 ? "  [OK] 路径项处理符合预期，用户配置全部保留" : "  [FAIL] 有 " + bad + " 处不符合预期");
            return (ok && bad == 0) ? 0 : 1;
        }

        // ---------- C：有 ini 且路径有效，应一个字节都不改 ----------
        private static int CaseC(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("===== C：已有 ini 且路径有效，应完全不动 =====");
            Directory.CreateDirectory(dir);

            // 建一个真实存在且里面有内容的资源目录，让 ini 里的相对路径站得住
            string realShaders = Path.Combine(dir, "my-shaders");
            Directory.CreateDirectory(realShaders);
            File.WriteAllText(Path.Combine(realShaders, "dummy.fx"), "// dummy", new UTF8Encoding(false));

            string original =
                "[ADDON]\r\n" +
                "AddonPath=.\\my-shaders\r\n" +
                "\r\n" +
                "[GENERAL]\r\n" +
                "EffectSearchPaths=.\\my-shaders\\\r\n" +
                "TextureSearchPaths=.\\my-shaders\\\r\n" +
                "\r\n" +
                "[STYLE]\r\n" +
                "StyleIndex=4\r\n";

            string ini = Path.Combine(dir, "ReShade.ini");
            File.WriteAllText(ini, original, new UTF8Encoding(false));
            byte[] before = File.ReadAllBytes(ini);

            List<string> report;
            string err;
            bool ok = IniPatcher.Ensure(dir, out report, out err);
            Report(ok, err, report);

            byte[] after = File.ReadAllBytes(ini);
            bool same = before.Length == after.Length;
            if (same)
            {
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != after[i]) { same = false; break; }
                }
            }

            Console.WriteLine(same ? "  [OK] 文件内容一个字节都没变" : "  [FAIL] 文件被改动了");
            Console.WriteLine("  报告: " + (report == null || report.Count == 0 ? "（无）" : string.Join(" | ", report.ToArray())));
            return (ok && same) ? 0 : 1;
        }

        // ---------- 小工具 ----------
        /// <summary>
        /// 检查某个路径项最终是否合理：
        ///   程序目录里有真内容 → 必须改成本工具的实际位置（指得到真目录）
        ///   否则             → 必须保持原样（created 时则是保留模板默认值）
        /// </summary>
        private static int CheckPathItem(string text, string gameDir, string key, bool appHasContent, bool created)
        {
            string v = GetValue(text, key);
            Console.WriteLine("  " + key + " = " + v + "   [程序目录有内容=" + appHasContent + "]");

            if (appHasContent)
            {
                if (!ResolvesIn(gameDir, v))
                {
                    Console.WriteLine("  [FAIL] " + key + " 应指向本工具的实际目录，但它解析不到（" + v + "）");
                    return 1;
                }
                return 0;
            }

            if (created)
            {
                // 新建的 ini 保留模板默认值，不能编一个死路径出来
                if (v == null || v.IndexOf("reshade-shaders", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Console.WriteLine("  [FAIL] " + key + " 应保留模板默认值（.\\reshade-shaders\\...）");
                    return 1;
                }
                return 0;
            }

            // 已有 ini：原样不动
            if (v == null || v.IndexOf("不存在的滤镜包", StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("  [FAIL] " + key + " 不该被改动（程序目录没有内容可指）");
                return 1;
            }
            return 0;
        }

        private static bool HasAddons() { return HasContentIn(AppDomain.CurrentDomain.BaseDirectory, AddonCand, "*.addon64"); }
        private static bool HasShaders() { return HasContentIn(AppDomain.CurrentDomain.BaseDirectory, ShaderCand, "*.fx"); }
        private static bool HasTextures() { return HasContentIn(AppDomain.CurrentDomain.BaseDirectory, TextureCand, "*.png"); }

        private static bool HasContentIn(string appDir, string[] relatives, string pattern)
        {
            for (int i = 0; i < relatives.Length; i++)
            {
                string full = Path.Combine(appDir, relatives[i]);
                if (!Directory.Exists(full)) continue;
                try
                {
                    if (Directory.GetFiles(full, pattern, SearchOption.AllDirectories).Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>按游戏目录解析 ini 里的路径，判断它指得到真实目录。</summary>
        private static bool ResolvesIn(string gameDir, string iniPath)
        {
            if (string.IsNullOrEmpty(iniPath)) return false;
            string p = iniPath.Trim();
            try
            {
                string full = Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(gameDir, p));
                return Directory.Exists(full) || Directory.Exists(full.TrimEnd('\\'));
            }
            catch { return false; }
        }

        private static void Report(bool ok, string err, List<string> report)
        {
            Console.WriteLine("  Ensure 返回: " + ok);
            if (!string.IsNullOrEmpty(err)) Console.WriteLine("  error: " + err);
            if (report != null)
            {
                for (int i = 0; i < report.Count; i++) Console.WriteLine("  - " + report[i]);
            }
        }

        private static int Expect(string text, string mustContain, string what)
        {
            if (text.IndexOf(mustContain, StringComparison.Ordinal) >= 0) return 0;
            Console.WriteLine("  [FAIL] 丢了 " + what + "（期望包含: " + mustContain + "）");
            return 1;
        }

        /// <summary>从 ini 文本里取某个键的值（取第一个匹配，忽略大小写）。</summary>
        private static string GetValue(string text, string key)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
            return null;
        }
    }
}