import SwiftUI

@main struct SpatialMacHostApp:App {
    @StateObject private var model=HostModel.shared
    var body:some Scene {
        WindowGroup("SpatialMac Host") { RootView(model:model).frame(minWidth:760,minHeight:640) }
        MenuBarExtra("SpatialMac",systemImage:"rectangle.3.group") {
            Button("EMERGENCY STOP"){model.emergencyStop()}
            Button("Stop server"){model.stop()}
            Divider()
            Text(model.connected ? "Headset connected":"No headset")
            Button("Quit"){model.stop();NSApplication.shared.terminate(nil)}
        }
    }
}
struct RootView:View {
    @ObservedObject var model:HostModel
    var body:some View {
        TabView {
            HostView(model:model).tabItem{Label("Workspace",systemImage:"rectangle.3.group")}
            IntegrationsView(model:model).tabItem{Label("Integrations",systemImage:"sparkles")}
        }
    }
}
struct IntegrationsView:View {
    @ObservedObject var model:HostModel
    @State private var key=""
    var body:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:14) {
                Text("Messages in your headset").font(.title2.bold())
                Text("Shows new iMessages as pop-ups in the headset. Needs Full Disk Access for this app (System Settings > Privacy & Security). Off by default; nothing is stored or logged.").font(.callout).foregroundStyle(.secondary)
                Toggle("Show incoming messages in the headset",isOn:Binding(get:{model.messagesEnabled},set:model.setMessages))
                Toggle("Show message text (off = sender only)",isOn:Binding(get:{model.messagePreview},set:model.setMessagePreview)).disabled(!model.messagesEnabled)
                Toggle("Look up sender names in Contacts",isOn:Binding(get:{model.contactLookup},set:model.setContactLookup)).disabled(!model.messagesEnabled)
                Toggle("Allow replying from the headset (off after disconnect)",isOn:Binding(get:{model.replyEnabled},set:model.setReply)).disabled(!model.messagesEnabled || !model.connected)
                Text(model.messagesStatus).font(.caption).foregroundStyle(.secondary)
                Divider()
                Text("ChatGPT assistant").font(.title2.bold())
                Text("Talk to ChatGPT from the headset. It can change your workspace through a short list of safe tools. Anything that acts on your Mac or other people is shown in the headset and needs your tap first. Your question (and tool results it asks for) go to OpenAI using your API key.").font(.callout).foregroundStyle(.secondary)
                HStack {
                    SecureField(model.assistantHasKey ? "API key saved in Keychain":"OpenAI API key",text:$key).textFieldStyle(.roundedBorder)
                    Button("Save"){model.saveAPIKey(key);key=""}.disabled(key.isEmpty)
                    Button("Remove"){model.removeAPIKey()}.disabled(!model.assistantHasKey)
                }
                HStack {Text("Model");TextField("gpt-5",text:$model.assistantModel).textFieldStyle(.roundedBorder).frame(width:220)
                    Button("New conversation"){model.newAssistantConversation()}}
                Toggle("Use OpenAI for speech-to-text (your voice audio leaves this Mac; off = local whisper.cpp)",isOn:Binding(get:{model.cloudSpeech},set:model.setCloudSpeech)).disabled(!model.assistantHasKey)
                Toggle("Let the assistant read recent messages when I ask",isOn:Binding(get:{model.assistantReadsMessages},set:model.setAssistantReadsMessages)).disabled(!model.messagesEnabled)
                if model.assistantBusy{ProgressView("Thinking…")}
                if !model.lastAssistantReply.isEmpty{Text("Last reply: \(model.lastAssistantReply)").font(.caption).textSelection(.enabled)}
                Divider()
                Text("Siri & Shortcuts").font(.title2.bold())
                Text("Say \"Hey Siri, show my room in SpatialMac\" on this Mac, iPhone or Watch. Siri needs the Xcode-built app (scripts/make-xcodeproj.sh). Intents: open workspace, real room/virtual world, recenter, show a note, ask the assistant, emergency stop.").font(.callout).foregroundStyle(.secondary)
            }.padding(24).frame(maxWidth:.infinity,alignment:.leading)
        }
    }
}
struct HostView:View {
    @ObservedObject var model:HostModel
    var body:some View {
        VStack(alignment:.leading,spacing:14) {
            Text("SpatialMac").font(.largeTitle.bold())
            Text("Your Mac is the computer. Quest is the spatial interface.").foregroundStyle(.secondary)
            HStack {
                Button("Start TLS server"){model.start()}.disabled(model.running)
                Button("Stop"){model.stop()}.disabled(!model.running)
                Button("Screen Recording permission"){model.requestScreenPermission()}
                Button("Accessibility permission"){model.requestAccessibility()}
            }
            Text(model.status).font(.callout).textSelection(.enabled)
            Toggle("Share only the sources checked below",isOn:Binding(get:{model.sharingEnabled},set:model.setSharing))
            Toggle("ARM remote mouse / keyboard (off after disconnect)",isOn:Binding(get:{model.controlEnabled},set:model.setControl))
            Toggle("Allow headset voice dictation (speech-to-text)",isOn:Binding(get:{model.speechEnabled},set:model.setSpeech))
            HStack {
                Text("Explicit capture allowlist").font(.headline)
                Spacer()
                Button("Refresh Mac windows"){Task{await model.refresh()}}
            }
            List(model.sources) { source in
                Toggle(isOn:Binding(get:{model.approved.contains(source.id)},set:{model.approve(source.id,$0)})) {
                    VStack(alignment:.leading){Text("\(source.app) — \(source.title)");Text(source.id).font(.caption).foregroundStyle(.secondary)}
                }
            }
            Text("Checking a whole display includes its notifications and other visible apps. Individual window input is conservative: popups and moved windows may require the full desktop view.").font(.caption)
            Button("EMERGENCY STOP — stop sharing and release all input"){model.emergencyStop()}
                .buttonStyle(.borderedProminent).tint(.red)
        }.padding(24)
    }
}
