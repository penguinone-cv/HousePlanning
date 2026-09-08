param([switch]$Export, [switch]$Test)
$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
Push-Location $workspaceRoot
try {
  $env:DOTNET_CLI_HOME=Join-Path $workspaceRoot '.tools/dotnet-home'
  $env:NUGET_PACKAGES=Join-Path $workspaceRoot '.tools/packages'
  $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
  $env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
  $env:APPDATA=Join-Path $workspaceRoot 'tmp/appdata'
  $env:LOCALAPPDATA=Join-Path $workspaceRoot 'tmp/localappdata'
  New-Item -ItemType Directory -Force $env:APPDATA,$env:LOCALAPPDATA | Out-Null
  $engine=Join-Path $workspaceRoot '.tools/Godot_v4.4.1-stable_mono_win64/Godot_v4.4.1-stable_mono_win64_console.exe'
  if (!(Test-Path $engine)) { throw 'Run scripts/bootstrap.py first.' }
  dotnet build src/App -p:NuGetAudit=false
  if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
  if ($Test) {
    dotnet run --project tests/Core.Tests
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    & $engine --headless --path src/App -- --self-test
    if ($LASTEXITCODE -ne 0) { throw 'App tests failed' }
  }
  if ($Export) {
    New-Item -ItemType Directory -Force dist/HousePlanning | Out-Null
    & $engine --headless --path src/App --editor --import --quit
    if ($LASTEXITCODE -ne 0) { throw 'Import failed' }
    $exportOutput = & $engine --headless --path src/App --export-release 'Windows Desktop' 2>&1
    $exportCode = $LASTEXITCODE
    $exportOutput | ForEach-Object { Write-Host $_ }
    if ($exportCode -ne 0 -or ($exportOutput -match 'ERROR: Export|Failed to build project').Count -gt 0) { throw 'Export failed' }
    if (!(Get-ChildItem dist/HousePlanning -Recurse -Filter HousePlanning.dll)) { throw 'Export did not produce the application assembly' }
    Copy-Item src/App/native/pdfium.dll dist/HousePlanning/pdfium.dll
    Copy-Item README.md dist/HousePlanning/README.md
    Copy-Item -Recurse -Force licenses dist/HousePlanning/
    Copy-Item -Recurse -Force docs dist/HousePlanning/
    if (Test-Path samples) { Copy-Item -Recurse -Force samples dist/HousePlanning/ }
    $smoke = Start-Process -FilePath "$workspaceRoot/dist/HousePlanning/HousePlanning.exe" -ArgumentList '--headless','--','--self-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput "$workspaceRoot/tmp/export-smoke.log" -RedirectStandardError "$workspaceRoot/tmp/export-smoke-errors.log"
    if (!$smoke.WaitForExit(45000)) { $smoke.Kill(); throw 'Exported application smoke test timed out' }
    Get-Content "$workspaceRoot/tmp/export-smoke.log"
    if ($smoke.ExitCode -ne 0 -or !(Select-String -Path "$workspaceRoot/tmp/export-smoke.log" -Pattern 'APP_SELF_TEST_PASS' -Quiet)) { throw 'Exported application smoke test failed' }
    Compress-Archive -Path dist/HousePlanning/* -DestinationPath dist/HousePlanning-win-x64.zip -Force
  }
} finally { Pop-Location }
