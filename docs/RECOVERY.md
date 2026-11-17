# Recover DMT without the original VM

The source repository is the authoritative development handoff. It includes product code, WPF setup, web assets, language packs, branding, design ADRs, deployment helpers, tests and unfinished-work documentation. Read [STATUS.md](STATUS.md) first: the physical-PC installer is currently blocked.

## Obtain the project

Clone `https://github.com/Kraugh/DMT.git`, or download **Code > Download ZIP**. For a fixed checkpoint, use a tag or source archive under [Releases](https://github.com/Kraugh/DMT/releases). GitHub source archives contain no database or .NET runtime. A Git clone also preserves commit history.

```powershell
git clone https://github.com/Kraugh/DMT.git
Set-Location DMT
git rev-parse HEAD
```

Use Windows x64, .NET 10 SDK and PowerShell 7. Python 3 and Node.js are only needed for the development checks. NuGet and official Caddy release downloads require internet access. No company share or Maintenance Toolkit source checkout is needed for a build or synthetic tests.

## Build and verify

```powershell
dotnet build .\DMT.sln -c Release --artifacts-path .\build\verification
dotnet .\build\verification\bin\DMT.Inventory.Tests\release\DMT.Inventory.Tests.dll
python .\tests\validate-language-packs.py
node .\tests\report-text.test.cjs
pwsh -NoProfile -File .\tests\Test-Pdf24Cleanup.ps1
pwsh -NoProfile -File .\tests\Test-CiscoVpnRemoval.ps1
pwsh -NoProfile -File .\scripts\Build-LocalInstaller.ps1 -OutputDirectory .\build\package
dotnet .\build\verification\bin\DMT.Setup.Tests\release\DMT.Setup.Tests.dll .\build\package\payload\service\DMT.Web.exe .\build\package\payload\service\caddy.exe
```

The package output directory must not already exist. Keep the entire package folder, not just `DMT-Setup.exe`. It contains the manager, service, runtime, Caddy, languages, branding, instructions and SHA-256 payload manifest. No separate .NET installation is required by the published programs. Do not treat package verification as a successful installation: see the blocked-pilot procedure in STATUS.md.

GitHub Actions repeats these checks and exposes the complete diagnostic package as an artifact on successful runs. Artifacts expire according to retention settings; tagged source archives and release assets remain the durable checkpoint. A checkpoint release must be marked prerelease while the installer incident remains open.

## Start a fresh development dashboard

```powershell
$inventoryDb = Join-Path $env:LOCALAPPDATA 'DMT\Development\dmt.sqlite'
dotnet run --project .\src\DMT.Web -- "--Dmt:Database=$inventoryDb" --Dmt:Tenant=local --urls=http://127.0.0.1:5081
```

Open `http://127.0.0.1:5081/`. An empty database is created if necessary. This development mode is unauthenticated, restricted to loopback and does not automatically import, archive or perform remote operations.

To import without modifying the source, provide your own accessible source explicitly:

```powershell
$reports = '\\server.example\reports'
dotnet run --project .\src\DMT.Web -- --Dmt:Import=true "--Dmt:Source=$reports" "--Dmt:Database=$inventoryDb" --Dmt:Tenant=local
```

For explicit source archiving, read [SERVICE-MANAGEMENT.md](SERVICE-MANAGEMENT.md) and [ARCHIVE-LIVE.md](ARCHIVE-LIVE.md) before enabling it. Those modes move/delete only according to their verified evidence rules. Ordinary dashboard refresh does not collect new data.

## What is deliberately private

Do not publish real JSON reports, raw payloads, databases/WAL/SHM files, export lists, run journals, screenshots containing inventory, migration archives, runtime/admin configuration, passwords, certificates/private keys or machine-specific launchers. Configure shares and identities on the replacement host. Example paths in public documentation are placeholders.

To preserve inventory independently, make a consistent **SQLite Backup API** copy while the database is open, or stop writers before copying the database together with any required WAL state. Never assume copying only an active `.sqlite` file is consistent. Store verified backups in a separate private location accessible after VM loss; check `PRAGMA integrity_check` and `PRAGMA foreign_key_check`, and rehearse restoration. Do not add a migration database to the public installer ZIP or GitHub release.

If private data is lost, DMT can start with an empty database and acquire future reports. History is recoverable only from separately retained databases/original reports; the current product does not yet offer a complete automatic rebuild/restore workflow.

## Resume unfinished development

Follow STATUS.md, ROADMAP.md and open GitHub issues. Start with the installer stall and durable diagnostics. Distinguish implemented code, passing synthetic tests and real installation acceptance. Preserve WPF architecture and external strings; never diagnose/fix MT by changing its source as part of DMT recovery. No BIOS/driver update or endpoint uninstall is authorized by recovery alone.
