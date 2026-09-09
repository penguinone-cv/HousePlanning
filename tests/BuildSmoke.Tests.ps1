$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path $PSScriptRoot -Parent
$testDir=Join-Path $workspaceRoot 'tmp/smoke-regression'
New-Item -ItemType Directory -Force $testDir | Out-Null
$runner=Join-Path $workspaceRoot 'scripts/test-exported-app.ps1'
if (!(Test-Path $runner)) { throw 'Missing smoke test runner' }
$shellPath=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$fixture=Join-Path $PSScriptRoot 'SmokeProcessFixture.ps1'
foreach ($mode in @('success','failure','missing','timeout')) {
    $failed=$false
    try {
        $timeout=10000
        if ($mode -eq 'timeout') { $timeout=1000 }
        & $runner -FilePath $shellPath -TestArguments @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$fixture+'"'),'-Mode',$mode) -OutputPath (Join-Path $testDir "$mode-out.log") -ErrorPath (Join-Path $testDir "$mode-err.log") -TimeoutMs $timeout
    } catch { $failed=$true; Write-Host $_.Exception.Message }
    if ($failed -ne ($mode -ne 'success')) { throw "Wrong result for $mode" }
    Write-Host "PASS $mode"
}
Write-Host 'BUILD_SMOKE_TESTS_PASS 4 scenarios'
