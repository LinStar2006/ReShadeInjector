// 命令行 harness：验证「等待进程启动」这条路径。
// 用法: wait-test.exe <进程名> <DLL路径>
// 做法：主线程先开始等（此时目标进程还不存在），后台线程 3 秒后模拟启动器
// 把同目录的 target.exe 拉起来，验证注入器能在进程出现的那一刻捕获并注入。
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HoYoInjector;

internal static class WaitTest
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: wait-test.exe <进程名> <DLL路径>");
            return 2;
        }

        string procName = args[0];
        string dllPath = args[1];
        if (!Path.IsPathRooted(dllPath)) dllPath = Path.GetFullPath(dllPath);

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string targetPath = Path.Combine(baseDir, "target.exe");
        if (!File.Exists(targetPath))
        {
            Console.WriteLine("找不到模拟靶子: " + targetPath);
            return 2;
        }

        Console.WriteLine("开始等待进程 " + procName + "（超时 15 秒）...");

        var launcher = new Thread(delegate()
        {
            Thread.Sleep(3000);
            Console.WriteLine("[模拟启动器] 现在拉起 target.exe");
            var psi = new ProcessStartInfo();
            psi.FileName = targetPath;
            psi.WorkingDirectory = baseDir;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            var p = Process.Start(psi);
            Console.WriteLine("[模拟启动器] 已拉起，PID " + p.Id);
        });
        launcher.IsBackground = true;
        launcher.Start();

        Action<string> log = delegate(string m) { Console.WriteLine("  [log] " + m); };

        var sw = Stopwatch.StartNew();
        var proc = Injector.WaitForProcess(procName, 15000, log, delegate() { return false; });
        if (proc == null)
        {
            Console.WriteLine();
            Console.WriteLine("RESULT: WAIT-TIMEOUT");
            return 1;
        }
        sw.Stop();
        Console.WriteLine("等到进程 PID " + proc.Id + "，耗时 " + sw.ElapsedMilliseconds + " ms");

        bool ok = Injector.Inject(proc.Id, dllPath, log);
        Console.WriteLine();
        Console.WriteLine(ok ? "RESULT: SUCCESS" : "RESULT: FAILED");
        return ok ? 0 : 1;
    }
}