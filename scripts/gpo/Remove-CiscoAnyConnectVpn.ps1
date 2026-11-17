#requires -Version 5.1
<#
.SYNOPSIS
Remove only Cisco AnyConnect VPN (including Secure Client 5.x). Default: plan only.
.DESCRIPTION
Run locally as an administrator, as a computer startup script, or with Invoke-Command
from the domain management PC. No stored credentials, gpresult or Win32_Product.
Other Cisco modules are excluded. Active Cisco VPN adapters block removal.
#>
[CmdletBinding()]
param([bool]$Execute = $false, [switch]$Apply)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CiscoVpnEntries {
    $views = @([Microsoft.Win32.RegistryView]::Registry32)
    if ([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry64 }
    foreach ($view in $views) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $root = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
            if ($null -eq $root) { continue }
            try {
                foreach ($name in $root.GetSubKeyNames()) {
                    $entry = $root.OpenSubKey($name)
                    if ($null -eq $entry) { continue }
                    try {
                        [pscustomobject]@{
                            ProductCode=$name; Name=[string]$entry.GetValue('DisplayName','')
                            Publisher=[string]$entry.GetValue('Publisher',''); Version=[string]$entry.GetValue('DisplayVersion','')
                            WindowsInstaller=$entry.GetValue('WindowsInstaller',0)
                        }
                    } finally { $entry.Dispose() }
                }
            } finally { $root.Dispose() }
        } finally { $base.Dispose() }
    }
}

function Select-CiscoVpnPlan {
    param([object[]]$Entries)
    $names = @('Cisco Secure Client - AnyConnect VPN', 'Cisco AnyConnect Secure Mobility Client', 'Cisco AnyConnect VPN Client')
    $seen = @{}
    foreach ($entry in $Entries) {
        if ($entry.Name -notin $names) { continue }
        if ($seen.ContainsKey($entry.ProductCode)) { continue }
        $seen[$entry.ProductCode] = $true
        $action='Remove'; $reason='exact_vpn_product'
        if ($entry.Publisher -notmatch '^Cisco Systems,?\s+Inc\.?$') { $action='Blocked'; $reason='unexpected_publisher' }
        elseif ($entry.WindowsInstaller -ne 1 -or $entry.ProductCode -notmatch '^\{[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}$') {
            $action='Blocked'; $reason='unsupported_uninstaller'
        }
        [pscustomobject]@{ Entry=$entry; Action=$action; Reason=$reason }
    }
}

function Get-CiscoVpnArguments {
    param([string]$ProductCode,[string]$Log)
    if ($ProductCode -notmatch '^\{[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}$' -or $Log.Contains('"')) { throw 'invalid_uninstall_arguments' }
    return "/x $ProductCode /qn /norestart REBOOT=ReallySuppress /L*v `"$Log`""
}

function Assert-CiscoVpnRemovalEnvironment {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'administrator_or_system_required' }
    if (@(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object {
        $_.Status -eq 'Up' -and $_.InterfaceDescription -match 'Cisco.*(AnyConnect|Secure Client)'
    }).Count -gt 0) { throw 'active_cisco_vpn_adapter_removal_deferred' }
}

function New-CiscoVpnLog {
    $folder=Join-Path $env:ProgramData ('DMT\CiscoVpnRemoval\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    return Join-Path $folder 'uninstall.log'
}

function Invoke-CiscoVpnRemoval {
    param([bool]$Execute)
    $plan = @(Select-CiscoVpnPlan @(Get-CiscoVpnEntries))
    if ($plan.Count -eq 0) {
        return [pscustomobject]@{ Computer=$env:COMPUTERNAME; Product=''; Version=''; Status='Absent'; ExitCode=$null; Log='' }
    }
    if ($Execute) {
        Assert-CiscoVpnRemovalEnvironment
        if (@($plan | Where-Object Action -eq 'Blocked').Count -gt 0) { throw 'unsupported_vpn_installation_no_changes' }
    }
    foreach ($item in $plan) {
        $entry=$item.Entry; $code=$null; $log=''
        $status = $item.Action
        if (-not $Execute) { $status='Would' + $status }
        else {
            # Re-read immediately before removal in case an upgrade changed the product.
            $fresh = @(Select-CiscoVpnPlan @(Get-CiscoVpnEntries) | Where-Object { $_.Action -eq 'Remove' -and $_.Entry.ProductCode -eq $entry.ProductCode })
            if ($fresh.Count -eq 0) { $status='ChangedSincePlanning' }
            else {
                $log=New-CiscoVpnLog
                $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
                    -ArgumentList (Get-CiscoVpnArguments $entry.ProductCode $log) -WindowStyle Hidden -Wait -PassThru
                $code=$process.ExitCode
                $remaining=@(Select-CiscoVpnPlan @(Get-CiscoVpnEntries) | Where-Object { $_.Entry.ProductCode -eq $entry.ProductCode })
                if ($code -eq 3010) { $status='RestartRequiredVerifyAfterRestart' }
                elseif ($code -in 0,1605,1614 -and $remaining.Count -eq 0) { $status='RemovedVerified' }
                else { $status='FailedOrStillPresent' }
            }
        }
        [pscustomobject]@{ Computer=$env:COMPUTERNAME; Product=$entry.Name; Version=$entry.Version; Status=$status; ExitCode=$code; Log=$log }
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
Invoke-CiscoVpnRemoval -Execute ($Apply.IsPresent -or $Execute)
