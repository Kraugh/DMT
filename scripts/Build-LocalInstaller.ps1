#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory, [string]$CaddyVersion='2.11.9')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repo ('build\local-installer-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output directory must not exist'}
if($CaddyVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid Caddy version'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$cache=Join-Path $repo 'build\caddy-downloads';New-Item -ItemType Directory -Path $cache -Force|Out-Null
$archiveName="caddy_${CaddyVersion}_windows_amd64.zip"
$checksums=Invoke-WebRequest "https://github.com/caddyserver/caddy/releases/download/v$CaddyVersion/caddy_${CaddyVersion}_checksums.txt"
$checkText=if($checksums.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($checksums.Content)}else{[string]$checksums.Content}
$line=($checkText -split "`n"|Where-Object {$_ -match ([regex]::Escape($archiveName)+'\s*$')})
if(@($line).Count -ne 1){throw 'Caddy checksum not found'}
$expected=($line.Trim() -split '\s+')[0]
$archive=Join-Path $cache $archiveName
if(-not(Test-Path -LiteralPath $archive)){Invoke-WebRequest "https://github.com/caddyserver/caddy/releases/download/v$CaddyVersion/$archiveName" -OutFile $archive}
$algorithm=switch($expected.Length){64{'SHA256'}128{'SHA512'}default{throw 'Unrecognized Caddy checksum format'}}
if((Get-FileHash -LiteralPath $archive -Algorithm $algorithm).Hash -ne $expected){throw 'Caddy archive checksum mismatch'}
dotnet publish (Join-Path $repo 'src\DMT.Setup') -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $OutputDirectory --artifacts-path (Join-Path $repo 'build\installer-publish-artifacts')
if($LASTEXITCODE -ne 0){throw 'Setup publish failed'}
$service=Join-Path $OutputDirectory 'payload\service';New-Item -ItemType Directory -Path $service -Force|Out-Null
dotnet publish (Join-Path $repo 'src\DMT.Web') -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $service --artifacts-path (Join-Path $repo 'build\installer-publish-artifacts')
if($LASTEXITCODE -ne 0){throw 'Web publish failed'}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $service 'caddy-release')
Copy-Item -LiteralPath (Join-Path $service 'caddy-release\caddy.exe') -Destination (Join-Path $service 'caddy.exe')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $OutputDirectory 'LICENSE-DMT.txt')
Copy-Item -LiteralPath (Join-Path $repo 'docs\INSTALL-LOCAL.it.md') -Destination (Join-Path $OutputDirectory 'LEGGIMI.md')
Copy-Item -LiteralPath (Join-Path $repo 'docs\STATUS.md') -Destination (Join-Path $OutputDirectory 'STATUS.md')
$entries=foreach($file in Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse){
 if($file.Extension -in '.sqlite','.sqlite3','.db'){throw 'Database must not be packaged with public binaries'}
 $relative=[IO.Path]::GetRelativePath($OutputDirectory,$file.FullName)
 $destination=if($relative.StartsWith('payload\service\')){'Service\'+$relative.Substring('payload\service\'.Length)}else{'Manager\'+$relative}
 [pscustomobject]@{Source=$relative;Destination=$destination;Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
$entries|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $OutputDirectory 'payload-manifest.json') -Encoding utf8
Write-Output $OutputDirectory
