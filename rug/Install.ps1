# Builds WindowsRug.exe into %LOCALAPPDATA%\WindowsRug with the C# compiler
# that ships with Windows, sets the wood wallpaper, starts the rug with
# Windows and launches it now.
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $env:LOCALAPPDATA 'WindowsRug'

& (Join-Path $src 'Uninstall.ps1') -KeepFiles 2>$null

New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'rug.png', 'wood.jpg', 'cat.png', 'Uninstall.ps1', 'Uninstall.bat') {
    Copy-Item (Join-Path $src $f) $dst -Force
}
Get-ChildItem $dst | Unblock-File

$exe = Join-Path $dst 'WindowsRug.exe'
Remove-Item $exe -ErrorAction SilentlyContinue
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $src 'Rug.cs'), [Text.Encoding]::UTF8)) `
    -ReferencedAssemblies System.Windows.Forms, System.Drawing `
    -OutputAssembly $exe -OutputType WindowsApplication

# Wood floor wallpaper (Fill)
Add-Type @'
using System.Runtime.InteropServices;
public static class Wallpaper {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(int action, int param, string value, int flags);
}
'@
$wall = Join-Path $dst 'wood.jpg'
Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name WallpaperStyle -Value '10'
Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name TileWallpaper -Value '0'
[Wallpaper]::SystemParametersInfo(20, 0, $wall, 3) | Out-Null

# Start with Windows
$startup = [Environment]::GetFolderPath('Startup')
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $startup 'Windows Rug.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dst
$lnk.Save()

Start-Process $exe
Write-Host 'Done! The rug is on your desktop.'
