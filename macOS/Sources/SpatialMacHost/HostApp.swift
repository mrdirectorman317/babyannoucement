import SwiftUI

@main struct SpatialMacHostApp:App {
    @StateObject private var model=HostModel()
    var body:some Scene {
        WindowGroup("SpatialMac Host") { HostView(model:model).frame(minWidth:720,minHeight:590) }
        MenuBarExtra("SpatialMac",systemImage:"rectangle.3.group") {
            Button("EMERGENCY STOP"){model.emergencyStop()}
            Button("Stop server"){model.stop()}
            Divider()
            Text(model.connected ? "Headset connected":"No headset")
            Button("Quit"){model.stop();NSApplication.shared.terminate(nil)}
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
            Toggle("Allow optional local speech transcription",isOn:Binding(get:{model.speechEnabled},set:model.setSpeech))
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
