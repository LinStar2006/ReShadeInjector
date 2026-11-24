# ReShade Injector

一个 Windows 平台的可视化 ReShade 注入器。用远程线程把 `ReShade64.dll` 注进正在运行的游戏进程，**不需要往游戏目录里放 `dxgi.dll` 之类代理文件**。

---

## 它解决什么问题

ReShade 官方装法是往游戏目录里放一个叫 `dxgi.dll`（或 `d3d11.dll`）的文件，游戏加载图形接口时顺带把它带进去。这个办法有两个麻烦：

1. 游戏每次校验文件完整性都可能把它当成篡改；
2. 放进去容易、想撤掉得手动删，忘了删就一直在。

这个工具换了个思路：**游戏正常启动，然后从外部把 DLL 注进它的进程**。游戏目录里只有一个 `ReShade.ini`（配置文件），没有可执行文件被替换。

---

## 快速开始

1. 解压压缩包到任意目录（不要放在 `Program Files` 这类需要管理员才能写的地方，程序要往自己目录写配置）。
2. 双击 `ReShadeInjector.exe`。会弹一次 UAC，点「是」——注入别的进程必须有管理员权限。
3. 在左侧「目标进程」里点一个游戏。
4. 如果游戏还没开，勾上「等待进程启动」，然后去开游戏（用它的启动器）。
5. 看到进程出现，程序会自动注入。日志区会写清楚每一步。

也可以反过来：先开游戏，再点游戏条目、点「开始注入」，直接注入。

注入成功后，游戏里按 `Home` 呼出 ReShade 覆盖层。

**同一个程序只开一个窗口**。重复双击时不会起第二个界面，而是把已经在跑的那个窗口叫到最前面。

---

## 目录约定

程序目录（`ReShadeInjector.exe` 所在处）：

```
ReShadeInjector.exe
DLL\                    放要注入的 DLL，启动时自动选中 ReShade64.dll
Shader\                 预设库，按游戏分子目录（见「预设系统」）
Addons\                 ReShade 插件（.addon64 / .addon32）
Fonts\                  覆盖层用的字体（.ttf / .ttc）
ScreenShot\             ReShade 截图输出目录
game-paths.ini          记录手动指定过的游戏 exe 路径（程序自动生成）
settings.ini            记录每个游戏当前选中的预设（程序自动生成）
```

`DLL\` 里只放一个 `ReShade64.dll`。放多个版本时程序会选排在最前面的那个，结果不确定，所以旧版请移到 `DLL\` **外面**单独放。

---

## 界面说明

### 主页

- **目标进程**：常驻列出支持的游戏，不管有没有在运行。条目右侧显示 `PID xxxxx`（绿）或 `未运行`（灰），状态每 2 秒自动刷新一次。
- **注入模块**：默认是 `DLL\ReShade64.dll`。
- **运行日志**：注入过程的每一步都会写在这里。
- **底部按钮**：
  - `开始注入` —— 注入当前选中的进程。
  - 等待过程中按钮变成 `启动游戏`，再点一次就直接把游戏拉起来，拉起后自动注入。
  - 等待过程中按钮旁边会多出一个 `×`，点它取消本次等待。
  - 「异环」这类必须用启动器启动的游戏不会变成 `启动游戏`，只会提示你去开启动器。

### 预设

按游戏分开管理滤镜包，每组是一个独立的 Shaders / Textures 组合。选中条目后右键有：打开预设文件夹 / 打开游戏目录 / 重命名 / 应用这个预设 / 删除预设。

`Default` 是每个游戏自带的兜底预设，不能重命名也不能删除。

### 设置

- **等待进程启动超时**：10 ~ 3600 秒，默认 120 秒。游戏启动器慢的话调大一点。
- **程序目录**：显示当前程序目录，带一个「打开文件夹」入口。
- **关于**：版本与注入方式说明。

---

## 支持的游戏

| 显示名 | 进程名 |
| --- | --- |
| 原神（国服 / 哔哩哔哩） | `YuanShen.exe` |
| 崩坏3 | `BH3.exe` |
| 崩坏：星穹铁道 | `StarRail.exe` |
| 绝区零 | `ZenlessZoneZero.exe` |
| 异环 | `HTGame.exe` |
| 蓝色星原：旅谣 | `AzurPromilia.exe` |

后两个必须从**它们自己的启动器**进游戏。直接跑 exe 会登录失败，所以等待期间再点一次按钮也不会替你启动它们，只会提示你去开启动器。

不在表里的进程也可以注入：在「目标进程」卡片的手动输入框里直接敲进程名（不带 `.exe` 也行）。

### 游戏目录是怎么找到的

ReShade 只认**目标 exe 所在目录**下的 `ReShade.ini`，所以必须先确定这个目录。程序按顺序尝试：

1. 目标是否正在运行 —— 从进程模块信息里读出 exe 路径；
2. 查 HoYoPlay 的安装记录（注册表 `Software\miHoYo\HYP`，逐个子项读 `GameInstallPath`）；
3. 查 `game-paths.ini` 里以前手动指定过的路径；
4. 都失败就弹窗让你手动选一次它的主程序 exe，选完会记住。

**这一步在点「开始注入」的当下就完成**，拿不到目录就不会开始注入。不会出现"注入到一半突然冒出个文件选择框"。

---

## 预设系统

### 目录结构

```
Shader\
  YuanShen\              游戏名（进程名去掉 .exe）
    001\
      Shaders\           .fx 效果文件
      Textures\          纹理（.png / .jpg / .dds / .bmp / .tga）
      001.ini            这个预设对应的 ReShade 预设文件
    Default\             兜底预设
  ZenlessZoneZero\
    ...
```

每个游戏**各自记住**当前选中的预设，互不影响。

### 新建 / 导入

- **新建**：给个名字，程序建好 `Shaders` 和 `Textures` 两个空目录，以后往里面丢文件即可。
- **导入文件夹**：选一个本地目录，程序会自动在最多 4 层子目录里找 `Shaders` / `Textures`，找到就复制进来。所以直接把 `reshade-shaders-master` 这种原始包解压出来选它的根目录就行。
- **导入压缩包**：目前支持 `.zip`。`.7z` / `.rar` 请先解压成文件夹再导入。

导入完成后会问你预设叫什么名字。

### 应用预设

点「应用选中的预设」，程序会：

1. 把该游戏的当前预设记进 `settings.ini`；
2. 改写游戏目录下 `ReShade.ini` 的 `EffectSearchPaths` / `TextureSearchPaths` / `PresetPath` 指向这个预设。

**不用退出游戏**。在游戏里按 `Home` 呼出覆盖层，点一下「重新加载」，新路径立刻生效。

---

## 常见问题

**注入提示 `LoadLibraryW 返回 0`**

三种可能：

1. DLL 路径不对——看日志里「模块绝对路径」那一行；
2. 位数不匹配——目标是 32 位进程而 DLL 是 64 位的（或反过来）。程序会在注入前读 PE 头预检，正常情况会直接拦下来；
3. 目标目录缺 `ReShade.ini`，被 ReShade 自己的加载检查拒绝。正常情况下程序会自动生成，看到这条说明生成失败了，检查游戏目录的写权限。

**提示「找不到游戏目录」**

按弹窗选一次它主程序 exe。或者直接手输完整路径到 `game-paths.ini`：

```ini
; 进程名(不含 .exe)=exe 的完整路径
HTGame=<游戏安装目录>\HTGame.exe
```

**注入成功但游戏里没效果**

- 按 `Home` 看看覆盖层能不能呼出来。能呼出说明注入成功，只是效果没勾——进预设列表勾选即可。
- 呼不出来可能是 ReShade 版本问题：必须用**带 Addon 支持**的构建（官网带 `_Addon` 后缀的那个），否则 `.addon64` 插件不加载、部分效果也起不来。

**提示「目标选错了（这是启动器）」**

注入目标应该是游戏本体，不是启动器。启动器进程就算注入进去也不会有效果。

**游戏里中文显示成方块**

`Fonts\` 目录里没有字体。放一个中文字体（`.ttf` / `.ttc`）进去，重新注入。

**双击没反应 / 提示权限不足**

程序要求管理员权限（manifest 里是 `requireAdministrator`）。如果 UAC 被系统策略挡掉，用管理员身份运行命令提示符再启动它。

---

## 从源码编译

### 需要什么

- Windows
- **.NET Framework 4.8**（Win10 1803 之后系统自带）
- 系统自带的 C# 编译器：`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`

### 三条构建命令

```powershell
# 开发版：带自检开关 + 断点日志，输出到 build-test\
.\build.ps1 -NoUac

# 正式版：普通开发构建，输出到部署目录
.\build.ps1

# 发布版：去掉所有开发用代码（条件编译 SHIPPING），输出到 pkg\
.\build.ps1 -Shipping
```

`-NoUac` 会换用 `app-asinvoker.manifest`，这样无人值守脚本也能启动它截图。正式发布的 exe 一律用 `app.manifest`。

### 打包

```powershell
.\pack.ps1            # 含 ReShade64.dll 与字体，生成 pkg\ReShadeInjector.zip
.\pack.ps1 -NoDll     # 不含 DLL
```

`pack.ps1` 会依次：清 `pkg\`、跑 `-Shipping` 构建、建空目录骨架、复制 `DLL\ReShade64.dll` 和 `Fonts\` 下的字体、写 `使用说明.txt`、压成 zip。

### 目录结构

```
src\
  Program.cs              入口
  MainWindow.cs           界面逻辑与注入流程编排（大头）
  Injector.cs             P/Invoke 注入实现
  GameLocator.cs          游戏目录定位
  IniPatcher.cs           ReShade.ini 生成与修正
  PresetManager.cs        预设库管理
  DefaultIni.cs           内置 ini 模板的读写
  DialogKit.cs            自绘对话框（替代系统 MessageBox）
  UiXaml.cs               XAML 资源加载
  ui.xaml                 主窗口界面
  dialogs.xaml            对话框样式表
  default-reshade.ini     内置 ReShade.ini 模板
  app.manifest            正式版（requireAdministrator）
  app-asinvoker.manifest  测试版（asInvoker）
test\                     自检程序
build.ps1                 编译
pack.ps1                  打包
verify-gui.ps1            截图验证
fix-bom.ps1               批量补 BOM
```

---

## 许可与致谢

- 本工具自身代码归作者所有。
- 随包分发的 `ReShade64.dll` 来自 [ReShade](https://reshade.me/)（作者 crosire），遵循其自身许可，详见 ReShade 官方说明。
- 字体文件遵循其各自许可。

**免责声明**：注入行为会修改目标进程的内存，可能与游戏的服务条款冲突。仅在单机 / 允许的场景使用。因使用本工具导致的账号处罚或数据异常，作者不承担责任。