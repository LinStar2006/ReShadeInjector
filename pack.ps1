# 打包发布版：编一个不含测试功能的 exe，连同干净的目录骨架压成 zip
# 用法： powershell -ExecutionPolicy Bypass -File pack.ps1
#        powershell -ExecutionPolicy Bypass -File pack.ps1 -NoDll      # 连 ReShade64.dll 也不带
#        powershell -ExecutionPolicy Bypass -File pack.ps1 -NoManual   # 不带 使用说明.txt（发 GitHub 用）
# 注意：本文件含中文，必须保存为 UTF-8 with BOM

param(
    # 默认把部署目录里的 DLL\ReShade64.dll 一起打进包 —— 没有它这个工具没法用。
    # 只想发个纯壳的时候加 -NoDll。
    [switch]$NoDll,

    # 不写 使用说明.txt。发 GitHub Releases 时用这个：
    # 说明内容在仓库的 README.md 里已经有了，再塞一份纯文本进包是重复的。
    [switch]$NoManual
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pkgRoot = Join-Path $root 'pkg'
$stageDir = Join-Path $pkgRoot 'ReShadeInjector'
$zipPath = Join-Path $pkgRoot 'ReShadeInjector.zip'
$deployDll = Join-Path (Split-Path -Parent $root) 'ReShadeInjector\DLL\ReShade64.dll'

Write-Host ''
Write-Host '===== 1. 准备干净的输出目录 =====' -ForegroundColor Cyan

# pkg\ 是我们自己的构建产物目录，整个删掉重建是安全的
# （部署目录不能这么干，那里有用户的 DLL 和游戏数据）
# 但上一版打出来的 exe 有可能正被运行着（双击试过），这时 Windows 不让删。
# 先用「改名」把它挪开 —— Windows 允许重命名正在运行的 exe。
if (Test-Path -LiteralPath $pkgRoot) {
    try {
        Remove-Item -LiteralPath $pkgRoot -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Host '上一版 exe 似乎还在运行，改成先改名再清理。' -ForegroundColor DarkYellow

        Get-ChildItem -LiteralPath $pkgRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -eq '.exe' } |
            ForEach-Object {
                # 关键：挪到 pkg 外面去。运行中的进程会一直占着这个文件句柄，
                # 改名到同目录只是换个名字，文件还是被锁着、还是会打进包。
                # 挪到 pkg\_stale_xxx\ 之下、且不在 stageDir 里，就与打包无关了。
                try {
                    $staleDir = Join-Path $root ('_stale_' + (Get-Date -Format 'HHmmss'))
                    New-Item -ItemType Directory -Path $staleDir -Force | Out-Null
                    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $staleDir $_.Name) -Force -ErrorAction Stop
                }
                catch { Write-Host ('  挪不动（忽略）：' + $_.FullName) -ForegroundColor DarkGray }
            }

        try {
            Remove-Item -LiteralPath $pkgRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            # 还是删不掉就清空文件、目录留着继续用，别把打包整个卡死
            Write-Host '改名后仍删不掉，改为只清空残留文件。' -ForegroundColor DarkYellow
            Get-ChildItem -LiteralPath $pkgRoot -Recurse -File -ErrorAction SilentlyContinue |
                ForEach-Object {
                    try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { }
                }
        }
    }
}
New-Item -ItemType Directory -Path $pkgRoot -Force | Out-Null

Write-Host ''
Write-Host '===== 2. 编译发布版（不含测试靶子） =====' -ForegroundColor Cyan
& (Join-Path $root 'build.ps1') -Shipping
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $stageDir 'ReShadeInjector.exe'))) {
    Write-Error '编译失败，已中止打包。'
    exit 1
}

Write-Host ''
Write-Host '===== 3. 补目录骨架 =====' -ForegroundColor Cyan

# 空目录骨架。名字和程序里写死的一致（Shader / Addons / Fonts / ScreenShot / DLL）。
$skeleton = @('DLL', 'Shader', 'Addons', 'Fonts', 'ScreenShot')
foreach ($d in $skeleton) {
    New-Item -ItemType Directory -Path (Join-Path $stageDir $d) -Force | Out-Null
}

Write-Host ''
Write-Host '===== 4. 放入必需文件 =====' -ForegroundColor Cyan
if ($NoDll) {
    Write-Host '按 -NoDll 跳过 ReShade64.dll（用户需自行放进 DLL\）' -ForegroundColor DarkYellow
} elseif (Test-Path -LiteralPath $deployDll) {
    $dllSize = (Get-Item -LiteralPath $deployDll).Length
    Copy-Item -LiteralPath $deployDll -Destination (Join-Path $stageDir 'DLL\ReShade64.dll') -Force
    Write-Host ("已放入 DLL\ReShade64.dll（{0:N1} MB）" -f ($dllSize / 1MB)) -ForegroundColor DarkGray
} else {
    Write-Host '警告：没找到 ReShade64.dll，包里不会有它（用户需自行放入 DLL\）' -ForegroundColor Yellow
}

# ---- 字体 ----
# ReShade 覆盖层的中文字体。不带的话游戏里中文会显示成方块/豆腐，
# 所以发布包必须自带一份 —— 由 IniPatcher 把 ini 的 Font/EditorFont/LatinFont
# 指向程序目录下的 Fonts\。
$deployFonts = Join-Path (Split-Path -Parent $root) 'ReShadeInjector\Fonts'
if (Test-Path -LiteralPath $deployFonts) {
    $fontFiles = @(Get-ChildItem -LiteralPath $deployFonts -File -ErrorAction SilentlyContinue)
    if ($fontFiles.Count -gt 0) {
        $fontTotal = 0
        foreach ($f in $fontFiles) {
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $stageDir 'Fonts') -Force
            $fontTotal += $f.Length
            Write-Host ("已放入 Fonts\{0}（{1:N1} MB）" -f $f.Name, ($f.Length / 1MB)) -ForegroundColor DarkGray
        }
    } else {
        Write-Host '警告：部署目录的 Fonts\ 是空的，包里不会有字体' -ForegroundColor Yellow
    }
} else {
    Write-Host '警告：没找到部署目录的 Fonts\，包里不会有字体（游戏内中文会显示成方块）' -ForegroundColor Yellow
}

# ---- 使用说明 ----
# here-string 的结束标记 '@ 必须顶格，所以这一整段没法缩进到 if 里面
if (-not $NoManual) {
$readme = @'
ReShade Injector

一个把 ReShade 之类的 DLL 注入到游戏进程里的小工具。


怎么用

1. 双击 ReShadeInjector.exe。会弹一次 UAC，点是 —— 注入游戏进程需要管理员权限。

2. 在左边「目标进程」里点一下要注入的游戏：
     原神（国服 / 哔哩哔哩）  → YuanShen.exe
     崩坏3                    → BH3.exe
     崩坏：星穹铁道           → StarRail.exe
     绝区零                   → ZenlessZoneZero.exe
     异环                     → HTGame.exe

   游戏还没启动也没关系，直接选它，然后勾上下方「等待进程启动」，
   再点右下角按钮，等游戏起来会自动注入。
   等待期间再点一次同一个按钮，会直接帮你把游戏启动起来
   （不知道装在哪的话会弹窗让你选一次它的 exe，选完就记住了）。

   注意：异环只能用它自己的启动器进游戏，直接跑 HTGame.exe 会登录失败。
   所以选异环时那个按钮不会替你启动游戏，只会提示你去开启动器 ——
   你先用启动器拉起来，这里照样会自动接上并注入。

   也可以不选列表，直接在输入框里敲任意进程名（不带 .exe，带也行）。

3. 确认「注入模块」那一栏指向 DLL\ReShade64.dll。文件状态要是绿的
   「文件存在」。

4. 点右下角的按钮。成功与否看底部状态点和「运行日志」里的输出。


目录约定

  ReShadeInjector.exe    主程序
  DLL\                   要注入的 DLL 放这里，启动时会自动选中 ReShade64.dll
  Shader\                滤镜包（.fx）。按游戏分开存放：
                           Shader\<进程名>\<预设名>\Shaders\
                           Shader\<进程名>\<预设名>\Textures\
  Addons\                ReShade 的插件（.addon64）
  Fonts\                 覆盖层用的字体（.ttf / .ttc），中文界面需要
  ScreenShot\            ReShade 截图输出目录

随包提供：ReShade64.dll（没有它这个工具没法用）和 Fonts\ 下的中文字体
（没有它覆盖层里的中文会显示成方块）。

需要你自己放：Shader\ 下的滤镜包、Addons\ 下的插件。
这两个目录发布包里是空的，程序会自动把路径指过去，扔进去内容就能用。


预设

左边第二个图标（四角星）是「预设」页。每个游戏可以有多个预设，
每个预设自带一份 Shader / Textures 和一个滤镜配置文件，互不干扰。

  新建预设      只建空目录，之后自己往里丢滤镜
  选择文件夹…   从一个文件夹导入，会自动找里面的 Shaders / Textures
  选择压缩包…   直接导入 .zip（.7z / .rar 请先解压）
  应用选中的预设 把这个预设的路径写进游戏目录下的 ReShade.ini

应用预设后不用退出游戏：在游戏里按 Home 呼出 ReShade 覆盖层，
点一下「重新加载」就生效。

预设列表上右键还有：打开预设文件夹、打开游戏目录、重命名、删除。


关于 ReShade.ini

ReShade 只认「游戏 exe 所在目录」下的 ReShade.ini，找不到会直接拒绝加载。

本工具会自动判断：
  · 游戏目录里没有 ReShade.ini → 用内置模板生成一份
  · 已经有 → 只改里面的路径项（滤镜路径、纹理路径、字体路径、预设文件路径），
    你自己的界面样式、快捷键、各项设置一个字符都不动

游戏不在默认位置时，工具会依次尝试：运行中的进程 → HoYoPlay 注册表 →
上次你手动指定过的路径。都失败才会弹窗让你手动选一次，选完会记住。


常见问题

· 注入失败，日志说 LoadLibraryW 返回 0
    多半是位数不匹配（要 64 位 DLL 注入 64 位游戏），或者 DLL 路径不对。

· 注入成功但游戏里没效果
    按 Home 看覆盖层能不能出来。能出来但没效果，检查 Shader\ 下的滤镜包
    是不是空的，或者在「预设」页应用一个已经装好滤镜的预设。

· 提示「目标选错了（这是启动器）」
    别把 HoYoPlay / launcher 当游戏注入，要选游戏本体（YuanShen.exe 之类）。

· 游戏有内核级反作弊
    对已经在跑的游戏做远程线程注入可能被拦。试试先点注入（勾上等待），
    再从启动器启动游戏。


许可

ReShade64.dll 版权归 crosire 及 ReShade 项目所有，遵循其自带许可。
本注入器本身只是一个 LoadLibraryW 的调用外壳。
'@

$readmePath = Join-Path $stageDir '使用说明.txt'
# 记事本要认中文得带 BOM
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($true)))
}
else {
    Write-Host '按 -NoManual 跳过 使用说明.txt（说明见仓库 README.md）' -ForegroundColor DarkYellow
}

Write-Host ''
Write-Host '===== 5. 打包 =====' -ForegroundColor Cyan

# 第 1 步为了绕开「exe 正在运行」而改出来的 *.old 是临时残留，
# 绝不能打进发布包 —— 用户解压出来看到两个 exe 会懵。
# 注意：这里必须用 Where-Object 过滤，不能写 -Include '*.old'。
# -LiteralPath 与 -Include 同时用的时候 -Include 会被忽略，结果是删掉全部文件
# （之前用这种写法把整个 test\ 目录清空过一次）。
Get-ChildItem -LiteralPath $stageDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq '.old' } |
    ForEach-Object {
        try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop }
        catch { Write-Host ('  清不掉（忽略）：' + $_.Name) -ForegroundColor DarkGray }
    }

# 用 .NET 的 ZipFile 而不是 Compress-Archive：PS 5.1 的 Compress-Archive
# 对中文文件名/路径的处理不如它稳。includeBaseDirectory=true 让解压出来是
# 一个 ReShadeInjector\ 文件夹，而不是散落一地。
[System.Reflection.Assembly]::LoadWithPartialName('System.IO.Compression.FileSystem') | Out-Null
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath)

Write-Host ''
Write-Host '===== 完成 =====' -ForegroundColor Green
Write-Host ("压缩包 : " + $zipPath)
Write-Host ("大小   : " + [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1KB, 1) + " KB")
Write-Host ''
Write-Host '包内容：' -ForegroundColor DarkGray
Get-ChildItem -LiteralPath $stageDir -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($stageDir.Length + 1)
    if ($_.PSIsContainer) {
        Write-Host ("  " + $rel + "\") -ForegroundColor DarkGray
    } else {
        Write-Host ("  " + $rel + "  " + $_.Length + " 字节") -ForegroundColor DarkGray
    }
}