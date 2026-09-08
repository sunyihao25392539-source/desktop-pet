using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Only event metadata is persisted. Prompts, transcripts and tool arguments are discarded.
internal sealed class CodexRecord
{
    public string Session;
    public string Turn;
    public string Phase;
    public long Updated;
    public Dictionary<string, string> Tools = new Dictionary<string, string>();
    public Dictionary<string, bool> Waiting = new Dictionary<string, bool>();
}

internal sealed class CodexSnapshot
{
    public string Text = "Codex：等待连接";
    public string Phase = "disconnected";
    public int Active;
    public long Revision;
}

internal static class CodexStatus
{
    internal static string DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "codex-status");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304 };

    private static string Value(Dictionary<string, object> input, string key)
    {
        object value;
        return input.TryGetValue(key, out value) && value is string ? (string)value : "";
    }

    internal static void Receive(string input)
    {
        var data = Json.Deserialize<Dictionary<string, object>>(input);
        string session = Value(data, "session_id"), turn = Value(data, "turn_id");
        string ev = Value(data, "hook_event_name"), tool = Value(data, "tool_name");
        if (session.Length == 0 || session.Length > 200 || turn.Length > 200) return;
        if (Array.IndexOf(new[] { "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "Interrupt", "SessionEnd" }, ev) < 0) return;
        if (turn.Length == 0 && ev != "SessionEnd") return;
        string key;
        using (SHA256 hash = SHA256.Create())
            key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(session))).Replace("-", "");
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, key + ".json");
        using (Mutex gate = new Mutex(false, "Local\\GoldenMonkeyCodexStatusV1"))
        {
            bool held = false;
            try
            {
                try { held = gate.WaitOne(800); } catch (AbandonedMutexException) { held = true; }
                if (!held) return;
                CodexRecord record = File.Exists(path) ? Json.Deserialize<CodexRecord>(File.ReadAllText(path)) : null;
                if (record == null || (record.Turn != turn && ev == "UserPromptSubmit"))
                    record = new CodexRecord { Session = session, Turn = turn, Phase = "busy" };
                if (ev != "SessionEnd" && record.Turn != turn) return; // Late events cannot revive an old turn.
                string id = Value(data, "tool_use_id");
                if (id.Length == 0) id = tool.Length > 0 ? tool : "tool";
                if (ev == "UserPromptSubmit") record.Phase = "busy";
                else if (ev == "PreToolUse")
                {
                    record.Phase = "busy";
                    record.Tools[id] = tool == "apply_patch" || tool == "Edit" || tool == "Write" ? "edit" : "tool";
                }
                else if (ev == "PermissionRequest") { record.Phase = "busy"; record.Waiting[id] = true; }
                else if (ev == "PostToolUse") { record.Tools.Remove(id); record.Waiting.Remove(id); }
                else
                {
                    record.Phase = ev == "Stop" ? "ended" : ev == "Interrupt" ? "interrupted" : "closed";
                    record.Tools.Clear(); record.Waiting.Clear();
                }
                record.Updated = DateTime.UtcNow.Ticks;
                string temp = path + ".tmp";
                File.WriteAllText(temp, Json.Serialize(record), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (held) gate.ReleaseMutex(); }
        }
    }

    internal static CodexSnapshot Read(DateTime now)
    {
        var result = new CodexSnapshot();
        if (!Directory.Exists(DirectoryPath)) return result;
        int waiting = 0, stale = 0;
        bool editing = false, tools = false;
        CodexRecord latest = null;
        foreach (string path in Directory.GetFiles(DirectoryPath, "*.json"))
        {
            CodexRecord record;
            try { record = Json.Deserialize<CodexRecord>(File.ReadAllText(path)); }
            catch (IOException) { continue; }
            catch (ArgumentException) { continue; }
            catch (InvalidOperationException) { continue; }
            if (record == null || record.Updated <= 0 || record.Tools == null || record.Waiting == null) continue;
            if (latest == null || record.Updated > latest.Updated) latest = record;
            result.Revision = Math.Max(result.Revision, record.Updated);
            if (record.Phase != "busy") continue;
            if (now.Ticks - record.Updated > TimeSpan.FromMinutes(30).Ticks) { stale++; continue; }
            result.Active++;
            waiting += record.Waiting.Count;
            editing |= record.Tools.ContainsValue("edit"); tools |= record.Tools.Count > 0;
        }
        if (latest == null) return result;
        if (waiting > 0) { result.Phase = "waiting"; result.Text = "Codex：需要确认"; }
        else if (result.Active > 0)
        {
            result.Phase = "busy";
            result.Text = editing ? "Codex：修改文件" : tools ? "Codex：执行工具" : "Codex：正在工作";
        }
        else if (stale > 0) { result.Phase = "unknown"; result.Text = "Codex：状态待更新"; }
        else
        {
            result.Phase = latest.Phase;
            bool recent = now.Ticks - latest.Updated < TimeSpan.FromSeconds(15).Ticks;
            result.Text = recent && latest.Phase == "ended" ? "Codex：本轮已结束"
                : recent && latest.Phase == "interrupted" ? "Codex：已中断" : "Codex：暂无活动";
        }
        if (result.Active > 1) result.Text += " ×" + result.Active;
        if (stale > 0 && result.Active > 0) result.Text += " ?";
        return result;
    }
}
