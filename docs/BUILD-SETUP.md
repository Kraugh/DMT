# Building DMT Setup

Requires Windows x64, .NET 10 SDK and PowerShell 7. Python 3 and Node.js are development/test prerequisites only.

```powershell
dotnet build .\DMT.sln
pwsh -NoProfile -File .\scripts\Build-LocalInstaller.ps1
```

The script publishes the WPF manager and web host with runtimes, downloads official Caddy and verifies its checksum, then creates a per-file SHA-256 manifest. The final printed path is the complete package. A standalone WPF publish does not contain the service payload. Languages and branding remain external. No separate .NET installation is required by the published programs.

See [recovery and all checks](RECOVERY.md). The physical-PC installer is blocked; [STATUS.md](STATUS.md) records the stall and acceptance still required. Checkpoint binaries are diagnostic previews, not a stable release.
