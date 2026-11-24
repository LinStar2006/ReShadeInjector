# 把项目里所有需要 BOM 的文本文件补成 UTF-8 with BOM。
# 为什么必须：本机 PowerShell 5.1 / csc.exe / cl.exe 都按 ANSI(GBK) 读无 BOM 的 UTF-8，
# 中文会变乱码，进而把 here-string、注释、字符串字面量的结构拆坏，报一堆莫名其妙的语法错。
# 注意：ui.xaml 是被 /resource 嵌入的资源，不需要也不需要 BOM（XAML 自己声明 encoding）。
# 用法： powershell -ExecutionPolicy Bypass -File fix-bom.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

$files = @()
$files += Get-ChildItem -LiteralPath $root -Filter '*.ps1' -File
$files += Get-ChildItem -LiteralPath $root -Filter '*.md' -File
$files += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File
$files += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.manifest' -File
$files += Get-ChildItem -LiteralPath (Join-Path $root 'test') -Filter '*.cs' -File -ErrorAction SilentlyContinue
$files += Get-ChildItem -LiteralPath (Join-Path $root 'test') -Filter '*.c' -File -ErrorAction SilentlyContinue
$files += Get-ChildItem -LiteralPath (Join-Path $root 'test') -Filter '*.cpp' -File -ErrorAction SilentlyContinue

$fixed = 0
foreach ($f in $files) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($b.Length -ge 3 -and $b[0] -eq 239 -and $b[1] -eq 187 -and $b[2] -eq 191) { continue }
    $txt = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($f.FullName, $txt, [System.Text.UTF8Encoding]::new($true))
    Write-Host ("补 BOM: " + $f.Name) -ForegroundColor Yellow
    $fixed++
}
Write-Host ("完成，补了 $fixed 个文件。") -ForegroundColor Green