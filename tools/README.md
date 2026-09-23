# tools

Standalone helpers that are not part of the shipped app. Each tool's dependencies and run command
are in the header block of its entry file; for .NET tools, the `.csproj` is the dependency manifest,
and the PowerShell tool needs nothing beyond Windows' built-in PowerShell and .NET.

| Path | Function |
|---|---|
| `icongen/Program.cs` | Draws the app icon and writes `TranscriptBuilder/Assets/app.ico` (16–256px, PNG frames). Output is deterministic: regenerating produces a byte-identical file. |
| `screenshot/capture.ps1` | Launches the built app and saves a PNG of exactly its visible window frame (the README screenshots, and on-screen layout checks). |

```
dotnet run --project tools/icongen -- TranscriptBuilder/Assets/app.ico
pwsh tools/screenshot/capture.ps1 -OutputPath .github/screenshot-light.png
```
