import Foundation
import GoldenMonkeyShared

do {
    let input = FileHandle.standardInput.readDataToEndOfFile()
    try CodexStatus.receive(input)
} catch {
    // Pet status reporting must never interrupt Codex work.
}
print("{}")
