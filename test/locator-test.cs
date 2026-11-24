// GameLocator 的真机验证：看它能不能从注册表/运行进程里认出游戏装在哪儿。
using System;

namespace HoYoInjector
{
    internal static class LocatorTest
    {
        private static int Main(string[] args)
        {
            string[] targets = args.Length > 0
                ? args
                : new string[] { "YuanShen", "GenshinImpact", "ZenlessZoneZero", "ZenlessZoneZeroBeta", "StarRail", "BH3" };

            int notFound = 0;
            foreach (string t in targets)
            {
                string source;
                string dir = GameLocator.Resolve(t, out source);
                if (string.IsNullOrEmpty(dir))
                {
                    Console.WriteLine(t.PadRight(20) + " → 没找到");
                    notFound++;
                }
                else
                {
                    Console.WriteLine(t.PadRight(20) + " → " + dir);
                    Console.WriteLine("".PadRight(20) + "   来源: " + source);

                    // 目录里那个 exe 是不是真存在
                    string exe = System.IO.Path.Combine(dir, t + ".exe");
                    Console.WriteLine("".PadRight(20) + "   " + t + ".exe 存在: " + System.IO.File.Exists(exe));
                }
            }

            Console.WriteLine();
            Console.WriteLine("没认出来的: " + notFound + " 个");
            return 0;
        }
    }
}