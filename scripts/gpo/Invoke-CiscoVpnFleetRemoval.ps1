#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$TargetsPath = (Join-Path $PSScriptRoot 'CiscoAnyConnect.targets.txt'),
    [switch]$Apply
)
$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot 'Remove-CiscoAnyConnectVpn.ps1'
$targets=@(Get-Content -LiteralPath $TargetsPath | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
if ($targets.Count -eq 0) { throw 'empty_target_list' }
$results=foreach ($target in $targets) {
    try {
        Invoke-Command -ComputerName $target -Authentication Kerberos -FilePath $scriptPath `
            -ArgumentList $Apply.IsPresent -ErrorAction Stop |
            Select-Object Computer,Product,Version,Status,ExitCode,Log,@{n='Message';e={''}}
    } catch {
        [pscustomobject]@{Computer=$target;Product='';Version='';Status='UnreachableOrError';ExitCode=$null;Log='';Message=$_.Exception.Message}
    }
}
$mode='plan'; if ($Apply) { $mode='removal' }
$report=Join-Path $PSScriptRoot ('CiscoVPN-' + $mode + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.csv')
$results | Export-Csv -LiteralPath $report -NoTypeInformation -Encoding UTF8
$output=($results | Format-Table Computer,Version,Status,Message -Wrap -AutoSize | Out-String -Width 250)
$output += "`nCSV: $report`nMode: $mode`n"
$output | Set-Clipboard
Get-Clipboard
