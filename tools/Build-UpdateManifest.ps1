param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [string]$OutPath = ".\\update-manifest.json",
    [string]$Changelog = ""
)

if (!(Test-Path $ZipPath)) {
    throw "ZIP не найден: $ZipPath"
}

$file = Get-Item $ZipPath
$size = $file.Length
$sha = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    version = $Version
    downloadUrl = $DownloadUrl
    sizeBytes = $size
    sha256 = $sha
    changelog = $Changelog
}

$json = $manifest | ConvertTo-Json -Depth 5
Set-Content -Path $OutPath -Value $json -Encoding UTF8

Write-Host "Готово:"
Write-Host "Version : $Version"
Write-Host "Size    : $size bytes"
Write-Host "SHA256  : $sha"
Write-Host "Manifest: $OutPath"
