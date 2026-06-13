[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$GjcBinary,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$GjcLinuxBinary,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$WslRootfs,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$WslKernelMsi,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^https?://")]
    [string]$GatewayBaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ModelId,

    [string]$ProviderId = "internal-anthropic",

    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "artifacts"
}

$projectPath = Join-Path $PSScriptRoot "GajaeCode.AirgapInstaller.csproj"
$payloadDirectory = Join-Path $PSScriptRoot "payload"
$payloadPath = Join-Path $payloadDirectory "gjc-windows-x64.exe"
$hashPath = "$payloadPath.sha256"
$linuxPayloadPath = Join-Path $payloadDirectory "gjc-linux-x64"
$linuxHashPath = "$linuxPayloadPath.sha256"
$rootfsPayloadPath = Join-Path $payloadDirectory "gajaecode-wsl-rootfs.tar"
$rootfsHashPath = "$rootfsPayloadPath.sha256"
$kernelPayloadPath = Join-Path $payloadDirectory "wsl_update_x64.msi"
$kernelHashPath = "$kernelPayloadPath.sha256"
$deploymentPath = Join-Path $payloadDirectory "deployment.json"
$publishDirectory = Join-Path $OutputDirectory "publish"

New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Copy-Item -LiteralPath $GjcBinary -Destination $payloadPath -Force
$hash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  gjc-windows-x64.exe" | Set-Content -LiteralPath $hashPath -Encoding ascii
Copy-Item -LiteralPath $GjcLinuxBinary -Destination $linuxPayloadPath -Force
$linuxHash = (Get-FileHash -LiteralPath $linuxPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$linuxHash  gjc-linux-x64" | Set-Content -LiteralPath $linuxHashPath -Encoding ascii
Copy-Item -LiteralPath $WslRootfs -Destination $rootfsPayloadPath -Force
$rootfsHash = (Get-FileHash -LiteralPath $rootfsPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$rootfsHash  gajaecode-wsl-rootfs.tar" | Set-Content -LiteralPath $rootfsHashPath -Encoding ascii
Copy-Item -LiteralPath $WslKernelMsi -Destination $kernelPayloadPath -Force
$kernelHash = (Get-FileHash -LiteralPath $kernelPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$kernelHash  wsl_update_x64.msi" | Set-Content -LiteralPath $kernelHashPath -Encoding ascii
@{
    gatewayBaseUrl = $GatewayBaseUrl.TrimEnd("/")
    modelId = $ModelId
    providerId = $ProviderId
} | ConvertTo-Json | Set-Content -LiteralPath $deploymentPath -Encoding utf8

try {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $publishedExe = Join-Path $publishDirectory "GajaeCode-Airgap-Setup.exe"
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw "Published installer was not found: $publishedExe"
    }

    $finalExe = Join-Path $OutputDirectory "GajaeCode-Airgap-Setup.exe"
    Copy-Item -LiteralPath $publishedExe -Destination $finalExe -Force
    $installerHash = (Get-FileHash -LiteralPath $finalExe -Algorithm SHA256).Hash.ToLowerInvariant()
    "$installerHash  GajaeCode-Airgap-Setup.exe" |
        Set-Content -LiteralPath (Join-Path $OutputDirectory "SHA256SUMS.txt") -Encoding ascii

    Write-Host "Installer: $finalExe"
    Write-Host "SHA-256 : $installerHash"
}
finally {
    Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $linuxPayloadPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $linuxHashPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $rootfsPayloadPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $rootfsHashPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $kernelPayloadPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $kernelHashPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $deploymentPath -Force -ErrorAction SilentlyContinue
}
