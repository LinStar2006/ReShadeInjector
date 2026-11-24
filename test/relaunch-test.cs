// 验证「等待期间再点一次按钮 = 直接启动游戏」这条链路：
// 进程一开始不存在 → WaitForProcess 进入轮询 → 中途 launchRequested() 变真
// → 触发 launchAction 启动靶子 → 继续等到进程出现 → 自动注入。
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using HoYoInjector;

internal static class RelaunchTest
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        string procName = args.Length > 0 ? args[0] : "target";
        string dll = args.Length > 1 ? args[1] : ".\\testdll.dll";
        string targetExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "target.exe");

        Console.WriteLine("靶子进程 : " + procName + ".exe");
        Console.WriteLine("靶子路径 : " + targetExe);
        Console.WriteLine("注入模块 : " + dll);
        Console.WriteLine("靶子路径存在: " + File.Exists(targetExe));

        int failed = 0;

        // 先清掉可能残留的靶子
        foreach (var p in Process.GetProcessesByName(procName))
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }
        Thread.Sleep(400);

        // 用数组而不是捕获局部 bool：C# 5 不允许 lambda 捕获 volatile 局部变量，
        // 而这里确实需要跨线程共享一个"用户点了第二次按钮"的标记。
        var launchFlag = new bool[1];
        bool launched = false;

        // 1.5 秒后模拟"用户在等待界面又点了一次按钮"
        var clicker = new Thread(() =>
        {
            Thread.Sleep(1500);
            Console.WriteLine("  [模拟] 用户第二次点击按钮");
            launchFlag[0] = true;
        });
        clicker.IsBackground = true;
        clicker.Start();

        var sw = Stopwatch.StartNew();
        var proc = Injector.WaitForProcess(
            procName, 15000,
            msg => Console.WriteLine("  [log] " + msg),
            () => false,
            () => launchFlag[0],
            () =>
            {
                launched = true;
                Console.WriteLine("  [launch] 收到「立刻启动」，正在拉起 " + targetExe);
                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(targetExe)
                };
                try { Process.Start(psi); Console.WriteLine("  [launch] 已启动"); return true; }
                catch (Exception ex) { Console.WriteLine("  [launch] 失败: " + ex.Message); return false; }
            });

        sw.Stop();

        if (!launched) { Console.WriteLine("FAIL: 启动回调从未被触发"); failed++; }
        if (proc == null) { Console.WriteLine("FAIL: 没等到进程（耗时 " + sw.ElapsedMilliseconds + " ms）"); }
        else
        {
            Console.WriteLine("等到进程 PID " + proc.Id + "，耗时 " + sw.ElapsedMilliseconds + " ms");
        }

        // ---- 第二个场景：进程已经存在时，不应该触发"启动游戏" ----
        Console.WriteLine();
        Console.WriteLine("--- 场景 2：进程已在运行，不该再启动一次 ---");

        bool launched2 = false;
        var proc2 = Injector.WaitForProcess(
            procName, 5000,
            msg => { },
            () => false,
            () => true,           // 一直请求启动
            () => { launched2 = true; return true; });

        Console.WriteLine("已存在时是否触发启动: " + launched2 + "（应为 False）");
        if (launched2) failed++;
        if (proc2 == null) { Console.WriteLine("FAIL: 已存在的进程没被找到"); failed++; }
        else Console.WriteLine("直接命中运行中的进程 PID " + proc2.Id);

        foreach (var p in Process.GetProcessesByName(procName))
        {
            try { p.Kill(); } catch { }
        }

        Console.WriteLine(failed == 0 ? "RESULT: ALL PASS" : "RESULT: " + failed + " FAILED");
        Console.Out.Flush();
        return failed == 0 ? 0 : 1;
    }
}