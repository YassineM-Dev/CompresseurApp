# Compresseur/Patch

Modern frosted-glass Windows desktop utility for high-quality video compression and instant TikTok FPS patching (60 / 120 FPS), powered by FFmpeg.

## Features

- **Glassmorphism UI**: Custom frosted-glass Windows Forms interface with dark title bar, drag-and-drop zone, and live progress bar.
- **Video Compression (CRF)**: Motion-interpolated (`minterpolate`) + Lanczos scaling with `libx264` (`veryslow` preset) and selectable CRF (`16`, `18`, `20`, `23`, `26`).
- **60 / 120 FPS Timestamp Patch**: Instant stream-copy `-itsscale` patching without re-encoding.
- **Self-Contained Engine**: Bundles `ffmpeg.exe` internally and auto-extracts it on first run if FFmpeg is not already installed.
- **Discord Rich Presence**: Automatically displays live status (`Idle`, `Compressing`, `Patching`, progress) on Discord.

## Build from Source

Requires **.NET 8 SDK** on Windows:

```powershell
# Build Release
dotnet build CompresseurPatch\CompresseurPatch.csproj -c Release

# Publish standalone single-file EXE (to dist/)
dotnet publish CompresseurPatch\CompresseurPatch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

---
© Developped By Yassine
