#requires -Version 5.1
<#
.SYNOPSIS
Removes machine-wide PDF24 Creator versions older than MinimumVersion.
.DESCRIPTION
Default: read-only inventory/plan. -Apply requires InstallerPath, stages and validates
the replacement MSI before any uninstall, then installs/repairs it after cleanup.
No Win32_Product queries, registry deletion, process killing or reboot commands.
Use a computer startup script. See README.md in this directory before deployment.
#>
[CmdletBinding()]
param(
    [string]$MinimumVersion = '11.31.1',
    [string]$InstallerPath,
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-Pdf24Version {
    param([string]$Text)
    if ($Text -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { return $null }
    $parts = $Text.Split('.')
    if ($parts.Count -eq 3) { $Text += '.0' }
    try { return [version]$Text } catch { return $null }
}

function Get-Pdf24Inventory {
    $views = @([Microsoft.Win32.RegistryView]::Registry32)
    if ([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry64 }
    foreach ($view in $views) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $uninstall = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
            if ($null -eq $uninstall) { continue }
            try {
                foreach ($name in $uninstall.GetSubKeyNames()) {
                    $key = $uninstall.OpenSubKey($name)
                    if ($null -eq $key) { continue }
                    try {
                        $displayName = [string]$key.GetValue('DisplayName', '')
                        if ($displayName -notmatch '^PDF24 Creator(?:\s+.*)?$') { continue }
                        [pscustomobject]@{
                            Key = $name; View = [string]$view; Name = $displayName
                            Version = [string]$key.GetValue('DisplayVersion', '')
                            Publisher = [string]$key.GetValue('Publisher', '')
                            WindowsInstaller = $key.GetValue('WindowsInstaller', 0)
                            UninstallString = [string]$key.GetValue('UninstallString', '')
                        }
                    } finally { $key.Dispose() }
                }
            } finally { $uninstall.Dispose() }
        } finally { $base.Dispose() }
    }
}

function Get-Pdf24Plan {
    param([object[]]$Inventory, [version]$Minimum)
    foreach ($entry in $Inventory) {
        $version = ConvertTo-Pdf24Version $entry.Version
        $action = 'Remove'
        $reason = 'older_version'
        if ($entry.Publisher -notmatch '^geek software(?: GmbH)?$') { $action = 'Blocked'; $reason = 'unrecognized_publisher' }
        elseif ($null -eq $version) { $action = 'Blocked'; $reason = 'unknown_version' }
        elseif ($version -ge $Minimum) { $action = 'Keep'; $reason = 'current_or_newer' }
        [pscustomobject]@{ Entry = $entry; Version = $version; Action = $action; Reason = $reason }
    }
}

function Get-Pdf24UninstallCommand {
    param($Entry, [string]$LogPath)
    if ($LogPath.Contains('"')) { throw 'invalid_log_path' }
    if ($Entry.WindowsInstaller -eq 1) {
        if ($Entry.Key -notmatch '^\{[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}$') { throw 'invalid_msi_product_code' }
        return [pscustomobject]@{
            File = Join-Path $env:SystemRoot 'System32\msiexec.exe'
            Arguments = "/x $($Entry.Key) /qn /norestart REBOOT=ReallySuppress /L*v `"$LogPath`""
            Kind = 'MSI'
        }
    }
    # Read the executable only. Never run a registry command line through a shell.
    $text = $Entry.UninstallString.Trim()
    if ($text -match '^"([^"\r\n]+\.exe)"(?:\s.*)?$') { $exe = $Matches[1] }
    elseif ($text -match '^([^"\r\n]+?\.exe)(?:\s.*)?$') { $exe = $Matches[1] }
    else { throw 'unsupported_uninstaller' }
    if (-not [IO.Path]::IsPathRooted($exe) -or $exe.StartsWith('\\')) { throw 'untrusted_uninstaller_path' }
    $exe = [IO.Path]::GetFullPath($exe)
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
    $allowed = $false
    foreach ($root in $roots) {
        $prefix = (Join-Path $root 'PDF24') + '\'
        if ($exe.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $allowed = $true }
    }
    if (-not $allowed -or [IO.Path]::GetFileName($exe) -notmatch '^unins\d{3}\.exe$') { throw 'untrusted_uninstaller_path' }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'uninstaller_missing' }
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?i)geek software') { throw 'untrusted_uninstaller_signature' }
    return [pscustomobject]@{
        File = $exe
        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$LogPath`""
        Kind = 'Inno'
    }
}

function New-Pdf24WorkFolder {
    # Files executed as SYSTEM must not be writable by ordinary users.
    $root = Join-Path $env:ProgramFiles 'DMT-GpoTools'
    if (Test-Path -LiteralPath $root) {
        if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'unsafe_work_folder' }
    } else { New-Item -ItemType Directory -Path $root | Out-Null }
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $identity = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $root -AclObject $acl
    $folder = Join-Path $root ('PDF24-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder | Out-Null
    return $folder
}

function Get-Pdf24MsiIdentity {
    param([string]$Path)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $null
    try {
        $database = $installer.OpenDatabase($Path, 0)
        $properties = @{}
        foreach ($property in @('ProductName', 'ProductVersion', 'Manufacturer', 'ProductCode')) {
            $view = $database.OpenView(('SELECT `Value` FROM `Property` WHERE `Property`=' + "'$property'"))
            try {
                $view.Execute()
                $record = $view.Fetch()
                if ($null -eq $record) { throw 'missing_msi_property' }
                try { $properties[$property] = $record.StringData(1) }
                finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
            } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
        }
        return $properties
    } finally {
        if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    }
}

function Invoke-Pdf24Process {
    param([string]$File, [string]$Arguments)
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}

function Assert-Pdf24ExecutionEnvironment {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'administrator_or_system_required' }
    if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) { throw 'use_64_bit_powershell' }
    if (@(Get-Process -Name 'pdf24*' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -gt 0 }).Count -gt 0) { throw 'pdf24_in_use_no_uninstall_started' }
}

function Invoke-Pdf24Cleanup {
    param([string]$TargetVersion, [string]$MsiPath, [bool]$Execute)
    $minimum = ConvertTo-Pdf24Version $TargetVersion
    if ($null -eq $minimum) { throw 'invalid_minimum_version' }
    $plan = @(Get-Pdf24Plan @(Get-Pdf24Inventory) $minimum)
    $plan | Select-Object @{n='Name';e={$_.Entry.Name}}, @{n='Version';e={$_.Entry.Version}}, Action, Reason | Format-Table -AutoSize | Out-Host
    $old = @($plan | Where-Object Action -eq 'Remove')
    if (-not $Execute) {
        Write-Host 'SIMULATION: no files, registry entries or installations changed.'
        return 0
    }
    if ($old.Count -eq 0) {
        if (@($plan | Where-Object Action -eq 'Blocked').Count -gt 0) { throw 'unrecognized_pdf24_installation' }
        Write-Host 'No older PDF24 installation found. Nothing changed.'
        return 0
    }
    if (@($plan | Where-Object Action -eq 'Blocked').Count -gt 0) { throw 'unrecognized_pdf24_installation' }
    # Removing a shared old install could damage a newer install; never repair it with an older MSI.
    if (@($plan | Where-Object { $_.Action -eq 'Keep' -and $_.Version -gt $minimum }).Count -gt 0) { throw 'newer_version_coexists_manual_review_required' }
    Assert-Pdf24ExecutionEnvironment
    if ([string]::IsNullOrWhiteSpace($MsiPath) -or -not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) { throw 'replacement_msi_not_accessible_no_uninstall_started' }
    # Do not terminate user processes or lose unsaved work. Retry at a later startup.
    $work = New-Pdf24WorkFolder
    $log = Join-Path $work 'cleanup.log'
    function Write-RunLog([string]$Message) {
        $line = [DateTimeOffset]::Now.ToString('o') + ' ' + $Message
        Add-Content -LiteralPath $log -Value $line -Encoding UTF8
        Write-Host $line
    }
    Write-RunLog ('START target=' + $TargetVersion + ' work=' + $work)
    $cached = Join-Path $work 'replacement.msi'
    Copy-Item -LiteralPath $MsiPath -Destination $cached
    if ((Get-FileHash -LiteralPath $MsiPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash) { throw 'msi_copy_hash_mismatch' }
    $msi = Get-Pdf24MsiIdentity $cached
    if ($msi.ProductName -notmatch '^PDF24 Creator(?:\s+.*)?$' -or $msi.Manufacturer -notmatch '^geek software(?: GmbH)?$' -or
        (ConvertTo-Pdf24Version $msi.ProductVersion) -ne $minimum) { throw 'replacement_msi_identity_mismatch_no_uninstall_started' }
    Write-RunLog ('Validated replacement SHA256=' + (Get-FileHash -LiteralPath $cached).Hash)
    # Validate ALL uninstallers before the first removal. No arbitrary registry commands.
    $commands = @()
    $index = 0
    foreach ($item in $old) {
        $index++
        $commands += [pscustomobject]@{ Item = $item; Command = Get-Pdf24UninstallCommand $item.Entry (Join-Path $work ("uninstall-$index.log")) }
    }
    $needsRestart = $false
    $cleanupError = $null
    $attempted = $false
    try {
        foreach ($command in $commands) {
            $fresh = @(Get-Pdf24Plan @(Get-Pdf24Inventory) $minimum)
            if (@($fresh | Where-Object { $_.Version -gt $minimum }).Count -gt 0) { throw 'newer_version_detected_cleanup_cancelled' }
            $stillOld = @($fresh | Where-Object { $_.Action -eq 'Remove' -and $_.Entry.Key -eq $command.Item.Entry.Key -and $_.Entry.View -eq $command.Item.Entry.View })
            if ($stillOld.Count -eq 0) { Write-RunLog 'SKIP entry changed since planning'; continue }
            $attempted = $true
            Write-RunLog ('UNINSTALL version=' + $command.Item.Entry.Version + ' kind=' + $command.Command.Kind)
            $code = Invoke-Pdf24Process $command.Command.File $command.Command.Arguments
            Write-RunLog ('UNINSTALL exit=' + $code)
            if ($code -eq 3010 -and $command.Command.Kind -eq 'MSI') { $needsRestart = $true }
            elseif ($code -ne 0) { throw ('uninstall_failed_exit_' + $code) }
        }
    } catch { $cleanupError = $_; Write-RunLog ('CLEANUP ERROR ' + $_.Exception.Message) }
    # Repair/install even after a partial uninstall failure: restore shared files/printer.
    if ($attempted) {
        $remaining = @(Get-Pdf24Plan @(Get-Pdf24Inventory) $minimum)
        if (@($remaining | Where-Object { $_.Version -gt $minimum }).Count -gt 0) { throw 'newer_version_detected_repair_cancelled' }
        $present = @($remaining | Where-Object { $_.Version -eq $minimum -and $_.Entry.WindowsInstaller -eq 1 -and $_.Entry.Key -eq $msi.ProductCode }).Count -gt 0
        $repair = ''
        if ($present) { $repair = ' REINSTALL=ALL REINSTALLMODE=vomus' }
        $installLog = Join-Path $work 'install-repair.log'
        $arguments = "/i `"$cached`" /qn /norestart REBOOT=ReallySuppress$repair /L*v `"$installLog`""
        $code = Invoke-Pdf24Process (Join-Path $env:SystemRoot 'System32\msiexec.exe') $arguments
        Write-RunLog ('INSTALL/REPAIR exit=' + $code)
        if ($code -eq 3010) { $needsRestart = $true }
        elseif ($code -ne 0) { throw ('replacement_install_failed_exit_' + $code + '_logs_' + $work) }
    }
    if ($null -ne $cleanupError) { throw $cleanupError }
    $after = @(Get-Pdf24Plan @(Get-Pdf24Inventory) $minimum)
    if (@($after | Where-Object Action -ne 'Keep').Count -gt 0 -or @($after | Where-Object { $_.Version -eq $minimum }).Count -eq 0) { throw ('post_verification_failed_logs_' + $work) }
    Write-RunLog ('VERIFIED target installed; restartRequired=' + $needsRestart + '; no restart requested by script')
    if ($needsRestart) { return 3010 }
    return 0
}

# Dot-source exposes functions for synthetic tests without touching this PC.
if ($MyInvocation.InvocationName -eq '.') { return }
$mutex = $null
$locked = $false
$result = 1
try {
    if ($Apply) {
        $mutex = New-Object Threading.Mutex($false, 'Global\DMT-PDF24-Cleanup')
        try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw 'another_pdf24_cleanup_is_running' }
    }
    $result = Invoke-Pdf24Cleanup -TargetVersion $MinimumVersion -MsiPath $InstallerPath -Execute $Apply.IsPresent
} catch { Write-Error -Message $_.Exception.Message -ErrorAction Continue }
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
}
exit $result
