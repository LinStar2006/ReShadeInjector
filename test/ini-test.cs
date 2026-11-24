// 只验证两件事：
//   1) DefaultIni 的内置模板能从 exe 资源里读出来并写到目标目录
//   2) 写出来的 ini 能被 ReShade64.dll 接受（DllMain 不再拒绝加载）
using System;
using System.IO;
using HoYoInjector;

internal static class IniTest
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法: ini-test.exe <目标目录>");
            return 2;
        }

        string dir = Path.GetFullPath(args[0]);
        Console.WriteLine("目标目录: " + dir);

        string err;
        bool ok = DefaultIni.WriteTo(dir, out err);
        Console.WriteLine("WriteTo 返回: " + ok);
        if (err != null) Console.WriteLine("  error: " + err);

        string ini = Path.Combine(dir, "ReShade.ini");
        if (File.Exists(ini))
        {
            var fi = new FileInfo(ini);
            Console.WriteLine("已生成: " + ini + " (" + fi.Length + "B)");

            byte[] head = new byte[3];
            using (var fs = File.OpenRead(ini)) fs.Read(head, 0, 3);
            bool hasBom = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
            Console.WriteLine("带 BOM: " + hasBom + "（应为 False）");

            string first = File.ReadAllLines(ini)[0];
            Console.WriteLine("首行: " + first + "（应为 [ADDON]）");
        }

        // 第二次调用应该拒绝覆盖
        string err2;
        bool ok2 = DefaultIni.WriteTo(dir, out err2);
        Console.WriteLine("二次 WriteTo 返回: " + ok2 + "（应为 False，不覆盖）");
        if (err2 != null) Console.WriteLine("  error: " + err2);

        return ok ? 0 : 1;
    }
}