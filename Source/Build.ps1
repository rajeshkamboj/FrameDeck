$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName
$output = Join-Path (Split-Path $PSScriptRoot -Parent) 'FrameDeck.exe'
$manifest = Join-Path $PSScriptRoot 'FrameDeck.manifest'
$icon = Join-Path $PSScriptRoot 'FrameDeck.ico'
& $compiler /nologo /warn:4 /optimize+ /platform:x64 /target:winexe "/out:$output" "/win32manifest:$manifest" "/win32icon:$icon" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
