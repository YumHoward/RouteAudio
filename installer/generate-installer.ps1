param(
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [Parameter(Mandatory = $true)][string]$OutputWxs,
    [string]$Version = "0.2.0"
)

$ErrorActionPreference = "Stop"

function XmlEscape([string]$text) {
    return $text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
}

$files = @(Get-ChildItem -Path $SourceDir -File | Where-Object { $_.Extension -ne '.pdb' } | Sort-Object Name)
if ($files.Count -eq 0) { throw "发布目录为空: $SourceDir" }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <!-- 由 installer/generate-installer.ps1 生成，请勿手工编辑 -->')
[void]$sb.AppendLine('  <Package Name="RouteAudio"')
[void]$sb.AppendLine('           Manufacturer="RouteAudio Contributors"')
[void]$sb.AppendLine(('           Version="{0}"' -f $Version))
[void]$sb.AppendLine('           UpgradeCode="8F1B9A7C-2E4D-4B6A-9C3F-1D8E5A6B7C90"')
[void]$sb.AppendLine('           Scope="perUser" Compressed="yes">')
[void]$sb.AppendLine('    <MajorUpgrade DowngradeErrorMessage="已安装更新版本的 RouteAudio。" />')
[void]$sb.AppendLine('    <MediaTemplate EmbedCab="yes" />')
[void]$sb.AppendLine('    <StandardDirectory Id="LocalAppDataFolder">')
[void]$sb.AppendLine('      <Directory Id="ProgramsFolder" Name="Programs">')
[void]$sb.AppendLine('        <Directory Id="INSTALLFOLDER" Name="RouteAudio" />')
[void]$sb.AppendLine('      </Directory>')
[void]$sb.AppendLine('    </StandardDirectory>')
[void]$sb.AppendLine('    <StandardDirectory Id="ProgramMenuFolder">')
[void]$sb.AppendLine('      <Directory Id="AppShortcutFolder" Name="RouteAudio" />')
[void]$sb.AppendLine('    </StandardDirectory>')
[void]$sb.AppendLine('    <Feature Id="MainFeature" Title="RouteAudio" Level="1">')
[void]$sb.AppendLine('      <ComponentGroupRef Id="AppFiles" />')
[void]$sb.AppendLine('      <ComponentRef Id="StartMenuShortcut" />')
[void]$sb.AppendLine('      <ComponentRef Id="InstallFolderCleanup" />')
[void]$sb.AppendLine('    </Feature>')
[void]$sb.AppendLine('  </Package>')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

foreach ($file in $files) {
    $safe = ($file.Name -replace '[^A-Za-z0-9]', '_')
    [void]$sb.AppendLine(('      <Component Id="cmp_{0}" Directory="INSTALLFOLDER" Guid="*">' -f $safe))
    [void]$sb.AppendLine(('        <File Id="fil_{0}" Source="{1}" />' -f $safe, (XmlEscape $file.FullName)))
    [void]$sb.AppendLine('      </Component>')
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <Component Id="InstallFolderCleanup" Directory="INSTALLFOLDER" Guid="B7E5D3C1-4A2F-4E6B-9C8D-0F1E2A3B4C5D">')
[void]$sb.AppendLine('      <RemoveFolder Id="RemoveInstallFolder" Directory="INSTALLFOLDER" On="uninstall" />')
[void]$sb.AppendLine('      <RegistryValue Root="HKCU" Key="Software\RouteAudio" Name="InstallFolder" Type="integer" Value="1" KeyPath="yes" />')
[void]$sb.AppendLine('    </Component>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <Component Id="StartMenuShortcut" Directory="AppShortcutFolder" Guid="3F0C2B1A-9D4E-4C8B-A7F6-2E1D0C9B8A70">')
[void]$sb.AppendLine('      <Shortcut Id="AppShortcut" Name="RouteAudio"')
[void]$sb.AppendLine('                Description="把每个应用的声音送到指定的输出设备"')
[void]$sb.AppendLine('                Target="[INSTALLFOLDER]RouteAudio.exe" WorkingDirectory="INSTALLFOLDER" />')
[void]$sb.AppendLine('      <RemoveFolder Id="RemoveAppShortcutFolder" Directory="AppShortcutFolder" On="uninstall" />')
[void]$sb.AppendLine('      <RegistryValue Root="HKCU" Key="Software\RouteAudio" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />')
[void]$sb.AppendLine('    </Component>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputWxs, $sb.ToString(), $encoding)
Write-Host ("已生成 {0}（{1} 个文件）" -f $OutputWxs, $files.Count)
