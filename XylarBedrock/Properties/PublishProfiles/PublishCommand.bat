@echo off
setlocal

set "PROJECT_DIR=%~dp0..\.."
pushd "%PROJECT_DIR%"

set "PUBLISH_DIR=bin\Publish\net8.0-windows10.0.17763.0\win-x64\publish"
set "STAGE_DIR=obj\release-stage\XylarBedrock-win-x64"
set "ZIP_DIR=releases"
set "ZIP_NAME=XylarBedrock-v0.0.0.5-win-x64.zip"

dotnet publish ".\XylarBedrock.csproj" ^
--configuration Publish ^
--runtime win-x64 ^
--self-contained true ^
--output "%PUBLISH_DIR%"

if errorlevel 1 (
    popd
    exit /b %errorlevel%
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$root = Resolve-Path '.';" ^
  "$publishDir = Join-Path $root '%PUBLISH_DIR%';" ^
  "$stageDir = Join-Path $root '%STAGE_DIR%';" ^
  "$dllDir = Join-Path $stageDir 'dll';" ^
  "$sourceDllDir = @((Join-Path $root 'Resources\\dll'), (Join-Path $root 'dll'), (Join-Path $root 'release\\dll')) | Where-Object { Test-Path $_ } | Select-Object -First 1;" ^
  "$zipDir = Join-Path $root '%ZIP_DIR%';" ^
  "$zipPath = Join-Path $zipDir '%ZIP_NAME%';" ^
  "if (-not $sourceDllDir) { throw 'Could not find a source dll folder. Expected .\\Resources\\dll, .\\dll or .\\release\\dll.' }" ^
  "Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue;" ^
  "New-Item -ItemType Directory -Path $dllDir -Force | Out-Null;" ^
  "New-Item -ItemType Directory -Path $zipDir -Force | Out-Null;" ^
  "Copy-Item (Join-Path $publishDir 'XylarBedrock.exe') $stageDir -Force;" ^
  "Copy-Item (Join-Path $sourceDllDir 'XylarBedrock.dll') (Join-Path $dllDir 'XylarBedrock.dll') -Force;" ^
  "Copy-Item (Join-Path $sourceDllDir 'XylarTools.dll') (Join-Path $dllDir 'XylarTools.dll') -Force;" ^
  "Copy-Item (Join-Path $sourceDllDir 'vcruntime140_1.dll') (Join-Path $dllDir 'vcruntime140_1.dll') -Force;" ^
  "Remove-Item $zipPath -Force -ErrorAction SilentlyContinue;" ^
  "Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force;"

set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
