import Foundation
import AVFoundation

/// JSON message types for communication with the .NET app
struct Message: Codable {
    let type: String
    let error: String?
    let state: String?
    let sampleRate: Int?
    let channels: Int?
    let bitsPerSample: Int?
}

/// Simple JSON encoder/decoder
let encoder = JSONEncoder()
let decoder = JSONDecoder()

/// Send a message to stderr (so it doesn't mix with audio data on stdout)
func sendMessage(_ message: Message) {
    if let data = try? encoder.encode(message),
       let json = String(data: data, encoding: .utf8) {
        FileHandle.standardError.write((json + "\n").data(using: .utf8)!)
    }
}

/// Send an error message
func sendError(_ error: String) {
    sendMessage(Message(type: "error", error: error, state: nil, sampleRate: nil, channels: nil, bitsPerSample: nil))
}

/// Send a state change message
func sendState(_ state: String) {
    sendMessage(Message(type: "state", error: nil, state: state, sampleRate: nil, channels: nil, bitsPerSample: nil))
}

/// Send ready message with audio format info
func sendReady(sampleRate: Int, channels: Int, bitsPerSample: Int) {
    sendMessage(Message(type: "ready", error: nil, state: "idle", sampleRate: sampleRate, channels: channels, bitsPerSample: bitsPerSample))
}

/// Audio capture manager - captures raw PCM audio and outputs to stdout
class AudioCaptureManager {
    private let audioEngine = AVAudioEngine()
    private var isCapturing = false

    // Audio format: 16kHz, 16-bit, mono (matches Whisper's expected input)
    let targetSampleRate: Double = 16000
    let targetChannels: AVAudioChannelCount = 1
    let targetBitsPerSample = 16

    private var converter: AVAudioConverter?
    private var outputFormat: AVAudioFormat?

    init() {
        // Create output format (what we want to send to Whisper)
        outputFormat = AVAudioFormat(commonFormat: .pcmFormatInt16,
                                      sampleRate: targetSampleRate,
                                      channels: targetChannels,
                                      interleaved: true)
    }

    func startCapture() {
        guard !isCapturing else { return }

        do {
            let inputNode = audioEngine.inputNode
            let inputFormat = inputNode.outputFormat(forBus: 0)

            // Create converter from input format to our target format
            guard let outputFormat = outputFormat,
                  let converter = AVAudioConverter(from: inputFormat, to: outputFormat) else {
                sendError("Failed to create audio converter")
                return
            }
            self.converter = converter

            // Remove any existing tap
            inputNode.removeTap(onBus: 0)

            // Install tap to capture audio
            inputNode.installTap(onBus: 0, bufferSize: 4096, format: inputFormat) { [weak self] buffer, time in
                self?.processAudioBuffer(buffer)
            }

            // Start the audio engine
            audioEngine.prepare()
            try audioEngine.start()

            isCapturing = true
            sendState("capturing")

        } catch {
            sendError("Failed to start audio capture: \(error.localizedDescription)")
        }
    }

    private func processAudioBuffer(_ buffer: AVAudioPCMBuffer) {
        guard let converter = converter,
              let outputFormat = outputFormat else { return }

        // Calculate output buffer size
        let ratio = outputFormat.sampleRate / buffer.format.sampleRate
        let outputFrameCapacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio)

        guard let outputBuffer = AVAudioPCMBuffer(pcmFormat: outputFormat, frameCapacity: outputFrameCapacity) else {
            return
        }

        var error: NSError?
        let inputBlock: AVAudioConverterInputBlock = { inNumPackets, outStatus in
            outStatus.pointee = .haveData
            return buffer
        }

        converter.convert(to: outputBuffer, error: &error, withInputFrom: inputBlock)

        if let error = error {
            // Don't spam errors for conversion issues
            return
        }

        // Write raw PCM data to stdout
        if let int16Data = outputBuffer.int16ChannelData {
            let frameLength = Int(outputBuffer.frameLength)
            let data = Data(bytes: int16Data[0], count: frameLength * 2) // 2 bytes per sample
            FileHandle.standardOutput.write(data)
        }
    }

    func stopCapture() {
        guard isCapturing else { return }

        audioEngine.stop()
        audioEngine.inputNode.removeTap(onBus: 0)

        isCapturing = false
        sendState("idle")
    }
}

/// Main application loop
class Application {
    private var manager: AudioCaptureManager?
    private var isRunning = true

    func run() {
        // Initialize audio capture manager
        manager = AudioCaptureManager()

        guard let manager = manager else {
            sendError("Failed to initialize audio capture manager")
            return
        }

        // Send ready message with audio format info
        sendReady(sampleRate: Int(manager.targetSampleRate),
                  channels: Int(manager.targetChannels),
                  bitsPerSample: manager.targetBitsPerSample)

        // Process commands from stdin
        processCommands()

        // Keep the run loop alive
        while isRunning {
            RunLoop.current.run(mode: .default, before: Date(timeIntervalSinceNow: 0.1))
        }
    }

    private func processCommands() {
        // Read commands from stdin in a background thread
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            while let self = self, self.isRunning {
                guard let line = readLine() else {
                    self.isRunning = false
                    break
                }

                DispatchQueue.main.async {
                    self.handleCommand(line)
                }
            }
        }
    }

    private func handleCommand(_ line: String) {
        let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)

        // Try to parse as JSON command
        if let data = trimmed.data(using: .utf8),
           let command = try? decoder.decode([String: String].self, from: data) {

            switch command["action"] {
            case "start":
                manager?.startCapture()

            case "stop":
                manager?.stopCapture()

            case "quit":
                manager?.stopCapture()
                isRunning = false

            default:
                sendError("Unknown action: \(command["action"] ?? "nil")")
            }
        } else {
            // Simple text commands as fallback
            switch trimmed.lowercased() {
            case "start":
                manager?.startCapture()
            case "stop":
                manager?.stopCapture()
            case "quit", "exit":
                manager?.stopCapture()
                isRunning = false
            default:
                sendError("Unknown command: \(trimmed)")
            }
        }
    }
}

// Entry point
let app = Application()
app.run()
