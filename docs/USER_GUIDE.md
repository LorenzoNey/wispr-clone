# AITextVoice User Guide

A comprehensive guide to using AITextVoice, your local AI-powered speech-to-text and text-to-speech desktop application.

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Installation](#2-installation)
3. [Getting Started](#3-getting-started)
4. [Speech-to-Text Providers](#4-speech-to-text-providers)
5. [Text-to-Speech Providers](#5-text-to-speech-providers)
6. [Hotkey Configuration](#6-hotkey-configuration)
7. [Settings Reference](#7-settings-reference)
8. [Daily Use Tips](#8-daily-use-tips)
9. [Troubleshooting](#9-troubleshooting)
10. [Appendix](#10-appendix)

---

## 1. Introduction

### What is AITextVoice?

AITextVoice is a cross-platform desktop application that provides:

- **Speech-to-Text (STT)**: Speak into your microphone and have your words transcribed into text, automatically inserted wherever your cursor is
- **Text-to-Speech (TTS)**: Have any text read aloud to you using natural-sounding voices

The application prioritizes **local AI processing** for privacy and speed, with optional cloud providers for additional capabilities.

### Key Features

| Feature | Description |
|---------|-------------|
| Local Processing | Run AI models on your computer - your voice never leaves your device |
| Global Hotkeys | Activate from any application with customizable keyboard shortcuts |
| Floating Overlay | Minimal, always-on-top window shows transcription in real-time |
| Multiple Providers | Choose from local or cloud-based speech engines |
| Cross-Platform | Works on Windows, macOS, and Linux |
| Auto-Insert | Transcribed text automatically appears at your cursor position |

### System Requirements

| Platform | Minimum Requirements |
|----------|---------------------|
| **Windows** | Windows 10 or later, 64-bit |
| **macOS** | macOS 11 (Big Sur) or later, Intel or Apple Silicon |
| **Linux** | Ubuntu 20.04+ or equivalent, X11 display server |

**For local AI processing (recommended):**
- 8 GB RAM minimum (16 GB recommended for larger models)
- GPU with CUDA support (optional, for faster Whisper processing)
- 2-5 GB free disk space (for AI models)

---

## 2. Installation

### Windows

1. Download the installer (`AITextVoice-Setup.exe`) from the [Releases page](https://github.com/your-repo/releases)
2. Run the installer and follow the prompts
3. Launch AITextVoice from the Start menu or desktop shortcut
4. The application will start minimized to the system tray

**Permissions**: No special permissions required. Windows may show a SmartScreen warning on first run - click "More info" then "Run anyway".

### macOS

1. Download the DMG file (`AITextVoice.dmg`) from the [Releases page](https://github.com/your-repo/releases)
2. Open the DMG and drag AITextVoice to your Applications folder
3. **Important**: On first launch, macOS will block the app. Go to **System Preferences > Security & Privacy > General** and click "Open Anyway"
4. **Critical Step - Accessibility Permission**:
   - Go to **System Preferences > Security & Privacy > Privacy > Accessibility**
   - Click the lock icon and enter your password
   - Add AITextVoice to the list and ensure it's checked
   - This permission is required for global hotkeys to work

**Note**: Without Accessibility permission, hotkeys will not function.

### Linux

1. Download the AppImage file (`AITextVoice.AppImage`) from the [Releases page](https://github.com/your-repo/releases)
2. Make it executable:
   ```bash
   chmod +x AITextVoice.AppImage
   ```
3. Run the application:
   ```bash
   ./AITextVoice.AppImage
   ```

**Requirements**:
- X11 display server (Wayland is not fully supported for global hotkeys)
- PulseAudio or PipeWire for audio

---

## 3. Getting Started

### First Launch Experience

When you first launch AITextVoice:

1. A small floating overlay window appears on your screen
2. The application icon appears in your system tray
3. Default settings are pre-configured for immediate use

The overlay shows:
- A colored status indicator (green = ready, blue = listening, yellow = processing)
- Transcription text area
- Provider selectors (STT and TTS)
- Control buttons (copy, play/stop, close)

### Your First Speech-to-Text

1. **Activate**: Press **Ctrl** twice quickly (Ctrl+Ctrl double-tap)
2. **Speak**: The indicator turns blue and shows "Listening..." - speak clearly into your microphone
3. **Stop**: Press the same hotkey again to stop recording
4. **Result**: Your transcribed text appears in the overlay and is automatically:
   - Copied to your clipboard
   - Pasted at your current cursor position (if enabled)

**Tip**: The overlay shows real-time transcription while you speak (when using streaming providers).

### Your First Text-to-Speech

1. **Copy text**: Select any text and copy it to your clipboard (Ctrl+C)
2. **Activate**: Press **Shift** twice quickly (Shift+Shift double-tap)
3. **Listen**: AITextVoice reads the clipboard text aloud
4. **Stop**: Press the same hotkey again to stop playback

The overlay highlights words as they're spoken, so you can follow along.

### Understanding the Overlay Window

```
┌──────────────────────────────────────────────────┐
│ ● Listening...  0:05  │ STT: Ctrl+Ctrl │ ▶ □ ✕  │  ← Header bar (drag to move)
├──────────────────────────────────────────────────┤
│                                                  │
│ Your transcribed text appears here...            │  ← Text area
│                                                  │
├──────────────────────────────────────────────────┤
│ STT: WhisperServer              TTS: Piper       │  ← Provider selectors
└──────────────────────────────────────────────────┘
```

**Header Elements:**
- **Status Indicator** (colored dot): Shows current state
  - Gray: Idle
  - Blue (pulsing): Listening/recording
  - Yellow: Processing
  - Green: Complete
  - Red: Error
- **Status Text**: Current operation ("Ready", "Listening...", "Processing...")
- **Elapsed Time**: Recording duration (shown when listening)
- **Hotkey Hints**: Quick reference (shown when idle)
- **Control Buttons**:
  - ▶ (Play): Start TTS with clipboard text
  - ⏸ (Pause): Pause/resume TTS playback
  - □ (Stop): Stop current TTS playback
  - Copy icon: Copy text to clipboard
  - ✕ (Close): Hide the overlay

**Footer Elements:**
- **STT Provider**: Click to switch speech-to-text engine
- **TTS Provider**: Click to switch text-to-speech engine

---

## 4. Speech-to-Text Providers

AITextVoice supports multiple speech recognition engines. Choose based on your needs:

### Provider Comparison

| Provider | Location | Speed | Accuracy | Cost | Platform |
|----------|----------|-------|----------|------|----------|
| **Whisper Server** | Local | Fast (~1s) | Excellent | Free | All |
| **Faster-Whisper** | Local | Medium | Excellent | Free | Windows |
| **Offline (System)** | Local | Fast | Good | Free | Windows |
| **macOS Native** | Local | Fast | Good | Free | macOS |
| **Azure** | Cloud | Fast | Excellent | Paid | All |
| **OpenAI Whisper** | Cloud | Medium | Excellent | Paid | All |
| **OpenAI Realtime** | Cloud | Real-time | Excellent | Paid | Windows |

### Whisper Server (Recommended)

The Whisper Server provider uses whisper.cpp to run OpenAI's Whisper model locally. It keeps the model loaded in memory for instant transcriptions.

**Setup:**
1. Go to **Settings > STT** tab
2. Select "Whisper Server" as the provider
3. Click **Download** to get whisper.cpp (~460 MB)
4. Choose a model size (see model comparison below)
5. Click **Download Model** to get your selected model

**Model Sizes:**

| Model | Size | Speed | Accuracy | Best For |
|-------|------|-------|----------|----------|
| tiny.en | ~75 MB | Fastest | Good | Quick notes, testing |
| base.en | ~142 MB | Fast | Better | Daily use (recommended) |
| small.en | ~466 MB | Medium | Very Good | Detailed transcription |
| medium.en | ~1.5 GB | Slow | Excellent | High accuracy needs |
| large-v3 | ~3 GB | Slowest | Best | Maximum accuracy |
| large-v3-turbo | ~1.6 GB | Medium | Excellent | Balance of speed/quality |

**Streaming Mode:**
- Enable "Real-time streaming" to see transcription while speaking
- Adjust "Window size" (5-15 seconds) - smaller = faster updates, larger = better context

### Faster-Whisper (Windows)

Uses the faster-whisper-xxl executable for local transcription with GPU acceleration.

**Setup:**
1. Go to **Settings > STT** tab
2. Select "Faster-Whisper" as the provider
3. Click **Download** to get faster-whisper-xxl (~1.5 GB)
4. Configure options:
   - **Model**: tiny through large-v3 (model downloads automatically on first use)
   - **Language**: Auto-detect or specify a language
   - **GPU acceleration**: Enable for CUDA-capable GPUs
   - **GPU Device ID**: Select which GPU to use (0 = first GPU)
   - **Compute Type**: float16 (default), float32, int8, or int8_float16

**Note**: First transcription with a new model may take longer as the model downloads.

### Offline / System Speech (Windows)

Uses Windows built-in speech recognition (System.Speech). No setup required.

**Pros:**
- Works immediately, no downloads needed
- Low resource usage
- Good for basic dictation

**Cons:**
- Lower accuracy than Whisper models
- English-focused (other languages vary in quality)

### macOS Native

Uses Apple's built-in SFSpeechRecognizer. Provides on-device recognition with no API costs.

**Setup:**
1. Grant microphone permission when prompted
2. Grant Speech Recognition permission in System Preferences

### Azure Cognitive Services

Cloud-based recognition with excellent accuracy and language support.

**Setup:**
1. Create an Azure account at [azure.microsoft.com](https://azure.microsoft.com)
2. Create a Speech resource in Azure Portal
3. Copy your subscription key and region
4. In AITextVoice Settings > STT tab:
   - Select "Azure" as provider
   - Enter your subscription key
   - Enter your region (e.g., "eastus", "westeurope")

**Azure Fallback**: Enable "Use Azure as fallback" to automatically switch to Azure when offline recognition fails.

### OpenAI Whisper (Batch API)

Uses OpenAI's Whisper API for cloud transcription.

**Setup:**
1. Get an API key from [platform.openai.com](https://platform.openai.com)
2. In Settings > STT tab:
   - Select "OpenAI Whisper" as provider
   - Enter your API key

**Note**: This provider re-transcribes the entire recording every 2 seconds, so you see updates but with a slight delay.

### OpenAI Realtime (Windows)

True real-time streaming transcription via WebSocket connection.

**Setup:**
1. Get an API key from OpenAI
2. Select "OpenAI Realtime" as provider
3. Enter your API key

**Requirements**: Windows only (uses NAudio for audio capture)

---

## 5. Text-to-Speech Providers

### Provider Comparison

| Provider | Location | Quality | Voices | Cost | Platform |
|----------|----------|---------|--------|------|----------|
| **Piper TTS** | Local | Excellent | Many | Free | All |
| **Offline (System)** | Local | Good | System | Free | Windows/macOS |
| **Azure TTS** | Cloud | Excellent | Many | Paid | All |
| **OpenAI TTS** | Cloud | Excellent | 6 | Paid | All |

### Piper TTS (Recommended)

High-quality neural text-to-speech that runs entirely on your computer.

**Setup:**
1. Go to **Settings > TTS** tab
2. Select "Piper" as the provider
3. Click **Download Piper** (~22 MB)
4. A default voice is included; download more from the voice catalog

**Downloading Additional Voices:**
1. Select a language from the dropdown
2. Click "Refresh" to load available voices
3. Select a voice from the list
4. Click "Download Selected Voice"

Popular voice qualities:
- **low**: Smallest files, lower quality
- **medium**: Balanced quality and size (recommended)
- **high**: Best quality, larger files

### Windows SAPI / macOS Native

Uses your operating system's built-in speech synthesis.

**Windows**: Uses SAPI voices. Additional voices can be installed through Windows Settings > Time & Language > Speech.

**macOS**: Uses AVSpeechSynthesizer. Voices can be managed in System Preferences > Accessibility > Spoken Content.

### Azure TTS

Cloud-based neural text-to-speech with natural-sounding voices.

**Setup:**
1. Configure Azure credentials in the STT tab (shared credentials)
2. Select "Azure" as TTS provider
3. Choose a voice (Jenny, Guy, Aria, Davis, Sonia, Ryan)

### OpenAI TTS

High-quality cloud TTS with 6 distinct voices.

**Setup:**
1. Configure OpenAI API key in the STT tab (shared credentials)
2. Select "OpenAI" as TTS provider
3. Choose voice and model:
   - **Voices**: Alloy, Echo, Fable, Onyx, Nova, Shimmer
   - **Models**: TTS-1 (faster) or TTS-1-HD (higher quality)

---

## 6. Hotkey Configuration

### Activation Types

AITextVoice supports four types of hotkey activation:

| Type | Description | Example | Best For |
|------|-------------|---------|----------|
| **DoubleTap** | Press a key twice quickly | Ctrl+Ctrl | Avoiding conflicts (default) |
| **SinglePress** | Press a key once | F9 | Quick activation |
| **Hold** | Hold a key for a duration | Hold Ctrl 1s | Intentional activation |
| **Combination** | Modifier + key | Ctrl+Space | Traditional shortcuts |

### Default Hotkeys

| Function | Default Hotkey | Description |
|----------|----------------|-------------|
| Speech-to-Text | Ctrl + Ctrl (double-tap) | Start/stop voice recording |
| Text-to-Speech | Shift + Shift (double-tap) | Read clipboard text aloud |

### Changing Hotkeys

1. Go to **Settings > Advanced** tab
2. Find "Hotkey Configuration"
3. Select your preferred hotkey from the dropdown for STT and TTS
4. Available presets include:
   - Double-tap: Ctrl, Shift, Alt
   - Combinations: Ctrl+Space
   - Function keys: F9, F10, F13-F15
   - Hold: Ctrl or Shift for 1 second

**Warning**: The settings will warn you if STT and TTS use the same hotkey.

### Fine-Tuning Double-Tap Timing

For double-tap hotkeys, you can adjust the timing:

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Tap interval | 400 ms | 200-600 ms | Maximum time between the two taps |
| Max hold | 200 ms | 100-400 ms | Maximum time to hold each tap (longer = not a tap) |

**Tip**: If you find yourself accidentally triggering the hotkey, increase the tap interval or decrease max hold time.

### Recommendations for Avoiding Conflicts

1. **Double-tap hotkeys are safest** - they rarely conflict with other applications
2. **F13-F24 keys** are ideal for single-press hotkeys (almost never used by other software)
3. **Avoid common shortcuts** like F1 (help), F5 (refresh), F11 (fullscreen)
4. **Test in your commonly used apps** to ensure no conflicts

---

## 7. Settings Reference

Access settings by right-clicking the system tray icon and selecting "Settings", or clicking the gear icon in the main window.

### General Tab

| Setting | Default | Description |
|---------|---------|-------------|
| **Recognition Language** | English (US) | Primary language for speech recognition |
| **Copy transcription to clipboard** | Off | Automatically copy completed text |
| **Insert transcription at cursor** | On | Automatically paste text where cursor is |
| **Start with overlay hidden** | On | Start minimized to tray |
| **Run on Windows startup** | On | Launch automatically when Windows starts |
| **Check for updates automatically** | On | Notify when new versions are available |

### STT Tab

| Setting | Description |
|---------|-------------|
| **Select STT Provider** | Choose speech recognition engine |
| **Azure Subscription Key** | API key for Azure Speech Services |
| **Azure Region** | Azure datacenter region (e.g., "eastus") |
| **Use Azure as fallback** | Fall back to Azure when local recognition fails |
| **OpenAI API Key** | API key for OpenAI Whisper |
| **Faster-Whisper Model** | Model size (tiny through large-v3) |
| **Faster-Whisper Language** | Recognition language or auto-detect |
| **Use GPU acceleration** | Enable CUDA for faster processing |
| **GPU Device ID** | Which GPU to use (0 = first) |
| **Compute Type** | float16, float32, int8, or int8_float16 |
| **Whisper Server Model** | GGML model for whisper.cpp |
| **Server Port** | Port for whisper.cpp server (default: 8178) |
| **Enable real-time streaming** | Show text while speaking |
| **Window size** | Streaming transcription window (5-15 seconds) |

### TTS Tab

| Setting | Default | Description |
|---------|---------|-------------|
| **Select TTS Provider** | Offline | Choose text-to-speech engine |
| **Speech Rate** | 1.0x | Speed of speech (0.5x to 2.0x) |
| **Volume** | 100% | TTS output volume |
| **OpenAI TTS Voice** | Alloy | OpenAI voice selection |
| **OpenAI TTS Model** | TTS-1 | Standard or HD quality |
| **Azure TTS Voice** | Jenny (US) | Azure voice selection |
| **Piper Voice** | Amy (US) | Installed Piper voice |

### Advanced Tab

| Setting | Default | Description |
|---------|---------|-------------|
| **Speech-to-Text Hotkey** | Ctrl+Ctrl | Hotkey for STT activation |
| **Text-to-Speech Hotkey** | Shift+Shift | Hotkey for TTS activation |
| **Tap interval** | 400 ms | Double-tap timing |
| **Max hold** | 200 ms | Maximum tap duration |
| **Max recording duration** | 120 seconds | Auto-stop recording after this time |
| **Enable logging** | Off | Save debug logs to AppData |
| **Restore Defaults** | - | Reset all settings to defaults |

### About Tab

- View current version
- Check for updates
- Download and install updates

---

## 8. Daily Use Tips

### Workflow Suggestions

**For Writers and Content Creators:**
1. Use Whisper Server with `base.en` or `small.en` model for good accuracy
2. Enable "Insert transcription at cursor" to type directly into your document
3. Keep the overlay visible while drafting to review as you speak

**For Email and Messages:**
1. Position cursor in the message field
2. Press Ctrl+Ctrl, dictate your message
3. Press Ctrl+Ctrl again to stop and auto-insert

**For Proofreading:**
1. Copy the text you want to review
2. Press Shift+Shift to have it read back
3. Errors often become obvious when heard aloud

**For Language Learning:**
1. Use TTS to hear correct pronunciation
2. Use STT to practice your speaking

### Best Practices

1. **Speak clearly** but naturally - don't over-enunciate
2. **Pause briefly** between sentences for better punctuation
3. **Use a quality microphone** for best results
4. **Position the microphone** 6-12 inches from your mouth
5. **Minimize background noise** when possible
6. **Edit after dictating** - it's faster than being perfect while speaking

### System Tray Usage

Right-click the tray icon for quick access to:
- Show/hide overlay
- Open settings
- Check for updates
- Quit application

The tray icon changes color to indicate status:
- Normal: Application ready
- Active: Currently listening or speaking

---

## 9. Troubleshooting

### Speech Recognition Not Working

**Symptom**: Hotkey activates but no transcription appears

**Solutions**:
1. Check microphone permissions:
   - Windows: Settings > Privacy > Microphone
   - macOS: System Preferences > Security & Privacy > Privacy > Microphone
2. Verify microphone is working in system settings
3. Try a different speech provider
4. Check if the selected provider's service is running (for Whisper Server)

### Hotkey Not Responding

**Symptom**: Pressing the hotkey does nothing

**Solutions**:
1. **macOS**: Ensure Accessibility permission is granted (System Preferences > Security & Privacy > Privacy > Accessibility)
2. Check for hotkey conflicts with other applications
3. Try a different hotkey configuration
4. Restart the application
5. Verify the overlay isn't hidden behind other windows

### macOS Permission Issues

**Symptom**: App launches but hotkeys don't work

**Solution**:
1. Go to System Preferences > Security & Privacy > Privacy > Accessibility
2. Find AITextVoice in the list
3. If it's already there, remove it and re-add it
4. Ensure the checkbox is checked
5. Restart AITextVoice

### Poor Transcription Quality

**Possible Causes and Solutions**:

| Issue | Solution |
|-------|----------|
| Background noise | Use a headset microphone or reduce ambient noise |
| Microphone too far | Position 6-12 inches from mouth |
| Speaking too fast | Slow down and articulate |
| Wrong language | Check Recognition Language setting |
| Low-quality model | Upgrade to a larger Whisper model |
| Microphone gain too low | Increase input volume in system settings |

### TTS Not Playing

**Symptom**: Shift+Shift activates but no audio

**Solutions**:
1. Check system volume isn't muted
2. Verify TTS volume in Settings > TTS tab
3. Ensure clipboard contains text (not images/files)
4. Try a different TTS provider
5. For Piper: verify piper.exe is downloaded and voice is installed

### Whisper Server Issues

**Symptom**: Whisper Server fails to start or transcribe

**Solutions**:
1. Ensure whisper.cpp is downloaded (Settings > STT > Download)
2. Verify model is downloaded
3. Check if port 8178 is available (or change port in settings)
4. Try restarting the application
5. Check logs if logging is enabled

### Debug Logging

To enable diagnostic logging:
1. Go to Settings > Advanced
2. Enable "Enable logging"
3. Logs are saved to:
   - Windows: `%AppData%\AITextVoice\logs`
   - macOS: `~/Library/Application Support/AITextVoice/logs`
   - Linux: `~/.config/AITextVoice/logs`

Share log files when reporting issues.

---

## 10. Appendix

### Keyboard Shortcut Reference

| Action | Default Shortcut | Configurable |
|--------|------------------|--------------|
| Start/Stop STT | Ctrl + Ctrl | Yes |
| Start/Stop TTS | Shift + Shift | Yes |
| Copy Text | Click copy button | No |
| Hide Overlay | Click × button | No |
| Stop TTS | Shift + Shift (while playing) | Yes |
| Pause TTS | Click pause button | No |

### File Locations

| File | Windows | macOS | Linux |
|------|---------|-------|-------|
| Settings | `%AppData%\AITextVoice\settings.json` | `~/Library/Application Support/AITextVoice/settings.json` | `~/.config/AITextVoice/settings.json` |
| Logs | `%AppData%\AITextVoice\logs` | `~/Library/Application Support/AITextVoice/logs` | `~/.config/AITextVoice/logs` |
| Whisper Models | `%AppData%\AITextVoice\models` | `~/Library/Application Support/AITextVoice/models` | `~/.config/AITextVoice/models` |
| Piper | `%AppData%\AITextVoice\piper` | `~/Library/Application Support/AITextVoice/piper` | `~/.config/AITextVoice/piper` |
| Piper Voices | `%AppData%\AITextVoice\piper\voices` | `~/Library/Application Support/AITextVoice/piper/voices` | `~/.config/AITextVoice/piper/voices` |

### Whisper Model Reference

| Model | Parameters | Size | Relative Speed |
|-------|------------|------|----------------|
| tiny | 39 M | ~75 MB | 32x |
| base | 74 M | ~142 MB | 16x |
| small | 244 M | ~466 MB | 6x |
| medium | 769 M | ~1.5 GB | 2x |
| large-v3 | 1550 M | ~3 GB | 1x |
| large-v3-turbo | 809 M | ~1.6 GB | 4x |

**Note**: Speed is relative - actual performance depends on your hardware. GPU acceleration significantly improves speed.

### Supported Languages

AITextVoice supports these languages for speech recognition:

| Language | Code | Whisper | Azure | System |
|----------|------|---------|-------|--------|
| English (US) | en-US | ✓ | ✓ | ✓ |
| English (UK) | en-GB | ✓ | ✓ | ✓ |
| Spanish | es-ES | ✓ | ✓ | ✓ |
| French | fr-FR | ✓ | ✓ | ✓ |
| German | de-DE | ✓ | ✓ | ✓ |
| Italian | it-IT | ✓ | ✓ | ✓ |
| Portuguese | pt-BR | ✓ | ✓ | ✓ |
| Chinese | zh-CN | ✓ | ✓ | Varies |
| Japanese | ja-JP | ✓ | ✓ | Varies |
| Romanian | ro-RO | ✓ | ✓ | Limited |

Whisper models support 90+ languages. Use language-specific models (e.g., `base.en`) for best English performance.

### Glossary

| Term | Definition |
|------|------------|
| **STT** | Speech-to-Text - converting spoken words to written text |
| **TTS** | Text-to-Speech - converting written text to spoken audio |
| **Whisper** | OpenAI's open-source speech recognition model |
| **GGML** | A file format for machine learning models (used by whisper.cpp) |
| **Piper** | An open-source neural text-to-speech engine |
| **CUDA** | NVIDIA's GPU computing platform for accelerated AI processing |
| **VAD** | Voice Activity Detection - automatically detecting when someone is speaking |
| **Diarization** | Identifying and separating different speakers in audio |
| **Streaming** | Real-time processing that shows results as you speak |
| **Batch** | Processing that transcribes audio after recording stops |

---

## Getting Help

- **GitHub Issues**: Report bugs or request features at [github.com/your-repo/issues](https://github.com/your-repo/issues)
- **Documentation**: This guide and README are available in the repository

---

*AITextVoice - Your voice, your words, your privacy.*
