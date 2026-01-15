# WisprClone

A Windows speech-to-text application inspired by WhisprFlow. Press **Ctrl+Ctrl** (double-tap) to start/stop speech recognition, and the transcribed text is automatically copied to your clipboard.

## Features

- **Multiple Speech Providers**: Choose from Windows offline recognition, Azure Speech Service, or OpenAI Whisper
- **Floating Overlay**: Semi-transparent overlay window showing real-time transcription
- **Global Hotkey**: Double-tap Ctrl key to toggle speech recognition from anywhere
- **System Tray**: Runs in the background with dynamic tray icon showing current state
- **Auto-Clipboard**: Automatically copies transcribed text to clipboard when done
- **Recording Safety**: Configurable maximum recording duration to prevent forgotten recordings
- **Optional Logging**: Debug logging to help troubleshoot issues
- **Customizable Settings**: Configure language, hotkey timing, API credentials, and more

## Installation

### Option 1: Installer (Recommended)

1. Download `WisprClone-Setup-1.2.0.exe` from the [Releases](../../releases) page
2. Run the installer and follow the prompts
3. Optionally enable "Start WisprClone when Windows starts"

### Option 2: Build from Source

See [Building from Source](#building-from-source) below.

## Requirements

- Windows 10 or Windows 11 (64-bit)
- Microphone
- For cloud recognition (optional):
  - Azure Speech Service subscription, or
  - OpenAI API key

## Usage

1. **Start the application** - The overlay window appears and the app minimizes to the system tray
2. **Press Ctrl+Ctrl** (double-tap Ctrl) - Start listening for speech
3. **Speak** - Your words appear in real-time in the overlay
4. **Press Ctrl+Ctrl again** - Stop listening and copy text to clipboard
5. **Paste** (Ctrl+V) - Paste your transcribed text anywhere

### System Tray

The tray icon changes color to indicate the current state:
- **Gray**: Idle/Ready
- **Green**: Listening
- **Orange**: Processing
- **Red**: Error

Actions:
- **Double-click** the tray icon to toggle the overlay
- **Right-click** for options:
  - Show/Hide Overlay
  - Settings
  - About
  - Exit

### Settings

Access settings from the system tray right-click menu or the Settings window:

#### Speech Provider
- **Offline (Windows)**: Uses Windows built-in speech recognition - no internet required
- **Azure Speech Service**: Cloud-based recognition with high accuracy
- **OpenAI Whisper**: Uses OpenAI's Whisper model for transcription
- **Hybrid**: Uses offline recognition with Azure fallback

#### Recording Limits
- **Maximum recording duration**: Auto-stop recording after specified seconds (10-600s, default 120s)
- Useful as a safety feature if you forget to stop recording

#### Hotkey Timing
- **Double-tap interval**: Time window for detecting Ctrl double-tap (100-1000ms, default 400ms)
- **Max key hold duration**: Maximum time key can be held for tap detection (50-500ms, default 200ms)

#### Behavior
- **Auto-copy to clipboard**: Automatically copy transcribed text when done
- **Start minimized**: Start in system tray without showing overlay
- **Minimize to tray**: Minimize to tray instead of taskbar

#### Debugging
- **Enable logging**: Write debug logs to `%APPDATA%\WisprClone\logs\`

## Configuration

Settings are stored in: `%APPDATA%\WisprClone\settings.json`

Logs (when enabled) are stored in: `%APPDATA%\WisprClone\logs\wispr_YYYY-MM-DD.log`

### Azure Speech Service Setup

1. Create an [Azure Speech Service resource](https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices)
2. Copy your subscription key and region (e.g., `eastus`, `westeurope`)
3. Open Settings and select "Azure" as the speech provider
4. Enter your subscription key and region

### OpenAI Whisper Setup

1. Get an API key from [OpenAI Platform](https://platform.openai.com/api-keys)
2. Open Settings and select "OpenAI Whisper" as the speech provider
3. Enter your OpenAI API key

## Building from Source

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. (Optional) [Visual Studio 2022](https://visualstudio.microsoft.com/) with WPF workload
3. (Optional) [Inno Setup 6](https://jrsoftware.org/isdl.php) for building the installer

### Build Steps

```bash
# Clone the repository
git clone https://github.com/LorenzoNey/wispr-clone.git
cd wispr-clone

# Restore NuGet packages
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project src/WisprClone/WisprClone.csproj
```

### Build for Release

```bash
# Build self-contained executable
dotnet publish src/WisprClone/WisprClone.csproj -c Release -r win-x64 --self-contained
```

The output will be in `src/WisprClone/bin/Release/net8.0-windows/win-x64/publish/`

### Build Installer

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php) to be installed.

```powershell
# Run the installer build script
.\installer\build-installer.ps1
```

The installer will be created in `installer/output/WisprClone-Setup-X.X.X.exe`

## Project Structure

```
wispr-clone/
├── WisprClone.sln                 # Solution file
├── README.md                      # This file
├── installer/
│   ├── WisprClone.iss            # Inno Setup installer script
│   ├── build-installer.ps1       # Installer build script
│   ├── generate-icons.ps1        # Icon generation script
│   └── output/                   # Built installers
├── src/WisprClone/
│   ├── Core/                     # Core types (states, events, constants)
│   ├── Infrastructure/           # Low-level code (keyboard hooks, system tray)
│   ├── Models/                   # Data models (settings)
│   ├── Services/                 # Business logic services
│   │   ├── Interfaces/           # Service interfaces
│   │   └── Speech/               # Speech recognition implementations
│   │       ├── OfflineSpeechRecognitionService.cs
│   │       ├── AzureSpeechRecognitionService.cs
│   │       ├── OpenAIWhisperSpeechRecognitionService.cs
│   │       └── HybridSpeechRecognitionService.cs
│   ├── ViewModels/               # MVVM view models
│   ├── Views/                    # WPF windows and controls
│   └── Resources/
│       └── Icons/                # Application and tray icons
```

## Troubleshooting

### Speech recognition not working

1. Ensure your microphone is set as the default recording device
2. Check Windows privacy settings allow apps to access microphone
3. For offline recognition, ensure Windows Speech Recognition is enabled
4. Try switching to a cloud provider (Azure/OpenAI) for better accuracy

### Hotkey not responding

1. Try adjusting the double-tap interval in Settings (default: 400ms)
2. Increase the value if double-taps are not being detected
3. Decrease the value if accidental triggers occur
4. Ensure no other application is capturing the Ctrl key globally

### Azure not connecting

1. Verify your subscription key and region are correct
2. Check your internet connection
3. Ensure Azure Speech Service is available in your region
4. Check the logs (enable in Settings > Debugging) for error details

### OpenAI Whisper not working

1. Verify your API key is correct
2. Check your OpenAI account has available credits
3. Ensure your internet connection is stable
4. Check the logs for error details

### Recording stops unexpectedly

1. Check the "Maximum recording duration" setting
2. The default is 120 seconds (2 minutes)
3. Increase this value in Settings if you need longer recordings

## Version History

- **1.2.0** - Added About dialog, version display in settings
- **1.1.0** - Added installer with version checking, icon pack, logging options
- **1.0.0** - Initial release with core functionality

## License

This project is provided as-is for educational purposes.

## Acknowledgments

- Inspired by [Wispr Flow](https://wisprflow.com/)
- Uses [System.Speech](https://www.nuget.org/packages/System.Speech) for offline recognition
- Uses [Azure Cognitive Services Speech SDK](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech) for cloud recognition
- Uses [OpenAI API](https://platform.openai.com/) for Whisper transcription
- Uses [NAudio](https://github.com/naudio/NAudio) for audio capture
- Uses [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM pattern
