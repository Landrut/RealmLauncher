param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [string]$OutPath = ".\update-manifest.json",
    [string]$Changelog = ""
)

if (!(Test-Path -LiteralPath $ZipPath)) {
    throw "ZIP not found: $ZipPath"
}

$file = Get-Item -LiteralPath $ZipPath
$size = $file.Length
$sha = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    version = $Version
    downloadUrl = $DownloadUrl
    sizeBytes = $size
    sha256 = $sha
    changelog = $Changelog
    signatureAlgorithm = "RSA-SHA256"
    signatureBase64 = ""
}

$json = $manifest | ConvertTo-Json -Depth 5
Set-Content -LiteralPath $OutPath -Value $json -Encoding UTF8

Write-Host "Done:"
Write-Host "Version : $Version"
Write-Host "Size    : $size bytes"
Write-Host "SHA256  : $sha"
Write-Host "Manifest: $OutPath"
