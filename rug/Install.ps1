# Builds WindowsRug.exe into %LOCALAPPDATA%\WindowsRug with the C# compiler
# that ships with Windows, starts the rug with Windows and launches it now.
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $env:LOCALAPPDATA 'WindowsRug'

& (Join-Path $src 'Uninstall.ps1') -KeepFiles 2>$null

New-Item -ItemType Directory -Force -Path $dst | Out-Null
# Everything in this folder (images, sub-folders, the uninstaller) goes along.
Get-ChildItem $src | Where-Object { $_.Name -notin 'Install.ps1', 'Install.bat', 'Rug.cs' } |
    ForEach-Object { Copy-Item $_.FullName $dst -Recurse -Force }
foreach ($d in 'rugs', 'floors', 'furniture') {
    if (-not (Test-Path (Join-Path $dst $d))) { throw "Missing folder '$d' - extract the whole zip and run Install.bat from inside it." }
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
