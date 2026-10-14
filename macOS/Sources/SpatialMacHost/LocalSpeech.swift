import Foundation
import SpatialCore

// Optional, explicit local whisper.cpp CLI integration. No microphone or cloud
// access is needed on the Mac: only a push-to-talk utterance arrives from Quest.
struct LocalSpeech {
    let settings:HostSettings
    func transcribe(_ pcm:Data) async throws -> String {
        guard pcm.count>=10,pcm.count<=Wire.maxPCM else{throw SpeechFailure.invalidAudio}
        let b=[UInt8](pcm),rate=read32(b,0),count=read32(b,4)
        guard rate==16_000,count>0,count<=192_000,pcm.count==8+Int(count)*2 else{throw SpeechFailure.invalidAudio}
        guard let binary=settings.whisperExecutable,let model=settings.whisperModel,
              binary.hasPrefix("/"),model.hasPrefix("/"),
              FileManager.default.isExecutableFile(atPath:binary),FileManager.default.fileExists(atPath:model)
        else{throw SpeechFailure.notConfigured}
        let dir=FileManager.default.temporaryDirectory.appendingPathComponent("spatialmac-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        defer{try? FileManager.default.removeItem(at:dir)}
        let wav=dir.appendingPathComponent("speech.wav"),output=dir.appendingPathComponent("transcript")
        var data=Data("RIFF".utf8);data.appendLE(UInt32(36+Int(count)*2));data.append(Data("WAVEfmt ".utf8))
        data.appendLE(UInt32(16));data.appendLE(UInt16(1));data.appendLE(UInt16(1))
        data.appendLE(rate);data.appendLE(rate*2);data.appendLE(UInt16(2));data.appendLE(UInt16(16))
        data.append(Data("data".utf8));data.appendLE(count*2)
        // Transport PCM samples are little-endian, unlike the network metadata.
        data.append(pcm.dropFirst(8));try data.write(to:wav,options:.atomic)
        try await withCheckedThrowingContinuation { (continuation:CheckedContinuation<Void,Error>) in
            DispatchQueue.global(qos:.userInitiated).async {
                let process=Process();process.executableURL=URL(fileURLWithPath:binary)
                process.arguments=["-m",model,"-f",wav.path,"-otxt","-of",output.path,"-nt"]
                process.standardOutput=FileHandle.nullDevice;process.standardError=FileHandle.nullDevice
                do {
                    try process.run()
                    let timeout=DispatchWorkItem{if process.isRunning{process.terminate()}}
                    DispatchQueue.global().asyncAfter(deadline:.now()+30,execute:timeout)
                    process.waitUntilExit();timeout.cancel()
                    guard process.terminationStatus==0 else{throw SpeechFailure.failed}
                    continuation.resume()
                } catch{continuation.resume(throwing:error)}
            }
        }
        let text=try String(contentsOf:output.appendingPathExtension("txt"),encoding:.utf8)
        return String(text.trimmingCharacters(in:.whitespacesAndNewlines).prefix(2000))
    }
}
private extension Data {
    mutating func appendLE<T:FixedWidthInteger>(_ n:T) {
        var v=n.littleEndian;Swift.withUnsafeBytes(of:&v){append(contentsOf:$0)}
    }
}
enum SpeechFailure:LocalizedError {
    case invalidAudio,notConfigured,failed
    var errorDescription:String? {
        switch self {
        case .invalidAudio:return "Invalid PCM utterance. Expected up to 12 seconds at 16 kHz mono."
        case .notConfigured:return "Local speech is not configured. Set whisperExecutable and whisperModel on the Mac."
        case .failed:return "Local transcription failed or timed out."
        }
    }
}
