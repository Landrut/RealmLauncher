param(
    [string]$OutDir = ".\keys"
)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$privatePath = Join-Path $OutDir "update-sign-private.xml"
$publicPath = Join-Path $OutDir "update-sign-public.xml"

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(3072)
$rsa.PersistKeyInCsp = $false

$privateXml = $rsa.ToXmlString($true)
$publicXml = $rsa.ToXmlString($false)

Set-Content -LiteralPath $privatePath -Value $privateXml -Encoding UTF8
Set-Content -LiteralPath $publicPath -Value $publicXml -Encoding UTF8

Write-Host "Done:"
Write-Host "Private key: $privatePath"
Write-Host "Public key : $publicPath"
Write-Host ""
Write-Host "Вставь содержимое public XML в:"
Write-Host "AppRuntimeConfig.UpdateManifestSignaturePublicKeyXml"
