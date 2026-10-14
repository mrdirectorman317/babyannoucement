import Foundation
import SpatialCore

/// OPTIONAL speech-to-text through OpenAI. Unlike LocalSpeech, the audio leaves this Mac, so it is a separate
/// opt-in toggle (off by default) that also needs an API key in the Keychain.
struct CloudSpeech {
    let apiKey:String
    var model="whisper-1"
    func transcribe(_ pcm:Data) async throws -> String {
        guard pcm.count>=10,pcm.count<=Wire.maxPCM else{throw SpeechFailure.invalidAudio}
        let b=[UInt8](pcm),rate=read32(b,0),count=read32(b,4)
        guard rate==16_000,count>0,count<=192_000,pcm.count==8+Int(count)*2 else{throw SpeechFailure.invalidAudio}
        let wav=LocalSpeech.makeWAV(pcm,rate:rate,count:count)
        let boundary="spatialmac-\(UUID().uuidString)"
        var body=Data()
        func field(_ name:String,_ value:String){body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".utf8))}
        field("model",model);field("response_format","text")
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"speech.wav\"\r\nContent-Type: audio/wav\r\n\r\n".utf8))
        body.append(wav);body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        var request=URLRequest(url:URL(string:"https://api.openai.com/v1/audio/transcriptions")!)
        request.httpMethod="POST";request.httpBody=body;request.timeoutInterval=45
        request.setValue("Bearer \(apiKey)",forHTTPHeaderField:"Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)",forHTTPHeaderField:"Content-Type")
        let (data,response)=try await URLSession(configuration:.ephemeral).data(for:request)
        guard (response as? HTTPURLResponse)?.statusCode==200,let text=String(data:data,encoding:.utf8) else{throw SpeechFailure.failed}
        return String(text.trimmingCharacters(in:.whitespacesAndNewlines).prefix(2000))
    }
}
