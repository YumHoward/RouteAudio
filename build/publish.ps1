param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outRoot = Join-Path $root "artifacts\publish"
$appDir = Join-Path $outRoot "RouteAudio"

Write-Host "== 清理输出 =="
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

Write-Host "== 发布 ($Configuration / $Runtime / SelfContained=$SelfContained) =="
$publishArgs = @(
    "publish", (Join-Path $root "src\RouteAudio.App\RouteAudio.App.csproj"),
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-o", $appDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# 附带文档与许可
Copy-Item (Join-Path $root "README.md") $appDir -Force
if (Test-Path (Join-Path $root "LICENSE")) { Copy-Item (Join-Path $root "LICENSE") $appDir -Force }
Copy-Item (Join-Path $root "docs") (Join-Path $appDir "docs") -Recurse -Force

Write-Host "== 打包 zip =="
$zip = Join-Path $outRoot "RouteAudio-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zip -Force

$size = "{0:N1} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "完成：$appDir"
Write-Host "完成：$zip ($size)"
