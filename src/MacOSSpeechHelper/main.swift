import Foundation
import Speech
import AVFoundation

/// JSON message types for communication with the .NET app
struct Message: Codable {
    let type: String
    let text: String?
    let isFinal: Bool?
    let error: String?
    let state: String?
}

/// Simple JSON encoder/decoder
let encoder = JSONEncoder()
let decoder = JSONDecoder()

/// Send a message to stdout
func send(_ message: Message) {
    if let data = try? encoder.encode(message),
       let json = String(data: data, encoding: .utf8) {
        print(json)
        fflush(stdout)
    }
}

/// Send an error message
func sendError(_ error: String) {
    send(Message(type: "error", text: nil, isFinal: nil, error: error, state: nil))
}

/// Send a state change message
func sendState(_ state: String) {
    send(Message(type: "state", text: nil, isFinal: nil, error: nil, state: state))
}

/// Send partial transcription
func sendPartial(_ text: String) {
    send(Message(type: "partial", text: text, isFinal: false, error: nil, state: nil))
}

/// Send final transcription
func sendFinal(_ text: String) {
    send(Message(type: "final", text: text, isFinal: true, error: nil, state: nil))
}

/// Speech recognition manager
class SpeechRecognitionManager {
    private let speechRecognizer: SFSpeechRecognizer
    private let audioEngine = AVAudioEngine()
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private var currentTranscription = ""
    private var accumulatedTranscription = ""  // Text from completed segments
    private var isListening = false
    private var isRestarting = false  // Prevent restart loops

    init?(locale: Locale) {
        guard let recognizer = SFSpeechRecognizer(locale: locale) else {
            return nil
        }
        self.speechRecognizer = recognizer
    }

    var isAvailable: Bool {
        return speechRecognizer.isAvailable
    }

    func requestAuthorization(completion: @escaping (Bool) -> Void) {
        SFSpeechRecognizer.requestAuthorization { status in
            DispatchQueue.main.async {
                completion(status == .authorized)
            }
        }
    }

    func startListening() {
        guard !isListening else { return }
        guard speechRecognizer.isAvailable else {
            sendError("Speech recognizer is not available")
            return
        }

        do {
            accumulatedTranscription = ""  // Clear accumulated text on fresh start
            try startRecognition()
            isListening = true
            sendState("listening")
        } catch {
            sendError("Failed to start recognition: \(error.localizedDescription)")
        }
    }

    private func startRecognition() throws {
        // Cancel any previous task
        recognitionTask?.cancel()
        recognitionTask = nil

        // Note: AVAudioSession is iOS-only. On macOS, AVAudioEngine handles audio directly.

        // Create recognition request
        recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
        guard let recognitionRequest = recognitionRequest else {
            throw NSError(domain: "SpeechHelper", code: 1, userInfo: [NSLocalizedDescriptionKey: "Unable to create recognition request"])
        }

        recognitionRequest.shouldReportPartialResults = true

        // Use on-device recognition if available (iOS 13+/macOS 10.15+)
        if #available(macOS 10.15, *) {
            recognitionRequest.requiresOnDeviceRecognition = speechRecognizer.supportsOnDeviceRecognition
        }

        currentTranscription = ""

        // Start recognition task
        recognitionTask = speechRecognizer.recognitionTask(with: recognitionRequest) { [weak self] result, error in
            guard let self = self else { return }

            if let result = result {
                let transcription = result.bestTranscription.formattedString
                self.currentTranscription = transcription

                // Combine accumulated text with current segment
                let fullText = self.accumulatedTranscription.isEmpty
                    ? transcription
                    : self.accumulatedTranscription + " " + transcription

                if result.isFinal {
                    // Segment completed (possibly due to silence)
                    // Save this segment to accumulated text
                    if !transcription.isEmpty {
                        if self.accumulatedTranscription.isEmpty {
                            self.accumulatedTranscription = transcription
                        } else {
                            self.accumulatedTranscription += " " + transcription
                        }
                    }

                    // Send the full accumulated text as partial (not final, since we're still listening)
                    if self.isListening && !self.isRestarting {
                        sendPartial(self.accumulatedTranscription)

                        // Restart recognition to continue listening
                        self.restartRecognition()
                    } else {
                        sendFinal(fullText)
                    }
                } else {
                    sendPartial(fullText)
                }
            }

            if let error = error {
                // Check if this is just an end-of-speech error (expected when stopping)
                let nsError = error as NSError
                if nsError.domain == "kAFAssistantErrorDomain" && nsError.code == 1110 {
                    // This is "No speech detected" - not an error, just silence
                    // Restart recognition if we're still supposed to be listening
                    if self.isListening && !self.isRestarting {
                        self.restartRecognition()
                    }
                    return
                }
                // Code 216 is also a common "end of utterance" error
                if nsError.domain == "kAFAssistantErrorDomain" && nsError.code == 216 {
                    if self.isListening && !self.isRestarting {
                        self.restartRecognition()
                    }
                    return
                }
                sendError("Recognition error: \(error.localizedDescription)")
            }
        }

        // Configure audio input (only if not already configured)
        let inputNode = audioEngine.inputNode

        // Remove existing tap if any
        inputNode.removeTap(onBus: 0)

        let recordingFormat = inputNode.outputFormat(forBus: 0)

        inputNode.installTap(onBus: 0, bufferSize: 1024, format: recordingFormat) { [weak self] buffer, _ in
            self?.recognitionRequest?.append(buffer)
        }

        if !audioEngine.isRunning {
            audioEngine.prepare()
            try audioEngine.start()
        }
    }

    private func restartRecognition() {
        guard isListening && !isRestarting else { return }

        isRestarting = true

        // End current request
        recognitionRequest?.endAudio()
        recognitionRequest = nil
        recognitionTask?.cancel()
        recognitionTask = nil

        // Small delay before restarting
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in
            guard let self = self, self.isListening else {
                self?.isRestarting = false
                return
            }

            do {
                // Don't clear accumulated - we want to keep it
                self.currentTranscription = ""

                // Create new recognition request
                self.recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
                guard let recognitionRequest = self.recognitionRequest else {
                    self.isRestarting = false
                    return
                }

                recognitionRequest.shouldReportPartialResults = true
                if #available(macOS 10.15, *) {
                    recognitionRequest.requiresOnDeviceRecognition = self.speechRecognizer.supportsOnDeviceRecognition
                }

                // Start new recognition task
                self.recognitionTask = self.speechRecognizer.recognitionTask(with: recognitionRequest) { [weak self] result, error in
                    guard let self = self else { return }

                    if let result = result {
                        let transcription = result.bestTranscription.formattedString
                        self.currentTranscription = transcription

                        let fullText = self.accumulatedTranscription.isEmpty
                            ? transcription
                            : self.accumulatedTranscription + " " + transcription

                        if result.isFinal {
                            if !transcription.isEmpty {
                                if self.accumulatedTranscription.isEmpty {
                                    self.accumulatedTranscription = transcription
                                } else {
                                    self.accumulatedTranscription += " " + transcription
                                }
                            }

                            if self.isListening && !self.isRestarting {
                                sendPartial(self.accumulatedTranscription)
                                self.restartRecognition()
                            } else {
                                sendFinal(fullText)
                            }
                        } else {
                            sendPartial(fullText)
                        }
                    }

                    if let error = error {
                        let nsError = error as NSError
                        if nsError.domain == "kAFAssistantErrorDomain" && (nsError.code == 1110 || nsError.code == 216) {
                            if self.isListening && !self.isRestarting {
                                self.restartRecognition()
                            }
                            return
                        }
                        sendError("Recognition error: \(error.localizedDescription)")
                    }
                }

                self.isRestarting = false
            } catch {
                sendError("Failed to restart recognition: \(error.localizedDescription)")
                self.isRestarting = false
            }
        }
    }

    func stopListening() -> String {
        guard isListening else {
            // Return whatever we have accumulated
            let result = accumulatedTranscription.isEmpty ? currentTranscription : accumulatedTranscription
            return result
        }

        isListening = false
        isRestarting = false  // Ensure we don't restart

        // Stop audio engine
        audioEngine.stop()
        audioEngine.inputNode.removeTap(onBus: 0)

        // End recognition request
        recognitionRequest?.endAudio()
        recognitionRequest = nil

        // Cancel task
        recognitionTask?.cancel()
        recognitionTask = nil

        sendState("idle")

        // Return full accumulated text plus any current segment
        let finalText: String
        if accumulatedTranscription.isEmpty {
            finalText = currentTranscription
        } else if currentTranscription.isEmpty {
            finalText = accumulatedTranscription
        } else {
            finalText = accumulatedTranscription + " " + currentTranscription
        }

        // Reset for next session
        accumulatedTranscription = ""
        currentTranscription = ""

        return finalText
    }
}

/// Main application loop
class Application {
    private var manager: SpeechRecognitionManager?
    private var currentLocale: Locale = Locale(identifier: "en-US")
    private var isRunning = true

    func run() {
        // Initialize with default locale
        manager = SpeechRecognitionManager(locale: currentLocale)

        guard manager != nil else {
            sendError("Failed to initialize speech recognizer for locale: \(currentLocale.identifier)")
            return
        }

        // Request authorization
        manager?.requestAuthorization { [weak self] authorized in
            guard let self = self else { return }

            if authorized {
                send(Message(type: "ready", text: nil, isFinal: nil, error: nil, state: "idle"))
                self.processCommands()
            } else {
                sendError("Speech recognition authorization denied")
                self.isRunning = false
            }
        }

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
                manager?.startListening()

            case "stop":
                let finalText = manager?.stopListening() ?? ""
                sendFinal(finalText)

            case "setLanguage":
                if let locale = command["locale"] {
                    setLanguage(locale)
                }

            case "quit":
                _ = manager?.stopListening()
                isRunning = false

            default:
                sendError("Unknown action: \(command["action"] ?? "nil")")
            }
        } else {
            // Simple text commands as fallback
            switch trimmed.lowercased() {
            case "start":
                manager?.startListening()
            case "stop":
                let finalText = manager?.stopListening() ?? ""
                sendFinal(finalText)
            case "quit", "exit":
                _ = manager?.stopListening()
                isRunning = false
            default:
                sendError("Unknown command: \(trimmed)")
            }
        }
    }

    private func setLanguage(_ localeIdentifier: String) {
        _ = manager?.stopListening()

        currentLocale = Locale(identifier: localeIdentifier)
        manager = SpeechRecognitionManager(locale: currentLocale)

        if manager == nil {
            sendError("Failed to initialize speech recognizer for locale: \(localeIdentifier)")
            // Fall back to en-US
            currentLocale = Locale(identifier: "en-US")
            manager = SpeechRecognitionManager(locale: currentLocale)
        }

        send(Message(type: "languageChanged", text: currentLocale.identifier, isFinal: nil, error: nil, state: nil))
    }
}

// Entry point
let app = Application()
app.run()
