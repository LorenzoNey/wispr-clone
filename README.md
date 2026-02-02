# AITextVoice

A **cross-platform** AI-powered speech-to-text and text-to-speech application. Press **Ctrl+Ctrl** (double-tap) for speech-to-text or **Shift+Shift** for text-to-speech.

[![Release](https://github.com/LorenzoNey/aitextvoice/actions/workflows/release.yml/badge.svg)](https://github.com/LorenzoNey/aitextvoice/actions/workflows/release.yml)
[![Latest Version](https://img.shields.io/github/v/release/LorenzoNey/aitextvoice)](https://github.com/LorenzoNey/aitextvoice/releases/latest)

## Features

- **Cross-Platform**: Runs on Windows, macOS, and Linux
- **Speech-to-Text (STT)**: Local AI-powered transcription using Whisper, plus Windows offline recognition
- **Text-to-Speech (TTS)**: Read clipboard text aloud with local Piper voices or system voices
- **Floating Overlay**: Semi-transparent overlay window showing real-time transcription with auto-scroll
- **Global Hotkeys**: Customizable hotkeys for STT (Ctrl+Ctrl) and TTS (Shift+Shift)
- **System Tray**: Runs in the background with dynamic tray icon showing current state
- **Auto-Clipboard**: Automatically copies transcribed text to clipboard when done
- **Insert at Cursor**: Optionally paste transcription directly into the active application
- **Elapsed Time Display**: Shows recording duration in the overlay while transcribing
- **Recording Safety**: Configurable maximum recording duration to prevent forgotten recordings
- **Update Notifications**: Red dot indicator on tray icon when updates are available
- **Optional Logging**: Debug logging to help troubleshoot issues (toggleable without restart)
- **Tabbed Settings**: Clean, organized settings interface with General, STT, TTS, and Advanced tabs

## Downloads

> **[Download Latest Release](https://github.com/LorenzoNey/aitextvoice/releases/latest)**

| Platform | Architecture | Download |
|----------|--------------|----------|
| Windows | x64 | [AITextVoice-Setup.exe](https://github.com/LorenzoNey/aitextvoice/releases/latest) |
| macOS | Apple Silicon | [AITextVoice-macOS-arm64.dmg](https://github.com/LorenzoNey/aitextvoice/releases/latest) |
| Linux | x64 | [AITextVoice-Linux-x64.AppImage](https://github.com/LorenzoNey/aitextvoice/releases/latest) |

## Installation

### Windows

1. Download the latest Windows release from the [Downloads](#downloads) section above
2. Run the installer `AITextVoice-Setup.exe`
3. Follow the installation prompts

### macOS

1. Download the latest macOS release (Apple Silicon or Intel) from the [Downloads](#downloads) section
2. Open the DMG file and drag AITextVoice to Applications
3. On first launch, grant required permissions

**Important**: On first launch, macOS will require you to grant:
- **Accessibility** permission: System Preferences > Security & Privacy > Privacy > Accessibility
- **Microphone** permission: Granted automatically on first use

### Linux

1. Download the latest Linux release from the [Downloads](#downloads) section
2. Make it executable: `chmod +x AITextVoice-*.AppImage`
3. Run: `./AITextVoice-*.AppImage`

**Note**: Global keyboard hooks require X11. Wayland support is limited.

## Requirements

### All Platforms
- Microphone (for STT)
- No API keys required - uses local AI models

### Platform-Specific
- **Windows**: Windows 10/11 (x64 or ARM64)
- **macOS**: macOS 10.15+ (Catalina or later)
- **Linux**: X11 desktop environment (GNOME, KDE, etc.)

### Speech Provider Availability

| Provider | Windows | macOS | Linux | Streaming |
|----------|---------|-------|-------|-----------|
| Whisper Server (Local) | Yes | Yes | Yes | Yes |
| Faster-Whisper (Local) | Yes | No | No | Yes |
| Offline (System.Speech) | Yes | No | No | Yes |

### TTS Provider Availability

| Provider | Windows | macOS | Linux |
|----------|---------|-------|-------|
| Piper (Local) | Yes | Yes | Yes |
| Windows SAPI | Yes | No | No |
| macOS Native | No | Yes | No |

## Usage

### Speech-to-Text
1. **Start the application** - The overlay window appears and the app minimizes to the system tray
2. **Press Ctrl+Ctrl** (double-tap Ctrl) - Start listening for speech
3. **Speak** - Your words appear in real-time in the overlay
4. **Press Ctrl+Ctrl again** - Stop listening and copy text to clipboard

### Text-to-Speech
1. **Copy text** to your clipboard
2. **Press Shift+Shift** (double-tap Shift) - Start reading the clipboard text aloud
3. **Press Shift+Shift again** - Stop reading

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

Settings are organized into tabs: **General**, **STT**, **TTS**, and **Advanced**.

Access settings via the system tray icon → Settings, or click the gear icon in the overlay.

## Building from Source

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. (Optional) [Visual Studio 2022](https://visualstudio.microsoft.com/) or [JetBrains Rider](https://www.jetbrains.com/rider/)

### Build Commands

```bash
# Clone the repository
git clone https://github.com/LorenzoNey/aitextvoice.git
cd aitextvoice

# Build the project
dotnet build

# Run the application
dotnet run --project src/AITextVoice.Avalonia/AITextVoice.Avalonia.csproj
```

### Publish for Release

```bash
# Windows x64
dotnet publish src/AITextVoice.Avalonia/AITextVoice.Avalonia.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# macOS Apple Silicon
dotnet publish src/AITextVoice.Avalonia/AITextVoice.Avalonia.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# macOS Intel
dotnet publish src/AITextVoice.Avalonia/AITextVoice.Avalonia.csproj -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

# Linux x64
dotnet publish src/AITextVoice.Avalonia/AITextVoice.Avalonia.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## Creating a Release

Releases are automatically created by GitHub Actions when you push a version tag.

### Steps to Create a New Release

```bash
# Make sure you're on the main branch with all changes committed
git checkout main
git pull

# Create and push a version tag
git tag v2.4.0
git push origin v2.4.0
```

The GitHub Actions workflow will automatically:
1. Build the application for all platforms (Windows, macOS, Linux - both x64 and ARM64)
2. Create platform-specific installers
3. Create a GitHub Release with all artifacts attached
4. Generate release notes from commit history

## Project Structure

```
aitextvoice/
├── .github/
│   └── workflows/
│       ├── ci.yml              # CI build on push/PR
│       └── release.yml         # Release workflow on tag push
├── README.md                   # This file
├── installer/                  # Platform-specific installers
├── src/
│   └── AITextVoice.Avalonia/   # Cross-platform Avalonia version
│       ├── Core/               # Core types (states, events, constants)
│       ├── Infrastructure/     # Keyboard hooks (SharpHook)
│       ├── Models/             # Data models (settings)
│       ├── Services/           # Business logic services
│       │   ├── Interfaces/     # Service interfaces
│       │   ├── Speech/         # STT implementations
│       │   └── Tts/            # TTS implementations
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
4. Add AITextVoice to the list and enable it

### Debug Logging

If you're experiencing issues, enable debug logging in Settings to help diagnose problems:

1. Open **Settings** from the system tray menu
2. Go to the **Advanced** tab
3. Check **Enable Debug Logging**
4. Click **Save**
5. Reproduce the issue
6. Find log files at:
   - **Windows**: `%AppData%\AITextVoice\logs\aitextvoice_YYYY-MM-DD.log`
   - **macOS/Linux**: `~/.config/AITextVoice/logs/aitextvoice_YYYY-MM-DD.log`

## Tech Stack

- **UI Framework**: [Avalonia UI](https://avaloniaui.net/) (cross-platform)
- **Keyboard Hooks**: [SharpHook](https://github.com/TolikPyl662/SharpHook) (cross-platform)
- **Speech Recognition**:
  - [System.Speech](https://www.nuget.org/packages/System.Speech) (Windows offline)
  - [Azure Cognitive Services Speech SDK](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech)
  - [OpenAI API](https://platform.openai.com/)
  - [Faster-Whisper](https://github.com/SYSTRAN/faster-whisper) (local)
- **Text-to-Speech**:
  - Windows SAPI
  - Azure Cognitive Services
  - OpenAI TTS
  - [Piper](https://github.com/rhasspy/piper) (local)
- **Audio**: [NAudio](https://github.com/naudio/NAudio) (Windows)
- **MVVM**: [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)

## License

This project is provided as-is for educational purposes.
