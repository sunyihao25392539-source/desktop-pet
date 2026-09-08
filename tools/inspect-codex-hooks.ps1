$ErrorActionPreference = 'Stop'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command codex).Source
$start.Arguments = 'app-server --stdio'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardInputEncoding = New-Object Text.UTF8Encoding $false
$start.StandardOutputEncoding = New-Object Text.UTF8Encoding $false
$process = [Diagnostics.Process]::Start($start)
$errorsTask = $process.StandardError.ReadToEndAsync()
try {
    $process.StandardInput.WriteLine('{"id":1,"method":"initialize","params":{"clientInfo":{"name":"pet-hook-diagnostic","version":"1.0"},"capabilities":{"experimentalApi":true}}}')
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline) {
        $lineTask = $process.StandardOutput.ReadLineAsync()
        if (-not $lineTask.Wait(5000)) { throw 'Codex hook inspection timed out' }
        if ($null -eq $lineTask.Result) { throw ('Codex app-server exited: ' + $errorsTask.GetAwaiter().GetResult()) }
        $message = $lineTask.Result | ConvertFrom-Json
        if ($message.id -eq 1) {
            if ($message.error) { throw ($message.error | ConvertTo-Json -Compress) }
            $process.StandardInput.WriteLine('{"method":"initialized","params":{}}')
            $request = @{ id = 2; method = 'hooks/list'; params = @{ cwds = @((Split-Path $PSScriptRoot -Parent)) } } | ConvertTo-Json -Depth 5 -Compress
            $process.StandardInput.WriteLine($request)
        }
        if ($message.id -eq 2) {
            $message | ConvertTo-Json -Depth 15
            break
        }
    }
} finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(1500)) { $process.Kill() }
    $process.Dispose()
}
