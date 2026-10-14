import Foundation
import SpatialCore

/// What the assistant is allowed to do is decided by `AssistantTools` (SpatialCore) and executed by the host.
@MainActor protocol AssistantToolHost: AnyObject {
    func performTool(_ call: ValidatedCall) -> String
}

/// ChatGPT (OpenAI Responses API) as the voice assistant "brain". The model only ever receives the typed
/// tools in `AssistantTools`; consequential ones are proposals that need a human tap in the headset.
@MainActor final class AssistantService {
    static let keychainAccount = "openai-api-key"
    private var lastResponseID: String?
    private var lastUsed = Date.distantPast
    private let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 60; c.waitsForConnectivity = false
        return c
    }()

    var hasKey: Bool { KeychainStore.read(Self.keychainAccount) != nil }
    func newConversation() { lastResponseID = nil }

    func ask(_ prompt: String, model: String, host: AssistantToolHost) async throws -> String {
        guard let key = KeychainStore.read(Self.keychainAccount), !key.isEmpty else { throw AssistantFailure.noKey }
        if Date().timeIntervalSince(lastUsed) > 600 { lastResponseID = nil }
        var input = AssistantInput.user(prompt)
        var finalText = ""
        for _ in 0..<5 {
            let body = try OpenAIWire.requestBody(model: model, input: input, previousResponseID: lastResponseID)
            var request = URLRequest(url: URL(string: "https://api.openai.com/v1/responses")!)
            request.httpMethod = "POST"; request.httpBody = body
            request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            let (data, response) = try await session.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            let parsed: AssistantResponse
            do { parsed = try OpenAIWire.parse(data) }
            catch { lastResponseID = nil; if status == 401 { throw AssistantFailure.badKey }; throw error }
            lastResponseID = parsed.id; lastUsed = Date()
            if !parsed.text.isEmpty { finalText = parsed.text }
            if parsed.calls.isEmpty { return finalText }
            input = .toolOutputs(parsed.calls.map { call in
                guard let valid = AssistantTools.validate(name: call.name, argumentsJSON: call.arguments) else {
                    return (callID: call.callID, output: "Error: unknown tool or invalid arguments. Nothing was done.")
                }
                return (callID: call.callID, output: host.performTool(valid))
            })
        }
        return finalText.isEmpty ? "I stopped after too many steps." : finalText
    }
}
enum AssistantFailure: LocalizedError {
    case noKey, badKey
    var errorDescription: String? {
        switch self {
        case .noKey: return "No OpenAI API key is saved. Add one in SpatialMac Host > Integrations."
        case .badKey: return "OpenAI rejected the API key."
        }
    }
}
