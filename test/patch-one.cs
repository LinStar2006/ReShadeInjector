// 手动补一次 ReShade.ini：patch-one.exe <游戏目录> [游戏键] [预设名]
// 用来验证 IniPatcher 的路径改写真、以及样式项的两种行为：
//   新建出来的 ini  -> 样式项按固定值写（干净外观）
//   用户已有的 ini  -> 样式项一个字符都不动
using System;
using System.Collections.Generic;
using System.Text;
using HoYoInjector;

internal static class PatchOne
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 1)
        {
            Console.WriteLine("用法: patch-one.exe <游戏目录> [游戏键] [预设名]");
            return 2;
        }

        string gameDir = args[0];
        string gameKey = args.Length > 1 ? args[1] : null;
        string preset = args.Length > 2 ? args[2] : null;

        string iniPath = System.IO.Path.Combine(gameDir, "ReShade.ini");
        bool existedBefore = System.IO.File.Exists(iniPath);
        string before = existedBefore ? System.IO.File.ReadAllText(iniPath, Encoding.UTF8) : "";

        List<string> report;
        string error;
        bool ok = IniPatcher.Ensure(gameDir, gameKey, preset, out report, out error);

        Console.WriteLine("目录: " + gameDir);
        Console.WriteLine("游戏键: " + (gameKey == null ? "(无)" : gameKey) +
                          "  预设: " + (preset == null ? "(无)" : preset));
        Console.WriteLine("改前已有 ini: " + (existedBefore ? "是（样式项应保持原样）" : "否（应写入固定值）"));
        Console.WriteLine("Ensure 返回: " + ok);
        if (error != null) Console.WriteLine("error: " + error);
        for (int i = 0; i < report.Count; i++) Console.WriteLine("  - " + report[i]);

        string after = System.IO.File.ReadAllText(iniPath, Encoding.UTF8);
        Console.WriteLine("字节: " + before.Length + " -> " + after.Length);

        string[] watch = new string[]
        {
            "KeyScreenshot", "Alpha", "StyleIndex", "EditorStyleIndex",
            "TabRounding", "ChildRounding", "FrameRounding", "PopupRounding",
            "WindowRounding", "ScrollbarRounding", "GrabRounding",
        };

        string[,] fixedValue = new string[,]
        {
            { "KeyScreenshot",    "0,0,0,0" },
            { "Alpha",            "0.850000" },
            { "StyleIndex",       "1" },        // 1 = Light
            { "EditorStyleIndex", "0" },        // 0 = Dark
            { "TabRounding",      "8.000000" },
        };

        bool pass = true;

        if (!existedBefore)
        {
            Console.WriteLine("--- 新建 ini：样式项应为固定值 ---");
            for (int i = 0; i < fixedValue.GetLength(0); i++)
            {
                string key = fixedValue[i, 0];
                string want = fixedValue[i, 1];
                string got = Grab(after, key);
                if (got == null)
                {
                    Console.WriteLine("  " + key + " = (没有这一项)  期望 " + want);
                    pass = false;
                    continue;
                }
                bool valueOk = string.Equals(got.Trim(), want, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine("  " + key + " = " + got + (valueOk ? "  OK" : "  期望 " + want));
                if (!valueOk) pass = false;
            }
        }
        else
        {
            Console.WriteLine("--- 已有 ini：样式项应一个字符都没动 ---");
            for (int i = 0; i < watch.Length; i++)
            {
                string key = watch[i];
                string oldValue = Grab(before, key);
                string newValue = Grab(after, key);
                bool same = (oldValue == null && newValue == null) ||
                            (oldValue != null && newValue != null &&
                             string.Equals(oldValue, newValue, StringComparison.Ordinal));
                Console.WriteLine("  " + key + " : " + Describe(oldValue) + " -> " + Describe(newValue) +
                                  (same ? "  未变" : "  被改了！"));
                if (!same) pass = false;
            }
        }

        Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
        return pass ? 0 : 1;
    }

    private static string Describe(string v)
    {
        return v == null ? "(无)" : v.Trim();
    }

    private static string Grab(string text, string key)
    {
        if (text == null) return null;
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq <= 0) continue;
            if (string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                return lines[i].Substring(eq + 1);
        }
        return null;
    }
}