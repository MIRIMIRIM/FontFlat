#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('checked', 'unchecked')]
    [string] $Arithmetic = 'checked',
    [ValidateRange(30, 1800)]
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmss.fffffffZ')
$output = Join-Path $root "artifacts/fontweave-gate/$stamp-$Arithmetic"
[void] [IO.Directory]::CreateDirectory($output)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$steps = [Collections.Generic.List[object]]::new()
$status = 'FAIL'
$reportPath = Join-Path $output 'tests.ctrf.json'
$head = $null
$dirty = @()
$sdk = $null

function Invoke-GateProcess([string] $Name, [string[]] $Arguments,
    [string] $Executable = 'dotnet') {
    $remaining = [int] [Math]::Floor(($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if ($remaining -le 0) { throw 'FontWeave gate exceeded its total timeout' }
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        [void] $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($remaining)
        if ($timedOut) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw "$Name could not be terminated" }
        }
        if (-not [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]] @($stdout, $stderr), 10000)) {
            throw "$Name output streams did not close"
        }
        $log = $stdout.Result + $stderr.Result
        [IO.File]::WriteAllText((Join-Path $output "$Name.log"), $log)
        Write-Host $log
        $steps.Add(@{
            name = $Name; executable = $Executable; arguments = $Arguments
            exitCode = $process.ExitCode
            timedOut = $timedOut; log = "$Name.log"
        })
        if ($timedOut) { throw "$Name exceeded the gate's total timeout" }
        if ($process.ExitCode -ne 0) { throw "$Name failed with exit $($process.ExitCode)" }
        return $log
    }
    finally { $process.Dispose() }
}

try {
    $head = (Invoke-GateProcess 'git-head' @('rev-parse', 'HEAD') 'git').Trim()
    $dirty = @((Invoke-GateProcess 'git-status' @('status', '--porcelain=v1') 'git') -split
        '\r?\n' | Where-Object { $_ })
    $sdk = (Invoke-GateProcess 'sdk' @('--version')).Trim()
    $checked = ($Arithmetic -eq 'checked').ToString().ToLowerInvariant()
    foreach ($configuration in 'Debug', 'Release') {
        $null = Invoke-GateProcess "build-$configuration" @(
            'build', 'FontWeave.slnx', '-c', $configuration, '--nologo',
            "-p:CheckForOverflowUnderflow=$checked",
            "-bl:$(Join-Path $output "build-$configuration.binlog")"
        )
    }
    # The native xUnit entry point produces a machine-readable report without MTP's
    # argument forwarding. A missing, empty, skipped or failed report cannot pass.
    $null = Invoke-GateProcess 'tests-Release' @(
        'FontWeave.Tests/bin/Release/net10.0/FontWeave.Tests.dll',
        '-failSkips', '-result-ctrf', $reportPath
    )
    $counts = (Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).results.summary
    if ($counts.tests -le 0 -or $counts.passed -ne $counts.tests) {
        throw 'The gate requires at least one test and every test must pass'
    }
    $status = 'PASS'
    Write-Host "FontWeave $Arithmetic gate: $($counts.passed) tests passed"
}
finally {
    @{
        status = $status; arithmetic = $Arithmetic; timeoutSeconds = $TimeoutSeconds
        head = $head; dirtyPaths = $dirty; sdk = $sdk
        steps = $steps.ToArray(); testReport = 'tests.ctrf.json'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
    Write-Host "Artifacts: $output"
}
