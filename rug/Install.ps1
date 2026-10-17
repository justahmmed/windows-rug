# Builds WindowsRug.exe into %LOCALAPPDATA%\WindowsRug with the C# compiler
# that ships with Windows, starts the rug with Windows and launches it now.
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $env:LOCALAPPDATA 'WindowsRug'

& (Join-Path $src 'Uninstall.ps1') -KeepFiles 2>$null

New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'cat.png', 'items.png', 'robot.png', 'Uninstall.ps1', 'Uninstall.bat') {
    Copy-Item (Join-Path $src $f) $dst -Force
}
foreach ($d in 'rugs', 'floors') {
    Copy-Item (Join-Path $src $d) $dst -Recurse -Force
}
Get-ChildItem $dst -Recurse -File | Unblock-File

$exe = Join-Path $dst 'WindowsRug.exe'
Remove-Item $exe -ErrorAction SilentlyContinue
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $src 'Rug.cs'), [Text.Encoding]::UTF8)) `
    -ReferencedAssemblies System.Windows.Forms, System.Drawing `
    -OutputAssembly $exe -OutputType WindowsApplication

# The rug sets the wood-floor wallpaper itself when it starts.

# Start with Windows
$startup = [Environment]::GetFolderPath('Startup')
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $startup 'Windows Rug.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dst
$lnk.Save()

Start-Process $exe
Write-Host 'Done! The rug is on your desktop.'
