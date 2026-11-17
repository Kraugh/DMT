$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\gpo\Remove-OldPdf24.ps1')
$script:assertions = 0
function Assert($Value, $Message) {
    if (-not $Value) { throw $Message }
    $script:assertions++
}
function Entry([string]$Version, [string]$Publisher = 'Geek Software GmbH') {
    [pscustomobject]@{ Key='{01234567-89AB-CDEF-0123-456789ABCDEF}'; View='Registry64'; Name='PDF24 Creator'; Version=$Version; Publisher=$Publisher; WindowsInstaller=1; UninstallString='not executed' }
}
$minimum = ConvertTo-Pdf24Version '11.31.1'
Assert ($minimum -eq (ConvertTo-Pdf24Version '11.31.1.0')) 'three/four component version normalization'
Assert ($null -eq (ConvertTo-Pdf24Version '11.31.1-beta')) 'ambiguous version is blocked'
$plan = @(Get-Pdf24Plan @((Entry '11.9.0'), (Entry '11.30.1'), (Entry '11.31.1'), (Entry '11.32.0')) $minimum)
Assert ($plan[0].Action -eq 'Remove' -and $plan[1].Action -eq 'Remove') 'numeric comparisons remove old versions'
Assert ($plan[2].Action -eq 'Keep' -and $plan[3].Action -eq 'Keep') 'current/newer preserved'
Assert ((@(Get-Pdf24Plan @((Entry '')) $minimum))[0].Action -eq 'Blocked') 'unknown version blocked'
Assert ((@(Get-Pdf24Plan @((Entry '11.0.0' 'Other Publisher')) $minimum))[0].Action -eq 'Blocked') 'unrecognized publisher blocked'
$cmd = Get-Pdf24UninstallCommand (Entry '11.0.0') 'C:\Test Logs\remove.log'
Assert ($cmd.Arguments -match '^/x \{01234567-' -and $cmd.Arguments -match '/qn /norestart REBOOT=ReallySuppress') 'MSI command explicitly suppresses reboot'
Assert ($cmd.Arguments -notmatch 'not executed') 'registry command is never executed'
$invalid = Entry '11.0.0'; $invalid.Key = '{invalid} /i evil.msi'
$rejected = $false
try { Get-Pdf24UninstallCommand $invalid 'C:\logs\test.log' | Out-Null } catch { $rejected = $true }
Assert $rejected 'invalid product code injection rejected'
$exe = Entry '11.0.0'; $exe.WindowsInstaller = 0; $exe.UninstallString = 'cmd.exe /c anything'
$rejected = $false
try { Get-Pdf24UninstallCommand $exe 'C:\logs\test.log' | Out-Null } catch { $rejected = $true }
Assert $rejected 'unsupported EXE path rejected'
$script:fakeUninstaller = Join-Path $env:ProgramFiles 'PDF24\unins123.exe'
$script:signatureStatus = 'Valid'
function Test-Path {
    param([string]$LiteralPath, [string]$PathType = 'Any')
    if ($LiteralPath -eq $script:fakeUninstaller) { return $true }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType
}
function Get-AuthenticodeSignature {
    [pscustomobject]@{ Status=$script:signatureStatus; SignerCertificate=[pscustomobject]@{ Subject='CN=geek software GmbH' } }
}
$exe.UninstallString = '"' + $script:fakeUninstaller + '" /FORCERESTART'
$inno = Get-Pdf24UninstallCommand $exe 'C:\logs\test.log'
Assert ($inno.Kind -eq 'Inno' -and $inno.Arguments -match '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -and $inno.Arguments -notmatch '/FORCERESTART') 'Inno command uses fixed no-restart parameters'
$script:signatureStatus = 'NotSigned'
$rejected=$false
try { Get-Pdf24UninstallCommand $exe 'C:\logs\test.log' | Out-Null } catch { $rejected=$_.Exception.Message -eq 'untrusted_uninstaller_signature' }
Assert $rejected 'unsigned EXE is never executed'
$script:signatureStatus = 'Valid'
# The default workflow must not call a process, stage a package or query COM/MSI.
function Get-Pdf24Inventory { Entry '11.28.2' }
function Invoke-Pdf24Process { throw 'A real process must never be started by simulation' }
function New-Pdf24WorkFolder { throw 'Simulation must not create folders' }
function Get-Pdf24MsiIdentity { throw 'Simulation must not open MSI packages' }
Assert ((Invoke-Pdf24Cleanup -TargetVersion '11.31.1' -Execute $false) -eq 0) 'default simulation has no mutations'
function Get-Pdf24Inventory { Entry '11.31.1'; Entry '11.32.0' }
Assert ((Invoke-Pdf24Cleanup -TargetVersion '11.31.1' -Execute $true) -eq 0) 'already current does not stage/uninstall/repair'
function Get-Pdf24Inventory { Entry '11.28.2'; Entry '11.32.0' }
$rejected = $false
try { Invoke-Pdf24Cleanup -TargetVersion '11.31.1' -Execute $true | Out-Null } catch { $rejected = $_.Exception.Message -eq 'newer_version_coexists_manual_review_required' }
Assert $rejected 'mixed newer installation is not downgraded'
function Get-Pdf24Inventory { Entry '' }
$rejected = $false
try { Invoke-Pdf24Cleanup -TargetVersion '11.31.1' -Execute $true | Out-Null } catch { $rejected = $_.Exception.Message -eq 'unrecognized_pdf24_installation' }
Assert $rejected 'unknown installed version prevents execution'

# Exercise the actual orchestration against synthetic registry state and a dummy
# package. Every native process/COM/elevation boundary is replaced; no installer runs.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('DMT-PDF24-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $package = Join-Path $testRoot 'source.msi'
    [IO.File]::WriteAllText($package, 'SYNTHETIC: not an MSI')
    $script:inventory = @()
    $script:calls = @()
    $script:uninstallExit = 0
    $script:installExit = 0
    $script:badIdentity = $false
    function Get-Pdf24Inventory { $script:inventory }
    function Assert-Pdf24ExecutionEnvironment {}
    function New-Pdf24WorkFolder {
        $folder = Join-Path $testRoot ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $folder | Out-Null
        return $folder
    }
    function Get-Pdf24MsiIdentity {
        $v = '11.31.1'
        if ($script:badIdentity) { $v = '11.0.0' }
        @{ ProductName='PDF24 Creator'; Manufacturer='geek software GmbH'; ProductVersion=$v; ProductCode='{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}' }
    }
    function Invoke-Pdf24Process {
        param($File, $Arguments)
        $script:calls += $Arguments
        if ($Arguments.StartsWith('/x ')) {
            if ($script:uninstallExit -eq 0 -or $script:uninstallExit -eq 3010) {
                $script:inventory = @($script:inventory | Where-Object Version -eq '11.31.1')
            }
            return $script:uninstallExit
        }
        if ($script:installExit -eq 0 -or $script:installExit -eq 3010) {
            $current = Entry '11.31.1'; $current.Key='{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}'
            $script:inventory = @($script:inventory | Where-Object Version -ne '11.31.1') + @($current)
        }
        return $script:installExit
    }
    $script:inventory = @((Entry '11.28.2'))
    Assert ((Invoke-Pdf24Cleanup '11.31.1' $package $true) -eq 0) 'old-only cleanup reinstalls target'
    Assert ($script:calls.Count -eq 2 -and $script:calls[1] -notmatch 'REINSTALL=ALL') 'fresh target uses installation, not repair'
    $current = Entry '11.31.1'; $current.Key='{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}'
    $script:inventory = @((Entry '11.28.2'), $current); $script:calls=@()
    Assert ((Invoke-Pdf24Cleanup '11.31.1' $package $true) -eq 0) 'mixed old/current cleanup succeeds'
    Assert ($script:calls[1] -match 'REINSTALL=ALL REINSTALLMODE=vomus') 'current matching MSI is repaired'
    $script:inventory = @((Entry '11.28.2')); $script:calls=@(); $script:uninstallExit=3010
    Assert ((Invoke-Pdf24Cleanup '11.31.1' $package $true) -eq 3010) 'restart-required preserved without reboot command'
    $script:uninstallExit=1603; $script:inventory=@((Entry '11.28.2')); $script:calls=@()
    $rejected=$false
    try { Invoke-Pdf24Cleanup '11.31.1' $package $true | Out-Null } catch { $rejected=$_.Exception.Message -eq 'uninstall_failed_exit_1603' }
    Assert ($rejected -and $script:calls.Count -eq 2) 'uninstall failure attempts restoration but is not reported as success'
    $script:uninstallExit=0; $script:installExit=1603; $script:inventory=@((Entry '11.28.2')); $script:calls=@()
    $rejected=$false
    try { Invoke-Pdf24Cleanup '11.31.1' $package $true | Out-Null } catch { $rejected=$_.Exception.Message -like 'replacement_install_failed_exit_1603*' }
    Assert $rejected 'replacement failure is explicit'
    $script:installExit=0; $script:badIdentity=$true; $script:inventory=@((Entry '11.28.2')); $script:calls=@()
    $rejected=$false
    try { Invoke-Pdf24Cleanup '11.31.1' $package $true | Out-Null } catch { $rejected=$_.Exception.Message -eq 'replacement_msi_identity_mismatch_no_uninstall_started' }
    Assert ($rejected -and $script:calls.Count -eq 0) 'wrong replacement version prevents all uninstall calls'
    $rejected=$false
    try { Invoke-Pdf24Cleanup '11.31.1' (Join-Path $testRoot 'missing.msi') $true | Out-Null } catch { $rejected=$_.Exception.Message -eq 'replacement_msi_not_accessible_no_uninstall_started' }
    Assert ($rejected -and $script:calls.Count -eq 0) 'missing replacement prevents uninstall'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'DMT-PDF24-test-*') { throw 'unsafe_test_cleanup' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "PASS PDF24 cleanup assertions=$script:assertions"
