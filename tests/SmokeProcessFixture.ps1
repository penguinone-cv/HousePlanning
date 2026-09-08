param([string]$Mode)
Start-Sleep -Milliseconds 300
if ($Mode -eq 'timeout') { Start-Sleep -Seconds 5 }
if ($Mode -ne 'missing') { Write-Output 'APP_SELF_TEST_PASS fixture' }
if ($Mode -eq 'failure') { [Console]::Error.WriteLine('fixture intentional failure'); exit 7 }
exit 0
