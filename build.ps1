[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The 64-bit .NET Framework C# compiler is required. This project targets 64-bit Windows.'
}
$output = Join-Path $PSScriptRoot 'dist'
[void](New-Item -ItemType Directory -Path $output -Force)
$source = Join-Path $PSScriptRoot 'src\CustomSnippingTool.cs'
$manifest = Join-Path $PSScriptRoot 'src\CustomSnippingTool.exe.manifest'
$executable = Join-Path $output 'CustomSnippingTool.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "/win32manifest:$manifest" "/out:$executable" $source
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Read-ScreenshotText.ps1') -Destination $output
$test = Start-Process -FilePath $executable -ArgumentList '--self-test' -Wait -PassThru
$resultPath = Join-Path $output 'self-test-result.txt'
if ($test.ExitCode -ne 0) {
    throw ("Self-tests failed: " + (Get-Content -LiteralPath $resultPath -Raw))
}
Get-Content -LiteralPath $resultPath
Write-Output "Built: $executable"
