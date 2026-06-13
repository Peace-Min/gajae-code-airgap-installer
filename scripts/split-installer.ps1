[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Installer,

    [ValidateRange(1, 95)]
    [int]$PartSizeMiB = 75
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $repoRoot "release"
$partsDirectory = Join-Path $releaseDirectory "parts"
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
$partPrefix = "GajaeCode-Airgap-Setup.exe.part-"
$bufferSize = 1024 * 1024
$partSize = [int64]$PartSizeMiB * 1024 * 1024

New-Item -ItemType Directory -Path $partsDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $partsDirectory -Filter "$partPrefix*" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$input = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Installer))
try {
    $buffer = New-Object byte[] $bufferSize
    $partNumber = 0
    while ($input.Position -lt $input.Length) {
        $partNumber++
        $partPath = Join-Path $partsDirectory ("{0}{1:D3}" -f $partPrefix, $partNumber)
        $output = [System.IO.File]::Open(
            $partPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $written = [int64]0
            while ($written -lt $partSize -and $input.Position -lt $input.Length) {
                $remaining = [Math]::Min($buffer.Length, $partSize - $written)
                $read = $input.Read($buffer, 0, [int]$remaining)
                if ($read -le 0) {
                    break
                }
                $output.Write($buffer, 0, $read)
                $written += $read
            }
        }
        finally {
            $output.Dispose()
        }
        Write-Host "Created $(Split-Path -Leaf $partPath)"
    }
}
finally {
    $input.Dispose()
}

$hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  GajaeCode-Airgap-Setup.exe" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Parts: $partNumber"
Write-Host "SHA-256: $hash"

