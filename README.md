# WisprClone

A **cross-platform** speech-to-text application inspired by Wispr Flow. Press **Ctrl+Ctrl** (double-tap) to start/stop speech recognition, and the transcribed text is automatically copied to your clipboard.

[![Release](https://github.com/LorenzoNey/wispr-clone/actions/workflows/release.yml/badge.svg)](https://github.com/LorenzoNey/wispr-clone/actions/workflows/release.yml)
[![Latest Version](https://img.shields.io/github/v/release/LorenzoNey/wispr-clone)](https://github.com/LorenzoNey/wispr-clone/releases/latest)

## Features

- **Cross-Platform**: Runs on Windows, macOS, and Linux
- **Multiple Speech Providers**: Choose from Windows offline recognition (Windows only), Azure Speech Service, or OpenAI Whisper
- **Floating Overlay**: Semi-transparent overlay window showing real-time transcription with auto-scroll
- **Global Hotkey**: Double-tap Ctrl key to toggle speech recognition from anywhere
- **System Tray**: Runs in the background with dynamic tray icon showing current state
- **Auto-Clipboard**: Automatically copies transcribed text to clipboard when done
- **Insert at Cursor**: Optionally paste transcription directly into the active application
- **Elapsed Time Display**: Shows recording duration in the overlay while transcribing
- **Recording Safety**: Configurable maximum recording duration to prevent forgotten recordings
- **Update Notifications**: Red dot indicator on tray icon when updates are available
- **Optional Logging**: Debug logging to help troubleshoot issues (toggleable without restart)
- **Tabbed Settings**: Clean, organized settings interface with General, Advanced, and About tabs

## Downloads

> **[Download Latest Release](https://github.com/LorenzoNey/wispr-clone/releases/latest)**

| Platform | Architecture | Download |
|----------|--------------|----------|
| Windows | x64 | [WisprClone-Setup.exe](https://github.com/LorenzoNey/wispr-clone/releases/latest) |
| macOS | Apple Silicon | [WisprClone-macOS-arm64.dmg](https://github.com/LorenzoNey/wispr-clone/releases/latest) |
| Linux | x64 | [WisprClone-Linux-x64.AppImage](https://github.com/LorenzoNey/wispr-clone/releases/latest) |

## Installation

### Windows

1. Download the latest Windows release from the [Downloads](#downloads) section above
2. Extract the ZIP archive
3. Run `WisprClone.exe`

### macOS

1. Download the latest macOS release (Apple Silicon or Intel) from the [Downloads](#downloads) section
2. Extract: `tar -xzf WisprClone-macOS-*.tar.gz`
3. Run: `./WisprClone-macOS-*/WisprClone`

**Important**: On first launch, macOS will require you to grant:
- **Accessibility** permission: System Preferences > Security & Privacy > Privacy > Accessibility
- **Microphone** permission: Granted automatically on first use

### Linux

1. Download the latest Linux release from the [Downloads](#downloads) section
2. Extract: `tar -xzf WisprClone-Linux-*.tar.gz`
3. Run: `./WisprClone-Linux-*/WisprClone`

**Note**: Global keyboard hooks require X11. Wayland support is limited.

## Requirements

### All Platforms
- Microphone
- For cloud recognition:
  - Azure Speech Service subscription, or
  - OpenAI API key

### Platform-Specific
- **Windows**: Windows 10/11 (x64 or ARM64)
- **macOS**: macOS 10.15+ (Catalina or later)
- **Linux**: X11 desktop environment (GNOME, KDE, etc.)

### Speech Provider Availability

| Provider | Windows | macOS | Linux | Streaming |
|----------|---------|-------|-------|-----------|
| Offline (System.Speech) | Yes | No | No | Yes |
| Azure Speech Service | Yes | Yes | Yes | Yes |
| OpenAI Whisper (Batch) | Yes | Yes | Yes | No* |
| Hybrid (Offline + Azure) | Yes | No | No | Yes |

\* Whisper re-transcribes entire audio every 2 seconds (pseudo-streaming)

### Cost Comparison

| Provider | Cost | Notes |
|----------|------|-------|
| **Offline (Windows)** | Free | Uses built-in Windows speech recognition |
| **Azure Speech Service** | ~$0.017/min | Pay-as-you-go, first 5 hours/month free |
| **OpenAI Whisper** | ~$0.006/min | Most cost-effective cloud option |
| **OpenAI Realtime** | ~$0.06/min | 10x more expensive than Whisper (not implemented) |

> **Why no OpenAI Realtime?** We initially implemented OpenAI's Realtime API for true streaming transcription, but removed it due to cost concerns (~$0.06/min vs Whisper's ~$0.006/min) and inconsistent transcription quality. The Whisper batch API with 2-second re-transcription intervals provides better value and more reliable results for most use cases.

## Usage

1. **Start the application** - The overlay window appears and the app minimizes to the system tray
2. **Press Ctrl+Ctrl** (double-tap Ctrl) - Start listening for speech
3. **Speak** - Your words appear in real-time in the overlay (with elapsed time shown)
4. **Press Ctrl+Ctrl again** - Stop listening and copy text to clipboard
5. **Done!** - Text is copied to clipboard, or automatically inserted at cursor if enabled

> **Tip:** Enable "Insert transcription at cursor" in Settings to have text automatically pasted into your active application when you stop recording.

### System Tray

The tray icon changes color to indicate the current state:
- **Gray**: Idle/Ready
- **Gray + Red dot**: Update available
- **Green**: Listening
- **Orange**: Processing
- **Red**: Error

Actions:
- **Click** the tray icon to toggle the overlay
- **Right-click** for options: Show/Hide Overlay, Settings, Update (when available), Exit

## Settings

Settings are organized into three tabs: **General**, **Advanced**, and **About**.

### Speech Provider (General Tab)
- **Offline (Windows only)**: Uses Windows built-in speech recognition - no internet required
- **Azure Speech Service**: Cloud-based recognition with real-time streaming
- **OpenAI Whisper (Batch)**: Re-transcribes entire audio every 2 seconds for pseudo-streaming

### Cloud Service Setup

#### Azure Speech Service
1. Create an [Azure Speech Service resource](https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices)
2. Copy your subscription key and region (e.g., `eastus`, `westeurope`)
3. Open Settings and select "Azure" as the speech provider
4. Enter your subscription key and region

#### OpenAI Whisper

1. Get an API key from [OpenAI Platform](https://platform.openai.com/api-keys)
2. Open Settings and enter your OpenAI API key
3. Select **OpenAI Whisper** as the speech provider

Whisper provides excellent transcription quality at a low cost (~$0.006/min). It re-transcribes the entire audio every 2 seconds for pseudo-streaming updates.

## Building from Source

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. (Optional) [Visual Studio 2022](https://visualstudio.microsoft.com/) or [JetBrains Rider](https://www.jetbrains.com/rider/)

### Build Commands

```bash
# Clone the repository
git clone https://github.com/LorenzoNey/wispr-clone.git
cd wispr-clone

# Build the project
dotnet build

# Run the application
dotnet run --project src/WisprClone.Avalonia/WisprClone.Avalonia.csproj
```

### Publish for Release

```bash
# Windows x64
dotnet publish src/WisprClone.Avalonia/WisprClone.Avalonia.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# macOS Apple Silicon
dotnet publish src/WisprClone.Avalonia/WisprClone.Avalonia.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# macOS Intel
dotnet publish src/WisprClone.Avalonia/WisprClone.Avalonia.csproj -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

# Linux x64
dotnet publish src/WisprClone.Avalonia/WisprClone.Avalonia.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Creating a Release

Releases are automatically created by GitHub Actions when you push a version tag.

### Steps to Create a New Release

```bash
# Make sure you're on the main branch with all changes committed
git checkout main
git pull

# Create and push a version tag
git tag v2.0.0
git push origin v2.0.0
```

The GitHub Actions workflow will automatically:
1. Build the application for all platforms (Windows, macOS, Linux - both x64 and ARM64)
2. Create platform-specific archives
3. Create a GitHub Release with all artifacts attached
4. Generate release notes from commit history

### Version Tag Format

- **Release**: `v1.0.0`, `v2.1.0`
- **Pre-release**: `v1.0.0-beta.1`, `v2.0.0-alpha.1`, `v1.0.0-rc.1`

Pre-release tags will create pre-release GitHub releases.

## Project Structure

```
wispr-clone/
├── .github/
│   └── workflows/
│       ├── ci.yml              # CI build on push/PR
│       └── release.yml         # Release workflow on tag push
├── README.md                   # This file
├── installer/                  # Windows installer (Inno Setup)
├── src/
│   └── WisprClone.Avalonia/    # Cross-platform Avalonia version
│       ├── Core/               # Core types (states, events, constants)
│       ├── Infrastructure/     # Keyboard hooks (SharpHook)
│       ├── Models/             # Data models (settings)
│       ├── Services/           # Business logic services
│       │   ├── Interfaces/     # Service interfaces
│       │   └── Speech/         # Speech recognition implementations
│       ├── ViewModels/         # MVVM view models
│       ├── Views/              # Avalonia windows
│       └── Resources/          # Icons, styles
```

## Troubleshooting

### Speech recognition not working

1. Ensure your microphone is set as the default recording device
2. Check system privacy settings allow apps to access microphone
3. For offline recognition (Windows), ensure Windows Speech Recognition is enabled
4. Try switching to a cloud provider (Azure/OpenAI) for better accuracy

### Hotkey not responding

1. Try adjusting the double-tap interval in Settings (default: 400ms)
2. Increase the value if double-taps are not being detected
3. Decrease the value if accidental triggers occur
4. **macOS**: Ensure Accessibility permission is granted
5. **Linux**: Ensure you're running on X11 (not Wayland)

### macOS Permissions

If the global hotkey doesn't work:
1. Open System Preferences > Security & Privacy > Privacy
2. Select "Accessibility" from the sidebar
3. Click the lock to make changes
4. Add WisprClone to the list and enable it

### Linux Global Keyboard

Global keyboard hooks require X11. If using Wayland:
- Try running with `XDG_SESSION_TYPE=x11`
- Or use XWayland compatibility mode

### Debug Logging

If you're experiencing issues, enable debug logging in Settings to help diagnose problems:

1. Open **Settings** from the system tray menu
2. Go to the **Advanced** tab
3. Check **Enable Debug Logging**
4. Click **Save**
5. Reproduce the issue
6. Find log files at:
   - **Windows**: `%AppData%\WisprClone\logs\wispr_YYYY-MM-DD.log`
   - **macOS/Linux**: `~/.config/WisprClone/logs/wispr_YYYY-MM-DD.log`

Logging takes effect immediately - no restart required.

## Version History

- **2.4.x** - Insert at cursor, elapsed time display, tabbed settings, centralized logging
- **2.3.x** - Auto-scroll overlay, update notifications with red dot indicator, UX improvements
- **2.0.0** - Cross-platform support (Windows, macOS, Linux) using Avalonia UI
- **1.2.0** - Added About dialog, version display in settings
- **1.1.0** - Added installer with version checking, icon pack, logging options
- **1.0.0** - Initial release with core functionality

## Tech Stack

- **UI Framework**: [Avalonia UI](https://avaloniaui.net/) (cross-platform)
- **Keyboard Hooks**: [SharpHook](https://github.com/TolikPyl662/SharpHook) (cross-platform)
- **Speech Recognition**:
  - [System.Speech](https://www.nuget.org/packages/System.Speech) (Windows offline)
  - [Azure Cognitive Services Speech SDK](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech)
  - [OpenAI API](https://platform.openai.com/)
- **Audio**: [NAudio](https://github.com/naudio/NAudio) (Windows)
- **MVVM**: [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)

## License

This project is provided as-is for educational purposes.

## Acknowledgments

- Inspired by [Wispr Flow](https://wisprflow.com/)
