# WisprClone

A **cross-platform** speech-to-text application inspired by Wispr Flow. Press **Ctrl+Ctrl** (double-tap) to start/stop speech recognition, and the transcribed text is automatically copied to your clipboard.

[![Latest Release](https://img.shields.io/github/v/release/blockchainadvisors/wispr-clone?label=Latest%20Version&style=for-the-badge)](https://github.com/blockchainadvisors/wispr-clone/releases/latest)
[![CI](https://github.com/blockchainadvisors/wispr-clone/actions/workflows/ci.yml/badge.svg)](https://github.com/blockchainadvisors/wispr-clone/actions/workflows/ci.yml)
[![Release](https://github.com/blockchainadvisors/wispr-clone/actions/workflows/release.yml/badge.svg)](https://github.com/blockchainadvisors/wispr-clone/actions/workflows/release.yml)
[![Downloads](https://img.shields.io/github/downloads/blockchainadvisors/wispr-clone/total?label=Downloads)](https://github.com/blockchainadvisors/wispr-clone/releases)

## Features

- **Cross-Platform**: Runs on Windows, macOS, and Linux
- **Multiple Speech Providers**: Choose from Windows offline recognition (Windows only), Azure Speech Service, or OpenAI Whisper
- **Floating Overlay**: Semi-transparent overlay window showing real-time transcription
- **Global Hotkey**: Double-tap Ctrl key to toggle speech recognition from anywhere
- **System Tray**: Runs in the background with dynamic tray icon showing current state
- **Auto-Clipboard**: Automatically copies transcribed text to clipboard when done
- **Recording Safety**: Configurable maximum recording duration to prevent forgotten recordings
- **Optional Logging**: Debug logging to help troubleshoot issues
- **Customizable Settings**: Configure language, hotkey timing, API credentials, and more

## Downloads

> **[Download Latest Release](https://github.com/blockchainadvisors/wispr-clone/releases/latest)**

| Platform | Architecture | Download |
|----------|--------------|----------|
| Windows | x64 | [WisprClone-Windows-x64.zip](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |
| Windows | ARM64 | [WisprClone-Windows-arm64.zip](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |
| macOS | Intel | [WisprClone-macOS-x64.tar.gz](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |
| macOS | Apple Silicon | [WisprClone-macOS-arm64.tar.gz](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |
| Linux | x64 | [WisprClone-Linux-x64.tar.gz](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |
| Linux | ARM64 | [WisprClone-Linux-arm64.tar.gz](https://github.com/blockchainadvisors/wispr-clone/releases/latest) |

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

| Provider | Windows | macOS | Linux |
|----------|---------|-------|-------|
| Offline (System.Speech) | Yes | No | No |
| Azure Speech Service | Yes | Yes | Yes |
| OpenAI Whisper | Yes | Yes | Yes |
| Hybrid (Offline + Azure) | Yes | No | No |

## Usage

1. **Start the application** - The overlay window appears and the app minimizes to the system tray
2. **Press Ctrl+Ctrl** (double-tap Ctrl) - Start listening for speech
3. **Speak** - Your words appear in real-time in the overlay
4. **Press Ctrl+Ctrl again** - Stop listening and copy text to clipboard
5. **Paste** (Ctrl+V / Cmd+V) - Paste your transcribed text anywhere

### System Tray

The tray icon changes color to indicate the current state:
- **Gray**: Idle/Ready
- **Green**: Listening
- **Orange**: Processing
- **Red**: Error

Actions:
- **Click** the tray icon to toggle the overlay
- **Right-click** for options: Show/Hide Overlay, Settings, Exit

## Settings

### Speech Provider
- **Offline (Windows only)**: Uses Windows built-in speech recognition - no internet required
- **Azure Speech Service**: Cloud-based recognition with high accuracy
- **OpenAI Whisper**: Uses OpenAI's Whisper model for transcription

### Cloud Service Setup

#### Azure Speech Service
1. Create an [Azure Speech Service resource](https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices)
2. Copy your subscription key and region (e.g., `eastus`, `westeurope`)
3. Open Settings and select "Azure" as the speech provider
4. Enter your subscription key and region

#### OpenAI Whisper
1. Get an API key from [OpenAI Platform](https://platform.openai.com/api-keys)
2. Open Settings and select "OpenAI Whisper" as the speech provider
3. Enter your OpenAI API key

## Building from Source

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. (Optional) [Visual Studio 2022](https://visualstudio.microsoft.com/) or [JetBrains Rider](https://www.jetbrains.com/rider/)

### Build Commands

```bash
# Clone the repository
git clone https://github.com/blockchainadvisors/wispr-clone.git
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
├── WisprClone.sln              # Solution file
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

## Version History

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
