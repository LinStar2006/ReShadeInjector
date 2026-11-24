// 命令行 harness：直接复用 src\Injector.cs 里真实的 Injector.Inject 实现，
// 验证注入到指定 PID 是否成功。
using System;
using System.Diagnostics;
using System.IO;
using HoYoInjector;

internal static class InjectTest
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: inject-test.exe <目标PID> <DLL路径>");
            return 2;
        }

        int pid;
        if (!int.TryParse(args[0], out pid))
        {
            Console.WriteLine("PID 无效: " + args[0]);
            return 2;
        }

        string dllPath = args[1];
        if (!Path.IsPathRooted(dllPath)) dllPath = Path.GetFullPath(dllPath);

        Console.WriteLine("目标 PID: " + pid);
        Console.WriteLine("DLL 路径: " + dllPath);
        Console.WriteLine("当前提权: " + (Injector.IsElevated() ? "是（管理员）" : "否"));
        Console.WriteLine("可用调试权限: ");

        string privErr;
        bool priv = Injector.EnableDebugPrivilege(out privErr);
        Console.WriteLine("  " + (priv ? "已启用" : "未启用：" + privErr));
        Console.WriteLine();

        Action<string> log = delegate(string m) { Console.WriteLine("  [log] " + m); };

        bool ok = Injector.Inject(pid, dllPath, log);
        Console.WriteLine();
        Console.WriteLine(ok ? "RESULT: SUCCESS" : "RESULT: FAILED");
        return ok ? 0 : 1;
    }
}