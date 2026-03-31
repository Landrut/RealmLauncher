param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyXmlPath
)

if (!(Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

if (!(Test-Path -LiteralPath $PrivateKeyXmlPath)) {
    throw "Private key XML not found: $PrivateKeyXmlPath"
}

$raw = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
$manifest = $raw | ConvertFrom-Json

if (-not $manifest.version) { throw "Manifest is missing 'version'" }
if (-not $manifest.downloadUrl) { throw "Manifest is missing 'downloadUrl'" }

function Normalize-String([object]$v) {
    if ($null -eq $v) { return "" }
    return [string]$v
}

$version = (Normalize-String $manifest.version).Trim()
$downloadUrl = (Normalize-String $manifest.downloadUrl).Trim()
$size = ""
if ($null -ne $manifest.sizeBytes -and "$($manifest.sizeBytes)" -ne "") {
    $size = [string]$manifest.sizeBytes
}
$sha = (Normalize-String $manifest.sha256).Trim().ToLowerInvariant()
$changelog = (Normalize-String $manifest.changelog) -replace "`r`n", "`n"

$payload = ($version, $downloadUrl, $size, $sha, $changelog) -join "`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

$privateXml = Get-Content -LiteralPath $PrivateKeyXmlPath -Raw -Encoding UTF8
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
$rsa.FromXmlString($privateXml)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$signatureBytes = $rsa.SignData($bytes, $sha256)
$signatureBase64 = [Convert]::ToBase64String($signatureBytes)

$manifest.signatureAlgorithm = "RSA-SHA256"
$manifest.signatureBase64 = $signatureBase64

$outJson = $manifest | ConvertTo-Json -Depth 10
Set-Content -LiteralPath $ManifestPath -Value $outJson -Encoding UTF8

Write-Host "Manifest signed:"
Write-Host $ManifestPath
