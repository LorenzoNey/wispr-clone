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
    private var isListening = false

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

        // Configure audio session
        let audioSession = AVAudioSession.sharedInstance()
        try audioSession.setCategory(.record, mode: .measurement, options: .duckOthers)
        try audioSession.setActive(true, options: .notifyOthersOnDeactivation)

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

                if result.isFinal {
                    sendFinal(transcription)
                } else {
                    sendPartial(transcription)
                }
            }

            if let error = error {
                // Check if this is just an end-of-speech error (expected when stopping)
                let nsError = error as NSError
                if nsError.domain == "kAFAssistantErrorDomain" && nsError.code == 1110 {
                    // This is "No speech detected" - not an error, just silence
                    return
                }
                sendError("Recognition error: \(error.localizedDescription)")
            }
        }

        // Configure audio input
        let inputNode = audioEngine.inputNode
        let recordingFormat = inputNode.outputFormat(forBus: 0)

        inputNode.installTap(onBus: 0, bufferSize: 1024, format: recordingFormat) { [weak self] buffer, _ in
            self?.recognitionRequest?.append(buffer)
        }

        audioEngine.prepare()
        try audioEngine.start()
    }

    func stopListening() -> String {
        guard isListening else { return currentTranscription }

        isListening = false

        // Stop audio engine
        audioEngine.stop()
        audioEngine.inputNode.removeTap(onBus: 0)

        // End recognition request
        recognitionRequest?.endAudio()
        recognitionRequest = nil

        // Cancel task
        recognitionTask?.cancel()
        recognitionTask = nil

        // Deactivate audio session
        try? AVAudioSession.sharedInstance().setActive(false)

        sendState("idle")

        return currentTranscription
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
