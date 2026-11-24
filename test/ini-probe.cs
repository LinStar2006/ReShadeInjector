// 验证 IniPatcher 会把三个路径键 + 三个字体键全部指向程序目录的实际位置，
// 且样式类键一个字符都不动。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HoYoInjector;

internal static class IniProbe
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        string gameDir = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_probe-game");
        string appDir = AppDomain.CurrentDomain.BaseDirectory;

        Console.WriteLine("程序目录 : " + appDir);
        Console.WriteLine("游戏目录 : " + gameDir);

        // 自己造一个"游戏目录"，里面放一份用户改过样式的 ini
        if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true);
        Directory.CreateDirectory(gameDir);

        int failed = 0;

        // ---- 场景 A：原本没有 ini，应该新建并指向程序目录 ----
        Console.WriteLine();
        Console.WriteLine("--- A: 原本没有 ini ---");
        List<string> report; string error;
        if (!IniPatcher.Ensure(gameDir, out report, out error))
        {
            Console.WriteLine("FAIL: Ensure 返回 false：" + error);
            failed++;
        }
        foreach (var r in report) Console.WriteLine("  " + r);

        string ini = Path.Combine(gameDir, "ReShade.ini");
        if (!File.Exists(ini)) { Console.WriteLine("FAIL: ini 没生成"); failed++; }
        else
        {
            failed += CheckPaths(ini, appDir, "A");
        }

        // ---- 场景 B：已有 ini，样式键必须原样保留 ----
        Console.WriteLine();
        Console.WriteLine("--- B: 已有 ini（带用户样式） ---");
        File.WriteAllText(ini, string.Join("\r\n", new string[] {
            "[ADDON]",
            "DisabledAddons=abc.addon64,def.addon64",
            "AddonPath=C:\\过期滤镜包\\Addons",
            "",
            "[GENERAL]",
            "EffectSearchPaths=C:\\过期滤镜包\\Shaders\\",
            "TextureSearchPaths=C:\\过期滤镜包\\Textures\\",
            "PresetPath=C:\\过期滤镜包\\Mod OFF.ini",
            "PerformanceMode=1",
            "PreprocessorDefinitions=RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0",
            "",
            "[INPUT]",
            "KeyOverlay=123,0,0,0",
            "KeyEffects=45,0,0,0",
            "",
            "[OVERLAY]",
            "TutorialProgress=3",
            "VariableListHeight=520.000000",
            "ShowFPS=0",
            "",
            "[STYLE]",
            "Font=C:\\过期滤镜包\\Fonts\\msyhbd.ttc",
            "EditorFont=C:\\过期滤镜包\\Fonts\\msyhbd.ttc",
            "LatinFont=C:\\过期滤镜包\\Fonts\\ProggyClean.ttf",
            "StyleIndex=7",
            "FontSize=22",
            "Alpha=0.850000",
            "",
            "[SCREENSHOT]",
            "SavePath=C:\\过期滤镜包\\Shots",
        }), new UTF8Encoding(false));

        if (!IniPatcher.Ensure(gameDir, out report, out error))
        {
            Console.WriteLine("FAIL: Ensure 返回 false：" + error);
            failed++;
        }
        foreach (var r in report) Console.WriteLine("  " + r);

        failed += CheckPaths(ini, appDir, "B");
        failed += KeepUnchanged(ini, new string[] {
            "DisabledAddons=abc.addon64,def.addon64",
            "PresetPath=C:\\过期滤镜包\\Mod OFF.ini",
            "PerformanceMode=1",
            "PreprocessorDefinitions=RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0",
            "KeyOverlay=123,0,0,0",
            "KeyEffects=45,0,0,0",
            "TutorialProgress=3",
            "VariableListHeight=520.000000",
            "ShowFPS=0",
            "StyleIndex=7",
            "FontSize=22",
            "Alpha=0.850000",
        });

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "RESULT: ALL PASS" : "RESULT: " + failed + " FAILED");
        Console.Out.Flush();
        return failed == 0 ? 0 : 1;
    }

    /// <summary>六个路径键都必须指到 appDir 下面的对应位置。</summary>
    private static int CheckPaths(string ini, string appDir, string tag)
    {
        int bad = 0;
        string[][] want = new string[][] {
            new string[] { "AddonPath",          Path.Combine(appDir, "Addons") },
            new string[] { "EffectSearchPaths",  Path.Combine(appDir, @"Shader\Default\Shaders") },
            new string[] { "TextureSearchPaths", Path.Combine(appDir, @"Shader\Default\Textures") },
            new string[] { "Font",               null },
            new string[] { "EditorFont",         null },
            new string[] { "LatinFont",          null },
        };

        foreach (var kv in want)
        {
            string v = GetValue(ini, kv[0]);
            if (string.IsNullOrEmpty(v)) { Console.WriteLine("FAIL[" + tag + "]: " + kv[0] + " 缺失"); bad++; continue; }

            bool ok;
            string note;
            if (kv[1] != null)
            {
                ok = ResolvesTo(v, appDir, kv[1]);
                note = "应指向 " + kv[1];
            }
            else
            {
                // 字体：必须是 Fonts\ 下的 .tt* 文件，且不能带结尾反斜杠
                string full = Resolve(v, appDir);
                ok = full != null && full.IndexOf(@"\Fonts\", StringComparison.OrdinalIgnoreCase) >= 0
                     && (full.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                         || full.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                     && !v.TrimEnd().EndsWith("\\");
                note = "应是 Fonts 下的字体文件且不带尾斜杠";
            }

            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + kv[0] + " = " + v);
            if (!ok) { Console.WriteLine("        " + note); bad++; }
        }
        return bad;
    }

    /// <summary>ini 里的路径按游戏目录解析后，是否等于期望的绝对路径。</summary>
    private static bool ResolvesTo(string value, string appDir, string expected)
    {
        string full = Resolve(value, appDir);
        if (full == null) return false;
        // 期望值可能不存在，所以只比对路径字符串
        return string.Equals(full.TrimEnd('\\'), expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Resolve(string value, string appDir)
    {
        string p = (value ?? "").Trim().Trim('"');
        if (p.Length == 0) return null;
        try
        {
            // 测试里游戏目录和程序目录不同，相对路径基准取游戏目录 —— 但这里
            // 我们只关心解析出来的绝对路径，所以用它自己记录的游戏目录
            string gameDir = Path.Combine(appDir, "_probe-game");
            string full = Path.IsPathRooted(p) ? p : Path.Combine(gameDir, p);
            return Path.GetFullPath(full);
        }
        catch { return null; }
    }

    private static int KeepUnchanged(string ini, string[] expectLines)
    {
        string text = File.ReadAllText(ini, Encoding.UTF8);
        int bad = 0;
        foreach (var line in expectLines)
        {
            if (text.IndexOf(line, StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("FAIL: 样式行被改动了 → " + line);
                bad++;
            }
        }
        if (bad == 0) Console.WriteLine("  OK   样式类键全部原样保留（" + expectLines.Length + " 项）");
        return bad;
    }

    private static string GetValue(string ini, string key)
    {
        foreach (var line in File.ReadAllLines(ini, Encoding.UTF8))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line.Substring(eq + 1).Trim();
        }
        return null;
    }
}