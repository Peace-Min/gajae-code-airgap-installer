[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

function Test-SharingOrLockViolation {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.IO.IOException]) {
            $windowsError = $exception.HResult -band 0xFFFF
            return $windowsError -eq 32 -or $windowsError -eq 33
        }
        $exception = $exception.InnerException
    }
    return $false
}

function Invoke-WithLockRetry {
    param([Parameter(Mandatory = $true)][scriptblock]$Operation)

    $delayMs = 100
    for ($attempt = 1; ; $attempt++) {
        try {
            return (& $Operation)
        }
        catch {
            if ($attempt -ge 10 -or -not (Test-SharingOrLockViolation $_)) {
                throw
            }
            Start-Sleep -Milliseconds $delayMs
            $delayMs = [Math]::Min($delayMs * 2, 1500)
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $repoRoot "release"
$partsDirectory = Join-Path $releaseDirectory "parts"
$outputDirectory = Join-Path $releaseDirectory "assembled"
$outputPath = Join-Path $outputDirectory "GajaeCode-Airgap-Setup.exe"
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"

if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Checksum file not found: $checksumPath"
}

$parts = @(Get-ChildItem -LiteralPath $partsDirectory -Filter "GajaeCode-Airgap-Setup.exe.part-*" -File |
    Sort-Object Name)
if ($parts.Count -eq 0) {
    throw "Installer parts were not found: $partsDirectory"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryPath = "$outputPath.new"
$assemblyMutex = New-Object System.Threading.Mutex($false, "Local\GajaeCodeAirgapAssembler")
$mutexAcquired = $false

try {
    try {
        $mutexAcquired = $assemblyMutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw "설치 파일 조립이 이미 다른 창에서 실행 중입니다."
    }

    $output = [System.IO.File]::Open(
        $temporaryPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        foreach ($part in $parts) {
            Write-Host "Joining $($part.Name)"
            $input = [System.IO.File]::OpenRead($part.FullName)
            try {
                $input.CopyTo($output)
            }
            finally {
                $input.Dispose()
            }
        }
    }
    finally {
        $output.Dispose()
    }

    $expectedLine = Get-Content -LiteralPath $checksumPath |
        Where-Object { $_ -match "GajaeCode-Airgap-Setup\.exe$" } |
        Select-Object -First 1
    if (-not $expectedLine) {
        throw "Installer checksum entry is missing from $checksumPath"
    }
    $expectedHash = ($expectedLine -split "\s+")[0].Trim().ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Installer SHA-256 mismatch. Expected $expectedHash, actual $actualHash"
    }

    Invoke-WithLockRetry {
        Move-Item -LiteralPath $temporaryPath -Destination $outputPath -Force
    }
    Write-Host "Installer restored: $outputPath"
    Write-Host "SHA-256 verified: $actualHash"

    if ($Run) {
        Write-Host "Starting installer with administrator privileges..."
        Start-Process -FilePath $outputPath -Verb RunAs
    }
}
finally {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    if ($mutexAcquired) {
        $assemblyMutex.ReleaseMutex()
    }
    $assemblyMutex.Dispose()
}
