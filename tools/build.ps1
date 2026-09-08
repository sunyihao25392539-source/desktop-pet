$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' /nologo /target:winexe /out:GoldenMonkeyPet-v14.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll '.\src\GoldenMonkeyPet.cs' '.\src\CodexStatus.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' /nologo /target:exe /out:GoldenMonkeyCodexHook.exe /reference:System.Web.Extensions.dll '.\src\CodexHookBridge.cs' '.\src\CodexStatus.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Hook bridge build failed' }
} finally { Pop-Location }
