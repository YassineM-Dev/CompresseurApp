<div align="center">

# Compresseur/Patch

**Frosted-Glass Windows Desktop Utility for High-Quality Video Compression & Instant 60 / 120 FPS TikTok Patching**

[![Latest Release](https://img.shields.io/github/v/release/YassineM-Dev/CompresseurApp?label=Version&color=FF2E52&style=flat-square)](https://github.com/YassineM-Dev/CompresseurApp/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows_x64-1F2937?style=flat-square&logo=windows&logoColor=white)](#download)
[![Framework](https://img.shields.io/badge/.NET-8.0_WinForms-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#tech-stack)
[![Engine](https://img.shields.io/badge/Engine-Bundled_FFmpeg-007808?style=flat-square&logo=ffmpeg&logoColor=white)](#features)

*Drop a video — compress it with motion-interpolated x264 CRF encoding, or patch its timestamps to 60 / 120 FPS for TikTok in seconds.*

</div>

---

## Download

Grab the standalone Windows executable — **no FFmpeg or .NET installation required**:

<p align="center">
  <a href="https://github.com/YassineM-Dev/CompresseurApp/releases/latest/download/Compresseur-Patch.exe">
    <img src="https://img.shields.io/badge/Download-Compresseur--Patch.exe_(Windows_x64)-FF2E52?style=for-the-badge&logo=windows&logoColor=white" alt="Download Compresseur-Patch.exe" />
  </a>
</p>

| Release | Direct Link | Notes |
| :--- | :--- | :--- |
| **Windows Portable (x64)** | [**Compresseur-Patch.exe**](https://github.com/YassineM-Dev/CompresseurApp/releases/latest/download/Compresseur-Patch.exe) | Self-contained single-file EXE with bundled FFmpeg |
| **All Releases** | [**GitHub Releases**](https://github.com/YassineM-Dev/CompresseurApp/releases/latest) | Full version history |

> **Windows SmartScreen Note:** On first launch, if Windows displays *"Windows protected your PC"*, click **More info** → **Run anyway**.

---

## About the Project

**Compresseur/Patch** is a self-contained Windows desktop application built with **C# / .NET 8 (Windows Forms)** and powered by **FFmpeg**. Designed for video editors and content creators uploading to TikTok, it combines a custom frosted-glass dark interface with two core workflows: high-efficiency `libx264` CRF compression with motion interpolation, and instant lossless 60 / 120 FPS timestamp patching via stream copy.

---

## Features

- **High-Quality Video Compression (`Compress (CRF)`)**
  - Encodes with `libx264` (`veryslow` preset, `yuv420p`, `-movflags +faststart`) paired with `minterpolate` (`mci` / `aobmc` / `bidir` / `vsbmc`) and **Lanczos** scaling.
  - Selectable CRF quality levels: `16`, `18` *(default)*, `20`, `23`, and `26`.
  - Copies the original audio stream losslessly, automatically falling back to **AAC 192k stereo** if the source audio codec is incompatible with MP4.
  - Outputs `<filename>_Compressed.mp4` and reports exact file-size savings in the console log.
- **Instant 60 / 120 FPS TikTok Patch (`60 fps Patch` & `120 fps Patch`)**
  - Uses FFmpeg `-itsscale` timestamp scaling with pure stream copy (`-c copy`) — **zero re-encoding** and near-instant processing.
  - Automatically computes the exact scale factor from the probed source framerate (`sourceFps / targetFps`) and previews playback speed and output filename (`_60fps.mp4` / `_120fps.mp4`).
- **Self-Contained FFmpeg Engine**
  - Embeds `ffmpeg.gz` directly inside the executable and auto-extracts `ffmpeg.exe` to `%LOCALAPPDATA%\CompresseurPatch\bin\` on first run if FFmpeg is not already available beside the app, in `PATH`, or via WinGet.
  - Automatically probes resolution, framerate, duration, and file size (with `ffmpeg -i` fallback when `ffprobe` is unavailable).
- **Custom Frosted-Glass UI**
  - Dark Windows title bar with rounded corners (`DwmSetWindowAttribute`), ambient gradient canvas, and custom owner-drawn glass cards, buttons, radio buttons, combo box, and smooth progress bar.
  - Supports **drag-and-drop**, click-to-browse, and passing a video file path via command-line argument (or dragging a file onto the EXE).
  - Live console output log, clean job cancellation (automatically deletes partial outputs), and one-click **Open folder** highlighting the exported file in Explorer.
- **Zero-Dependency Discord Rich Presence**
  - Native Windows Named Pipe IPC client (`discord-ipc-0..9`) that displays live status on Discord (`Video Loaded`, `Compressing Video (CRF XX)`, `Patching Video to 60/120 FPS`, real-time percentage, and completion).

---

## Supported Input Formats

`.mp4` · `.m4v` · `.mov` · `.mkv` · `.avi` · `.webm` · `.flv` · `.wmv` · `.mpg` · `.mpeg` · `.ts` · `.mts` · `.m2ts` · `.3gp` · `.ogv` · `.gif`

---

## Tech Stack

- **Language & Runtime:** C#, .NET 8 (`net8.0-windows`, `PerMonitorV2` High DPI)
- **UI:** Custom owner-drawn Windows Forms (`GDI+` frosted-glass controls & DWM dark title bar)
- **Video Engine:** Embedded FFmpeg (`libx264`, `minterpolate`, `lanczos`, `-itsscale` stream copy)
- **Integrations:** Native zero-dependency Discord Rich Presence over Named Pipes (`System.IO.Pipes`)

---

## Project Structure

```text
CompresseurApp/
├── CompresseurPatch/
│   ├── Assets/
│   │   ├── app.ico                  # Application & window icon
│   │   └── ffmpeg.gz                # Embedded gzipped FFmpeg binary
│   ├── CompresseurPatch.csproj      # .NET 8 WinForms project config
│   ├── app.manifest                 # Windows manifest
│   ├── Program.cs                   # Entry point & CLI file argument handling
│   ├── MainForm.cs                  # Frosted-glass UI layout & painting
│   ├── MainForm.Workflow.cs         # Drag-and-drop, probing, compress/patch workflow
│   ├── Ffmpeg.cs                    # FFmpeg extraction, probing, argument builder & runner
│   ├── DiscordRpc.cs                # Zero-dependency Discord IPC Rich Presence client
│   ├── GlassControls.cs             # Custom glassmorphism controls & DWM theming
│   └── FlatProgressBar.cs           # Custom progress bar control
├── README.md
└── .gitignore
```

---

## Build from Source

Requires **.NET 8 SDK** on Windows:

```powershell
# Build Release
dotnet build CompresseurPatch\CompresseurPatch.csproj -c Release

# Publish standalone single-file EXE (to dist/)
dotnet publish CompresseurPatch\CompresseurPatch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

---

<div align="center">
  <sub>© 2026 Developed by <a href="https://github.com/YassineM-Dev">Yassine</a></sub>
</div>
