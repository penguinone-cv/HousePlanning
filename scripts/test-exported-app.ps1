param(
    [Parameter(Mandatory=$true)][string]$FilePath,
    [string[]]$TestArguments=@('--headless','--','--self-test'),
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$ErrorPath,
    [int]$TimeoutMs=45000
)
$ErrorActionPreference='Stop'
$smoke=Start-Process -FilePath $FilePath -ArgumentList $TestArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $OutputPath -RedirectStandardError $ErrorPath
try {
    # Windows PowerShell 5.1 can otherwise return null for ExitCode after waiting.
    $null=$smoke.Handle
    if (!$smoke.WaitForExit($TimeoutMs)) {
        $smoke.Kill()
        $smoke.WaitForExit()
        throw "Exported application smoke test timed out after $TimeoutMs ms. Logs: $OutputPath / $ErrorPath"
    }
    # Drain asynchronous redirected output before reading the success marker.
    $smoke.WaitForExit()
    $exitCode=$smoke.ExitCode
    Get-Content -LiteralPath $OutputPath
    $passed=Select-String -LiteralPath $OutputPath -Pattern '^APP_SELF_TEST_PASS(\s|$)' -Quiet
    if ($exitCode -ne 0 -or !$passed) {
        Get-Content -LiteralPath $ErrorPath | ForEach-Object { Write-Host $_ }
        throw "Exported application smoke test failed (exit code: $exitCode; success marker: $passed). Logs: $OutputPath / $ErrorPath"
    }
} finally { $smoke.Dispose() }
