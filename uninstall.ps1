# Stops BrightnessBridge, removes it from sign-in startup and deletes the installed files.
# Usage: powershell -ExecutionPolicy Bypass -File uninstall.ps1
param([string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\BrightnessBridge'))

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process BrightnessBridge -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 500

Remove-ItemProperty -Path $runKey -Name 'BrightnessBridge' -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force -Confirm:$false $InstallDir }

Write-Host "BrightnessBridge uninstalled."
