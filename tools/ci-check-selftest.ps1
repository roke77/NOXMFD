# Validates that tools/ci-check.ps1 fails for each class of breakage it is supposed to catch.
#
# This intentionally mutates temporary copies of the repository, never the active checkout. It still
# needs the same local GameDir.props/game DLL setup as ci-check.ps1, because the first check is the
# real plugin build.
#
# Usage: powershell tools/ci-check-selftest.ps1
#        powershell tools/ci-check-selftest.ps1 -KeepTemp

param(
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tempBase = Join-Path ([System.IO.Path]::GetTempPath()) "noxmfd-ci-check-selftest"
$runRoot = Join-Path $tempBase ([System.Guid]::NewGuid().ToString("N"))

function Fail($msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

function Copy-Repo($name) {
    $dest = Join-Path $runRoot $name
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    & robocopy $root $dest /MIR /XD .git bin obj .vs /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { Fail "robocopy failed for $name (exit $LASTEXITCODE)" }
    return $dest
}

function Run-CiCheck($caseDir, $name) {
    $log = Join-Path $caseDir "ci-check-selftest-$name.log"
    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $caseDir "tools\ci-check.ps1") 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }

    $text = ($output | ForEach-Object { $_.ToString() }) -join "`n"
    $text | Set-Content -Encoding UTF8 $log
    return @{ ExitCode = $exitCode; Log = $log; Text = $text }
}

function Expect-Failure($name, [scriptblock]$breakRepo, $expectedText) {
    Write-Host "== $name ==" -ForegroundColor Cyan
    $caseDir = Copy-Repo $name
    & $breakRepo $caseDir

    $result = Run-CiCheck $caseDir $name
    if ($result.ExitCode -eq 0) {
        Write-Host "ci-check unexpectedly passed. Log: $($result.Log)"
        Fail "$name did not fail"
    }
    if ($result.Text -notmatch [regex]::Escape($expectedText)) {
        Write-Host "ci-check failed, but not with the expected marker '$expectedText'. Log: $($result.Log)"
        Fail "$name failed for the wrong reason"
    }

    Write-Host "  caught expected failure: $expectedText"
}

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

try {
    if (-not (Test-Path (Join-Path $root "GameDir.props"))) {
        Fail "GameDir.props is required so temporary copies can build against the local game DLLs"
    }

    Expect-Failure "broken-build" {
        param($caseDir)
        Add-Content -Path (Join-Path $caseDir "src\plugin\Plugin.cs") -Value "`nthis is not valid csharp"
    } "dotnet build failed"

    Expect-Failure "failing-js-test" {
        param($caseDir)
        Add-Content -Path (Join-Path $caseDir "tools\map-cursor.test.js") -Value "`nprocess.exit(42);"
    } "tools\map-cursor.test.js failed"

    Expect-Failure "failing-dotnet-test" {
        param($caseDir)
        $testFile = Join-Path $caseDir "tools\tests\CiCheckIntentionalFailureTests.cs"
        @"
using Xunit;

public class CiCheckIntentionalFailureTests
{
    [Fact]
    public void IntentionalFailure()
    {
        Assert.True(false, "ci-check self-test intentional failure");
    }
}
"@ | Set-Content -Encoding UTF8 $testFile
    } "dotnet test failed"

    Expect-Failure "broken-route-smoke" {
        param($caseDir)
        Rename-Item -LiteralPath (Join-Path $caseDir "src\web\pages\hud\hud.html") -NewName "hud.html.broken"
    } "/hud returned 404"

    Write-Host "All ci-check self-tests passed." -ForegroundColor Green
} finally {
    if ($KeepTemp) {
        Write-Host "Kept temp workspace: $runRoot"
    } elseif (Test-Path $runRoot) {
        $resolvedRunRoot = (Resolve-Path $runRoot).Path
        $resolvedTempBase = (Resolve-Path $tempBase).Path
        if ($resolvedRunRoot.StartsWith($resolvedTempBase, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        } else {
            Write-Host "Skipped temp cleanup because resolved path was outside expected temp base: $resolvedRunRoot"
        }
    }
}
