[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

# A single shared mutex prevents two launches (e.g. install.cmd started in two
# windows, or Enter pressed twice) from assembling the installer at the same
# time and colliding on the output file.
$assemblyMutex = New-Object System.Threading.Mutex($false, "Local\GajaeCodeAirgapAssembler")
$mutexAcquired = $false

function Test-TransientIoError {
    param($ErrorRecord)

    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.IO.FileNotFoundException] -or
            $exception -is [System.IO.DirectoryNotFoundException]) {
            return $false
        }
        if ($exception -is [System.IO.IOException] -or
            $exception -is [System.UnauthorizedAccessException]) {
            return $true
        }
        $exception = $exception.InnerException
    }
    return $false
}

# Retries a file-system operation while a security product (AhnLab V3, Windows
# Defender, etc.) briefly locks a freshly written file. Non-lock errors are
# rethrown immediately so real failures still surface.
function Invoke-WithFileRetry {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Operation,
        [int]$MaxAttempts = 12
    )

    $delayMs = 100
    for ($attempt = 1; ; $attempt++) {
        try {
            return (& $Operation)
        }
        catch {
            if (-not (Test-TransientIoError $_) -or $attempt -ge $MaxAttempts) {
                throw
            }
            Start-Sleep -Milliseconds $delayMs
            $delayMs = [Math]::Min($delayMs * 2, 2000)
        }
    }
}

try {
    try {
        $mutexAcquired = $assemblyMutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        # A previous run exited without releasing the mutex; we now own it.
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw "설치 조립이 이미 다른 창에서 실행 중입니다. 기존 창이 끝난 뒤 창 하나로만 다시 실행하십시오."
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

    try {
        # FileShare.Read lets an antivirus scan read the file while it is being
        # assembled; the create itself is retried in case a stale lock remains.
        $output = Invoke-WithFileRetry {
            [System.IO.File]::Open(
                $temporaryPath,
                [System.IO.FileMode]::Create,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::Read)
        }
        try {
            foreach ($part in $parts) {
                Write-Host "Joining $($part.Name)"
                $reader = [System.IO.File]::OpenRead($part.FullName)
                try {
                    $reader.CopyTo($output)
                }
                finally {
                    $reader.Dispose()
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

        # The destination can be briefly locked by a real-time scan of the freshly
        # assembled executable, so the replace is retried instead of failing.
        Invoke-WithFileRetry {
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
    }
}
finally {
    if ($mutexAcquired) {
        $assemblyMutex.ReleaseMutex()
    }
    $assemblyMutex.Dispose()
}
