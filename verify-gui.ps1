# GUI 启动 + 截图验证脚本
# 目的：确认 exe 能正常启动、WPF 窗口能渲染出预期界面
# 用法：powershell -ExecutionPolicy Bypass -File verify-gui.ps1
#       powershell -ExecutionPolicy Bypass -File verify-gui.ps1 -Page settings
#
# 注意：正式版 ReShadeInjector.exe 的 manifest 是 requireAdministrator，
# 双击就弹 UAC，脚本无法在无人值守下启动它。所以本脚本默认验证
# build-test\ReShadeInjector-test.exe（asInvoker 版，同一份源码）。
# 传 -Release 才会去启动正式版（需要你手动点 UAC）。
#
# 截图方式：用程序自带的 --screenshot 参数让它把自己渲染成 PNG，
# 而不是从屏幕抓图 —— 屏幕抓图会被别的窗口挡住，分层窗口 PrintWindow 也不准。

param(
    [switch]$Release,
    # 直接指定要验证的 exe（例如部署目录里的副本）
    [string]$ExePath,
    # 截图文件名（不含扩展名），例如 home / settings
    [string]$Page = 'home'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$deployDir = Join-Path (Split-Path -Parent $root) 'ReShadeInjector'

if ($ExePath) {
    $exe = $ExePath
    $outDir = Split-Path -Parent $exe
} elseif ($Release) {
    $exe = Join-Path $deployDir 'ReShadeInjector.exe'
    $outDir = $deployDir
} else {
    $exe = Join-Path $root 'build-test\ReShadeInjector-test.exe'
    $outDir = Join-Path $root 'build-test'
}
$outLog = Join-Path $outDir 'stdout.txt'
$errLog = Join-Path $outDir 'stderr.txt'
$shot = Join-Path $outDir "screenshot-$Page.png"
$appErr = Join-Path $outDir 'injector-error.log'

if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

foreach ($f in @($outLog, $errLog, $shot, $appErr)) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "[FAIL] 找不到 exe：$exe"
    exit 1
}
Write-Host "exe : $exe"

# 关键：重定向子进程的标准输出/错误，否则 GUI 进程会继承宿主管道导致命令挂起
$proc = Start-Process -FilePath $exe -ArgumentList @('--screenshot', $shot) -PassThru -WindowStyle Normal `
    -RedirectStandardOutput $outLog -RedirectStandardError $errLog
Write-Host "PID : $($proc.Id)"

# 程序自己截图完就退出，等它退
if (-not $proc.WaitForExit(30000)) {
    Write-Host "[FAIL] 30 秒内没退出（可能卡在 MessageBox 或 XAML 解析失败）"
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $errLog) {
        $e = Get-Content -LiteralPath $errLog -Raw -Encoding UTF8
        if ($e) { Write-Host "--- stderr ---"; Write-Host $e }
    }
    exit 1
}

if (Test-Path -LiteralPath $shot) {
    $len = (Get-Item -LiteralPath $shot).Length
    Write-Host "[OK] 截图已保存：$shot（$len 字节）"
} else {
    Write-Host "[FAIL] 没有生成截图"
    if (Test-Path -LiteralPath $errLog) {
        $e = Get-Content -LiteralPath $errLog -Raw -Encoding UTF8
        if ($e) { Write-Host "--- stderr ---"; Write-Host $e }
    }
    exit 1
}

if (Test-Path -LiteralPath $appErr) {
    $content = Get-Content -LiteralPath $appErr -Raw -Encoding UTF8
    if ($content) {
        Write-Host "--- 应用错误日志 ---"
        Write-Host $content
    }
}

# 清掉脚本产生的输出文件，避免污染部署目录
foreach ($f in @($outLog, $errLog)) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}

Write-Host '[DONE]'