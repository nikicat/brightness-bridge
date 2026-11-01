# Builds BrightnessBridge from source, installs it for the current user and starts it.
# Usage: powershell -ExecutionPolicy Bypass -File install.ps1
param([string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\BrightnessBridge'))

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'BrightnessBridge.cs'
$exe = Join-Path $InstallDir 'BrightnessBridge.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc (.NET Framework 4.x is required)" }

Get-Process BrightnessBridge -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
& $csc /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll "/out:$exe" $source
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Set-ItemProperty -Path $runKey -Name 'BrightnessBridge' -Value "`"$exe`""
Start-Process $exe

Write-Host "Installed to $exe"
Write-Host "Starts automatically at sign-in. Run uninstall.ps1 to remove."
