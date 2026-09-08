using System;
using System.IO;
using System.Diagnostics;
using System.Web.Script.Serialization;

internal static class CodexStatusTests
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static void Send(string session, string turn, string ev, string tool, string id)
    {
        CodexStatus.Receive(new JavaScriptSerializer().Serialize(new { session_id = session, turn_id = turn, hook_event_name = ev, tool_name = tool, tool_use_id = id, prompt = "PRIVATE PROMPT", tool_input = "PRIVATE ARGUMENT" }));
    }
    private static CodexSnapshot Read() { return CodexStatus.Read(DateTime.UtcNow); }
    private static void Main()
    {
        CodexStatus.DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "tests-" + Guid.NewGuid().ToString("N"));
        Check(Read().Phase == "disconnected", "Must not pretend connected");
        Send("one", "a", "UserPromptSubmit", "", "");
        Send("two", "b", "UserPromptSubmit", "", "");
        Check(Read().Active == 2, "Multi task count");
        Send("one", "a", "PreToolUse", "apply_patch", "edit");
        Check(Read().Text.Contains("修改"), "Editing status");
        Send("one", "a", "PermissionRequest", "apply_patch", "edit");
        Send("one", "a", "PreToolUse", "Bash", "parallel");
        Send("one", "a", "PostToolUse", "Bash", "parallel");
        Check(Read().Phase == "waiting", "Parallel tool cleared another approval");
        Send("one", "a", "PostToolUse", "apply_patch", "edit");
        Check(Read().Phase == "busy", "Approval did not clear");
        Send("one", "a", "Stop", "", "");
        Check(Read().Active == 1, "One stop incorrectly completed all tasks");
        Send("two", "b", "Interrupt", "", "");
        Check(Read().Active == 0 && Read().Phase == "interrupted", "Interrupt status");
        Send("one", "new", "UserPromptSubmit", "", "");
        Send("one", "a", "Stop", "", "");
        Check(Read().Active == 1, "Late old stop ended new turn");
        Check(CodexStatus.Read(DateTime.UtcNow.AddHours(1)).Phase == "unknown", "Stale state claimed completion");
        foreach (string path in Directory.GetFiles(CodexStatus.DirectoryPath, "*.json"))
            Check(!File.ReadAllText(path).Contains("PRIVATE"), "Stored sensitive payload");
        string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GoldenMonkeyCodexHook.exe");
        ProcessStartInfo start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (Process process = Process.Start(start))
        {
            process.StandardInput.Write("{\"session_id\":\"pet-bridge-selftest\",\"turn_id\":\"selftest\",\"hook_event_name\":\"SessionEnd\"}");
            process.StandardInput.Close();
            Check(process.WaitForExit(2500), "Hook blocked");
            Check(process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Trim() == "{}", "Hook output invalid");
        }
        string live = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "codex-status");
        bool persisted = false;
        foreach (string path in Directory.GetFiles(live, "*.json"))
        {
            if (new JavaScriptSerializer().Deserialize<CodexRecord>(File.ReadAllText(path)).Session != "pet-bridge-selftest") continue;
            persisted = true;
            File.Delete(path); // Remove only this test's synthetic event, never a real session.
        }
        Check(persisted, "Real executable did not persist stdin event");
        Console.WriteLine("PASS: real hook process, concurrent tasks/tools, approvals, end, interruption, stale state, old-turn isolation, metadata privacy.");
    }
}
