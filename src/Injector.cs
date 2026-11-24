using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace HoYoInjector
{
    /// <summary>
    /// Win32 注入核心：CreateRemoteThread + LoadLibraryW。
    /// 与参考项目 inject.exe 的机制一致，只是把控制台参数换成了 GUI 参数。
    /// </summary>
    internal static class Injector
    {
        #region P/Invoke

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string systemName, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState,
            uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr processHandle, IntPtr address, IntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(
            IntPtr processHandle, IntPtr address, IntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr processHandle, IntPtr baseAddress, byte[] buffer, IntPtr size, out IntPtr bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr processHandle, IntPtr threadAttributes, IntPtr stackSize,
            IntPtr startAddress, IntPtr parameter, uint creationFlags, out IntPtr threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr threadHandle, out IntPtr exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr processHandle, out bool isWow64);

        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        #endregion

        /// <summary>当前进程是否已获得管理员权限（注入游戏进程必需）。</summary>
        public static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>启用 SeDebugPrivilege，否则 OpenProcess 对高权限进程会失败。</summary>
        public static bool EnableDebugPrivilege(out string error)
        {
            error = null;
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                {
                    error = "OpenProcessToken 失败：" + DescribeLastError();
                    return false;
                }

                LUID luid;
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid))
                {
                    error = "LookupPrivilegeValue 失败：" + DescribeLastError();
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                if (!AdjustTokenPrivileges(token, false, ref tp,
                        (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
                {
                    error = "AdjustTokenPrivileges 失败：" + DescribeLastError();
                    return false;
                }

                // AdjustTokenPrivileges 成功也可能只设置了 ERROR_NOT_ALL_ASSIGNED
                int err = Marshal.GetLastWin32Error();
                if (err == 1300 /* ERROR_NOT_ALL_ASSIGNED */)
                {
                    error = "当前账户未持有 SeDebugPrivilege（请以管理员身份运行）";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        /// <summary>等待指定进程名出现，返回第一个匹配的进程；超时返回 null。</summary>
        public static Process WaitForProcess(string processName, int timeoutMs, Action<string> log, Func<bool> cancelled)
        {
            return WaitForProcess(processName, timeoutMs, log, cancelled, null, null);
        }

        /// <summary>
        /// 等待指定进程名出现。等待期间如果 launchRequested() 变真，就调一次 launchAction()
        /// 把游戏拉起来（对应界面上"等的时候再点一次按钮 = 直接启动游戏"），然后继续等。
        ///
        /// launchAction 返回 false 表示这次启动没成（比如用户取消了选 exe 的对话框）。
        /// 这时不置 launched，用户还能再点一次重试 —— 否则一次取消就彻底没机会了。
        /// </summary>
        public static Process WaitForProcess(string processName, int timeoutMs, Action<string> log,
            Func<bool> cancelled, Func<bool> launchRequested, Func<bool> launchAction)
        {
            if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName += ".exe";

            var sw = Stopwatch.StartNew();
            bool announced = false;
            bool launched = false;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cancelled != null && cancelled()) { log("已取消等待"); return null; }

                var found = FindProcess(processName);
                if (found != null) return found;

                // 等待期间用户又点了一次按钮 → 直接启动游戏
                if (!launched && launchRequested != null && launchRequested())
                {
                    // 没给回调也算「处理过了」，免得一直空转
                    bool ok = launchAction == null || launchAction();
                    if (ok) launched = true;
                }

                if (!announced)
                {
                    log("未检测到 " + processName + "，持续等待中……");
                    announced = true;
                }
                System.Threading.Thread.Sleep(300);
            }

            log("等待超时（" + (timeoutMs / 1000) + " 秒），未检测到 " + processName);
            return null;
        }

        /// <summary>按进程名精确匹配查找运行中的进程。</summary>
        public static Process FindProcess(string processName)
        {
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4)
                : processName;

            Process[] all;
            try { all = Process.GetProcessesByName(bare); }
            catch { return null; }

            foreach (var p in all)
            {
                try
                {
                    if (!p.HasExited) return p;
                }
                catch { }
            }
            return null;
        }

        /// <summary>执行注入。成功返回 true。</summary>
        public static bool Inject(int pid, string dllPath, Action<string> log)
        {
            IntPtr hProcess = IntPtr.Zero;
            IntPtr remoteMem = IntPtr.Zero;

            // 0) 注入前先验 DLL：文件在不在、是不是有效 PE、多少位
            if (!System.IO.File.Exists(dllPath))
            {
                log("DLL 文件不存在：" + dllPath);
                return false;
            }

            int machine = ReadPeMachine(dllPath);
            if (machine == 0)
            {
                log("该文件不是有效的 PE/DLL：" + System.IO.Path.GetFileName(dllPath));
                return false;
            }
            bool dllIs32 = machine == 0x14C;
            log("待注入模块 " + System.IO.Path.GetFileName(dllPath) + "：" + DescribeMachine(machine));

            try
            {
                // 1) 打开目标进程
                uint access = PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION;
                hProcess = OpenProcess(access, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    log("OpenProcess 失败：" + DescribeLastError() + "（常见原因：未提权 / 反作弊拦截）");
                    return false;
                }
                log("已打开进程 PID " + pid);

                // 2) 位数检查：x64 注入器不能往 32 位进程写
                bool targetIsWow64;
                if (IsWow64Process(hProcess, out targetIsWow64) && targetIsWow64)
                {
                    log("目标是 32 位进程，x64 注入器无法注入");
                    log("若必须注入 32 位目标，需要另外编译一个 x86 版本的注入器");
                    return false;
                }

                // 2b) 目标既然不是 WOW64，就是 64 位进程；再塞 32 位 DLL 进去
                //     LoadLibraryW 一定返回 0。提前拦掉，省得看见莫名其妙的失败。
                if (dllIs32)
                {
                    log("位数不匹配：目标是 64 位进程，但 " +
                        System.IO.Path.GetFileName(dllPath) + " 是 32 位 DLL");
                    log("请换成对应的 64 位版本（例如 ReShade64.dll 而不是 ReShade32.dll）");
                    return false;
                }

                // 3) 在目标进程内申请一块可读写内存
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                var size = new IntPtr(pathBytes.Length);
                remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remoteMem == IntPtr.Zero)
                {
                    log("VirtualAllocEx 失败：" + DescribeLastError());
                    return false;
                }
                log("已分配远程内存 0x" + remoteMem.ToString("X"));

                // 4) 写入 DLL 的 UTF-16 绝对路径
                IntPtr written;
                if (!WriteProcessMemory(hProcess, remoteMem, pathBytes, size, out written))
                {
                    log("WriteProcessMemory 失败：" + DescribeLastError());
                    return false;
                }
                log("路径已写入（" + pathBytes.Length + " 字节）");

                // 5) 取 LoadLibraryW 地址（kernel32 在各进程中的基址一致，可直接用本进程的）
                IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                {
                    log("GetModuleHandle(kernel32.dll) 失败：" + DescribeLastError());
                    return false;
                }
                IntPtr loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
                if (loadLibrary == IntPtr.Zero)
                {
                    log("GetProcAddress(LoadLibraryW) 失败：" + DescribeLastError());
                    return false;
                }

                // 6) 远程线程调用 LoadLibraryW(path)
                IntPtr threadId;
                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, IntPtr.Zero,
                    loadLibrary, remoteMem, 0, out threadId);
                if (hThread == IntPtr.Zero)
                {
                    log("CreateRemoteThread 失败：" + DescribeLastError() + "（通常是被反作弊拦下）");
                    return false;
                }
                log("远程线程已创建，TID " + threadId);

                // 7) 等待并校验返回值：非 0 才是真正加载成功
                uint wait = WaitForSingleObject(hThread, 15000);
                IntPtr exitCode = IntPtr.Zero;
                GetExitCodeThread(hThread, out exitCode);
                CloseHandle(hThread);

                if (wait == 0x00000102 /* WAIT_TIMEOUT */)
                {
                    log("远程线程 15 秒未结束（DLL 可能在做初始化）");
                }

                if (exitCode == IntPtr.Zero)
                {
                    log("LoadLibraryW 返回 0，DLL 加载失败（路径错误或位数不匹配）");
                    return false;
                }

                log("注入成功，模块基址 0x" + exitCode.ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                log("注入过程异常：" + ex.Message);
                return false;
            }
            finally
            {
                if (remoteMem != IntPtr.Zero)
                    VirtualFreeEx(hProcess, remoteMem, IntPtr.Zero, MEM_RELEASE);
                if (hProcess != IntPtr.Zero)
                    CloseHandle(hProcess);
            }
        }

        private static string DescribeLastError()
        {
            int code = Marshal.GetLastWin32Error();
            string text;
            try { text = new System.ComponentModel.Win32Exception(code).Message; }
            catch { text = "未知错误"; }
            return code + " " + text;
        }

        /// <summary>
        /// 直接读 PE 头拿 Machine 字段，判断 DLL 是 x86 还是 x64。
        /// 返回 0x14C=x86、0x8664=x64、0xAA64=ARM64，读不出来返回 0。
        /// 这样不用把 DLL 加载进本进程就能知道位数，避免 DllMain 副作用。
        /// </summary>
        private static int ReadPeMachine(string path)
        {
            try
            {
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open,
                           System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                    var head = new byte[4];
                    if (fs.Read(head, 0, 4) != 4) return 0;
                    if (head[0] != 0x4D || head[1] != 0x5A) return 0;   // "MZ"

                    fs.Seek(0x3C, System.IO.SeekOrigin.Begin);
                    var off = new byte[4];
                    if (fs.Read(off, 0, 4) != 4) return 0;
                    int peOffset = BitConverter.ToInt32(off, 0);
                    if (peOffset <= 0 || peOffset > fs.Length - 32) return 0;

                    fs.Seek(peOffset, System.IO.SeekOrigin.Begin);
                    var sig = new byte[6];
                    if (fs.Read(sig, 0, 6) != 6) return 0;
                    if (sig[0] != 0x50 || sig[1] != 0x45) return 0;     // "PE"

                    // COFF 头紧接着签名（4 字节），Machine 是头 2 字节
                    return sig[4] | (sig[5] << 8);
                }
            }
            catch { return 0; }
        }

        private static string DescribeMachine(int machine)
        {
            switch (machine)
            {
                case 0x14C: return "32 位（x86）";
                case 0x8664: return "64 位（x64）";
                case 0xAA64: return "64 位（ARM64）";
                default: return "未知架构 0x" + machine.ToString("X");
            }
        }
    }
}