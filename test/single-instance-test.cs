using System;
using System.Text;
using System.Threading;

namespace HoYoInjector
{
    /// <summary>
    /// 单实例守卫的自检。
    ///
    /// 为什么不直接双击两次 exe 来测：第二个实例被拦住时会弹 MessageBox 等用户点确定，
    /// 自动化脚本会一直挂在那里。这里只测 SingleInstance 这一层，
    /// 弹窗属于 Program.cs 的呈现逻辑，不影响锁的语义。
    ///
    /// 用法：
    ///   single-instance-test.exe hold  [额外参数...]   抢锁并保持 N 秒
    ///   single-instance-test.exe holdforever          抢锁后一直不放（父进程被杀才释放）
    ///   single-instance-test.exe probe [额外参数...]   尝试抢锁，打印结果
    ///   single-instance-test.exe all                   一轮完整自检
    /// </summary>
    internal static class SingleInstanceTest
    {
        private const int HoldSeconds = 6;

        internal static int Execute(string[] argv)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try
            {
                return Run(argv);
            }
            catch (Exception ex)
            {
                Console.WriteLine("未处理异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 3;
            }
            finally
            {
                try { Console.Out.Flush(); } catch { }
            }
        }

        private static int Run(string[] argv)
        {
            if (argv == null || argv.Length == 0)
            {
                Console.WriteLine("用法: single-instance-test.exe hold|holdforever|probe|all [额外参数...]");
                return 2;
            }

            string cmd = argv[0];

            // 把额外参数原样传给 SingleInstance（它会看有没有 --screenshot 之类的自动化开关）
            string[] extra = new string[Math.Max(0, argv.Length - 1)];
            for (int i = 1; i < argv.Length; i++) extra[i - 1] = argv[i];

            if (cmd.Equals("hold", StringComparison.OrdinalIgnoreCase))
                return Hold(extra, HoldSeconds);

            if (cmd.Equals("holdforever", StringComparison.OrdinalIgnoreCase))
                return Hold(extra, 3600);

            if (cmd.Equals("probe", StringComparison.OrdinalIgnoreCase))
                return Probe(extra);

            if (cmd.Equals("all", StringComparison.OrdinalIgnoreCase))
                return All();

            Console.WriteLine("未知命令: " + cmd);
            return 2;
        }

        private static int Hold(string[] extra, int seconds)
        {
            string notice;
            bool ok = SingleInstance.TryTake(extra, out notice);
            Console.WriteLine("HOLD acquired=" + ok + " notice=" + (notice ?? "(null)"));
            Console.Out.Flush();
            Thread.Sleep(seconds * 1000);
            SingleInstance.Release();
            Console.WriteLine("HOLD released");
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        private static int Probe(string[] extra)
        {
            string notice;
            bool ok = SingleInstance.TryTake(extra, out notice);
            Console.WriteLine("PROBE acquired=" + ok + " notice=" + (notice ?? "(null)"));
            SingleInstance.Release();
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        private static int All()
        {
            int failed = 0;
            string self = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string exe = typeof(SingleInstanceTest).Assembly.Location;
            if (string.IsNullOrEmpty(exe)) exe = self;

            Console.WriteLine("程序: " + exe);

            // ---- 场景 1：本进程抢到锁，子进程应该被拦住 ----
            string notice;
            bool ok = SingleInstance.TryTake(new string[0], out notice);
            Expect("A1 当前进程能抢到锁", ok, ref failed);

            string outs = RunChild(exe, "probe");
            Console.WriteLine("  [子进程输出] " + outs.Trim());
            Expect("A2 持锁期间子进程被拦住", outs.Contains("PROBE acquired=False"), ref failed);

            // ---- 场景 2：放掉之后子进程能拿到 ----
            SingleInstance.Release();
            string outs2 = RunChild(exe, "probe");
            Console.WriteLine("  [子进程输出] " + outs2.Trim());
            Expect("B  释放后子进程能拿到", outs2.Contains("PROBE acquired=True"), ref failed);

            // ---- 场景 3：自动化开关用另一把锁，互不打扰 ----
            ok = SingleInstance.TryTake(new string[0], out notice);
            Expect("C1 重新抢到主锁", ok, ref failed);
            string outs3 = RunChild(exe, "probe --screenshot");
            Console.WriteLine("  [子进程输出] " + outs3.Trim());
            Expect("C2 主锁被占时，带 --screenshot 的子进程仍能起来",
                outs3.Contains("PROBE acquired=True"), ref failed);

            // ---- 场景 4：手工再抢一次（模拟 Reacquire）----
            bool ok2 = SingleInstance.TryTake(new string[0], out notice);
            Expect("D  同一进程重复抢锁被拦住", !ok2, ref failed);
            SingleInstance.Release();
            SingleInstance.Reacquire();
            string outs4 = RunChild(exe, "probe");
            Console.WriteLine("  [子进程输出] " + outs4.Trim());
            Expect("E  Reacquire 之后锁又回到本进程手上",
                outs4.Contains("PROBE acquired=False"), ref failed);

            SingleInstance.Release();

            Console.WriteLine();
            if (failed == 0) Console.WriteLine("RESULT: ALL PASS");
            else Console.WriteLine("RESULT: " + failed + " FAILED");
            return failed == 0 ? 0 : 1;
        }

        private static string RunChild(string exe, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(p.StandardOutput.ReadToEnd());
                string err = p.StandardError.ReadToEnd();
                if (err.Length > 0) sb.Append(" [stderr] ").Append(err);
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { }
                    sb.Append(" [超时]");
                }
                return sb.ToString();
            }
        }

        private static void Expect(string what, bool cond, ref int failed)
        {
            Console.WriteLine((cond ? "[PASS] " : "[FAIL] ") + what);
            if (!cond) failed++;
        }
    }
}