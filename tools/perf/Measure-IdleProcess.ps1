<#
.SYNOPSIS
Measures the resource footprint of a running IceCrow process (or any .NET process)
over a fixed window: average CPU, working set, private bytes, threads, handles,
and (via dotnet-counters) GC heap size, allocation rate, Gen0/1/2 counts and
exception rate.

.DESCRIPTION
Developer-only diagnostics for docs/performance.md. Starts the executable when
-ExecutablePath is given, waits -WarmupSeconds, samples for -DurationSeconds,
writes a JSON summary plus the raw dotnet-counters CSV next to it, and stops
the process it started. Requires the official dotnet-counters global tool
(dotnet tool install -g dotnet-counters). Numbers are diagnostic evidence for
one machine, not CI thresholds.

.EXAMPLE
.\tools\perf\Measure-IdleProcess.ps1 -ExecutablePath .\src\IceCrow.App\bin\Release\net10.0-windows\IceCrow.App.exe -Label baseline-closed-client -OutputDirectory .\artifacts\perf
#>
[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [int]$ProcessId,
    [string]$Label = "measurement",
    [string]$OutputDirectory = ".\artifacts\perf",
    [int]$WarmupSeconds = 10,
    [int]$DurationSeconds = 30
)

$ErrorActionPreference = "Stop"
if (-not $ExecutablePath -and -not $ProcessId) {
    throw "Pass -ExecutablePath to start a process or -ProcessId to attach to one."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$started = $null
if ($ExecutablePath) {
    $started = Start-Process -FilePath (Resolve-Path $ExecutablePath) -PassThru
    $ProcessId = $started.Id
}

try {
    Start-Sleep -Seconds $WarmupSeconds
    $process = Get-Process -Id $ProcessId
    $cores = [Environment]::ProcessorCount
    $countersCsv = Join-Path $OutputDirectory "$Label.counters.csv"
    $counters = Start-Process -FilePath "dotnet-counters" -ArgumentList @(
        "collect", "-p", $ProcessId, "--refresh-interval", "1", "--format", "csv",
        "-o", $countersCsv, "--duration", ([TimeSpan]::FromSeconds($DurationSeconds).ToString("dd\:hh\:mm\:ss")), "--counters", "System.Runtime"
    ) -PassThru -WindowStyle Hidden

    $samples = @()
    $cpuStart = (Get-Process -Id $ProcessId).TotalProcessorTime
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $DurationSeconds; $i++) {
        Start-Sleep -Seconds 1
        $p = Get-Process -Id $ProcessId
        $samples += [pscustomobject]@{
            WorkingSetMiB = [math]::Round($p.WorkingSet64 / 1MB, 1)
            PrivateMiB    = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
            Threads       = $p.Threads.Count
            Handles       = $p.HandleCount
        }
    }
    $clock.Stop()
    $cpuEnd = (Get-Process -Id $ProcessId).TotalProcessorTime
    $cpuPercent = [math]::Round((($cpuEnd - $cpuStart).TotalSeconds / $clock.Elapsed.TotalSeconds / $cores) * 100, 3)
    if (-not $counters.HasExited) { $counters.WaitForExit(15000) | Out-Null }

    $gc = $null
    if (Test-Path $countersCsv) {
        $rows = Import-Csv $countersCsv
        function Stat($pattern, $agg) {
            $values = @($rows | Where-Object { $_."Counter Name" -like $pattern } | ForEach-Object { [double]$_."Mean/Increment" })
            if ($values.Count -eq 0) { return $null }
            switch ($agg) {
                "avg" { return [math]::Round(($values | Measure-Object -Average).Average, 3) }
                "max" { return [math]::Round(($values | Measure-Object -Maximum).Maximum, 3) }
                "sum" { return [math]::Round(($values | Measure-Object -Sum).Sum, 3) }
                "last" { return $values[-1] }
            }
        }
        function HeapMiB($agg) {
            $byTimestamp = $rows | Where-Object { $_."Counter Name" -like "dotnet.gc.last_collection.heap.size*" } |
                Group-Object Timestamp | ForEach-Object { ($_.Group | ForEach-Object { [double]$_."Mean/Increment" } | Measure-Object -Sum).Sum / 1MB }
            if (-not $byTimestamp) { return $null }
            switch ($agg) {
                "avg" { return [math]::Round(($byTimestamp | Measure-Object -Average).Average, 2) }
                "max" { return [math]::Round(($byTimestamp | Measure-Object -Maximum).Maximum, 2) }
            }
        }
        $gc = [pscustomobject]@{
            GcHeapSizeMiBAvg        = HeapMiB "avg"
            GcHeapSizeMiBMax        = HeapMiB "max"
            GcCommittedMiBMax       = [math]::Round(((Stat "dotnet.gc.last_collection.memory.committed_size*" "max") / 1MB), 2)
            AllocationRateKiBPerSec = [math]::Round(((Stat "dotnet.gc.heap.total_allocated*" "avg") / 1KB), 2)
            Gen0CollectionsTotal    = Stat "dotnet.gc.collections*gen0*" "sum"
            Gen1CollectionsTotal    = Stat "dotnet.gc.collections*gen1*" "sum"
            Gen2CollectionsTotal    = Stat "dotnet.gc.collections*gen2*" "sum"
            ExceptionsPerSecAvg     = Stat "dotnet.exceptions*" "avg"
            ExceptionsTotal         = Stat "dotnet.exceptions*" "sum"
            CpuUserSecPerSecAvg     = Stat "dotnet.process.cpu.time*user*" "avg"
            CpuSystemSecPerSecAvg   = Stat "dotnet.process.cpu.time*system*" "avg"
            TimerCountAvg           = Stat "dotnet.timer.count*" "avg"
            LockContentionsTotal    = Stat "dotnet.monitor.lock_contentions*" "sum"
            AssemblyCountLast       = Stat "dotnet.assembly.count*" "last"
        }
    }

    $summary = [pscustomobject]@{
        Label            = $Label
        MeasuredAt       = (Get-Date).ToUniversalTime().ToString("o")
        Machine          = @{ Cores = $cores; OS = [Environment]::OSVersion.VersionString }
        Process          = @{ Name = $process.ProcessName; Id = $ProcessId; Path = $process.Path }
        WindowSeconds    = [math]::Round($clock.Elapsed.TotalSeconds, 1)
        AverageCpuPercent = $cpuPercent
        WorkingSetMiB    = @{ Avg = [math]::Round(($samples.WorkingSetMiB | Measure-Object -Average).Average, 1); Max = ($samples.WorkingSetMiB | Measure-Object -Maximum).Maximum }
        PrivateMiB       = @{ Avg = [math]::Round(($samples.PrivateMiB | Measure-Object -Average).Average, 1); Max = ($samples.PrivateMiB | Measure-Object -Maximum).Maximum }
        Threads          = @{ Avg = [math]::Round(($samples.Threads | Measure-Object -Average).Average, 1); Max = ($samples.Threads | Measure-Object -Maximum).Maximum }
        Handles          = @{ Avg = [math]::Round(($samples.Handles | Measure-Object -Average).Average, 1); Max = ($samples.Handles | Measure-Object -Maximum).Maximum }
        Runtime          = $gc
        LoadedIceCrowModules = @($process.Modules | Where-Object { $_.ModuleName -like "IceCrow*" } | ForEach-Object { $_.ModuleName } | Sort-Object)
    }
    $summaryPath = Join-Path $OutputDirectory "$Label.json"
    $summary | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 $summaryPath
    Write-Output ($summary | ConvertTo-Json -Depth 5)
}
finally {
    if ($started -and -not $started.HasExited) {
        Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue
    }
}
