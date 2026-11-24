// 验证「预设自带 preset ini」这条链路：
//   1. 预设目录里没有 ini → 生成「预设名.ini」，Techniques 列出 Shaders 里的 fx
//   2. 已经生成过 → 再调一次不覆盖
//   3. 已经有别的 ini（不叫 ReShade.ini）→ 直接用那个，不改名也不新建
//   4. ReShade.ini 的 PresetPath 指向它；已有 ini 里旧的 PresetPath 会被改写
//   5. 已有 ini 里根本没有 PresetPath 这个键 → 补进去
//
// 用法：preset-file-test.exe <测试根目录>
// 测试根目录必须放在工作区里（%TEMP% 会被沙箱拦），比如 injector-gui\test\_pftest
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class PresetFileTest
    {
        private static int _failed;

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch { }

            try
            {
                return Execute(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("未处理异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.Out.Flush();
                return 3;
            }
        }

        private static int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: preset-file-test.exe <测试根目录>");
                return 2;
            }

            string root = args[0];
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            Console.WriteLine("程序目录: " + appDir);
            Console.WriteLine("测试根目录: " + root);

            // 上一轮跑留下的 Shader\ 和 settings.ini 会让「新建」判断失真，先清掉
            try { if (Directory.Exists(Path.Combine(appDir, "Shader"))) Directory.Delete(Path.Combine(appDir, "Shader"), true); } catch { }
            try { if (File.Exists(Path.Combine(appDir, "settings.ini"))) File.Delete(Path.Combine(appDir, "settings.ini")); } catch { }

            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch { }
            Directory.CreateDirectory(root);

            string gameKey = "YuanShen";

            // ---------- 场景 A：目录里没有 ini，应生成「预设名.ini」 ----------
            Console.WriteLine();
            Console.WriteLine("=== A 没有 ini 时应生成预设名.ini ===");
            string dirA = PresetManager.PresetDir(gameKey, "TestA");
            Directory.CreateDirectory(Path.Combine(dirA, "Shaders"));
            Directory.CreateDirectory(Path.Combine(dirA, "Textures"));
            // fx 里必须有 technique 声明 —— ReShade 预设的 Techniques 写的是
            // 「技术名@文件名」，不是文件名本身。技术名常常和文件名不一样
            // （MartysMods_CLARITY - 细节锐化.fx 里的技术名是 MartysMods_Clarity），
            // 所以生成预设时必须扫 fx 内容。
            File.WriteAllText(Path.Combine(dirA, "Shaders", "aaa.fx"),
                "// a\r\ntechnique ProbeA\r\n{\r\n\tpass P0 { }\r\n}\r\n");
            File.WriteAllText(Path.Combine(dirA, "Shaders", "bbb.fx"),
                "// b\r\n// technique CommentedOut\r\ntechnique ProbeB\r\n{\r\n\tpass P0 { }\r\n}\r\n");
            Directory.CreateDirectory(Path.Combine(dirA, "Shaders", "Sub"));
            File.WriteAllText(Path.Combine(dirA, "Shaders", "Sub", "ccc.fx"),
                "// c\r\ntechnique ProbeC\r\n{\r\n\tpass P0 { }\r\n}\r\n");

            string error;
            string fileA = PresetManager.EnsurePresetFile(gameKey, "TestA", Path.Combine(dirA, "Shaders"), out error);
            Expect("返回了路径", fileA != null);
            Expect("路径是 预设目录下的 TestA.ini", fileA != null &&
                   string.Equals(Path.GetFileName(fileA), "TestA.ini", StringComparison.OrdinalIgnoreCase));
            Expect("文件真的建出来了", fileA != null && File.Exists(fileA));

            string textA = fileA != null && File.Exists(fileA) ? File.ReadAllText(fileA, Encoding.UTF8) : "";
            Console.WriteLine("  内容: " + textA.Replace("\r", "").Replace("\n", "\\n"));
            Expect("首行是 Techniques=", textA.StartsWith("Techniques=", StringComparison.Ordinal));
            Expect("有 TechniqueSorting=", textA.IndexOf("TechniqueSorting=", StringComparison.Ordinal) >= 0);
            Expect("aaa.fx 写成 技术名@文件名",
                   textA.IndexOf("ProbeA@aaa.fx", StringComparison.OrdinalIgnoreCase) >= 0);
            Expect("bbb.fx 写成 技术名@文件名",
                   textA.IndexOf("ProbeB@bbb.fx", StringComparison.OrdinalIgnoreCase) >= 0);
            Expect("子目录里的写成 技术名@子目录\\文件名",
                   textA.IndexOf("ProbeC@Sub\\ccc.fx", StringComparison.OrdinalIgnoreCase) >= 0);
            Expect("注释掉的 technique 没被列进去",
                   textA.IndexOf("CommentedOut", StringComparison.Ordinal) < 0);
            Expect("没写成裸文件名（每一项都该带 @）",
                   textA.IndexOf(",aaa.fx", StringComparison.OrdinalIgnoreCase) < 0 &&
                   textA.IndexOf("=aaa.fx", StringComparison.OrdinalIgnoreCase) < 0);
            Expect("不带 BOM", fileA != null && !HasBom(fileA));

            // ---------- 场景 B：再调一次不覆盖 ----------
            Console.WriteLine();
            Console.WriteLine("=== B 已有预设文件时不该覆盖 ===");
            File.WriteAllText(fileA, "Techniques=UserEdited\r\n", new UTF8Encoding(false));
            string fileB = PresetManager.EnsurePresetFile(gameKey, "TestA", Path.Combine(dirA, "Shaders"), out error);
            Expect("返回同一个路径", string.Equals(fileA, fileB, StringComparison.OrdinalIgnoreCase));
            string textB = File.ReadAllText(fileB, Encoding.UTF8);
            Expect("用户改过的内容还在", textB.IndexOf("UserEdited", StringComparison.Ordinal) >= 0);

            // ---------- 场景 C：目录里已有别的 ini，用那个 ----------
            Console.WriteLine();
            Console.WriteLine("=== C 已有别的 ini 时直接用那个 ===");
            string dirC = PresetManager.PresetDir(gameKey, "TestC");
            Directory.CreateDirectory(Path.Combine(dirC, "Shaders"));
            File.WriteAllText(Path.Combine(dirC, "MyPack.ini"), "Techniques=X\r\n", new UTF8Encoding(false));
            string fileC = PresetManager.EnsurePresetFile(gameKey, "TestC", Path.Combine(dirC, "Shaders"), out error);
            Expect("返回了路径", fileC != null);
            Expect("用的是 MyPack.ini", fileC != null &&
                   string.Equals(Path.GetFileName(fileC), "MyPack.ini", StringComparison.OrdinalIgnoreCase));
            Expect("没有另外造 TestC.ini", !File.Exists(Path.Combine(dirC, "TestC.ini")));

            // ---------- 场景 D：ReShade.ini 里 PresetPath 指过去 ----------
            Console.WriteLine();
            Console.WriteLine("=== D ReShade.ini 的 PresetPath 指向预设文件 ===");
            string gameDir = Path.Combine(root, "Game");
            Directory.CreateDirectory(gameDir);

            var report = new List<string>();
            Expect("Ensure 成功", IniPatcher.Ensure(gameDir, gameKey, "TestA", out report, out error));
            DumpReport(report);

            string ini = Path.Combine(gameDir, "ReShade.ini");
            Expect("ReShade.ini 已生成", File.Exists(ini));
            string iniText = File.ReadAllText(ini, Encoding.UTF8);
            string presetPath = Grab(iniText, "PresetPath");
            Console.WriteLine("  PresetPath = " + presetPath);
            Expect("PresetPath 非空", presetPath != null && presetPath.Length > 0);
            Expect("指向 TestA.ini", presetPath != null &&
                   presetPath.EndsWith("TestA.ini", StringComparison.OrdinalIgnoreCase));
            Expect("不是以反斜杠结尾（那是目录的写法）", presetPath != null && !presetPath.EndsWith("\\", StringComparison.Ordinal));
            Expect("确实指向存在的文件", ResolvesTo(presetPath, gameDir));

            // ---------- 场景 E：已有 ini 里的旧 PresetPath 会被改写 ----------
            Console.WriteLine();
            Console.WriteLine("=== E 已有 ini 的旧 PresetPath 会被改写 ===");
            string[] oldLines = new string[]
            {
                "[GENERAL]",
                "PresetPath=C:\\old\\Somewhere\\Old.ini",
                "PerformanceMode=1",
                "[STYLE]",
                "FontSize=17",
                "StyleIndex=2",
                "",
            };
            File.WriteAllText(ini, string.Join("\r\n", oldLines), new UTF8Encoding(false));

            report = new List<string>();
            Expect("Ensure 成功（第二轮）", IniPatcher.Ensure(gameDir, gameKey, "TestA", out report, out error));
            DumpReport(report);

            iniText = File.ReadAllText(ini, Encoding.UTF8);
            presetPath = Grab(iniText, "PresetPath");
            Console.WriteLine("  改后 PresetPath = " + presetPath);
            Expect("不再指向 C:\\old", presetPath != null &&
                   presetPath.IndexOf("C:\\old", StringComparison.OrdinalIgnoreCase) < 0);
            Expect("改成了 TestA.ini", presetPath != null &&
                   presetPath.EndsWith("TestA.ini", StringComparison.OrdinalIgnoreCase));
            Expect("样式项 FontSize=17 没被动", string.Equals(Grab(iniText, "FontSize"), "17", StringComparison.Ordinal));
            Expect("样式项 StyleIndex=2 没被动", string.Equals(Grab(iniText, "StyleIndex"), "2", StringComparison.Ordinal));
            Expect("PerformanceMode=1 还在", string.Equals(Grab(iniText, "PerformanceMode"), "1", StringComparison.Ordinal));

            // ---------- 场景 F：ini 里没有 PresetPath 键 → 补进去 ----------
            Console.WriteLine();
            Console.WriteLine("=== F 没有 PresetPath 键时补进去 ===");
            string[] noPresetLines = new string[]
            {
                "[GENERAL]",
                "PerformanceMode=1",
                "",
                "[STYLE]",
                "FontSize=17",
                "",
            };
            File.WriteAllText(ini, string.Join("\r\n", noPresetLines), new UTF8Encoding(false));

            report = new List<string>();
            Expect("Ensure 成功（第三轮）", IniPatcher.Ensure(gameDir, gameKey, "TestA", out report, out error));
            DumpReport(report);

            iniText = File.ReadAllText(ini, Encoding.UTF8);
            presetPath = Grab(iniText, "PresetPath");
            Console.WriteLine("  补上的 PresetPath = " + presetPath);
            Expect("键被补进去了", presetPath != null && presetPath.Length > 0);
            Expect("指向 TestA.ini", presetPath != null &&
                   presetPath.EndsWith("TestA.ini", StringComparison.OrdinalIgnoreCase));
            Expect("FontSize=17 没被动", string.Equals(Grab(iniText, "FontSize"), "17", StringComparison.Ordinal));

            Console.WriteLine();
            if (_failed == 0)
            {
                Console.WriteLine("RESULT: ALL PASS");
                Console.Out.Flush();
                return 0;
            }

            Console.WriteLine("RESULT: " + _failed + " FAILED");
            Console.Out.Flush();
            return 1;
        }

        private static void DumpReport(List<string> report)
        {
            for (int i = 0; i < report.Count; i++) Console.WriteLine("  - " + report[i]);
        }

        private static void Expect(string what, bool ok)
        {
            Console.WriteLine((ok ? "[OK]   " : "[FAIL] ") + what);
            if (!ok) _failed++;
        }

        private static bool HasBom(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            return b.Length >= 3 && b[0] == 239 && b[1] == 187 && b[2] == 191;
        }

        /// <summary>按行取 key= 的值（第一个匹配）。没有返回 null。</summary>
        private static string Grab(string text, string key)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return lines[i].Substring(eq + 1).Trim();
            }
            return null;
        }

        /// <summary>把 ini 里的路径（可能是相对游戏目录的）解析成绝对路径，判断文件在不在。</summary>
        private static bool ResolvesTo(string path, string gameDir)
        {
            try
            {
                string full = Path.IsPathRooted(path) ? path : Path.Combine(gameDir, path);
                return File.Exists(Path.GetFullPath(full));
            }
            catch
            {
                return false;
            }
        }
    }
}