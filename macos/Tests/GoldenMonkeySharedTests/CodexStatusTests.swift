import XCTest
@testable import GoldenMonkeyShared

final class CodexStatusTests: XCTestCase {
    func testBusyToolAndStopLifecycle() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        func send(_ event: String, tool: String = "") throws {
            let json = ["session_id":"one", "turn_id":"a", "hook_event_name":event, "tool_name":tool]
            try CodexStatus.receive(try JSONSerialization.data(withJSONObject: json), directory: root)
        }
        try send("UserPromptSubmit")
        XCTAssertEqual(CodexStatus.read(directory: root).phase, "busy")
        try send("PreToolUse", tool: "apply_patch")
        XCTAssertEqual(CodexStatus.read(directory: root).text, "Codex：修改文件")
        try send("Stop")
        XCTAssertEqual(CodexStatus.read(directory: root).phase, "ended")
    }

    func testPromptAndArgumentsAreNotPersisted() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let json: [String: Any] = [
            "session_id": "private", "turn_id": "one", "hook_event_name": "UserPromptSubmit",
            "prompt": "secret prompt", "tool_input": ["secret": "argument"]
        ]
        try CodexStatus.receive(try JSONSerialization.data(withJSONObject: json), directory: root)
        let files = try FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: nil).filter { $0.pathExtension == "json" }
        let persisted = String(data: try Data(contentsOf: files[0]), encoding: .utf8)!
        XCTAssertFalse(persisted.contains("secret prompt"))
        XCTAssertFalse(persisted.contains("argument"))
    }
}
