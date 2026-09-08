using System;

internal static class CodexHookBridge
{
    private static void Main()
    {
        try { CodexStatus.Receive(Console.In.ReadToEnd()); }
        catch (Exception) { /* Status reporting must never interrupt Codex work. */ }
        Console.WriteLine("{}");
    }
}
