#requires -Version 5.1
<#
.SYNOPSIS
    Unattended, antivirus-resilient GajaeCode installer.

.DESCRIPTION
    Assembles the offline setup executable, then launches it non-interactively
    (the installer's existing --resume-wsl mode reads the API key from the
    GJC_INTERNAL_API_KEY environment variable and runs the full install
    automatically). Progress is read from the installer's atomic state file
    (latest-install-state.json). If an attempt fails because a security product
    (AhnLab V3, Windows Defender, ...) briefly locked a freshly written file,
    the whole install is retried with exponential backoff. Because the install
    is idempotent, retrying is safe and eventually rides out the scan window.

    This wrapper needs no rebuild of the installer executable. The interactive
    GUI flow (install.cmd) is unchanged and still available.
#>
[CmdletBinding()]
param(
    [int]$MaxAttempts = 6,
    [int]$AttemptTimeoutSeconds = 1800
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

# Self-elevate. Admin rights let the wrapper launch the installer and, between
# retries, close the elevated installer process. The API key is never passed as
# an argument (it is read after elevation), so nothing secret crosses the UAC
# boundary on the command line.
if (-not (Test-IsAdministrator)) {
    Write-Host "관리자 권한으로 다시 시작합니다..."
    $relaunchArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", $PSCommandPath,
        "-MaxAttempts", $MaxAttempts,
        "-AttemptTimeoutSeconds", $AttemptTimeoutSeconds
    )
    Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $relaunchArgs -Verb RunAs
    return
}

# ---- Elevated from here ----

$repoRoot       = Split-Path -Parent $PSScriptRoot
$assembleScript = Join-Path $PSScriptRoot "assemble-installer.ps1"
$exePath        = Join-Path $repoRoot "release\assembled\GajaeCode-Airgap-Setup.exe"
$localAppData   = [Environment]::GetFolderPath("LocalApplicationData")
$rootDir        = Join-Path $localAppData "GajaeCode"
$binDir         = Join-Path $rootDir "bin"
$tempBinary     = Join-Path $binDir "gjc.exe.new"
$stateFile      = Join-Path $rootDir "diagnostics\latest-install-state.json"
$apiKeyEnv      = "GJC_INTERNAL_API_KEY"
$installerName  = "GajaeCode-Airgap-Setup"

$transientPattern =
    'used by another process|being used|사용 중|액세스할 수 없|sharing violation|0x80070020|0x80070021'

function Stop-Installer {
    Get-Process -Name $installerName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Read-State {
    if (-not (Test-Path -LiteralPath $stateFile)) { return $null }
    try {
        return (Get-Content -LiteralPath $stateFile -Raw -ErrorAction Stop | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

try {
    # 1) Best-effort Windows Defender exclusion. Managed third-party AV (AhnLab,
    #    etc.) cannot be configured here; the retry loop below covers those.
    try {
        if (Get-Command Add-MpPreference -ErrorAction SilentlyContinue) {
            Add-MpPreference -ExclusionPath $rootDir -ErrorAction Stop
            Add-MpPreference -ExclusionProcess "gjc.exe" -ErrorAction Stop
            Write-Host "Windows Defender 예외 등록(해당 시) 완료."
        }
    }
    catch {
        Write-Host "Defender 예외 등록 생략(관리형이거나 미사용)."
    }

    # 2) Read the API key once. Never logged, never passed as a process argument.
    $secure = Read-Host -AsSecureString "서버에서 발급받은 API Key를 입력하세요"
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $plainKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    if ([string]::IsNullOrWhiteSpace($plainKey)) {
        throw "API Key가 비어 있습니다."
    }

    # Persist for the installer (contract: key lives only in this user env var)
    # and set it in this process so the launched installer inherits it.
    [Environment]::SetEnvironmentVariable($apiKeyEnv, $plainKey, "User")
    [Environment]::SetEnvironmentVariable($apiKeyEnv, $plainKey, "Process")

    # 3) Assemble + SHA-256 verify the installer exe (does not launch it).
    Write-Host "설치기 조립 및 무결성 검증 중..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $assembleScript
    if ($LASTEXITCODE -ne 0) {
        throw "설치기 조립에 실패했습니다."
    }
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "조립된 설치기를 찾을 수 없습니다: $exePath"
    }

    # 4) Retry loop: launch the installer unattended and watch the state file.
    $succeeded = $false
    $lastError = ""
    $delayMs = 1000

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        Write-Host ""
        Write-Host "=== 설치 시도 $attempt/$MaxAttempts ==="

        Stop-Installer
        Remove-Item -LiteralPath $tempBinary -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue

        # --resume-wsl makes the installer auto-fill the API key from the
        # environment and run the full install with no clicks.
        Start-Process -FilePath $exePath -ArgumentList "--resume-wsl" | Out-Null

        $deadline = (Get-Date).AddSeconds($AttemptTimeoutSeconds)
        $outcome = "timeout"
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 700
            $state = Read-State
            if ($null -ne $state) {
                $status = "$($state.status)"
                $stage  = "$($state.stage)"
                if ($status -eq "succeeded") { $outcome = "succeeded"; break }
                if ($stage  -eq "restart-required") { $outcome = "restart-required"; break }
                if ($status -eq "failed") { $lastError = "$($state.error)"; $outcome = "failed"; break }
            }
            # Installer gone without a terminal state -> count this attempt as failed.
            if (-not (Get-Process -Name $installerName -ErrorAction SilentlyContinue)) {
                $state = Read-State
                if ($null -ne $state -and "$($state.status)" -eq "succeeded") { $outcome = "succeeded" }
                elseif ($null -ne $state -and "$($state.stage)" -eq "restart-required") { $outcome = "restart-required" }
                else {
                    if ($null -ne $state) { $lastError = "$($state.error)" }
                    $outcome = "exited"
                }
                break
            }
        }

        Stop-Installer

        if ($outcome -eq "succeeded") { $succeeded = $true; break }
        if ($outcome -eq "restart-required") {
            Write-Host "기본 설치 완료. WSL2 적용을 위해 재부팅이 필요합니다."
            $succeeded = $true
            break
        }

        # Retry only on a transient lock or an installer that vanished mid-run.
        $isTransient = ($outcome -eq "exited") -or ($outcome -eq "timeout") -or
                       ($lastError -match $transientPattern)
        if (-not $isTransient) {
            Write-Host "재시도로 해결되지 않는 오류입니다:"
            Write-Host "  $lastError"
            break
        }

        if ($attempt -lt $MaxAttempts) {
            Write-Host "일시적 파일 잠금/중단으로 보입니다. $([int]($delayMs/1000))초 후 재시도합니다..."
            Start-Sleep -Milliseconds $delayMs
            $delayMs = [Math]::Min($delayMs * 2, 15000)
        }
    }

    $plainKey = $null

    Write-Host ""
    if ($succeeded) {
        Write-Host "가재코드 자동 설치가 완료되었습니다. 새 터미널에서 gjc를 실행하세요."
        $exitCode = 0
    }
    else {
        Write-Host "자동 설치에 실패했습니다."
        if ($lastError) { Write-Host "마지막 오류: $lastError" }
        Write-Host "진단: $stateFile"
        if ($lastError -match $transientPattern -or -not $lastError) {
            Write-Host ""
            Write-Host "파일 잠금이 끝까지 풀리지 않았습니다. 안랩이 gjc.exe를 '격리/차단'하는 경우"
            Write-Host "재시도로는 해결되지 않습니다. 안랩 격리소를 확인하고, IT/보안팀에 아래 경로를"
            Write-Host "신뢰(검사 예외)로 등록 요청하세요(실시간/행위기반 모두):"
            Write-Host "  폴더 : $rootDir"
            Write-Host "  파일 : gjc.exe, $installerName.exe"
        }
        $exitCode = 1
    }
}
catch {
    Write-Host ""
    Write-Host "오류: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    $plainKey = $null
    [GC]::Collect()
}

Write-Host ""
Read-Host "엔터를 누르면 창을 닫습니다"
exit $exitCode
