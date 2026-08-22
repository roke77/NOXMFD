# CI smoke check — runs the three checks this repo already relies on by hand, in one command.
# See docs/ci-smoke-check.md. Warnings are tracked separately (docs/build-warning-cleanup.md) and
# do not fail this check; only a build error or a failing test/route does.
#
# Usage: pwsh tools/ci-check.ps1   (or: powershell tools/ci-check.ps1)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail($msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

# 1. dotnet build -c Release — fail on any error.
Write-Host "== dotnet build -c Release ==" -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed (exit $LASTEXITCODE)" }

# 2. Every src/web/**/*.test.js and tools/*.test.js via node.
Write-Host "== node *.test.js ==" -ForegroundColor Cyan
$testFiles = @(Get-ChildItem -Path "src/web" -Filter "*.test.js" -Recurse -File) +
             @(Get-ChildItem -Path "tools" -Filter "*.test.js" -File)
foreach ($f in $testFiles) {
    $rel = Resolve-Path -Relative $f.FullName
    Write-Host "  node $rel"
    node $f.FullName
    if ($LASTEXITCODE -ne 0) { Fail "$rel failed (exit $LASTEXITCODE)" }
}
Write-Host "  $($testFiles.Count) test file(s) passed"

# 3. serve_web.py smoke — start on a scratch port, hit a few page routes, assert 200, stop it.
Write-Host "== serve_web.py smoke ==" -ForegroundColor Cyan
$scratchPort = 8799
$proc = Start-Process -FilePath "python" `
    -ArgumentList @("tools/serve_web.py", "--port", $scratchPort) `
    -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\ci-check-serve.out" `
    -RedirectStandardError "$env:TEMP\ci-check-serve.err"

try {
    $base = "http://127.0.0.1:$scratchPort"
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 1 | Out-Null
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { Fail "serve_web.py never came up on port $scratchPort" }

    # Note: /map is the captured map IMAGE endpoint (404s with no preview/captures/ populated),
    # not the MAP page — use /map-view?bare, which is the actual page route (docs/ci-smoke-check.md).
    $routes = @("/", "/afm", "/map-view?bare", "/hud")
    foreach ($r in $routes) {
        try {
            $resp = Invoke-WebRequest -Uri "$base$r" -UseBasicParsing -TimeoutSec 5
            $code = $resp.StatusCode
        } catch {
            $code = $_.Exception.Response.StatusCode.value__
        }
        if ($code -ne 200) { Fail "$r returned $code, expected 200" }
        Write-Host "  $r -> $code"
    }
} finally {
    if ($proc -and -not $proc.HasExited) {
        # /T: the python.exe launcher shim spawns a child python3.12.exe that actually binds the
        # port — Stop-Process on just $proc.Id leaves that child running the server forever.
        taskkill /PID $proc.Id /T /F | Out-Null
    }
}

Write-Host "All checks passed." -ForegroundColor Green
