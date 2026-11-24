using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HoYoInjector
{
    /// <summary>
    /// 主窗口逻辑。界面来自 UiXaml.Xaml 字符串，运行时解析，
    /// 所以不需要 MSBuild 的 XAML 编译管线。
    /// </summary>
    internal sealed class MainWindow
    {
        private Window _window;
        private ListBox _processList;
        private TextBox _manualProcess;
        private TextBox _dllPath;
        private TextBlock _dllStatus;
        private TextBox _log;
        private Button _injectButton;

        /// <summary>等待进程时出现在主按钮右侧的 × 按钮，点了就取消等待。</summary>
        private Button _cancelButton;
        private CheckBox _waitCheck;
        private TextBlock _statusText;
        private TextBlock _privilegeText;
        private System.Windows.Shapes.Ellipse _statusDot;

        // 左侧导航 + 两个页面
        private Grid _pageHome;
        private Grid _pageSettings;

        /// <summary>预设页。</summary>
        private Grid _pagePresets;

        /// <summary>预设页控件。</summary>
        private ListBox _presetList;

        private TextBox _newPresetInput;
        private TextBlock _presetGameText;
        private TextBlock _presetCurrentText;

        /// <summary>
        /// 预设页当前服务哪个游戏。空串表示「还没选游戏」—— 这时候不建任何目录，
        /// 免得一打开程序就凭空冒出 Shader\Default\Default\ 来。
        /// </summary>
        private string _presetGameKey = "";

        /// <summary>预设列表右键菜单的条目（启用状态在菜单弹出时按当前选中项现算）。</summary>
        private MenuItem _miOpenPresetFolder;
        private MenuItem _miOpenGameFolder;
        private MenuItem _miRenamePreset;
        private MenuItem _miApplyPreset;
        private MenuItem _miDeletePreset;

        // 设置页控件
        private TextBox _timeoutInput;
        private TextBlock _timeoutText;

        /// <summary>等待进程启动的超时秒数（设置页里改，默认 120）。</summary>
        private int _timeoutSeconds = 120;

        private readonly Dispatcher _ui;
        private Thread _worker;
        private volatile bool _cancelRequested;

        /// <summary>定时刷新目标进程列表的运行状态（游戏/靶子起来后能自己变「运行中」）。</summary>
        private System.Windows.Threading.DispatcherTimer _statusTimer;

        /// <summary>正在等目标进程出现（此期间再点按钮 = 直接启动游戏，而不是取消）。</summary>
        private volatile bool _waitingForProcess;

        /// <summary>用户在等待期间又点了一次按钮，要求立刻把游戏拉起来。</summary>
        private volatile bool _launchNow;

        /// <summary>提权成功后置 true，让 worker 收尾时关掉本实例。</summary>
        private volatile bool _shutdownAfterWorker;

        /// <summary>
        /// 点「开始注入」时（UI 线程、worker 还没起来）就先定下来的游戏目录。
        /// ReShade 只认目标 exe 所在目录下的 ReShade.ini，目录不确定就没法准备好 ini，
        /// 所以「选游戏 exe」的对话框必须在注入开始之前弹完 ——
        /// 不能等 worker 跑起来了再弹，那样会在「注入中…」的状态里突然冒出个对话框。
        /// </summary>
        private string _preparedGameDir;

        /// <summary>
        /// 预设游戏列表。进程名取自参考项目「简体中文注入器.bat」里 inject.exe 的目标参数。
        /// 这些条目在界面上常驻显示，未运行也列出来，方便直接选它配合「等待进程启动」。
        /// </summary>
        private static readonly GamePreset[] GamePresets = new[]
        {
            new GamePreset("原神（国服 / 哔哩哔哩）", "YuanShen"),
            new GamePreset("崩坏3", "BH3"),
            new GamePreset("崩坏：星穹铁道", "StarRail"),
            new GamePreset("绝区零", "ZenlessZoneZero"),

            // 异环（Neverness To Everness）：必须从它自己的启动器进游戏，
            // 直接双击 exe 会登录失败。所以标成 LauncherOnly，
            // 等待界面里再点一次按钮不会替你启动它，只会提示用启动器。
            new GamePreset("异环", "HTGame", true),

            // 蓝色星原：旅谣。Unity 引擎，exe 在 ...\AzurPromilia_Game\ 下，
            // 同目录还有个 UnityCrashHandler64.exe（已在 GameLocator 的黑名单里）。
            // 非米哈游系游戏，注册表那级识别不到，靠「运行中的进程」或 game-paths.ini。
            //
            // 它默认也要求从启动器进，但有两个条件就能直启：
            //   1) 带上 -start=azurpromilia_launcher（注意是等号，不是连字符；
            //      少了或写错都会弹「请使用启动器启动游戏」）
            //   2) 必须是管理员身份（少了会报 SDK 初始化失败，点重试也没用）
            // 所以它不标 LauncherOnly，而是带着参数、必要时补提权去启动。
            new GamePreset("蓝色星原：旅谣", "AzurPromilia", "-start=azurpromilia_launcher", true),

#if !SHIPPING
            // ---- 以下两个只在开发构建里出现（build.ps1 -Shipping 会把它们去掉）----
            // 内置测试靶子有两个，验证的东西不一样：
            //   target.exe     没有图形接口 → 只能用 testdll.dll 验证「注入这条路通不通」
            //                  （DllMain 在同目录留 injected-marker.txt）
            //   d3d-target.exe 真的建 D3D11 设备并持续 Present → 用 ReShade64.dll 验证
            //                  「ReShade 能不能 hook 上」，跑完在同目录留 ReShade.log
            // 各自的 exe 要放在程序目录下的 测试\ 里（发布包里没有这个目录，也看不到这两项）。
            new GamePreset("测试靶子 · 注入", "target", "testdll.dll"),
            new GamePreset("测试靶子 · ReShade", "d3d-target", "ReShade64.dll"),
#endif
        };

        public MainWindow()
        {
            _ui = Dispatcher.CurrentDispatcher;
        }

        private string[] _startupArgs;

        public void Run()
        {
            Run(new string[0]);
        }

        /// <summary>整个进程共用的 Application 实例（必须有，见下方注释）。</summary>
        private static Application _app;

        public void Run(string[] args)
        {
            _startupArgs = args ?? new string[0];

            // 关键：以前这里直接 Dispatcher.Run() 进消息循环，从未创建 Application，
            // 导致 Application.Current 恒为 null —— 提权后调 Shutdown() 就抛
            // NullReferenceException（"未将对象引用设置到对象的实例"），窗口关不掉，
            // 于是提权后的新实例和旧窗口同时存在（双窗口 bug 的真正根因）。
            // 现在显式创建 Application，Shutdown() 才能正常工作。
            if (Application.Current == null)
            {
                _app = new Application();
                _app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            else
            {
                _app = Application.Current;
            }

            _window = (Window)UiXaml.LoadWindow();

            _processList = (ListBox)_window.FindName("ProcessList");
            _manualProcess = (TextBox)_window.FindName("ManualProcess");
            _dllPath = (TextBox)_window.FindName("DllPath");
            _dllStatus = (TextBlock)_window.FindName("DllStatus");
            _log = (TextBox)_window.FindName("Log");
            _injectButton = (Button)_window.FindName("InjectButton");
            _cancelButton = (Button)_window.FindName("CancelButton");
            _waitCheck = (CheckBox)_window.FindName("WaitCheck");
            _statusText = (TextBlock)_window.FindName("StatusText");
            _privilegeText = (TextBlock)_window.FindName("PrivilegeText");
            _statusDot = (System.Windows.Shapes.Ellipse)_window.FindName("StatusDot");

            _pageHome = (Grid)_window.FindName("PageHome");
            _pageSettings = (Grid)_window.FindName("PageSettings");
            _pagePresets = (Grid)_window.FindName("PagePresets");
            _presetList = (ListBox)_window.FindName("PresetList");
            _newPresetInput = (TextBox)_window.FindName("NewPresetInput");
            _presetGameText = (TextBlock)_window.FindName("PresetGameText");
            _presetCurrentText = (TextBlock)_window.FindName("PresetCurrentText");
            _timeoutInput = (TextBox)_window.FindName("TimeoutInput");
            _timeoutText = (TextBlock)_window.FindName("TimeoutText");

            var appDirText = (TextBlock)_window.FindName("AppDirText");
            if (appDirText != null) appDirText.Text = AppDir().TrimEnd('\\');

            WireUp();

            // 顺序有讲究：先定默认 DLL，再扫描进程列表。
            // 反过来的话，扫描过程中自动选中「内置测试靶子」会把 DLL 切成 testdll.dll，
            // 紧接着 InitDefaults() 又把它覆盖回 ReShade64.dll，等于白切。
            InitDefaults();
            RefreshProcessList();
            CheckPrivilege();
            StartStatusTimer();

            bool resume = ApplyStartupArgs();

            // 必须在 Show 之前适配：SizeToContent 定下最终高度后，
            // WindowStartupLocation=CenterScreen 才会按真实尺寸居中。
            FitToWorkArea();

            _app.MainWindow = _window;

            // --page settings|presets：自检用，直接打开指定页截图
            var pageArg = GetArgValue("--page");
            if (!string.IsNullOrEmpty(pageArg))
            {
                string navName = null;
                if (pageArg.Equals("settings", StringComparison.OrdinalIgnoreCase)) navName = "NavSettingsButton";
                else if (pageArg.Equals("presets", StringComparison.OrdinalIgnoreCase)) navName = "NavPresetsButton";

                if (navName != null)
                {
                    var nav = (RadioButton)_window.FindName(navName);
                    if (nav != null) nav.IsChecked = true;
                }
            }

            _window.Show();

            // 提权后带参数重新启动 → 自动接续刚才的注入
            if (resume)
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Log("已获得管理员权限，接续上次的注入任务");
                    OnInjectClick();
                }), DispatcherPriority.Background);
            }

            #if !SHIPPING
            // 自检开关：验证「等待中 → 点×取消 → 主按钮是否回到「开始注入」」。
            // 之前这里错过一次：取消路径用 BeginInvoke 改按钮文案，
            // 排队晚于 worker 收尾的 Invoke(ResetButton)，结果按钮卡在「取消」。
            // 发布版不需要它（里面引用了测试靶子用的 testdll.dll）。
            if (HasArg("--self-check-cancel"))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _manualProcess.Text = "__self_check_not_running__";
                    _processList.SelectedIndex = -1;
                    // 用 testdll.dll：名字里没有 reshade，DoInject 不会走
                    // 「定位游戏目录 / 弹文件对话框找 exe」那条路，
                    // 否则自检会被模态对话框卡住。
                    _dllPath.Text = @"DLL\testdll.dll";
                    _waitCheck.IsChecked = true;
                    OnInjectClick();

                    var t1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    t1.Tick += (s2, e2) =>
                    {
                        t1.Stop();
                        Log("自检：等待中，主按钮文案 = " + _injectButton.Content +
                            "，× 可见 = " + (_cancelButton.Visibility == Visibility.Visible));
                        OnCancelWait();

                        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                        t2.Tick += (s3, e3) =>
                        {
                            t2.Stop();
                            Log("自检：取消后，主按钮文案 = " + _injectButton.Content +
                                "，× 可见 = " + (_cancelButton.Visibility == Visibility.Visible) +
                                "，状态 = " + _statusText.Text);
                            try
                            {
                                System.IO.File.WriteAllText(
                                    System.IO.Path.Combine(AppDir(), "self-check-cancel.txt"),
                                    _log.Text, new System.Text.UTF8Encoding(false));
                            }
                            catch { }
                            var t3 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                            t3.Tick += (s4, e4) => { t3.Stop(); ShutdownCore(); };
                            t3.Start();
                        };
                        t2.Start();
                    };
                    t1.Start();
                }), DispatcherPriority.Background);
            }
#endif

            #if !SHIPPING
            // 自检开关：验证「找不到游戏目录时，选择对话框在注入开始【之前】就弹完」。
            // 用户报的问题：点注入后界面已经显示「注入中…」，然后才冒出文件选择框，很莫名。
            // 这里把对话框换成返回 null（模拟用户取消），断言：
            //   - worker 根本没起来（按钮文案应该还是「开始注入」）
            //   - 状态是「已取消」，不是「注入中…」
            if (HasArg("--self-check-dirfirst"))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    bool hookCalled = false;
                    _askForGameExeHook = delegate(string p)
                    {
                        hookCalled = true;
                        return null;   // 模拟用户点了「取消」
                    };

                    _processList.SelectedIndex = -1;
                    _manualProcess.Text = "__no_such_game_dir_check__";
                    _dllPath.Text = @"DLL\ReShade64.dll";   // 名字含 reshade 才会走定位目录那条路
                    _waitCheck.IsChecked = true;

                    OnInjectClick();

                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop();
                        Log("自检：对话框是否被调用 = " + hookCalled);
                        Log("自检：注入前按钮文案 = " + _injectButton.Content);
                        Log("自检：worker 是否已启动 = " + (_worker != null && _worker.IsAlive));
                        Log("自检：状态 = " + _statusText.Text);
                        try
                        {
                            System.IO.File.WriteAllText(
                                System.IO.Path.Combine(AppDir(), "self-check-dirfirst.txt"),
                                _log.Text, new System.Text.UTF8Encoding(false));
                        }
                        catch { }

                        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                        t2.Tick += (s3, e3) => { t2.Stop(); ShutdownCore(); };
                        t2.Start();
                    };
                    t.Start();
                }), DispatcherPriority.Background);
            }
#endif

            #if !SHIPPING
            // 自检开关：验证「能找到游戏目录时，注入照常进行」。
            // 和 --self-check-dirfirst 配成一对：那个查取消分支，这个查命中分支。
            // 用内置靶子 d3d-target（在 测试\ 下，GameLocator 第 0 级直接命中），
            // 所以不会弹任何对话框，日志里应该能看到「已识别游戏目录」。
            if (HasArg("--self-check-inject"))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _processList.SelectedIndex = -1;
                    _manualProcess.Text = "d3d-target";
                    _dllPath.Text = @"DLL\ReShade64.dll";
                    _waitCheck.IsChecked = false;   // 靶子已经在跑，不用等

                    Log("自检：开始注入 d3d-target，验证目录命中时流程照常走。");
                    OnInjectClick();

                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(9000) };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop();
                        Log("自检：结束，按钮文案 = " + _injectButton.Content +
                            "，状态 = " + _statusText.Text);
                        try
                        {
                            System.IO.File.WriteAllText(
                                System.IO.Path.Combine(AppDir(), "self-check-inject.txt"),
                                _log.Text, new System.Text.UTF8Encoding(false));
                        }
                        catch { }
                        ShutdownCore();
                    };
                    t.Start();
                }), DispatcherPriority.Background);
            }
#endif

#if !SHIPPING
            // 自检开关：验证 Application.Current 非 null 且 Shutdown() 真能让窗口退出。
            // 之前双窗口 bug 的根因就是 Application.Current 为 null，这里留个可复现的检查点。
            // 这个开关名也要包在 #if 里 —— 否则发布版二进制里会残留 "--self-check-shutdown" 这个串。
            if (HasArg("--self-check-shutdown"))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Log("自检：调用 ShutdownSelf()，窗口应立刻关闭");
                    ShutdownSelf();
                }), DispatcherPriority.Background);
            }
#endif

            // 自渲染截图：--screenshot <png路径>
            // 为什么不用屏幕抓图：窗口可能被别的程序挡住，CopyFromScreen 会截到别人的界面，
            // 而且分层窗口用 PrintWindow 抓不准。这里直接把视觉树渲染成位图，最可靠。
            string shotPath = GetArgValue("--screenshot");
            if (!string.IsNullOrEmpty(shotPath))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    timer.Tick += (s2, e2) =>
                    {
                        timer.Stop();
                        SaveScreenshot(shotPath);
                        ShutdownSelf();
                    };
                    timer.Start();
                }), DispatcherPriority.Background);
            }

            #if !SHIPPING
            // 自检：--menu-shot <png路径> 把预设列表的右键菜单截下来。
            // ContextMenu 是浮在独立 Popup 里的，窗口的 RenderTargetBitmap 截不到，
            // 只能单独渲染 PART_Popup 的子元素。
            string menuShotPath = GetArgValue("--menu-shot");
            if (!string.IsNullOrEmpty(menuShotPath))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var t1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                    t1.Tick += (s2, e2) =>
                    {
                        t1.Stop();

                        // 没选游戏的话菜单项会全是灰的，看不清样式，先随便挑一个
                        if (string.IsNullOrEmpty(_presetGameKey))
                        {
                            _manualProcess.Text = "d3d-target";
                            RefreshPresetPage();
                        }

                        var cm = _presetList == null ? null : _presetList.ContextMenu;
                        if (cm == null)
                        {
                            Log("自检：预设列表没有右键菜单。");
                            ShutdownSelf();
                            return;
                        }

                        if (_presetList.SelectedIndex < 0 && _presetList.Items.Count > 0)
                            _presetList.SelectedIndex = 0;

                        // 自检截图优先选 Default：它不能改名也不能删，
                        // 正好把「禁用」那两行的样式一起截进去
                        for (int i = 0; i < _presetList.Items.Count; i++)
                        {
                            var it = _presetList.Items[i] as PresetInfo;
                            if (it != null && string.Equals(it.Name, PresetManager.DefaultName, StringComparison.OrdinalIgnoreCase))
                            {
                                _presetList.SelectedIndex = i;
                                break;
                            }
                        }

                        // 不手动设 PlacementTarget / Placement：真机上 WPF 用鼠标位置定位，
                        // 这里保持默认，才能顺带验证「按鼠标位置弹出」这条路没被打断。
                        cm.IsOpen = true;

                        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                        t2.Tick += (s3, e3) =>
                        {
                            t2.Stop();

                            // 自检输出：菜单每项的可点状态，方便确认 Default 时该灰的是不是灰了
                            var selInfo = SelectedPreset();
                            Console.WriteLine("[menu] 选中预设 = " + (selInfo == null ? "(null)" : selInfo.Name)
                                + " / 游戏键 = " + _presetGameKey);
                            Console.WriteLine("[menu] Placement = " + cm.Placement
                                + " / Parent 类型 = " + (cm.Parent == null ? "(null)" : cm.Parent.GetType().Name)
                                + " / 尺寸 = " + cm.ActualWidth + " x " + cm.ActualHeight);
                            foreach (object o in cm.Items)
                            {
                                var mi = o as MenuItem;
                                if (mi != null) Console.WriteLine("[menu] " + mi.Header + " -> " + (mi.IsEnabled ? "可点" : "禁用"));
                            }

                            SavePopupShot(cm, menuShotPath);
                            cm.IsOpen = false;
                            ShutdownSelf();
                        };
                        t2.Start();
                    };
                    t1.Start();
                }), DispatcherPriority.Background);
            }
#endif

            _window.Closing += (s, e) =>
            {
                _cancelRequested = true;
                if (_worker != null && _worker.IsAlive) _worker.Join(600);
            };

            // 用 Application.Run() 而不是 Dispatcher.Run()：前者在 Shutdown() 被调用后
            // 会正常返回，后者只是个裸消息循环，配合 Shutdown() 容易留下僵尸窗口。
            _app.Run();
        }

        private void WireUp()
        {
            _window.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) { try { _window.DragMove(); } catch { } }
            };

            var titleBar = (Grid)_window.FindName("TitleBar");
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) { try { _window.DragMove(); } catch { } }
            };

            ((Button)_window.FindName("MinButton")).Click += (s, e) => _window.WindowState = WindowState.Minimized;
            ((Button)_window.FindName("CloseButton")).Click += (s, e) => _window.Close();

            ((Button)_window.FindName("RefreshButton")).Click += (s, e) =>
            {
                RefreshProcessList();
                Log("进程列表已刷新");
            };

            ((Button)_window.FindName("ClearButton")).Click += (s, e) => _log.Clear();

            ((Button)_window.FindName("BrowseButton")).Click += (s, e) => BrowseDll();

            _processList.SelectionChanged += (s, e) =>
            {
                var item = _processList.SelectedItem as ProcessItem;
                if (item != null)
                {
                    _manualProcess.Text = item.Title;
#if !SHIPPING
                    SyncDllForSelection(item);
#endif
                    if (item.Running)
                        SetStatus("已选中 " + item.Label + "（PID " + item.Pid + "）", "#FF3DD68C");
                    else
                        SetStatus("已选中 " + item.Label + "，它还没运行", "#FF4C8DFF");
                }
            };

            _dllPath.TextChanged += (s, e) => UpdateDllStatus();

            _injectButton.Click += (s, e) => OnInjectClick();
            _cancelButton.Click += (s, e) => OnCancelWait();

            // 底部「未提权」那行字可以直接点，立刻以管理员身份重启
            _privilegeText.MouseLeftButtonUp += (s, e) =>
            {
                if (Injector.IsElevated()) return;
                Log("正在以管理员身份重启……（UAC 确认后本窗口会关闭）");
                if (Elevate("")) ShutdownSelf();
            };

            // 左侧导航：房子=主界面，标签=预设，齿轮=设置
            ((RadioButton)_window.FindName("NavHomeButton")).Checked += (s, e) => ShowPage(0);
            var navPresets = (RadioButton)_window.FindName("NavPresetsButton");
            if (navPresets != null)
                navPresets.Checked += (s, e) => { ShowPage(1); RefreshPresetPage(); };
            ((RadioButton)_window.FindName("NavSettingsButton")).Checked += (s, e) => ShowPage(2);

            if (_timeoutInput != null)
            {
                _timeoutInput.TextChanged += (s, e) => ApplyTimeout();
                ApplyTimeout();
            }

            var openDir = (Button)_window.FindName("OpenAppDirButton");
            if (openDir != null)
            {
                openDir.Click += (s, e) =>
                {
                    try { Process.Start("explorer.exe", "\"" + AppDir().TrimEnd('\\') + "\""); }
                    catch (Exception ex) { Log("打不开文件夹：" + ex.Message); }
                };
            }

            WireUpPresets();
        }

        /// <summary>预设页的事件绑定。</summary>
        private void WireUpPresets()
        {
            var refresh = (Button)_window.FindName("RefreshPresetButton");
            if (refresh != null) refresh.Click += (s, e) => RefreshPresetPage();

            var create = (Button)_window.FindName("CreatePresetButton");
            if (create != null) create.Click += (s, e) => OnCreatePreset();

            if (_newPresetInput != null)
            {
                _newPresetInput.KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter) OnCreatePreset();
                };
            }

            var importFolder = (Button)_window.FindName("ImportFolderButton");
            if (importFolder != null) importFolder.Click += (s, e) => OnImportFolder();

            var importZip = (Button)_window.FindName("ImportZipButton");
            if (importZip != null) importZip.Click += (s, e) => OnImportZip();

            var apply = (Button)_window.FindName("ApplyPresetButton");
            if (apply != null) apply.Click += (s, e) => OnApplyPreset();

            var del = (Button)_window.FindName("DeletePresetButton");
            if (del != null) del.Click += (s, e) => OnDeletePreset();

            BuildPresetMenu();
        }

        // ================= 预设列表的右键菜单 =================

        /// <summary>
        /// 给预设列表装右键菜单。条目的启用状态在菜单弹出那一刻按当前选中项现算，
        /// 免得选中 Default 时「删除」「重命名」还是可点的。
        /// </summary>
        private void BuildPresetMenu()
        {
            if (_presetList == null) return;

            var menu = new ContextMenu();
            menu.Style = (Style)_window.FindResource("DarkContextMenu");

            _miOpenPresetFolder = MakeMenuItem("打开预设文件夹", delegate { OnOpenPresetFolder(); });
            _miOpenGameFolder = MakeMenuItem("打开游戏目录（ReShade.ini 所在）", delegate { OnOpenGameFolder(); });
            _miRenamePreset = MakeMenuItem("重命名", delegate { OnRenamePreset(); });
            _miApplyPreset = MakeMenuItem("应用这个预设", delegate { OnApplyPreset(); });
            _miDeletePreset = MakeMenuItem("删除预设", delegate { OnDeletePreset(); });

            var sep1 = new Separator();
            sep1.Style = (Style)_window.FindResource("DarkMenuSeparator");
            var sep2 = new Separator();
            sep2.Style = (Style)_window.FindResource("DarkMenuSeparator");

            menu.Items.Add(_miOpenPresetFolder);
            menu.Items.Add(_miOpenGameFolder);
            menu.Items.Add(sep1);
            menu.Items.Add(_miRenamePreset);
            menu.Items.Add(_miApplyPreset);
            menu.Items.Add(sep2);
            menu.Items.Add(_miDeletePreset);

            menu.Opened += (s, e) => UpdatePresetMenuState();
            _presetList.ContextMenu = menu;

            // 右键点在某一项上：先把它选中，操作的对象才明确。
            // 点在列表空白处则保持原选中不动。
            _presetList.PreviewMouseRightButtonDown += (s, e) =>
            {
                var hit = FindListBoxItem(e.OriginalSource as DependencyObject);
                if (hit != null) hit.IsSelected = true;
            };
        }

        private MenuItem MakeMenuItem(string header, Action action)
        {
            var mi = new MenuItem();
            mi.Header = header;
            mi.Style = (Style)_window.FindResource("DarkMenuItem");
            mi.Click += delegate { action(); };
            return mi;
        }

        /// <summary>从鼠标命中的元素往上找 ListBoxItem（找不到说明点在空白处）。</summary>
        private static ListBoxItem FindListBoxItem(DependencyObject dep)
        {
            while (dep != null && !(dep is ListBoxItem))
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            return dep as ListBoxItem;
        }

        private void UpdatePresetMenuState()
        {
            var info = SelectedPreset();
            bool hasPreset = info != null;
            bool hasGame = !string.IsNullOrEmpty(_presetGameKey);
            bool isDefault = hasPreset
                && string.Equals(info.Name, PresetManager.DefaultName, StringComparison.OrdinalIgnoreCase);

            if (_miOpenPresetFolder != null) _miOpenPresetFolder.IsEnabled = hasPreset;
            if (_miOpenGameFolder != null) _miOpenGameFolder.IsEnabled = hasGame;
            if (_miRenamePreset != null) _miRenamePreset.IsEnabled = hasPreset && !isDefault;
            // Default 也能应用（它就是没有自己滤镜包时的回落项），只是不能改名和删除
            if (_miApplyPreset != null) _miApplyPreset.IsEnabled = hasPreset && hasGame;
            if (_miDeletePreset != null) _miDeletePreset.IsEnabled = hasPreset && hasGame && !isDefault;
        }

        /// <summary>在资源管理器里打开选中预设的文件夹。</summary>
        private void OnOpenPresetFolder()
        {
            var info = SelectedPreset();
            if (info == null)
            {
                Log("请先选中一个预设。");
                return;
            }

            if (!RequirePresetGame()) return;

            OpenInExplorer(PresetManager.PresetDir(_presetGameKey, info.Name));
        }

        /// <summary>打开当前游戏的目录（ReShade.ini 就放在那儿）。</summary>
        private void OnOpenGameFolder()
        {
            if (!RequirePresetGame()) return;

            string source;
            string gameDir = GameLocator.Resolve(_presetGameKey + ".exe", out source);
            if (gameDir == null)
            {
                Log("找不到 " + _presetGameKey + ".exe 装在哪里。");
                SetStatus("找不到游戏目录", "#FFF0A53E");
                return;
            }

            OpenInExplorer(gameDir);
        }

        private void OpenInExplorer(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Log("目录不存在：" + dir);
                    return;
                }
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                Log("打不开文件夹：" + ex.Message);
            }
        }

        /// <summary>重命名选中预设：只改文件夹名，内容原样。</summary>
        private void OnRenamePreset()
        {
            var info = SelectedPreset();
            if (info == null)
            {
                Log("请先选中一个预设。");
                return;
            }

            if (!RequirePresetGame()) return;

            if (string.Equals(info.Name, PresetManager.DefaultName, StringComparison.OrdinalIgnoreCase))
            {
                Log("Default 预设不能改名。");
                return;
            }

            bool cancelled;
            string input = DialogKit.Prompt(
                _window,
                "重命名预设",
                "只改文件夹名，里面的 Shaders / Textures 原样保留。",
                info.Name,
                out cancelled);
            if (cancelled || input == null) return;

            string oldName = info.Name;
            string err;
            if (!PresetManager.Rename(_presetGameKey, oldName, input, out err))
            {
                Log("重命名失败：" + err);
                return;
            }

            string newName = PresetManager.Sanitize(input);
            Log("已重命名：" + oldName + " → " + newName);
            RefreshPresetPage();

            // 刷新后把改名后的那个重新选中，省得用户再点一次
            if (_presetList != null)
            {
                for (int i = 0; i < _presetList.Items.Count; i++)
                {
                    var it = _presetList.Items[i] as PresetInfo;
                    if (it != null && string.Equals(it.Name, newName, StringComparison.OrdinalIgnoreCase))
                    {
                        _presetList.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // ================= 预设页 =================

        /// <summary>
        /// 预设页当前服务哪个游戏：优先用主界面选中的游戏，没选就看手动输入的进程名。
        /// 每次进预设页都重算，这样主界面换了游戏、回预设页就跟着换。
        /// 返回值：true 表示确定了游戏；false 表示用户还没选，此时不该建任何目录。
        /// </summary>
        private bool ResolvePresetGame()
        {
            string proc = null;

            var sel = _processList.SelectedItem as ProcessItem;
            if (sel != null && sel.IsGame) proc = sel.Title;

            if (string.IsNullOrWhiteSpace(proc))
                proc = (_manualProcess.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(proc) || proc.Length == 0)
            {
                _presetGameKey = "";
                return false;
            }

            _presetGameKey = PresetManager.GameKey(proc);
            return _presetGameKey.Length > 0;
        }

        /// <summary>刷新预设列表和说明文字。</summary>
        private void RefreshPresetPage()
        {
            if (_presetList == null) return;

            bool hasGame = ResolvePresetGame();

            _presetList.Items.Clear();
            SetPresetActionsEnabled(hasGame);

            if (!hasGame)
            {
                if (_presetGameText != null)
                {
                    _presetGameText.Text = "还没选游戏。\n" +
                        "先在左边「目标进程」里点一个游戏，\n" +
                        "预设会按游戏分开存放。";
                }
                if (_presetCurrentText != null) _presetCurrentText.Text = "先选游戏才能用预设。";
                return;
            }

            PresetManager.EnsureDefault(_presetGameKey);

            string current = PresetManager.GetCurrent(_presetGameKey);
            var list = PresetManager.List(_presetGameKey);

            _presetList.Items.Clear();
            int pick = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var info = list[i];
                if (string.Equals(info.Name, current, StringComparison.OrdinalIgnoreCase)) pick = i;
                _presetList.Items.Add(info);
            }
            if (list.Count > 0) _presetList.SelectedIndex = pick;

            if (_presetGameText != null)
            {
                _presetGameText.Text = "当前游戏：" + _presetGameKey +
                    "\n目录：" + PresetManager.PresetRoot(_presetGameKey);
            }

            if (_presetCurrentText != null)
            {
                _presetCurrentText.Text = list.Count == 0
                    ? "这个游戏还没有任何预设。"
                    : "当前使用：" + current + "（注入时自动写进该游戏的 ReShade.ini）";
            }
        }

        private PresetInfo SelectedPreset()
        {
            return _presetList == null ? null : (_presetList.SelectedItem as PresetInfo);
        }

        /// <summary>没确定游戏时，把预设页的操作按钮全禁掉。</summary>
        private void SetPresetActionsEnabled(bool on)
        {
            string[] names = new string[]
            {
                "CreatePresetButton", "ImportFolderButton", "ImportZipButton",
                "ApplyPresetButton", "DeletePresetButton",
            };

            for (int i = 0; i < names.Length; i++)
            {
                var b = (Button)_window.FindName(names[i]);
                if (b != null) b.IsEnabled = on;
            }

            if (_newPresetInput != null) _newPresetInput.IsEnabled = on;
        }

        /// <summary>
        /// 预设相关操作的前置检查：没选游戏就提示用户去主界面选，不要凭空建目录。
        /// </summary>
        private bool RequirePresetGame()
        {
            if (!ResolvePresetGame())
            {
                Log("还没选游戏。请先在左边「目标进程」里点一个游戏，再来管理预设。");
                SetStatus("请先选择游戏", "#FFF0A53E");
                return false;
            }
            return true;
        }

        /// <summary>新建空预设。</summary>
        private void OnCreatePreset()
        {
            if (!RequirePresetGame()) return;

            string name = (_newPresetInput.Text ?? "").Trim();
            if (name.Length == 0)
            {
                Log("请先给预设起个名字。");
                return;
            }

            string err;
            bool existed;
            if (!PresetManager.Create(_presetGameKey, name, out err, out existed))
            {
                Log("新建预设失败：" + err);
                return;
            }

            if (existed)
                Log("预设「" + name + "」已存在，没有改动。");
            else
                Log("已新建预设：" + PresetManager.PresetDir(_presetGameKey, name));

            _newPresetInput.Text = "";
            RefreshPresetPage();

            // 新建完顺手选中它，省得用户再点一次
            for (int i = 0; i < _presetList.Items.Count; i++)
            {
                var it = _presetList.Items[i] as PresetInfo;
                if (it != null && string.Equals(it.Name, PresetManager.Sanitize(name), StringComparison.OrdinalIgnoreCase))
                {
                    _presetList.SelectedIndex = i;
                    break;
                }
            }
        }

        /// <summary>从文件夹导入。</summary>
        private void OnImportFolder()
        {
            if (!RequirePresetGame()) return;

            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择包含 Shaders / Textures 的滤镜包文件夹";
            try
            {
                string source;
                string remembered = GameLocator.Resolve(_presetGameKey + ".exe", out source);
                if (remembered != null && Directory.Exists(remembered))
                    dlg.SelectedPath = Path.GetDirectoryName(remembered);
            }
            catch { }

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            ImportFrom(dlg.SelectedPath, null);
        }

        /// <summary>从压缩包导入：先解到临时目录，再按文件夹那套逻辑搬。</summary>
        private void OnImportZip()
        {
            if (!RequirePresetGame()) return;

            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "选择滤镜包压缩包";
            dlg.Filter = "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog() != true) return;

            string temp = Path.Combine(Path.GetTempPath(), "_preset_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, temp);
                ImportFrom(temp, Path.GetFileNameWithoutExtension(dlg.FileName));
            }
            catch (Exception ex)
            {
                Log("解压失败：" + ex.Message);
                Log("  目前只支持 .zip；.7z / .rar 请先解压成文件夹再用「选择文件夹…」。");
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>
        /// 真正干活：让用户确认预设名，然后从 sourceDir 里找 Shaders / Textures 复制成预设。
        /// defaultName 用来预填输入框（压缩包名）。
        /// </summary>
        private void ImportFrom(string sourceDir, string defaultName)
        {
            string name = AskPresetName(defaultName);
            if (name == null) return;

            string err;
            int copied = PresetManager.ImportFromFolder(_presetGameKey, name, sourceDir, out err);

            if (copied <= 0)
            {
                Log("导入失败：" + err);
                return;
            }

            string dir = PresetManager.PresetDir(_presetGameKey, name);
            var info = PresetManager.Inspect(dir);
            Log("已导入预设「" + PresetManager.Sanitize(name) + "」：" + copied + " 个文件");
            if (info != null) Log("  " + info.ShaderCount + " 个效果 · " + info.TextureCount + " 个纹理");
            Log("  " + dir);

            RefreshPresetPage();
        }

        /// <summary>
        /// 问预设名称。用 DialogKit 自绘的深色输入框，避免系统 MessageBox 那种浅色原生弹窗破坏观感。
        /// 返回 null 表示取消。
        /// </summary>
        private string AskPresetName(string defaultName)
        {
            bool cancelled;
            string input = DialogKit.Prompt(
                _window,
                "预设名称",
                "会在 Shader\\<当前游戏>\\ 下新建同名文件夹。",
                PresetManager.Sanitize(defaultName),
                out cancelled);

            if (cancelled || input == null) return null;

            string clean = PresetManager.Sanitize(input);
            if (clean.Length == 0)
            {
                Log("预设名称不合法，已取消导入。");
                return null;
            }
            return clean;
        }

        /// <summary>应用选中预设：写 settings.ini 记住，并把游戏目录的 ReShade.ini 改过去。</summary>
        private void OnApplyPreset()
        {
            var info = SelectedPreset();
            if (info == null)
            {
                Log("请先选中一个预设。");
                return;
            }

            if (!RequirePresetGame()) return;

            PresetManager.SetCurrent(_presetGameKey, info.Name);

            Log("已应用预设：" + info.Name + "（" + _presetGameKey + "）");

            // 尽量立刻把 ini 改好，用户不用等下次注入
            string source;
            string gameDir = GameLocator.Resolve(_presetGameKey + ".exe", out source);
            if (gameDir == null)
            {
                Log("  找不到该游戏装在哪，ReShade.ini 会在下次注入时自动改。");
            }
            else
            {
                List<string> report;
                string err;
                if (IniPatcher.Ensure(gameDir, _presetGameKey, info.Name, out report, out err))
                {
                    for (int i = 0; i < report.Count; i++) Log("  " + report[i]);
                }
                else
                {
                    Log("  写入 ReShade.ini 失败：" + err);
                }
            }

            RefreshPresetPage();
        }

        /// <summary>删除选中预设（Default 不给删，删了会乱）。</summary>
        private void OnDeletePreset()
        {
            var info = SelectedPreset();
            if (info == null)
            {
                Log("请先选中一个预设。");
                return;
            }

            if (!RequirePresetGame()) return;

            if (string.Equals(info.Name, PresetManager.DefaultName, StringComparison.OrdinalIgnoreCase))
            {
                Log("Default 预设不能删除。");
                return;
            }

            string dir = PresetManager.PresetDir(_presetGameKey, info.Name);

            string question = "预设「" + info.Name + "」连同里面的 Shaders / Textures 会一起删掉，不能撤销。\n\n" + dir;
            if (!DialogKit.Confirm(_window, "删除预设", question, "删除", true)) return;

            try
            {
                Directory.Delete(dir, true);
                Log("已删除预设：" + info.Name);

                // 删的正好是当前用的，退回 Default
                if (string.Equals(PresetManager.GetCurrent(_presetGameKey), info.Name, StringComparison.OrdinalIgnoreCase))
                    PresetManager.SetCurrent(_presetGameKey, PresetManager.DefaultName);
            }
            catch (Exception ex)
            {
                Log("删除失败：" + ex.Message);
            }

            RefreshPresetPage();
        }

        /// <summary>
        /// 切换右侧内容页：0=主界面 1=预设 2=设置。
        /// 三页叠在同一个 Grid 里，只切 Visibility，窗口尺寸不动。
        /// </summary>
        private void ShowPage(int index)
        {
            if (_pageHome != null) _pageHome.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_pagePresets != null) _pagePresets.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_pageSettings != null) _pageSettings.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 把设置页里填的超时秒数转成"秒"，夹在 10 ~ 3600 之间。
        /// 输入非法时不报错、保持原值，免得边打字边弹错。
        /// </summary>
        private void ApplyTimeout()
        {
            if (_timeoutInput == null) return;

            int seconds;
            if (int.TryParse((_timeoutInput.Text ?? "").Trim(), out seconds))
            {
                if (seconds < 10) seconds = 10;
                if (seconds > 3600) seconds = 3600;
                _timeoutSeconds = seconds;
            }

            if (_timeoutText != null)
                _timeoutText.Text = "当前生效：" + _timeoutSeconds + " 秒（可选 10 ~ 3600）";
        }

        private void InitDefaults()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;

            // 约定的放置位置：exe 同目录下的 DLL\ 文件夹。
            // 这样 exe 旁边可以很干净，换 DLL 只需要往 DLL\ 里丢文件。
            // 同时也兼容旧习惯（DLL 直接和 exe 放一起），按优先级依次找。
            var searchDirs = new[]
            {
                Path.Combine(dir, "DLL"),
                dir
            };
            var candidates = new[] { "ReShade64.dll", "ReShade.dll", "dxgi.dll" };
            var found = string.Empty;
            foreach (var searchDir in searchDirs)
            {
                foreach (var candidate in candidates)
                {
                    var p = Path.Combine(searchDir, candidate);
                    if (File.Exists(p)) { found = p; break; }
                }
                if (found.Length > 0) break;
            }

            if (found.Length > 0)
            {
                // 落在程序目录下就显示相对路径，避免一长串绝对路径被文本框截断
                _dllPath.Text = ToDisplayPath(found);
                Log("已自动选中模块：" + ToDisplayPath(found));
            }
            else
            {
                // 都没找到就不硬塞一个不存在的路径，留空让用户自己选
                _dllPath.Text = string.Empty;
                Log("未在 DLL 文件夹或程序目录找到 ReShade64.dll，请点「浏览…」选择要注入的 DLL。");
                Log("  提示：把 DLL 放进 " + Path.Combine(dir, "DLL") + " 即可自动识别。");
                EnsureDllFolder();
            }

            UpdateDllStatus();
            Log("就绪。选择目标进程或直接输入进程名，然后点「开始注入」。");
        }

        /// <summary>
        /// 确保 exe 同目录下存在 DLL\ 文件夹，并把「浏览…」的起始目录定到那里。
        /// 换 DLL 时不用满硬盘翻，程序自己把位置准备好。
        /// </summary>
        private void EnsureDllFolder()
        {
            try
            {
                var folder = Path.Combine(AppDir(), "DLL");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    Log("已创建 DLL 文件夹：" + folder);
                }
            }
            catch (Exception ex)
            {
                // 程序目录只读（例如放在 Program Files）时创建会失败，不影响主流程
                Log("无法创建 DLL 文件夹：" + ex.Message);
            }
        }

        /// <summary>
        /// 解析命令行参数。提权后会以 "exe 目标进程 dll路径" 形式重启自己，
        /// 返回 true 表示应该自动继续注入。
        /// </summary>
        private bool ApplyStartupArgs()
        {
            if (_startupArgs == null || _startupArgs.Length == 0) return false;

            var positional = new List<string>();
            bool wantWait = false;
            for (int i = 0; i < _startupArgs.Length; i++)
            {
                var a = _startupArgs[i];
                if (string.IsNullOrWhiteSpace(a)) continue;

                if (a.Equals("--wait", StringComparison.OrdinalIgnoreCase)) { wantWait = true; continue; }

                // --screenshot <路径> 是内部自检用的，它的值不是"目标进程名"，
                // 必须连带跳过下一个参数，否则截图路径会被当成游戏进程名，
                // 进而误触发一次注入流程。
                if (a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)) { i++; continue; }

                // --menu-shot <路径> 是自检截右键菜单用的，值同样不是进程名。
                // 发布版里没有这个开关，连字符串都不该留下。
#if !SHIPPING
                if (a.Equals("--menu-shot", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
#endif

                // --page settings 同理，值也不该当进程名
                if (a.Equals("--page", StringComparison.OrdinalIgnoreCase)) { i++; continue; }

                if (!a.StartsWith("-")) positional.Add(a);
            }

            // 提权重启时把「等待进程启动」勾选状态一并带过来
            if (wantWait && _waitCheck != null) _waitCheck.IsChecked = true;

            if (positional.Count >= 1)
            {
                var target = positional[0].Trim();
                if (target.Length > 0)
                {
                    _manualProcess.Text = target;
                    Log("启动参数指定目标进程：" + target);
                }
            }

            if (positional.Count >= 2)
            {
                var dll = positional[1].Trim();
                if (dll.Length > 0)
                {
                    var dllAbs = ResolveDllPath(dll);
                    _dllPath.Text = ToDisplayPath(dllAbs);
                    if (File.Exists(dllAbs)) Log("启动参数指定模块：" + ToDisplayPath(dllAbs));
                    else Log("警告：启动参数中的 DLL 不存在 → " + dll);
                }
            }

            return positional.Count >= 1;
        }

        /// <summary>
        /// 把窗口尺寸夹到当前屏幕工作区内。
        /// 高 DPI（如 125%）下 604 DIP 会渲染成 755 物理像素，
        /// 小屏上不加约束就会被屏幕裁掉右侧和底部。
        /// </summary>
        private void FitToWorkArea()
        {
            var area = SystemParameters.WorkArea;
            double maxH = area.Height - 24;
            try
            {
                double maxW = area.Width - 24;

                _window.MaxWidth = maxW;
                _window.MaxHeight = maxH;

                // 窗口尺寸固定（宽 > 高 的横向形状），不用 SizeToContent ——
                // 那样高度会跟着内容变，切换主界面/设置页时窗口会跳大小。
                if (_window.Width > maxW) _window.Width = maxW;
                if (_window.Height > maxH) _window.Height = maxH;
            }
            catch { }

            // 尺寸是固定的，这里直接用设定值（ActualHeight 在 Show 之前还是 0）
            Log(string.Format("窗口已适配：{0:F0} x {1:F0}（可用区 {2:F0} x {3:F0}）",
                _window.Width, _window.Height, area.Width, area.Height));
        }

        private void CheckPrivilege()
        {
            bool elevated = Injector.IsElevated();
            _window.Title = elevated ? "ReShade Injector" : "ReShade Injector（未提权）";

            if (elevated)
            {
                _privilegeText.Text = "已获得管理员权限";
                _privilegeText.Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
            }
            else
            {
                _privilegeText.Text = "未提权 · 点此以管理员重启";
                _privilegeText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA5, 0x3E));
            }
        }

        private void BrowseDll()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要注入的 DLL",
                Filter = "动态链接库 (*.dll)|*.dll|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            try
            {
                // 优先从当前已选 DLL 所在目录开始；没有的话落到约定的 DLL\ 文件夹
                var cur = Path.GetDirectoryName(ResolveDllPath(_dllPath.Text));
                if (string.IsNullOrEmpty(cur) || !Directory.Exists(cur))
                {
                    cur = Path.Combine(AppDir(), "DLL");
                }
                if (Directory.Exists(cur)) dlg.InitialDirectory = cur;
            }
            catch { }

            if (dlg.ShowDialog() == true)
            {
                _dllPath.Text = ToDisplayPath(dlg.FileName);
                Log("已选择模块：" + ToDisplayPath(dlg.FileName));
            }
        }

        #if !SHIPPING
        /// <summary>
        /// 选中内置测试靶子时，把注入模块换成这个靶子该用的 DLL。
        /// 两个靶子验证的东西不一样：
        ///   target.exe     → testdll.dll，DllMain 在同目录留 injected-marker.txt（证明注入通了）
        ///   d3d-target.exe → ReShade64.dll，跑完在同目录留 ReShade.log（证明能 hook 上 D3D11）
        /// 选回真实游戏时，如果当前还挂在 testdll.dll 上就换回 ReShade64.dll。
        /// </summary>
        private void SyncDllForSelection(ProcessItem item)
        {
            if (_dllPath == null) return;

            const string reshadeDllPath = @"DLL\ReShade64.dll";

            var cur = (_dllPath.Text ?? "").Trim();
            bool curIsTest = cur.EndsWith("testdll.dll", StringComparison.OrdinalIgnoreCase);

            if (item != null && item.IsTest)
            {
                string want = @"DLL\" + item.TestDll;
                if (string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) return;

                if (!File.Exists(ResolveDllPath(want)))
                {
                    Log("找不到 " + want + "，这个测试靶子需要它才能跑。");
                    return;
                }

                _dllPath.Text = want;
                Log("内置测试靶子：注入模块已切换为 " + want + "。");

                if (string.Equals(item.TestDll, "testdll.dll", StringComparison.OrdinalIgnoreCase))
                {
                    Log("  注入成功后会生成 injected-marker.txt：" +
                        Path.GetDirectoryName(ResolveDllPath("测试\\target.exe")));
                }
                else
                {
                    Log("  注入后等它自己跑完，会在同一目录生成 ReShade.log：");
                    Log("  " + Path.GetDirectoryName(ResolveDllPath("测试\\d3d-target.exe")));
                }
            }
            else if (curIsTest && File.Exists(ResolveDllPath(reshadeDllPath)))
            {
                _dllPath.Text = reshadeDllPath;
                Log("已切回 " + reshadeDllPath + "。");
            }
        }
#endif

        private void UpdateDllStatus()
        {
            var raw = _dllPath.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                _dllStatus.Text = "请选择要注入的 DLL";
                _dllStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x66, 0x78));
                return;
            }
            var p = ResolveDllPath(raw);
            try
            {
                if (File.Exists(p))
                {
                    var size = new FileInfo(p).Length / 1024.0 / 1024.0;
                    _dllStatus.Text = string.Format("文件存在 · {0:F1} MB · {1}", size, Path.GetFileName(p));
                    _dllStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
                }
                else
                {
                    _dllStatus.Text = "文件不存在，请检查路径";
                    _dllStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                }
            }
            catch (Exception ex)
            {
                _dllStatus.Text = "路径无效：" + ex.Message;
                _dllStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            }
        }

        /// <summary>程序所在目录（结尾带反斜杠）。</summary>
        private static string AppDir()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// 把界面上的路径解析成绝对路径。
        /// 允许填相对路径（默认就是 DLL\ReShade64.dll），相对基准取 exe 所在目录 ——
        /// 不能依赖进程当前工作目录，双击启动和命令行启动的工作目录常常不一样。
        /// </summary>
        private static string ResolveDllPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim().Trim('"');
            try
            {
                return Path.IsPathRooted(s)
                    ? Path.GetFullPath(s)
                    : Path.GetFullPath(Path.Combine(AppDir(), s));
            }
            catch
            {
                return s;
            }
        }

        /// <summary>
        /// 反向操作：路径落在程序目录下就显示成相对形式，免得界面上被一长串绝对路径撑爆。
        /// </summary>
        private static string ToDisplayPath(string abs)
        {
            if (string.IsNullOrWhiteSpace(abs)) return abs;
            try
            {
                var full = Path.GetFullPath(abs);
                var dir = Path.GetFullPath(AppDir()).TrimEnd('\\') + "\\";
                if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    return full.Substring(dir.Length);
                }
                return full;
            }
            catch
            {
                return abs;
            }
        }

        private void RefreshProcessList()
        {
            // 注：csc.exe 4.0.30319 只支持 C# 5，不能用 ?. 运算符
            var sel0 = _processList.SelectedItem as ProcessItem;
            string selected = sel0 == null ? null : sel0.Title;

            // 把系统当前跑着的所有进程名收进字典，后面查运行状态用
            var running = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { procs = new Process[0]; }

            foreach (var p in procs)
            {
                try
                {
                    var n = p.ProcessName;
                    if (!string.IsNullOrEmpty(n) && !running.ContainsKey(n))
                        running[n] = p.Id;
                }
                catch { }
            }

            // 预设游戏全部常驻显示，未运行的也列出来，方便直接选它去「等待启动」
            var items = new List<ProcessItem>();
            int liveCount = 0;
            int gameCount = 0;
            foreach (var preset in GamePresets)
            {
                int pid;
                bool isRunning = running.TryGetValue(preset.Process, out pid);
                if (isRunning && !preset.IsTest) liveCount++;
                if (!preset.IsTest) gameCount++;

                var item = new ProcessItem();
                item.Title = preset.Process;
                item.Label = preset.Label;
                item.Pid = isRunning ? pid : 0;
                item.IsGame = true;
                item.TestDll = preset.TestDll;
                item.LauncherOnly = preset.LauncherOnly;
                item.Running = isRunning;
                item.Display = preset.Label + " · " + preset.Process + ".exe";
                item.Status = isRunning ? "PID " + pid : "未运行";
                item.DisplayBrush = new SolidColorBrush(isRunning
                    ? Color.FromRgb(0xED, 0xF0, 0xF6)     // 运行中：亮白
                    : Color.FromRgb(0xC0, 0xCA, 0xD9));   // 未运行：稍暗但仍清晰
                item.DisplayBrush.Freeze();
                item.StatusBrush = new SolidColorBrush(isRunning
                    ? Color.FromRgb(0x3D, 0xD6, 0x8C)     // 运行中：绿
                    : Color.FromRgb(0x83, 0x8F, 0xA3));   // 未运行：灰
                item.StatusBrush.Freeze();
                items.Add(item);
            }

            _processList.ItemsSource = items;

            // 尽量恢复之前的选择；没有选择时自动选中第一个正在运行的
            int pick = -1;
            if (selected != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (string.Equals(items[i].Title, selected, StringComparison.OrdinalIgnoreCase))
                    { pick = i; break; }
                }
            }
            if (pick < 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Running) { pick = i; break; }
                }
            }
            _processList.SelectedIndex = pick;

            Log(string.Format("进程扫描完成：{0} 个预设游戏中有 {1} 个正在运行。", gameCount, liveCount));
        }

        // ---- 列表状态用的四支画刷：冻结成静态的，定时刷新时不反复 new ----
        private static readonly SolidColorBrush BrushRowRunning = MakeFrozenBrush(0xED, 0xF0, 0xF6, 1.0);
        private static readonly SolidColorBrush BrushRowIdle = MakeFrozenBrush(0xC0, 0xCA, 0xD9, 1.0);
        private static readonly SolidColorBrush BrushStatusRunning = MakeFrozenBrush(0x3D, 0xD6, 0x8C, 1.0);
        private static readonly SolidColorBrush BrushStatusIdle = MakeFrozenBrush(0x83, 0x8F, 0xA3, 1.0);

        private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b, double a)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        /// <summary>
        /// 每 2 秒把列表里的「运行中 / 未运行」同步一次。
        /// 只改条目的字段（ProcessItem 实现了 INotifyPropertyChanged），
        /// 不重建 ItemsSource —— 重建会丢当前选中、列表还会闪。
        /// </summary>
        private void StartStatusTimer()
        {
            _statusTimer = new System.Windows.Threading.DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(2);
            _statusTimer.Tick += (s, e) => TickProcessStatus();
            _statusTimer.Start();
        }

        private void TickProcessStatus()
        {
            var list = _processList == null ? null : _processList.ItemsSource as List<ProcessItem>;
            if (list == null || list.Count == 0) return;

            var running = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var all = Process.GetProcesses();
                try
                {
                    foreach (var p in all)
                    {
                        try { running[p.ProcessName] = p.Id; }
                        catch { }
                    }
                }
                finally
                {
                    foreach (var p in all) { try { p.Dispose(); } catch { } }
                }
            }
            catch { return; }

            foreach (var item in list)
            {
                int pid;
                bool isRunning = running.TryGetValue(item.Title, out pid);
                if (isRunning == item.Running && (!isRunning || pid == item.Pid)) continue;

                item.Running = isRunning;
                item.Pid = isRunning ? pid : 0;
                item.Status = isRunning ? "PID " + pid : "未运行";
                item.DisplayBrush = isRunning ? BrushRowRunning : BrushRowIdle;
                item.StatusBrush = isRunning ? BrushStatusRunning : BrushStatusIdle;

                // 刚起来的游戏/靶子顺手提示一下，用户知道可以点注入了
                if (isRunning && item.IsGame)
                    Log(item.Label + " 已经在运行了（PID " + pid + "）。");
            }
        }

        private void OnInjectClick()
        {
            if (_worker != null && _worker.IsAlive)
            {
                // 正在等游戏出现时，再点一次 = 直接启动游戏（不是取消）
                if (_waitingForProcess)
                {
                    string targetNow = GetTargetName();

                    // 有些游戏必须从自己的启动器进，直启会登录失败（只有异环是这种）。
                    // 这种情况不要替用户启动，老实提示去开启动器。
                    // 蓝色星原虽然默认也要启动器，但它能用 -start=azurpromilia_launcher 直启，
                    // 所以它不标 LauncherOnly，会走下面的正常启动流程。
                    if (IsLauncherOnly(targetNow))
                    {
                        Log(NormalizeExe(targetNow) + " 必须用它的启动器启动，直接跑 exe 会登录失败。");
                        Log("  请打开启动器进游戏，这里会继续等进程出现并自动注入。");
                        SetStatus("请用启动器启动游戏", "#FFF0A53E");
                        return;
                    }

                    _launchNow = true;
                    _injectButton.Content = "正在启动游戏…";
                    Log("收到：立刻启动 " + NormalizeExe(targetNow) + "，启动后会自动注入。");
                    SetStatus("正在启动游戏…", "#FFF5B93E");
                    return;
                }

                // 其它阶段再点 = 取消
                _cancelRequested = true;
                _injectButton.Content = "正在取消…";
                return;
            }

            var selItem = _processList.SelectedItem as ProcessItem;
            string target = selItem == null ? null : selItem.Title;
            if (string.IsNullOrWhiteSpace(target)) target = _manualProcess.Text;
            target = (target ?? "").Trim();

            if (string.IsNullOrWhiteSpace(target))
            {
                Log("请先选择或输入目标进程名");
                SetStatus("缺少目标进程", "#FFEF5350");
                return;
            }

            var dll = ResolveDllPath(_dllPath.Text);
            if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll))
            {
                Log("DLL 路径无效：" + _dllPath.Text);
                SetStatus("DLL 路径无效", "#FFEF5350");
                return;
            }

            var absoluteDll = Path.GetFullPath(dll);

            bool wait = _waitCheck.IsChecked == true;

            // 选中的预设游戏还没跑、又没勾「等待进程启动」时，自动替用户勾上——
            // 否则必然报"进程未运行"，白白浪费一次点击。
            if (selItem != null && selItem.IsGame && !selItem.Running && !wait)
            {
                _waitCheck.IsChecked = true;
                wait = true;
                Log(selItem.Label + " 当前没在运行，已自动勾选「等待进程启动」。" +
                    "请用启动器拉起游戏，注入器会自动接上。");
            }

            // 注入前先把游戏目录定下来。ReShade 只认目标 exe 所在目录的 ReShade.ini，
            // 目录拿不到就不该开始注入 —— 所以「选游戏 exe」的对话框在这一次点击里走完，
            // 而不是等 worker 跑起来、界面已经显示「注入中…」了再弹出来打断。
            _preparedGameDir = null;
            if (Path.GetFileName(absoluteDll).IndexOf("reshade", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool dirCancelled;
                _preparedGameDir = PrepareGameDir(target, out dirCancelled);
                if (dirCancelled || _preparedGameDir == null)
                {
                    // 没目录就不能注入：不启动 worker，按钮保持「开始注入」，
                    // 用户改正后可以再点一次。
                    Log("没有确定游戏目录，本次不注入。");
                    SetStatus(dirCancelled ? "已取消" : "定位不到游戏目录", "#FFF0A53E");
                    return;
                }
            }

            _cancelRequested = false;
            _launchNow = false;
            _injectButton.Content = "取消";
            SetStatus("正在处理…", "#FF4C8DFF");

            int timeoutMs = _timeoutSeconds * 1000;

            string runTarget = target;
            string runDll = absoluteDll;

            _worker = new Thread(() =>
            {
                try { DoInject(runTarget, runDll, wait, timeoutMs); }
                catch (Exception ex) { Log("线程异常：" + ex.Message); SetStatus("异常", "#FFEF5350"); }
                finally
                {
                    // 提权成功的话本实例要退出，交给新实例干活。
                    // 用 BeginInvoke 而不是 Invoke：Invoke 会等 UI 线程，
                    // 而 Shutdown 走 Closing → Join(worker) 就互相卡住了。
                    if (_shutdownAfterWorker)
                    {
                        _shutdownAfterWorker = false;
                        _ui.BeginInvoke(new Action(ShutdownCore));
                    }
                    else
                    {
                        _ui.Invoke(new Action(ResetButton));
                    }
                }
            });
            _worker.IsBackground = true;
            _worker.Start();
        }

        private void ResetButton()
        {
            _injectButton.Content = "开始注入";
            ShowCancel(false);
        }

        /// <summary>
        /// 显示/隐藏主按钮右侧那个 × 。必须回到 UI 线程改 Visibility，
        /// 调用方可能来自 worker 线程，所以统一走 BeginInvoke。
        /// </summary>
        private void ShowCancel(bool show)
        {
            Action act = () =>
            {
                if (_cancelButton == null) return;
                _cancelButton.IsEnabled = true;
                _cancelButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };

            if (_ui.CheckAccess()) act();
            else _ui.BeginInvoke(act);
        }

        /// <summary>
        /// 点了 ×：停止等待目标进程。给用户一个"选错游戏就撤"的机会。
        /// 只在等待阶段有意义，其它阶段按钮本身是隐藏的。
        /// </summary>
        private void OnCancelWait()
        {
            if (!_waitingForProcess) return;

            _cancelRequested = true;
            if (_cancelButton != null) _cancelButton.IsEnabled = false;

            Log("已取消等待，不再注入。");
            SetStatus("正在取消…", "#FFF0A53E");
        }

        private void DoInject(string target, string dll, bool wait, int timeoutMs)
        {
            // 提权：非管理员时先尝试自我提升，成功后本实例关闭、由新实例接管。
            // 发布版里这几个自检开关根本不存在（见下面 #if SHIPPING 分支），
            // 所以连它们的字符串都不该留在二进制里。
#if SHIPPING
            if (!Injector.IsElevated())
#else
            // 无人值守自检要跳过提权，否则 Elevate 会拉起一个管理员实例、
            // 本实例随即关掉，脚本既等不到日志也看不到结果。
            if (!Injector.IsElevated() && !HasArg("--self-check-cancel") && !HasArg("--self-check-inject"))
#endif
            {
                Log("当前不是管理员，正在请求提权……");
                // 注意：这里运行在 worker 线程，绝对不能碰任何 WPF 控件
                // （_waitCheck.IsChecked 之类的跨线程读取会抛
                //  "调用线程无法访问此对象，因为另一个线程拥有该对象"）。
                // wait 是 UI 线程算好后传进来的，直接用即可。
                string args = string.Format("{0}\"{1}\" \"{2}\"", wait ? "--wait " : "", target, dll);
                if (Elevate(args))
                {
                    Log("已启动提权实例，本窗口即将关闭。");
                    _shutdownAfterWorker = true;
                }
                else
                {
                    SetStatus("未提权", "#FFF0A53E");
                }
                return;
            }

            string err;
            if (!Injector.EnableDebugPrivilege(out err))
                Log("提权调试权限失败：" + err);
            else
                Log("已启用 SeDebugPrivilege");

            Process proc = null;

            if (wait)
            {
                SetStatus("等待 " + NormalizeExe(target) + " …", "#FF4C8DFF");
                _waitingForProcess = true;
                _ui.BeginInvoke(new Action(() => { _injectButton.Content = "启动游戏"; }));
                // 主按钮右侧分裂出 ×，选错游戏了可以点它撤
                ShowCancel(true);
                try
                {
                    proc = Injector.WaitForProcess(target, timeoutMs, Log, () => _cancelRequested,
                        () => _launchNow,
                        () =>
                        {
                            // 先消费掉这次请求再干活。否则 LaunchGame 失败（比如用户在
                            // 「选游戏 exe」对话框点了取消）之后，_launchNow 还是 true，
                            // 等待循环每 300 ms 就会重弹一次对话框 —— 根本没法用。
                            _launchNow = false;
                            bool launchOk = LaunchGame(target);
                            if (!launchOk && _waitingForProcess)
                            {
                                // 没启动成，把按钮文案退回「启动游戏」，
                                // 否则会一直停在「正在启动游戏…」，像卡死了。
                                _ui.BeginInvoke(new Action(() =>
                                {
                                    if (_waitingForProcess) _injectButton.Content = "启动游戏";
                                }));
                            }
                            return launchOk;
                        });
                }
                finally
                {
                    _waitingForProcess = false;
                    ShowCancel(false);
                    // 这里必须用 Invoke（同步）而不是 BeginInvoke：
                    // 外层 worker 的 finally 马上会用 Invoke(ResetButton) 把按钮改回「开始注入」，
                    // BeginInvoke 是排队执行的，会晚于那次 Invoke 落地，
                    // 结果就是「点了取消之后按钮停在取消」—— 踩过一次。
                    _ui.Invoke(new Action(() =>
                    {
                        if (!_shutdownAfterWorker) _injectButton.Content = "注入中…";
                    }));
                }

                // 用户点了 × 主动取消：给个明确的状态，别报"等待超时"
                if (proc == null && _cancelRequested)
                {
                    SetStatus("已取消等待", "#FFF0A53E");
                    return;
                }
                if (proc == null) { SetStatus("等待超时", "#FFF0A53E"); return; }
            }
            else
            {
                proc = Injector.FindProcess(target);
                if (proc == null)
                {
                    Log("未找到运行中的 " + NormalizeExe(target));
                    Log("提示：勾选「等待进程启动」可以从启动器拉起游戏时自动注入");
                    SetStatus("进程未运行", "#FFF0A53E");
                    return;
                }
            }

            int pid;
            try { pid = proc.Id; }
            catch (Exception ex) { Log("读取 PID 失败：" + ex.Message); return; }

            // 启动器不是图形程序，把 ReShade 注进去没有任何意义，提前拦一下
            string targetName;
            try { targetName = proc.ProcessName; }
            catch { targetName = target; }
            if (IsLauncher(targetName))
            {
                Log("注意：" + targetName + ".exe 是启动器，不是游戏本体。");
                Log("ReShade 要注入到游戏进程（如 YuanShen.exe / StarRail.exe），");
                Log("请勾选「等待进程启动」后重新点击，等启动器把游戏拉起来时自动注入。");
                SetStatus("目标选错了（这是启动器）", "#FFF0A53E");
                return;
            }

            // ReShade 会在【目标进程 exe 所在目录】找 ReShade.ini，找不到就直接拒绝加载，
            // 注入器只会看到 "LoadLibraryW 返回 0"。这是最常见的"注入失败"真凶。
            // 这里会自动定位游戏目录、把 ini 放对地方并修正其中的路径项，
            // 用户不用再手动复制。定位不到时才弹窗让用户指一次。
            if (!EnsureReShadeIni(proc, dll, targetName))
            {
                SetStatus("缺少 ReShade.ini", "#FFF0A53E");
                return;
            }

            Log("找到目标进程 PID " + pid + "，开始注入 " + Path.GetFileName(dll));
            // 打印最终使用的绝对路径：界面上显示的是相对路径（DLL\xxx.dll），
            // 真注入用的是绝对路径，出问题时这行日志一眼就能看出解析对不对。
            Log("模块绝对路径：" + dll);
            SetStatus("正在注入…", "#FF4C8DFF");

            bool ok = Injector.Inject(pid, dll, Log);

            if (ok)
            {
                // 双保险：再枚举一次目标进程的模块表，确认 DLL 真的挂上去了
                string verify = VerifyModuleLoaded(pid, Path.GetFileName(dll));
                if (verify != null)
                {
                    Log("校验通过：" + verify);
                }

                Log("注入完成 ✓");
                SetStatus("注入成功", "#FF3DD68C");
                _ui.Invoke(new Action(() =>
                {
                    var anim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(420))
                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) };
                    _statusDot.BeginAnimation(System.Windows.Shapes.Shape.OpacityProperty, anim);
                }));
            }
            else
            {
                SetStatus("注入失败", "#FFEF5350");
                Log("排查建议：");
                Log("  1. 确认目标不是启动器，而是游戏本体进程；");
                Log("  2. 确认 " + Path.GetFileName(dll) + " 是 64 位、且与游戏位数一致；");
                Log("  3. ReShade 类 DLL 要求目标目录下有 ReShade.ini，缺了会被它自己拒绝加载；");
                Log("  4. 若是米哈游游戏，内核反作弊可能拦下了远程线程，试试先起注入器再起游戏；");
                Log("  5. 用参考项目原版 inject.exe 注入同一个进程，若原版也失败则问题不在本工具。");
            }
        }

        /// <summary>目标是不是启动器（不是游戏本体）。</summary>
        private static bool IsLauncher(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            var n = processName.ToLowerInvariant();
            return n == "hyp" || n == "launcher" || n == "hoyoplay" ||
                   n.Contains("launcher") || n.StartsWith("hyp");
        }

        /// <summary>
        /// ReShade 类 DLL 的加载前置条件。
        /// 实测确认（2026-11-23）：ReShade 只看【目标进程 exe 所在目录】有没有 ReShade.ini，
        /// 与 DLL 本体放在哪无关；没有就 DllMain 直接返回 FALSE（GetLastError=1114），
        /// 注入器这边只会看到 "LoadLibraryW 返回 0" —— 极容易误判成位数/权限问题。
        /// 这里先检查，能自动补齐就补齐，补不齐就在真注入前停下来说清楚。
        /// </summary>
        private bool EnsureReShadeIni(Process proc, string dll, string targetName)
        {
            string dllName = Path.GetFileName(dll);
            if (dllName.IndexOf("reshade", StringComparison.OrdinalIgnoreCase) < 0)
                return true;   // 不是 ReShade，没这个约束

            // 先把「游戏装在哪里」找出来。ReShade 只认【目标 exe 所在目录】下的
            // ReShade.ini，光知道进程在跑不够（受保护进程读不到 MainModule）。
            string gameDir = LocateGameDir(proc, targetName);

            string gameKey = PresetManager.GameKey(targetName);
            string preset = PresetManager.GetCurrent(gameKey);

            if (gameDir != null)
            {
                List<string> report;
                string err;
                if (IniPatcher.Ensure(gameDir, gameKey, preset, out report, out err))
                {
                    for (int i = 0; i < report.Count; i++) Log("  " + report[i]);
                    return true;
                }
                Log("准备 ReShade.ini 失败：" + err);
            }

            // 兜底：定位不到或补丁失败时，退回「从本工具目录找一份 ini 复制过去」，
            // 前提是能从进程里读到 exe 路径。
            return CopyFallbackIni(proc, dll, gameDir);
        }

        /// <summary>
        /// 注入开始【之前】在 UI 线程上把游戏目录定下来（可能弹「选择游戏 exe」对话框）。
        /// 拿不到就返回 null 并把 cancelled 置灰，调用方据此直接放弃本次注入。
        ///
        /// 为什么要在 worker 起来之前做：这一步可能要弹模态对话框，等 worker 跑起来、
        /// 界面已经显示「注入中…」时再弹出来会让人莫名其妙，而且那时候按钮的语义
        /// 已经变成「启动游戏」了，用户根本没在等一个文件选择框。
        /// </summary>
        private string PrepareGameDir(string targetName, out bool cancelled)
        {
            cancelled = false;

            // 第 1、2、3 级回退：运行中的进程 → HoYoPlay 注册表 → 上次手动指定
            string source;
            string dir = GameLocator.Resolve(targetName, out source);
            if (dir != null)
            {
                Log("已识别游戏目录（来源：" + source + "）：" + dir);
                return dir;
            }

            Log("没能自动识别 " + NormalizeExe(targetName) + " 装在哪里。");
            Log("  请选择一次游戏主程序 exe（选完会记住，以后直接注入）。");
            string picked = AskForGameExe(targetName);
            if (picked == null)
            {
                Log("你取消了选择。");
                cancelled = true;
                return null;
            }

            GameLocator.Remember(targetName, picked);
            dir = Path.GetDirectoryName(picked);
            Log("已记住该路径，下次自动使用：" + dir);
            return dir;
        }

        /// <summary>
        /// 找出游戏 exe 所在目录。顺序：
        ///   点注入时已准备好的目录 → 运行中的进程 → HoYoPlay 注册表
        ///   → 上次手动指定 → 弹窗问用户。
        /// 全都问不到返回 null。
        /// </summary>
        private string LocateGameDir(Process proc, string targetName)
        {
            // 正常情况下 OnInjectClick 已经把目录定好了（可能刚让用户选过一次），
            // 这里直接用，不再弹第二次对话框。
            if (!string.IsNullOrEmpty(_preparedGameDir) &&
                Directory.Exists(_preparedGameDir))
            {
                Log("使用点注入时确定的游戏目录：" + _preparedGameDir);
                return _preparedGameDir;
            }

            string source;
            string dir = GameLocator.Resolve(targetName, out source);
            if (dir != null)
            {
                Log("已自动识别游戏目录（来源：" + source + "）：");
                Log("  " + dir);
                return dir;
            }

            // 定位器没命中时，再从已经拿到的 proc 上试一次：
            // WaitForProcess 返回的句柄可能比按名字自查更直接。
            try
            {
                string exe = proc.MainModule.FileName;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    dir = Path.GetDirectoryName(exe);
                    Log("已从进程读取到游戏目录：");
                    Log("  " + dir);
                    return dir;
                }
            }
            catch
            {
                // 受保护进程（反作弊）读不到模块信息，继续往下问用户
            }

            Log("没能自动识别 " + NormalizeExe(targetName) + " 装在哪里。");
            Log("  请手动选择游戏主程序 exe（选一次就会记住，下次自动使用）。");
            string picked = AskForGameExe(targetName);
            if (picked == null)
            {
                Log("你取消了选择，无法准备 ReShade.ini。");
                return null;
            }

            GameLocator.Remember(targetName, picked);
            dir = Path.GetDirectoryName(picked);
            Log("已记住该路径，下次会自动使用：");
            Log("  " + dir);
            return dir;
        }

        #if !SHIPPING
        /// <summary>
        /// 自检用：替代真实的文件选择对话框，让无人值守脚本能验证「注入前先定目录」这个时序。
        /// 发布版里不存在。
        /// </summary>
        private static Func<string, string> _askForGameExeHook;
#endif

        /// <summary>
        /// 在 UI 线程上弹「选择游戏 exe」对话框并同步等结果。
        /// 本方法可能从 worker 线程调用 —— WPF 的文件对话框必须回到 UI 线程弹，
        /// 所以用 Invoke 同步过去（这里没有 Shutdown 参与，不会死锁）。
        /// </summary>
        private string AskForGameExe(string processName)
        {
#if !SHIPPING
            if (_askForGameExeHook != null) return _askForGameExeHook(processName);
#endif

            string picked = null;

            Action act = () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "没能自动找到 " + NormalizeExe(processName) + "，请选择游戏主程序";
                dlg.Filter = "游戏主程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                dlg.Multiselect = false;

                // 起点给个合理的位置：先看本工具 DLL 目录的上一级，再退到桌面
                try
                {
                    string guess = Path.GetFullPath(Path.Combine(AppDir(), ".."));
                    if (Directory.Exists(guess)) dlg.InitialDirectory = guess;
                }
                catch
                {
                    // 猜不出起点就让系统用默认的
                }

                if (dlg.ShowDialog(_window) == true) picked = dlg.FileName;
            };

            if (_ui.CheckAccess()) act();
            else _ui.Invoke(act);

            return picked;
        }

        /// <summary>
        /// 老兜底：目标目录缺 ini 时，从本工具目录找一份现成的复制过去。
        /// 正常情况下走不到这里（IniPatcher 会直接生成），
        /// 只有「定位不到游戏目录、但从进程里读到了 exe 路径」时才会用到。
        /// </summary>
        private bool CopyFallbackIni(Process proc, string dll, string knownDir)
        {
            string dir = knownDir;
            if (dir == null)
            {
                try { dir = Path.GetDirectoryName(proc.MainModule.FileName); }
                catch (Exception ex)
                {
                    Log("读不到目标进程路径（" + ex.Message + "），无法放置 ReShade.ini。");
                    Log("  ReShade 会因此拒绝加载，注入必定失败，已中止。");
                    return false;
                }
            }

            string ini = Path.Combine(dir, "ReShade.ini");

            // 候选：DLL 同目录 → 本程序目录 → 本程序 DLL\ 子目录
            string[] candidates = new string[]
            {
                Path.Combine(Path.GetDirectoryName(dll), "ReShade.ini"),
                Path.Combine(AppDir(), "ReShade.ini"),
                Path.Combine(AppDir(), "DLL", "ReShade.ini"),
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                string c = candidates[i];
                if (!File.Exists(c)) continue;
                if (File.Exists(ini)) return true;   // 已经有了，不动用户的文件
                try
                {
                    File.Copy(c, ini, false);
                    Log("已复制现成的 ReShade.ini 到目标目录：");
                    Log("  " + c);
                    Log("  → " + ini);
                    return true;
                }
                catch (Exception ex)
                {
                    Log("复制 ReShade.ini 失败：" + ex.Message);
                    Log("  请手动把 ReShade.ini 复制到：" + dir);
                    return false;
                }
            }

            Log("找不到可用的 ReShade.ini，内置模板也写入失败。");
            Log("  ReShade 只认目标程序目录下的 ReShade.ini，请手动把它放到：" + dir);
            return false;
        }

        /// <summary>
        /// 枚举目标进程模块表，确认 dllName 真的被加载了。
        /// 返回 null 表示无法确认（受保护进程读不到模块表），返回空串表示确认未加载。
        /// </summary>
        private static string VerifyModuleLoaded(int pid, string dllName)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    foreach (ProcessModule m in p.Modules)
                    {
                        if (string.Equals(m.ModuleName, dllName, StringComparison.OrdinalIgnoreCase))
                        {
                            return Path.GetFileName(dllName) + " 已加载，基址 0x" +
                                   m.BaseAddress.ToString("X");
                        }
                    }
                    return "";
                }
            }
            catch
            {
                // 受保护进程（反作弊）读不到模块表，属于正常情况，不做否定判断
                return null;
            }
        }

        private static string NormalizeExe(string name)
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }

        /// <summary>
        /// 按进程名在 GamePresets 里找对应条目，找不到返回 null。
        /// GamePresets 是只读静态表，worker 线程调用也安全。
        /// </summary>
        private static GamePreset FindPreset(string processName)
        {
            string name = (processName ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            for (int i = 0; i < GamePresets.Length; i++)
            {
                if (string.Equals(GamePresets[i].Process, name, StringComparison.OrdinalIgnoreCase))
                    return GamePresets[i];
            }
            return null;
        }

        /// <summary>
        /// 这个进程名对应的游戏是不是「必须用启动器启动」的。
        /// 注意：蓝色星原虽然默认要用启动器，但它可以用参数直启，
        /// 所以条目上不是 LauncherOnly，这里返回 false。
        /// </summary>
        private static bool IsLauncherOnly(string processName)
        {
            GamePreset p = FindPreset(processName);
            return p != null && p.LauncherOnly;
        }

        /// <summary>读界面上当前选中的目标进程名。只能在 UI 线程调。</summary>
        private string GetTargetName()
        {
            var selItem = _processList.SelectedItem as ProcessItem;
            string t = selItem == null ? null : selItem.Title;
            if (string.IsNullOrWhiteSpace(t)) t = _manualProcess.Text;
            return (t ?? "").Trim();
        }

        /// <summary>
        /// 等待期间用户又点了一次按钮 → 直接把游戏启动起来。
        /// 跑在 worker 线程，这里只用 GameLocator 查路径 + Process.Start，不碰控件
        /// （AskForGameExe 自己会 Invoke 回 UI 线程弹窗）。
        ///
        /// 找不到游戏装在哪就问用户选一次 —— 选了就记住，以后直接能启动。
        /// 返回 false 表示这次没启动成功（用户取消 / 选错 / 启动抛异常），
        /// 调用方（WaitForProcess）会保持「可再次触发」的状态，让用户能重试。
        /// </summary>
        private bool LaunchGame(string targetName)
        {
            // 兜底：UI 那边已经拦过一次，这里再挡一下，
            // 免得别的调用路径（以后加的）把这类游戏直接跑起来。
            if (IsLauncherOnly(targetName))
            {
                Log(NormalizeExe(targetName) + " 必须用它的启动器启动，直接跑 exe 会登录失败。");
                Log("  请打开启动器进游戏，这里会继续等进程出现。");
                SetStatus("请用启动器启动游戏", "#FFF0A53E");
                return false;
            }

            string source;
            string exe = GameLocator.ResolveExePath(targetName, out source);

            if (string.IsNullOrEmpty(exe))
            {
                // 自动识别不出来就别认输，让用户自己指一次。
                // 本工具不需要知道游戏装在哪也能干活（等启动器拉起进程就行），
                // 只有「帮你启动游戏」这个功能非要知道具体 exe 不可。
                Log("没能自动找到 " + NormalizeExe(targetName) + " 的安装位置。");
                Log("  请手动选择它的主程序 exe（选一次就会记住，下次直接启动）。");

                string picked = AskForGameExe(targetName);
                if (picked == null)
                {
                    Log("你取消了选择。");
                    Log("  可以继续用启动器拉起游戏，注入器仍会自动接上；");
                    Log("  想再试一次直接启动，就再点一次按钮。");
                    SetStatus("找不到游戏主程序", "#FFF0A53E");
                    return false;
                }

                GameLocator.Remember(targetName, picked);
                exe = picked;
                Log("已记住这个路径，下次直接用它启动：");
                Log("  " + exe);
            }

            try
            {
                GamePreset preset = FindPreset(targetName);
                string launchArgs = preset == null ? null : preset.LaunchArgs;
                bool wantElevated = preset != null && preset.LaunchElevated;

                Log("正在启动游戏：" + exe);
                if (!string.IsNullOrEmpty(launchArgs))
                    Log("  启动参数：" + launchArgs);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                if (!string.IsNullOrEmpty(launchArgs))
                    psi.Arguments = launchArgs;

                // 有些游戏少了管理员会初始化失败（蓝色星原：旅谣 的 SDK 初始化）。
                // 本程序本身是 requireAdministrator，正常情况下子进程已经继承管理员令牌；
                // 只有跑在非提权环境（开发版 asInvoker）时才需要显式补一次 runas。
                if (wantElevated && !Injector.IsElevated())
                {
                    psi.Verb = "runas";
                    Log("  这个游戏需要管理员权限，已改用提权方式启动（可能弹一次 UAC）。");
                }

                Process.Start(psi);
                SetStatus("已启动游戏，等待进程出现…", "#FFF5B93E");
                return true;
            }
            catch (Exception ex)
            {
                Log("启动游戏失败：" + ex.Message);
                Log("  如果提示需要权限，请先用启动器启动游戏，注入器会自动接上。");
                SetStatus("启动游戏失败", "#FFF0A53E");
                return false;
            }
        }

        /// <summary>判断启动参数里是否含某个开关（不区分大小写）。</summary>
        private bool HasArg(string flag)
        {
            if (_startupArgs == null) return false;
            foreach (var a in _startupArgs)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>取某个开关后面跟的参数值，例如 --screenshot C:\a.png 里的 C:\a.png。</summary>
        private string GetArgValue(string flag)
        {
            if (_startupArgs == null) return null;
            for (int i = 0; i < _startupArgs.Length - 1; i++)
            {
                if (string.Equals(_startupArgs[i], flag, StringComparison.OrdinalIgnoreCase))
                    return _startupArgs[i + 1];
            }
            return null;
        }

        /// <summary>把当前视觉树渲染成 PNG，失败只记日志不抛。</summary>
        private void SaveScreenshot(string path)
        {
            try
            {
                _window.UpdateLayout();

                int w = (int)Math.Ceiling(_window.ActualWidth);
                int h = (int)Math.Ceiling(_window.ActualHeight);
                if (w <= 0 || h <= 0) { Log("截图失败：窗口尺寸无效"); return; }

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(_window);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
                Log("截图已保存：" + path + "（" + w + " x " + h + "）");
            }
            catch (Exception ex)
            {
                Log("截图失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 把右键菜单渲染成 PNG。
        /// ContextMenu 打开后会被 WPF 塞进它自己内部的 Popup，所以直接渲染菜单自身即可。
        /// </summary>
        private void SavePopupShot(ContextMenu menu, string path)
        {
            try
            {
                menu.UpdateLayout();

                int w = (int)Math.Ceiling(menu.ActualWidth);
                int h = (int)Math.Ceiling(menu.ActualHeight);
                if (w <= 0 || h <= 0)
                {
                    Log("自检：菜单尺寸为 0，可能没打开。");
                    return;
                }

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(menu);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }
                Log("自检：菜单已截图 " + path + "（" + w + " x " + h + "）");
            }
            catch (Exception ex)
            {
                Log("自检：菜单截图失败 —— " + ex.Message);
            }
        }

        private bool Elevate(string arguments)
        {
            // 提权实例是一个新进程，它启动后同样要走单实例检查。
            // 不先放掉手里的锁，它就会以为「已经有一个在跑了」而直接退出，
            // 结果是点了 UAC 同意、窗口也关了，却什么都没起来。
            // 这里的取舍：放掉锁到本实例真正关闭之间有一小段窗口期，
            // 用户手动再点一次 exe 能开出第三个实例 —— 但旧实例已经开始 Shutdown，
            // 相比「提权后接不上注入」这个必然故障，这点概率可以接受。
            bool released = false;

            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };

                SingleInstance.Release();
                released = true;

                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 1223 = ERROR_CANCELLED，用户在 UAC 对话框点了「否」
                if (ex.NativeErrorCode == 1223)
                    Log("你取消了 UAC 授权。");
                else
                    Log("启动提权进程失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                Log("提权异常：" + ex.Message);
            }

            // 走到这里说明新实例没起来（UAC 被拒 / 启动失败），本实例继续活着，
            // 必须把锁收回来，否则这会儿再开一个副本进来就成多开了。
            if (released) SingleInstance.Reacquire();
            return false;
        }

        /// <summary>
        /// 关闭本实例。
        /// 必须在 UI 线程上调用 Application.Shutdown()，从后台线程直接调会抛异常。
        /// 另外程序现在真的创建了 Application 对象（见 Run()），Shutdown 才有效；
        /// 以前 Application.Current 是 null，这里抛 NullReferenceException，
        /// 于是提权后的新实例和旧窗口同时存在。
        /// </summary>
        private void ShutdownSelf()
        {
            if (_ui.CheckAccess())
            {
                ShutdownCore();
            }
            else
            {
                _ui.BeginInvoke(new Action(ShutdownCore));
            }
        }

        private static void ShutdownCore()
        {
            try
            {
                if (Application.Current != null) Application.Current.Shutdown();
            }
            catch { }
        }

        // ----- 日志与状态（线程安全） -----
        private void Log(string message)
        {
            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            if (_ui.CheckAccess())
            {
                _log.AppendText(line);
                _log.ScrollToEnd();
            }
            else
            {
                _ui.BeginInvoke(new Action(() =>
                {
                    _log.AppendText(line);
                    _log.ScrollToEnd();
                }));
            }
        }

        private void SetStatus(string text, string colorHex)
        {
            var act = new Action(() =>
            {
                _statusText.Text = text;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(colorHex);
                    _statusText.Foreground = new SolidColorBrush(c);
                    _statusDot.Fill = new SolidColorBrush(c);
                }
                catch { }
            });

            if (_ui.CheckAccess()) act();
            else _ui.BeginInvoke(act);
        }
    }

    /// <summary>预设游戏：显示名 + 进程名（不含 .exe）。</summary>
    internal sealed class GamePreset
    {
        public readonly string Label;
        public readonly string Process;

        /// <summary>
        /// true 表示这游戏必须从它自己的启动器启动，直接跑 exe 会出问题
        /// （例如异环直启会登录失败）。这类游戏不给「帮你启动」功能，
        /// 等不到进程时只提示用户去开启动器。
        /// </summary>
        public readonly bool LauncherOnly;

        /// <summary>
        /// 直启时该带的命令行参数（null / 空 = 不带参数）。
        /// 有些游戏把「我是从启动器来的」写在命令行里，少了它就弹「请用启动器启动」。
        /// 实例：蓝色星原：旅谣 需要 -start=azurpromilia_launcher（等号，别写成连字符）。
        /// </summary>
        public readonly string LaunchArgs;

        /// <summary>
        /// true 表示直启时必须用管理员权限，否则游戏自己会初始化失败
        /// （蓝色星原少管理员会报 SDK 初始化失败，点了重试也没用）。
        /// 本程序自己就是 requireAdministrator，子进程默认继承管理员令牌，
        /// 这个标记主要用于「万一运行在非提权环境」时补一次 runas。
        /// </summary>
        public readonly bool LaunchElevated;

        /// <summary>
        /// 非 null 表示这是内置测试靶子，值是选中它时该用的 DLL 文件名（在 DLL\ 下）。
        /// 靶子没有真实游戏的图形/反作弊环境，配哪个 DLL 是固定的：
        ///   - target.exe     → testdll.dll，注入后在同目录留 injected-marker.txt
        ///   - d3d-target.exe → ReShade64.dll，验证 ReShade 能不能 hook 上真实的 D3D11 设备
        /// </summary>
        public readonly string TestDll;

        public GamePreset(string label, string process)
        {
            Label = label;
            Process = process;
            LauncherOnly = false;
            LaunchArgs = null;
            LaunchElevated = false;
            TestDll = null;
        }

        public GamePreset(string label, string process, bool launcherOnly)
        {
            Label = label;
            Process = process;
            LauncherOnly = launcherOnly;
            LaunchArgs = null;
            LaunchElevated = false;
            TestDll = null;
        }

        /// <summary>
        /// 「可以用特定参数直启」的游戏：launchArgs 是命令行参数，
        /// launchElevated 表示还必须以管理员身份跑。
        /// </summary>
        public GamePreset(string label, string process, string launchArgs, bool launchElevated)
        {
            Label = label;
            Process = process;
            LauncherOnly = false;
            LaunchArgs = launchArgs;
            LaunchElevated = launchElevated;
            TestDll = null;
        }

        public GamePreset(string label, string process, string testDll)
        {
            Label = label;
            Process = process;
            LauncherOnly = false;
            LaunchArgs = null;
            LaunchElevated = false;
            TestDll = testDll;
        }

        public bool IsTest { get { return TestDll != null; } }
    }

    /// <summary>
    /// 进程列表条目。
    /// 实现了 INotifyPropertyChanged：运行状态会被定时器原地刷新，
    /// 不重建 ItemsSource（重建会丢选中、还会闪），所以状态字段必须能通知 UI。
    /// </summary>
    internal sealed class ProcessItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        /// <summary>进程名（不含 .exe），注入时用它。</summary>
        public string Title { get; set; }

        /// <summary>界面上显示的中文名。</summary>
        public string Label { get; set; }

        private int _pid;
        /// <summary>运行中则为真实 PID，未运行则为 0。</summary>
        public int Pid
        {
            get { return _pid; }
            set { if (_pid != value) { _pid = value; Raise("Pid"); } }
        }

        public bool IsGame { get; set; }

        private bool _running;
        public bool Running
        {
            get { return _running; }
            set { if (_running != value) { _running = value; Raise("Running"); } }
        }

        private string _display;
        /// <summary>列表里展示的整行文字。</summary>
        public string Display
        {
            get { return _display; }
            set { if (_display != value) { _display = value; Raise("Display"); } }
        }

        private string _status;
        /// <summary>右侧状态文字：运行中显示 PID，否则「未运行」。</summary>
        public string Status
        {
            get { return _status; }
            set { if (_status != value) { _status = value; Raise("Status"); } }
        }

        private SolidColorBrush _displayBrush;
        /// <summary>
        /// 该行的文字颜色。直接给 Brush 而不是靠 XAML DataTrigger，
        /// 省得触发器优先级/作用域的问题把颜色吃掉（踩过一次）。
        /// 运行中 = 亮白，未运行 = 稍暗但清晰可读。
        /// </summary>
        public SolidColorBrush DisplayBrush
        {
            get { return _displayBrush; }
            set { _displayBrush = value; Raise("DisplayBrush"); }
        }

        private SolidColorBrush _statusBrush;
        /// <summary>右侧状态文字的颜色：运行中绿、未运行灰。</summary>
        public SolidColorBrush StatusBrush
        {
            get { return _statusBrush; }
            set { _statusBrush = value; Raise("StatusBrush"); }
        }

        /// <summary>
        /// 内置测试靶子对应的 DLL 文件名（在 DLL\ 下）；null 表示不是测试项。
        /// 两个靶子需要的 DLL 不同（target→testdll.dll，d3d-target→ReShade64.dll），
        /// 所以这里带的是具体文件名而不是一个 bool。
        /// </summary>
        public string TestDll { get; set; }

        /// <summary>是不是内置测试靶子。</summary>
        public bool IsTest { get { return TestDll != null; } }

        /// <summary>
        /// 这游戏是不是「必须用启动器启动」的。
        /// 是的话等待期间再点按钮不会替你启动它 —— 直接跑 exe 会登录失败。
        /// </summary>
        public bool LauncherOnly { get; set; }

        public override string ToString() { return Display == null ? Title : Display; }
    }
}
