# 用系统自带的 csc.exe（.NET Framework 4.8）直接编译，无需 MSBuild / SDK / dotnet
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1
#        powershell -ExecutionPolicy Bypass -File build.ps1 -NoUac   # 编译测试版（不弹 UAC）
# 注意：本文件含中文，必须保存为 UTF-8 with BOM

param(
    # 正式版 manifest 是 requireAdministrator（双击即弹 UAC）。
    # 自动化截图 / 无人值守测试时用 -NoUac 编一个 asInvoker 的副本到 build-test\。
    [switch]$NoUac,

    # 发布构建：定义 SHIPPING 宏，把「测试靶子」两项从界面和进程识别里去掉，
    # 并输出到 pkg\ 下的干净目录。给最终用户打包用这个。
    [switch]$Shipping
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$srcDir = Join-Path $root 'src'

# 约定：正式产物直接输出到 ../ReShadeInjector（部署目录），
# 该目录下的 DLL\ 子目录就是放 DLL 的固定位置，exe 会自动去那里找。
# 这样编完即可用，不用再从 build\ 手动拷一份过去。
$deployDir = Join-Path (Split-Path -Parent $root) 'ReShadeInjector'

if ($Shipping) {
    # 发布构建：产物进 pkg\ReShadeInjector\，这里是个干净的目录，
    # 打包脚本（pack.ps1）会往里面补 DLL / 空目录再压成 zip。
    $outDir = Join-Path $root 'pkg\ReShadeInjector'
    $outExe = Join-Path $outDir 'ReShadeInjector.exe'
    $manifestFile = Join-Path $srcDir 'app.manifest'
} elseif ($NoUac) {
    $outDir = Join-Path $root 'build-test'
    $outExe = Join-Path $outDir 'ReShadeInjector-test.exe'
    $manifestFile = Join-Path $srcDir 'app-asinvoker.manifest'
} else {
    $outDir = $deployDir
    $outExe = Join-Path $outDir 'ReShadeInjector.exe'
    $manifestFile = Join-Path $srcDir 'app.manifest'
}

# ---- 定位 csc.exe ----
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    Write-Error '找不到 csc.exe（需要 .NET Framework 4.x，Windows 自带）'
    exit 1
}

# ---- 定位 .NET Framework 引用程序集 ----
$refRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
$refDir = $null
foreach ($v in @('v4.8.1', 'v4.8', 'v4.7.2', 'v4.7.1', 'v4.7', 'v4.6.2', 'v4.6.1', 'v4.6', 'v4.5.2', 'v4.5.1', 'v4.5')) {
    $cand = Join-Path $refRoot $v
    if (Test-Path -LiteralPath (Join-Path $cand 'PresentationFramework.dll')) { $refDir = $cand; break }
}
if (-not $refDir) {
    # 退回运行时目录（WPF 可以只引用 GAC 里的程序集，编译期够用）
    $refDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
    Write-Warning "未找到 Reference Assemblies，改用运行时目录：$refDir"
}

Write-Host "csc    : $csc"
Write-Host "引用集 : $refDir"

# ---- 准备输出目录 ----
# 重要：正式产物输出到部署目录，那里还放着用户的 DLL\ 子目录（ReShade64.dll 有 4.9 MB）。
# 所以绝对不能整个目录 Remove-Item -Recurse，只能删上一次的 exe 本身。
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
if (Test-Path -LiteralPath $outExe) {
    try {
        Remove-Item -LiteralPath $outExe -Force
    } catch {
        Write-Error ("无法删除旧的 " + $outExe + "，请先关闭正在运行的程序再重编。" + $_.Exception.Message)
        exit 1
    }
}

# ---- 收集所有源文件 ----
$sources = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' -File | Sort-Object Name |
             ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    Write-Error "src 目录下没有 .cs 源文件：$srcDir"
    exit 1
}
Write-Host "源文件 : $($sources.Count) 个"

# ---- 组装参数（用 List 传参，避免 PowerShell 对路径做二次解析）----
# 只显式引用 WPF 独有的程序集；System / System.Core / System.Xml 等
# 已由 csc.exe 默认的 csc.rsp 自动引用，重复引用会报 CS1703。
$refs = @(
    'PresentationFramework.dll',
    'PresentationCore.dll',
    'WindowsBase.dll',
    'System.Xaml.dll',
    # 预设导入要解 .zip。csc.rsp 里没有这两个，显式加上不会 CS1703。
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll'
)

$arguments = New-Object System.Collections.Generic.List[string]
$arguments.Add('/nologo')
$arguments.Add('/target:winexe')
$arguments.Add('/platform:x64')
$arguments.Add('/optimize+')
$arguments.Add('/unsafe-')
if ($Shipping) {
    # 去掉「测试靶子」两项（界面上看不到，进程识别里也不认 测试\ 目录）
    $arguments.Add('/define:SHIPPING')
    Write-Host '模式   : 发布构建（不含测试靶子）'
}
$arguments.Add("/out:`"$outExe`"")
$arguments.Add("/win32manifest:`"$manifestFile`"")
# 应用图标：多尺寸 ico（16/24/32/48/64/128 用 DIB，256 用 PNG 压缩）。
# 只决定资源管理器 / 任务栏 / Alt+Tab 上看到的那个图标；
# 界面左上角那个是 ui.xaml 里画的，不受影响。
$arguments.Add("/win32icon:`"$(Join-Path $srcDir 'app.ico')`"")
# 把 ui.xaml 作为嵌入资源编进 exe（C# 里用 GetManifestResourceStream 读）
# 注意：资源名必须写在引号外，写成 /resource:"路径",资源名
$arguments.Add("/resource:`"$(Join-Path $srcDir 'ui.xaml')`",HoYoInjector.ui.xaml")
# 内置的 ReShade.ini 模板：目标游戏目录缺 ini 时由程序写出去，避免「明明注入了却报失败」
$arguments.Add("/resource:`"$(Join-Path $srcDir 'default-reshade.ini')`",HoYoInjector.default-reshade.ini")
# 自绘对话框的样式表：深色确认框 / 输入框，替代系统 MessageBox
$arguments.Add("/resource:`"$(Join-Path $srcDir 'dialogs.xaml')`",HoYoInjector.dialogs.xaml")
foreach ($r in $refs) {
    $full = Join-Path $refDir $r
    if (Test-Path -LiteralPath $full) { $arguments.Add("/r:`"$full`"") }
    else { Write-Warning "跳过缺失的引用：$full" }
}
foreach ($s in $sources) { $arguments.Add("`"$s`"") }

Write-Host ''
Write-Host '===== 编译 =====' -ForegroundColor Cyan
& $csc $arguments.ToArray()
$code = $LASTEXITCODE

if ($code -ne 0 -or -not (Test-Path -LiteralPath $outExe)) {
    Write-Host ''
    Write-Error "编译失败（exit=$code）"
    exit 1
}

$info = Get-Item -LiteralPath $outExe
Write-Host ''
Write-Host '===== 成功 =====' -ForegroundColor Green
Write-Host ("输出 : " + $info.FullName)
Write-Host ("大小 : " + [math]::Round($info.Length / 1KB, 1) + " KB")

if (-not $NoUac -and -not $Shipping) {
    # 顺便确保 DLL\ 文件夹存在，这样用户拿到就能直接往里丢 DLL
    $dllDir = Join-Path $outDir 'DLL'
    if (-not (Test-Path -LiteralPath $dllDir)) {
        New-Item -ItemType Directory -Path $dllDir -Force | Out-Null
        Write-Host ("已创建 DLL 文件夹：" + $dllDir) -ForegroundColor DarkGray
    }
    $existing = @(Get-ChildItem -LiteralPath $dllDir -Filter '*.dll' -File -ErrorAction SilentlyContinue)
    if ($existing.Count -eq 0) {
        Write-Host '提示：DLL 文件夹还是空的，把 ReShade64.dll 等放进该目录，程序会自动识别。' -ForegroundColor DarkGray
    } else {
        Write-Host ("DLL 文件夹已有 $($existing.Count) 个 dll，程序启动时会自动选中 ReShade64.dll。") -ForegroundColor DarkGray
    }
}