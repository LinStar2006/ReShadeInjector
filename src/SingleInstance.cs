using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoYoInjector
{
    /// <summary>
    /// 单实例守卫：同一时间只允许打开一个注入器界面。
    ///
    /// 做法是一个命名的互斥对象（Mutex）。它由操作系统管理，进程无论怎么退出
    /// （正常关窗、被杀、崩溃）句柄都会被回收，不会留下「锁死了打不开」的残留 ——
    /// 这正是不用「写个 lock 文件再删掉」那种土办法的原因。
    ///
    /// 为什么用 Local\ 前缀：Local\ 的范围是「当前登录会话」，
    /// 正好是我们要的粒度 —— 同一台机器上另一个人登录进来开一份互不干扰，
    /// 同一个人在自己桌面里点第二次就会撞上。
    ///
    /// 为什么自检/截图那套开关要用另一把锁（.auto 后缀）：那些是脚本拉起来的
    /// 临时实例，本来就允许和用户已经开着的界面并存，否则自动化验证一跑就撞锁。
    /// </summary>
    internal static class SingleInstance
    {
        private const string BaseName = @"Local\ReShadeInjector.SingleInstance";
        private const int SwRestore = 9;

        /// <summary>
        /// 互斥对象必须挂在静态字段上。它是可回收对象，一旦被 GC 收走句柄就关了，
        /// 命名互斥随之消失，单实例保护也就跟着失效 —— 这个坑很隐蔽，
        /// 因为程序平时看着是好的，要等 GC 真的跑过一次才暴露。
        /// </summary>
        private static Mutex _mutex;

        /// <summary>本次实际用的那把锁的名字（Reacquire 时要用）。</summary>
        private static string _name;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 尝试取得独占。返回 false 表示已经有实例在跑（此时 message 里是给用户看的说明）。
        /// 返回 true 时无论 message 是否为 null 都应当继续启动。
        /// </summary>
        public static bool TryTake(string[] args, out string message)
        {
            message = null;
            _name = HasAutomationArgs(args) ? BaseName + ".auto" : BaseName;

            bool createdNew;
            try
            {
                _mutex = new Mutex(true, _name, out createdNew);
            }
            catch (Exception ex)
            {
                // 互斥对象建不出来（权限被策略限制之类）属于极罕见情况。
                // 宁可放行让用户用，也不要因为一个辅助功能把程序挡死。
                _mutex = null;
                message = "单实例检查没做成（" + ex.Message + "），这次照常启动。";
                return true;
            }

            if (createdNew) return true;

            try { _mutex.Dispose(); } catch { }
            _mutex = null;

            message = "ReShade Injector 已经开着了，不能重复打开。\r\n\r\n"
                    + "已经帮你把那个窗口叫到最前面。";
            ActivateExisting();
            return false;
        }

        /// <summary>
        /// 主动放弃独占。自提权重启前调用：新进程要用同一把锁，
        /// 不先放掉的话它会以为「已经有一个实例在运行」而立刻退出。
        /// 注意不能用 ReleaseMutex() —— 那是线程亲和的，而提权发生在 worker 线程上，
        /// 从非持有线程调用会抛异常。直接 Dispose 关句柄，效果一样。
        /// </summary>
        public static void Release()
        {
            if (_mutex == null) return;
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
        }

        /// <summary>
        /// 把刚才 Release() 掉的独占再拿回来。用户取消 UAC 时用：
        /// 提权没成，原实例还活着，不能让这段时间里出现第二个实例。
        /// 拿不到就算了 —— 说明确实有别人占了，那本来也不该抢。
        /// </summary>
        public static void Reacquire()
        {
            if (_mutex != null || string.IsNullOrEmpty(_name)) return;

            bool createdNew;
            try
            {
                _mutex = new Mutex(true, _name, out createdNew);
            }
            catch
            {
                _mutex = null;
                return;
            }

            if (!createdNew)
            {
                try { _mutex.Dispose(); } catch { }
                _mutex = null;
            }
        }

        /// <summary>
        /// 自动化用的开关：它们启起来的是临时实例，允许跟用户界面并存。
        /// 具体的名字都不在这里写死（尤其是 --self-check-* 那批），
        /// 免得发布版二进制里留下开发期才有的字符串。
        /// </summary>
        private static bool HasAutomationArgs(string[] args)
        {
            if (args == null) return false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;

                if (a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)) return true;

#if !SHIPPING
                if (a.Equals("--menu-shot", StringComparison.OrdinalIgnoreCase)) return true;
                if (a.StartsWith("--self-check-", StringComparison.OrdinalIgnoreCase)) return true;
#endif
            }

            return false;
        }

        /// <summary>
        /// 把已经在运行的那个窗口叫到前台。找不到就安静地算了 ——
        /// 用户至少已经看到「不能重复打开」那条提示了。
        /// </summary>
        private static void ActivateExisting()
        {
            // 对方可能刚被启动、窗口还没建出来，所以找三轮再放弃。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryActivate()) return;
                Thread.Sleep(150);
            }
        }

        private static bool TryActivate()
        {
            string myName;
            int myPid;
            try
            {
                Process me = Process.GetCurrentProcess();
                myName = me.ProcessName;
                myPid = me.Id;
            }
            catch
            {
                return false;
            }

            Process[] all;
            try
            {
                all = Process.GetProcessesByName(myName);
            }
            catch
            {
                return false;
            }

            foreach (Process p in all)
            {
                try
                {
                    if (p.Id == myPid) continue;

                    // 跨权限级别时（对方是提权进程、自己是普通权限）这里会返回 0，
                    // 属于预期内的失败，交给调用方去重试/放弃。
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;

                    ShowWindow(h, SwRestore);
                    SetForegroundWindow(h);
                    return true;
                }
                catch
                {
                    // 进程在此期间退出了，换下一个
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return false;
        }
    }
}