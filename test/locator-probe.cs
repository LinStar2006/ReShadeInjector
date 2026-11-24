// 临时探针：确认 GameLocator 能认到程序目录下 测试\ 里的靶子。
using System;
using HoYoInjector;

internal static class LocatorProbe
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int bad = 0;

        string[] names = new string[] { "target", "d3d-target", "YuanShen", "ZenlessZoneZero", "BH3" };

        for (int i = 0; i < names.Length; i++)
        {
            string source;
            string dir = GameLocator.Resolve(names[i], out source);
            string source2;
            string exe = GameLocator.ResolveExePath(names[i], out source2);

            Console.WriteLine(names[i]);
            Console.WriteLine("  目录 : " + (dir == null ? "(没找到)" : dir));
            Console.WriteLine("  来源 : " + (source == null ? "-" : source));
            Console.WriteLine("  exe  : " + (exe == null ? "(没找到)" : exe));
            Console.WriteLine("  exe存在: " + (exe != null && System.IO.File.Exists(exe)));
        }

        string s3;
        string targetDir = GameLocator.Resolve("target", out s3);
        if (targetDir == null || !targetDir.EndsWith("测试", StringComparison.Ordinal))
        {
            Console.WriteLine("[FAIL] target 应该落在 测试 目录，实际: " + targetDir);
            bad++;
        }
        else
        {
            Console.WriteLine("[OK] target 落在 测试 目录");
        }

        Console.Out.Flush();
        Console.WriteLine(bad == 0 ? "RESULT: ALL PASS" : "RESULT: " + bad + " FAILED");
        return bad == 0 ? 0 : 1;
    }
}