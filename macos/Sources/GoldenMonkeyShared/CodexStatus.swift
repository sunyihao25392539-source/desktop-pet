import Foundation
import CryptoKit

public struct CodexRecord: Codable {
    public var session: String
    public var turn: String
    public var phase: String
    public var updated: Date
    public var tools: [String: String]
    public var waiting: [String: Bool]

    enum CodingKeys: String, CodingKey {
        case session = "Session", turn = "Turn", phase = "Phase", updated = "Updated"
        case tools = "Tools", waiting = "Waiting"
    }
}

public struct CodexSnapshot {
    public var text = "Codex：等待连接"
    public var phase = "disconnected"
    public var active = 0
    public var revision = Date.distantPast
    public init() {}
}

public enum CodexStatus {
    public static let changedNotification = Notification.Name("com.sunyihao.goldenmonkeypet.codex-status-changed")
    private static let busyExpiry: TimeInterval = 5 * 60
    public static var directoryURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("GoldenMonkeyPet/codex-status", isDirectory: true)
    }

    public static func receive(_ input: Data, directory: URL? = nil) throws {
        guard let object = try JSONSerialization.jsonObject(with: input) as? [String: Any] else { return }
        let session = object["session_id"] as? String ?? ""
        let turn = object["turn_id"] as? String ?? ""
        let event = object["hook_event_name"] as? String ?? ""
        let tool = object["tool_name"] as? String ?? ""
        let accepted = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "Interrupt", "SessionEnd"]
        guard !session.isEmpty, session.count <= 200, turn.count <= 200, accepted.contains(event) else { return }
        guard !turn.isEmpty || event == "SessionEnd" else { return }

        let root = directory ?? directoryURL
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let digest = SHA256.hash(data: Data(session.utf8)).map { String(format: "%02X", $0) }.joined()
        let target = root.appendingPathComponent(digest + ".json")
        let lock = root.appendingPathComponent(digest + ".lock", isDirectory: true)
        guard acquireLock(lock) else { return }
        defer { try? FileManager.default.removeItem(at: lock) }

        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        var record = (try? Data(contentsOf: target)).flatMap { try? decoder.decode(CodexRecord.self, from: $0) }
        if record == nil || (record?.turn != turn && event == "UserPromptSubmit") {
            record = CodexRecord(session: session, turn: turn, phase: "busy", updated: Date(), tools: [:], waiting: [:])
        }
        guard var value = record, event == "SessionEnd" || value.turn == turn else { return }
        let suppliedID = object["tool_use_id"] as? String ?? ""
        let id = suppliedID.isEmpty ? (tool.isEmpty ? "tool" : tool) : suppliedID
        switch event {
        case "UserPromptSubmit": value.phase = "busy"
        case "PreToolUse":
            value.phase = "busy"
            value.tools[id] = ["apply_patch", "Edit", "Write"].contains(tool) ? "edit" : "tool"
        case "PermissionRequest":
            value.phase = "busy"
            value.waiting[id] = true
        case "PostToolUse":
            value.tools.removeValue(forKey: id)
            value.waiting.removeValue(forKey: id)
        default:
            value.phase = event == "Stop" ? "ended" : event == "Interrupt" ? "interrupted" : "closed"
            value.tools.removeAll()
            value.waiting.removeAll()
        }
        value.updated = Date()
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .millisecondsSince1970
        let data = try encoder.encode(value)
        let temp = root.appendingPathComponent(digest + ".tmp")
        try data.write(to: temp, options: .atomic)
        if FileManager.default.fileExists(atPath: target.path) { try FileManager.default.removeItem(at: target) }
        try FileManager.default.moveItem(at: temp, to: target)
        DistributedNotificationCenter.default().postNotificationName(changedNotification, object: nil, userInfo: nil, deliverImmediately: true)
    }

    public static func read(now: Date = Date(), directory: URL? = nil) -> CodexSnapshot {
        var result = CodexSnapshot()
        let root = directory ?? directoryURL
        guard let files = try? FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: nil) else { return result }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        var latest: CodexRecord?
        var waiting = 0, stale = 0
        var editing = false, tools = false
        for file in files where file.pathExtension == "json" {
            guard let data = try? Data(contentsOf: file), let record = try? decoder.decode(CodexRecord.self, from: data) else { continue }
            if latest == nil || record.updated > latest!.updated { latest = record }
            if record.updated > result.revision { result.revision = record.updated }
            guard record.phase == "busy" else { continue }
            if now.timeIntervalSince(record.updated) > busyExpiry { stale += 1; continue }
            result.active += 1
            waiting += record.waiting.count
            editing = editing || record.tools.values.contains("edit")
            tools = tools || !record.tools.isEmpty
        }
        guard let newest = latest else { return result }
        if waiting > 0 { result.phase = "waiting"; result.text = "Codex：需要确认" }
        else if result.active > 0 {
            result.phase = "busy"
            result.text = editing ? "Codex：修改文件" : tools ? "Codex：执行工具" : "Codex：正在工作"
        } else if stale > 0 { result.phase = "idle"; result.text = "Codex：暂无活动" }
        else {
            result.phase = newest.phase
            let recent = now.timeIntervalSince(newest.updated) < 15
            result.text = recent && newest.phase == "ended" ? "Codex：本轮已结束" : recent && newest.phase == "interrupted" ? "Codex：已中断" : "Codex：暂无活动"
        }
        if result.active > 1 { result.text += " ×\(result.active)" }
        if stale > 0 && result.active > 0 { result.text += " ?" }
        return result
    }

    private static func acquireLock(_ url: URL) -> Bool {
        for _ in 0..<40 {
            do { try FileManager.default.createDirectory(at: url, withIntermediateDirectories: false); return true }
            catch { Thread.sleep(forTimeInterval: 0.02) }
        }
        return false
    }
}
