import Contacts
import Foundation
import SQLite3
import SpatialCore

struct IncomingMessage {
    let rowID: Int64
    let sender: String        // display name or handle, already sanitized
    let body: String          // already sanitized; may be a placeholder
    let chatGUID: String?     // stays on the Mac; used only for replies
    let isGroup: Bool
}

/// Watches the Messages database (read-only) for NEW incoming messages and reports them.
///
/// Requires the user to grant this app "Full Disk Access" (System Settings > Privacy & Security),
/// because ~/Library/Messages is protected. Nothing is stored or logged by this class. It starts from the
/// current newest message, so history is never replayed into the headset.
final class MessageWatcher {
    var onMessage: ((IncomingMessage) -> Void)?
    var onStatus: ((String) -> Void)?
    var lookUpContacts = false
    private let queue = DispatchQueue(label: "SpatialMac.messages", qos: .utility)
    private var timer: DispatchSourceTimer?
    private var db: OpaquePointer?
    private var watermark: Int64 = -1
    private var names = [String: String]()
    private let path = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Messages/chat.db").path

    func start() {
        queue.async {
            guard self.timer == nil else { return }
            let t = DispatchSource.makeTimerSource(queue: self.queue)
            t.schedule(deadline: .now() + 0.5, repeating: 2.0)
            t.setEventHandler { [weak self] in self?.poll() }
            self.timer = t; t.resume()
        }
    }
    func stop() {
        queue.async {
            self.timer?.cancel(); self.timer = nil
            if let db = self.db { sqlite3_close(db); self.db = nil }
            self.watermark = -1; self.names.removeAll()
        }
    }
    private func open() -> Bool {
        if db != nil { return true }
        var handle: OpaquePointer?
        guard sqlite3_open_v2(path, &handle, SQLITE_OPEN_READONLY, nil) == SQLITE_OK, let handle else {
            if let handle { sqlite3_close(handle) }
            onStatus?("Messages unavailable: grant SpatialMac Full Disk Access, then toggle this off and on.")
            return false
        }
        sqlite3_busy_timeout(handle, 500)
        db = handle
        return true
    }
    private func poll() {
        guard open(), let db else { return }
        if watermark < 0 {
            var stmt: OpaquePointer?
            guard sqlite3_prepare_v2(db, "SELECT COALESCE(MAX(ROWID),0) FROM message", -1, &stmt, nil) == SQLITE_OK else {
                onStatus?("Messages database could not be read (permission or format change)."); sqlite3_close(db); self.db = nil; return
            }
            defer { sqlite3_finalize(stmt) }
            guard sqlite3_step(stmt) == SQLITE_ROW else { return }
            watermark = sqlite3_column_int64(stmt, 0)
            onStatus?("Watching for new messages.")
            return
        }
        let sql = """
        SELECT m.ROWID, m.text, m.attributedBody, m.cache_has_attachments, h.id, c.guid, c.display_name, c.style
        FROM message m
        LEFT JOIN handle h ON m.handle_id = h.ROWID
        LEFT JOIN chat_message_join j ON j.message_id = m.ROWID
        LEFT JOIN chat c ON c.ROWID = j.chat_id
        WHERE m.ROWID > ?1 AND m.is_from_me = 0 AND m.associated_message_type = 0 AND m.item_type = 0
        ORDER BY m.ROWID ASC LIMIT 20
        """
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK else {
            onStatus?("Messages database schema not recognized."); sqlite3_close(db); self.db = nil; return
        }
        defer { sqlite3_finalize(stmt) }
        sqlite3_bind_int64(stmt, 1, watermark)
        var batch = [IncomingMessage]()
        while sqlite3_step(stmt) == SQLITE_ROW {
            let rowID = sqlite3_column_int64(stmt, 0)
            watermark = max(watermark, rowID)
            var text = column(stmt, 1)
            if text == nil, let blob = sqlite3_column_blob(stmt, 2) {
                let count = Int(sqlite3_column_bytes(stmt, 2))
                if count > 0 && count < 1_000_000 { text = AttributedBodyDecoder.plainText(from: Data(bytes: blob, count: count)) }
            }
            let hasAttachment = sqlite3_column_int(stmt, 3) != 0
            let handle = column(stmt, 4) ?? "Unknown"
            let guid = column(stmt, 5)
            let groupName = column(stmt, 6)
            let isGroup = sqlite3_column_int(stmt, 7) == 43
            var body = TextSanitizer.clean(text ?? "", maxLength: 240)
            if body.isEmpty { body = hasAttachment ? "Sent an attachment" : "New message" }
            var sender = TextSanitizer.clean(resolve(handle), maxLength: 60)
            if isGroup, let groupName, !groupName.isEmpty { sender += " in " + TextSanitizer.clean(groupName, maxLength: 40) }
            batch.append(IncomingMessage(rowID: rowID, sender: sender, body: body, chatGUID: guid, isGroup: isGroup))
        }
        for message in batch { onMessage?(message) }
    }
    private func column(_ stmt: OpaquePointer?, _ index: Int32) -> String? {
        guard let c = sqlite3_column_text(stmt, index) else { return nil }
        return String(cString: c)
    }
    private func resolve(_ handle: String) -> String {
        guard lookUpContacts else { return handle }
        if let cached = names[handle] { return cached }
        var result = handle
        if CNContactStore.authorizationStatus(for: .contacts) == .authorized {
            let store = CNContactStore()
            let keys = [CNContactGivenNameKey, CNContactFamilyNameKey] as [CNKeyDescriptor]
            let predicate = handle.contains("@")
                ? CNContact.predicateForContacts(matchingEmailAddress: handle)
                : CNContact.predicateForContacts(matching: CNPhoneNumber(stringValue: handle))
            if let contact = try? store.unifiedContacts(matching: predicate, keysToFetch: keys).first {
                let name = "\(contact.givenName) \(contact.familyName)".trimmingCharacters(in: .whitespaces)
                if !name.isEmpty { result = name }
            }
        }
        names[handle] = result
        return result
    }
    static func requestContactsAccess(_ done: @escaping (Bool) -> Void) {
        CNContactStore().requestAccess(for: .contacts) { granted, _ in done(granted) }
    }
}

/// Sends a text through Messages.app via a FIXED AppleScript. Recipient and text travel as
/// separate process arguments, never interpolated into script source (no injection).
enum MessageSender {
    private static let script = """
    on run argv
        set theChat to item 1 of argv
        set theText to item 2 of argv
        tell application "Messages"
            send theText to chat id theChat
        end tell
    end run
    """
    static func isValidChatGUID(_ guid: String) -> Bool {
        guid.range(of: #"^[A-Za-z]+;[-+];[A-Za-z0-9@.+_:%-]{1,200}$"#, options: .regularExpression) != nil
    }
    static func send(text: String, chatGUID: String) async throws {
        guard isValidChatGUID(chatGUID), !text.isEmpty, text.count <= 500 else { throw SendFailure.invalid }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            DispatchQueue.global(qos: .userInitiated).async {
                let process = Process()
                process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
                process.arguments = ["-e", script, chatGUID, text]
                process.standardOutput = FileHandle.nullDevice; process.standardError = FileHandle.nullDevice
                do {
                    try process.run()
                    let kill = DispatchWorkItem { if process.isRunning { process.terminate() } }
                    DispatchQueue.global().asyncAfter(deadline: .now() + 15, execute: kill)
                    process.waitUntilExit(); kill.cancel()
                    if process.terminationStatus == 0 { continuation.resume() } else { continuation.resume(throwing: SendFailure.failed) }
                } catch { continuation.resume(throwing: error) }
            }
        }
    }
    enum SendFailure: LocalizedError {
        case invalid, failed
        var errorDescription: String? {
            switch self {
            case .invalid: return "That reply was not valid."
            case .failed: return "Messages did not accept the reply. Allow SpatialMac to control Messages in Privacy & Security > Automation."
            }
        }
    }
}
