[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'dist\BehaviorTests.exe'
$manifest = Join-Path $PSScriptRoot 'src\CustomSnippingTool.exe.manifest'
& $compiler /nologo /target:exe /platform:x64 /main:VerificationHarness /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "/win32manifest:$manifest" "/out:$output" (Join-Path $PSScriptRoot 'tests\BehaviorTests.cs') (Join-Path $PSScriptRoot 'src\CustomSnippingTool.cs')
if ($LASTEXITCODE -ne 0) { throw 'Behavior test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Behavior tests failed.' }
