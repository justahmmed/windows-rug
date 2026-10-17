# Stops the rug and removes it from startup. -KeepFiles leaves the installed files.
param([switch]$KeepFiles)
Stop-Process -Name WindowsRug -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'Windows Rug.lnk') -ErrorAction SilentlyContinue
if (-not $KeepFiles) {
    Remove-Item (Join-Path $env:APPDATA 'WindowsRug') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:LOCALAPPDATA 'WindowsRug') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Rug removed. (Your wallpaper stays; change it from Settings > Personalization.)'
}
