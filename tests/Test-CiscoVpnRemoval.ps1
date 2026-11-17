$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\scripts\gpo\Remove-CiscoAnyConnectVpn.ps1')
$count=0
function Assert($Value,$Message) { if (-not $Value) { throw $Message }; $script:count++ }
function Entry($Name='Cisco Secure Client - AnyConnect VPN') {
    [pscustomobject]@{Name=$Name; Publisher='Cisco Systems, Inc.'; Version='5.1.12.146'; WindowsInstaller=1; ProductCode='{01234567-89ab-cdef-0123-456789abcdef}'}
}
Assert (@(Select-CiscoVpnPlan @((Entry))).Count -eq 1) 'Secure Client VPN included'
Assert (@(Select-CiscoVpnPlan @((Entry 'Cisco AnyConnect Secure Mobility Client'))).Count -eq 1) 'legacy VPN included'
Assert (@(Select-CiscoVpnPlan @((Entry 'Cisco Secure Client - Umbrella'))).Count -eq 0) 'other Cisco modules excluded'
Assert (@(Select-CiscoVpnPlan @((Entry),(Entry))).Count -eq 1) 'duplicate registry views deduplicated'
$bad=Entry; $bad.ProductCode='cmd /c anything'
Assert ((@(Select-CiscoVpnPlan @($bad)))[0].Action -eq 'Blocked') 'invalid product code blocked'
$bad=Entry; $bad.Publisher='Other'
Assert ((@(Select-CiscoVpnPlan @($bad)))[0].Action -eq 'Blocked') 'publisher validated'
$command=Get-CiscoVpnArguments (Entry).ProductCode 'C:\Log Folder\uninstall.log'
Assert ($command -match '/qn /norestart REBOOT=ReallySuppress') 'standard reboots suppressed'
Assert ($command -match '/L\*v "C:\\Log Folder\\uninstall.log"') 'log path quoted'
function Get-CiscoVpnEntries { Entry }
function Start-Process { throw 'simulation must not launch installers' }
$result=Invoke-CiscoVpnRemoval -Execute $false
Assert ($result.Status -eq 'WouldRemove') 'default plan never calls installer'
$remoteBinding=& { param([bool]$Execute = $false, [switch]$Apply) $Apply.IsPresent -or $Execute } $true
Assert $remoteBinding 'Invoke-Command ArgumentList boolean enables execution explicitly'
function Assert-CiscoVpnRemovalEnvironment {}
function New-CiscoVpnLog { 'C:\Synthetic\uninstall.log' }
$script:installed=$true; $script:code=0; $script:leaveRegistered=$false
function Get-CiscoVpnEntries { if ($script:installed) { Entry } }
function Start-Process {
    if (-not $script:leaveRegistered) { $script:installed=$false }
    [pscustomobject]@{ExitCode=$script:code}
}
Assert ((Invoke-CiscoVpnRemoval $true).Status -eq 'RemovedVerified') 'successful removal verified from registry'
$script:installed=$true; $script:leaveRegistered=$true
Assert ((Invoke-CiscoVpnRemoval $true).Status -eq 'FailedOrStillPresent') 'exit zero alone is not successful removal'
$script:installed=$true; $script:code=3010
Assert ((Invoke-CiscoVpnRemoval $true).Status -eq 'RestartRequiredVerifyAfterRestart') 'pending restart is not reported as verified removal'
$script:installed=$true; $script:code=1603
Assert ((Invoke-CiscoVpnRemoval $true).Status -eq 'FailedOrStillPresent') 'installer failure remains visible'
$script:installed=$false
Assert ((Invoke-CiscoVpnRemoval $true).Status -eq 'Absent') 'idempotent absent product'
function Assert-CiscoVpnRemovalEnvironment { throw 'active_cisco_vpn_adapter_removal_deferred' }
$script:installed=$true
$blocked=$false
try { Invoke-CiscoVpnRemoval $true | Out-Null } catch { $blocked=$_.Exception.Message -eq 'active_cisco_vpn_adapter_removal_deferred' }
Assert $blocked 'active VPN prevents removal'
Write-Host "PASS Cisco VPN removal assertions=$count"
