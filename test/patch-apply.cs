// 对任意一个目录跑一遍 IniPatcher.Ensure，用来拿真实数据验证。
// 用法: patch-apply.exe <游戏目录> [--show]
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HoYoInjector
{
    internal static class PatchApply
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: patch-apply.exe <游戏目录> [--show]");
                return 2;
            }

            string dir = Path.GetFullPath(args[0]);
            bool show = args.Length > 1 && args[1] == "--show";

            Console.WriteLine("游戏目录: " + dir);

            string ini = Path.Combine(dir, "ReShade.ini");
            bool existedBefore = File.Exists(ini);
            byte[] before = existedBefore ? File.ReadAllBytes(ini) : null;
            Console.WriteLine("原 ini: " + (existedBefore ? before.Length + " 字节" : "不存在（会新建）"));

            List<string> report;
            string err;
            bool ok = IniPatcher.Ensure(dir, out report, out err);
            Console.WriteLine("Ensure 返回: " + ok);
            if (!string.IsNullOrEmpty(err)) Console.WriteLine("error: " + err);
            if (report != null)
            {
                for (int i = 0; i < report.Count; i++) Console.WriteLine("  - " + report[i]);
            }

            byte[] after = File.Exists(ini) ? File.ReadAllBytes(ini) : null;
            if (after == null) { Console.WriteLine("结果: ini 没了？！"); return 1; }

            if (before == null)
            {
                Console.WriteLine("结果: 新建了 " + after.Length + " 字节");
            }
            else
            {
                bool same = before.Length == after.Length;
                if (same)
                {
                    for (int i = 0; i < before.Length; i++)
                    {
                        if (before[i] != after[i]) { same = false; break; }
                    }
                }
                Console.WriteLine("结果: " + (same
                    ? "一个字节都没改（原 " + before.Length + " 字节）"
                    : "改动了（" + before.Length + " → " + after.Length + " 字节）"));
            }

            if (show)
            {
                Console.WriteLine();
                Console.WriteLine("--- 现在的 ini ---");
                Console.WriteLine(File.ReadAllText(ini, Encoding.UTF8));
            }

            return ok ? 0 : 1;
        }
    }
}