import AppIntents
import Foundation

// Siri / Shortcuts / Spotlight entry points ("Hey Siri, show my room").
//
// Siri runs on your Mac, iPhone, Watch or AirPods - not inside the Quest. These intents drive the headset
// THROUGH this Mac app. Only reversible, UI-level actions are exposed here (plus emergency stop). Anything that
// acts on the Mac or on other people stays behind the in-headset confirmation flow.
//
// IMPORTANT: Siri only discovers intents when the app is built by Xcode (it generates the App Intents
// metadata). Use `scripts/make-xcodeproj.sh`, not the plain SwiftPM bundle, for Siri.

enum EnvironmentChoice: String, AppEnum {
    case passthrough, virtual
    static let typeDisplayRepresentation: TypeDisplayRepresentation = "Environment"
    static let caseDisplayRepresentations: [EnvironmentChoice: DisplayRepresentation] = [
        .passthrough: "Real room", .virtual: "Virtual world"
    ]
}

struct OpenWorkspaceIntent: AppIntent {
    static let title: LocalizedStringResource = "Open Spatial Workspace"
    static let description = IntentDescription("Starts the SpatialMac host so your headset can connect.")
    @MainActor func perform() async throws -> some IntentResult & ProvidesDialog {
        HostModel.shared.ensureStarted()
        return .result(dialog: "Workspace is ready. Put on your headset and open SpatialMac.")
    }
}

struct SetEnvironmentIntent: AppIntent {
    static let title: LocalizedStringResource = "Set Headset Environment"
    static let description = IntentDescription("Switch the headset between your real room and the virtual world.")
    @Parameter(title: "Environment") var environment: EnvironmentChoice
    static var parameterSummary: some ParameterSummary { Summary("Show \(\.$environment) in the headset") }
    @MainActor func perform() async throws -> some IntentResult & ProvidesDialog {
        guard HostModel.shared.sendUICommand("set_environment", arg: environment.rawValue) else {
            return .result(dialog: "The headset isn't connected.")
        }
        return .result(dialog: environment == .passthrough ? "Showing your room." : "Entering the virtual world.")
    }
}

struct RecenterWorkspaceIntent: AppIntent {
    static let title: LocalizedStringResource = "Recenter Workspace"
    static let description = IntentDescription("Bring your panels back in front of you.")
    @MainActor func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: HostModel.shared.sendUICommand("recenter") ? "Recentered." : "The headset isn't connected.")
    }
}

struct ShowInHeadsetIntent: AppIntent {
    static let title: LocalizedStringResource = "Show in Headset"
    static let description = IntentDescription("Pop a short note up in your headset.")
    @Parameter(title: "Title") var heading: String
    @Parameter(title: "Text") var text: String
    @MainActor func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: HostModel.shared.toast(title: heading, body: text) ? "Sent to your headset." : "The headset isn't connected.")
    }
}

struct AskAssistantIntent: AppIntent {
    static let title: LocalizedStringResource = "Ask the Assistant"
    static let description = IntentDescription("Ask ChatGPT, with control over your workspace. Needs an OpenAI API key in SpatialMac.")
    @Parameter(title: "Question") var question: String
    @MainActor func perform() async throws -> some IntentResult & ReturnsValue<String> & ProvidesDialog {
        let model = HostModel.shared
        guard model.assistantHasKey else { return .result(value: "", dialog: "Add an OpenAI API key in SpatialMac first.") }
        let reply = await model.askAssistant(question)
        return .result(value: reply, dialog: IntentDialog(stringLiteral: reply))
    }
}

struct EmergencyStopIntent: AppIntent {
    static let title: LocalizedStringResource = "Emergency Stop"
    static let description = IntentDescription("Stop sharing and disarm all remote control immediately.")
    @MainActor func perform() async throws -> some IntentResult & ProvidesDialog {
        HostModel.shared.emergencyStop()
        return .result(dialog: "Stopped. Sharing and remote control are off.")
    }
}

struct SpatialShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: OpenWorkspaceIntent(),
                    phrases: ["Open my \(.applicationName) workspace", "Start \(.applicationName)"],
                    shortTitle: "Open Workspace", systemImageName: "visionpro")
        AppShortcut(intent: SetEnvironmentIntent(),
                    phrases: ["Show \(\.$environment) in \(.applicationName)", "Switch \(.applicationName) to \(\.$environment)"],
                    shortTitle: "Set Environment", systemImageName: "mountain.2")
        AppShortcut(intent: RecenterWorkspaceIntent(),
                    phrases: ["Recenter \(.applicationName)", "Bring my \(.applicationName) windows back"],
                    shortTitle: "Recenter", systemImageName: "scope")
        AppShortcut(intent: AskAssistantIntent(),
                    phrases: ["Ask \(.applicationName) a question", "Ask the \(.applicationName) assistant"],
                    shortTitle: "Ask Assistant", systemImageName: "sparkles")
        AppShortcut(intent: EmergencyStopIntent(),
                    phrases: ["Emergency stop \(.applicationName)", "Stop \(.applicationName) now"],
                    shortTitle: "Emergency Stop", systemImageName: "exclamationmark.octagon")
    }
}
