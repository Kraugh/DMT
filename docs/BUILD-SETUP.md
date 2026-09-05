# Building DMT Setup

The first executable slice is `src/DMT.Setup`, a WPF application targeting `net10.0-windows`.

## Requirements

- Windows 10/11
- .NET 10 SDK

## Development build

From the repository root:

```powershell
dotnet build .\DMT.sln
```

Run directly:

```powershell
dotnet run --project .\src\DMT.Setup\DMT.Setup.csproj
```

## Self-contained Windows publish

```powershell
dotnet publish .\src\DMT.Setup\DMT.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The project copies `languages/*.json` and the branding assets beside the published executable. Language packs deliberately remain external so adding a normal language does not require recompiling the application.

## Before a release

Validate language-key parity:

```powershell
python .\tests\validate-language-packs.py
```

All shipped packs, including hidden `tlh.json`, must contain the complete `en-US` key set.

The current Klingon translations are a development draft and require linguistic review before public distribution.
