# Read-only preflight for an interrupted physical-PC installation.
[CmdletBinding()]
param([string]$SetupDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('=== DMT installation diagnostics (read-only) ===')
$lines.Add('Computer: ' + $env:COMPUTERNAME)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$lines.Add('Elevated: ' + $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
$identity.Dispose()
try {
    $service = Get-Service -Name DMT -ErrorAction Stop
    $lines.Add('Service DMT: ' + $service.Status)
} catch { $lines.Add('Service DMT: ' + $_.Exception.Message) }
try {
    $active = @(Get-CimInstance Win32_Process -Filter "Name='DMT-Setup.exe'")
    foreach ($process in $active) { $lines.Add('Setup process PID: ' + $process.ProcessId) }
    if (-not $active.Count) { $lines.Add('Setup processes: none') }
} catch { $lines.Add('Process inspection: ' + $_.Exception.Message) }
foreach ($root in @((Join-Path $env:ProgramFiles 'DMT'), (Join-Path $env:ProgramData 'DMT'))) {
    $lines.Add('Directory: ' + $root)
    try {
        if (-not (Test-Path -LiteralPath $root)) { $lines.Add('  absent'); continue }
        $lines.Add('  Owner: ' + (Get-Acl -LiteralPath $root).Owner)
        $children = @(Get-ChildItem -LiteralPath $root -Force)
        $lines.Add('  Top-level entries: ' + ($children.Name -join ', '))
    } catch { $lines.Add('  ' + $_.Exception.Message) }
}
$setup = Join-Path $SetupDirectory 'DMT-Setup.exe'
if (Test-Path -LiteralPath $setup) {
    $lines.Add('Candidate: ' + (Get-Item -LiteralPath $setup).VersionInfo.ProductVersion)
    $lines.Add('Candidate SHA256: ' + (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash)
}
$logDirectory = Join-Path $env:LOCALAPPDATA 'DMT\SetupLogs'
if (Test-Path -LiteralPath $logDirectory) {
    $logs = @(Get-ChildItem -LiteralPath $logDirectory -Filter 'DMT-Setup-*.log' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 3)
    foreach ($log in $logs) { $lines.Add('Log available: ' + $log.FullName + ' (' + $log.Length + ' bytes)') }
}
$lines.Add('No files, services, certificates or permissions were changed. No database, credentials or log contents were read.')
$lines -join [Environment]::NewLine
