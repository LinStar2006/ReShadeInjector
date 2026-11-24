// 验证 GameLocator.ResolveExePath：等待期间再点一次按钮要能直接启动游戏，
// 依赖它拿到"游戏主程序 exe 的完整路径"，而不是只拿目录。
using System;
using System.Text;
using HoYoInjector;

internal static class LaunchProbe
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        string[] targets = args.Length > 0
            ? args
            : new string[] { "YuanShen", "ZenlessZoneZero", "StarRail", "BH3", "GenshinImpact" };

        int bad = 0;
        foreach (var t in targets)
        {
            string source;
            string exe = GameLocator.ResolveExePath(t, out source);

            if (string.IsNullOrEmpty(exe))
            {
                Console.WriteLine(t + " → 没找到（来源: " + (source ?? "-") + "）");
                continue;
            }

            bool exists = System.IO.File.Exists(exe);
            string name = System.IO.Path.GetFileName(exe);
            bool nameOk = name.Equals(t + ".exe", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(t + " → " + exe);
            Console.WriteLine("    来源: " + (source ?? "-") + "  exe存在: " + exists + "  文件名匹配: " + nameOk);

            if (!exists || !nameOk) bad++;
        }

        Console.WriteLine(bad == 0 ? "RESULT: ALL PASS" : "RESULT: " + bad + " FAILED");
        Console.Out.Flush();
        return bad == 0 ? 0 : 1;
    }
}