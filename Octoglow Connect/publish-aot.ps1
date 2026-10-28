# Publishes Octoglow Connect as a Native AOT app into bin\Publish\NativeAot.
#
# Native AOT links with Visual Studio's C++ linker ("Desktop development with
# C++"). The script that finds it runs Visual Studio 2026's vcvarsall.bat, which
# calls vswhere.exe by name: without the Visual Studio Installer folder on PATH
# the error message ends up in the linker's path and the link fails.
$ErrorActionPreference = "Stop"
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path (Join-Path $installer "vswhere.exe")) { $env:PATH = "$installer;$env:PATH" }
dotnet publish (Join-Path $PSScriptRoot "OctoglowSender.csproj") -p:PublishProfile=NativeAot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to $(Join-Path $PSScriptRoot 'bin\Publish\NativeAot')"
