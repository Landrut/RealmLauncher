# Release Security Guide

## A) Free protection (recommended baseline)

Use signed update manifests (`RSA-SHA256`) and package hash verification.

### 1) Generate update-signing keys (one time)
```powershell
.\tools\Generate-UpdateSigningKey.ps1 -OutDir .\keys
```

- Keep `keys/update-sign-private.xml` secret (never commit).
- Put `keys/update-sign-public.xml` into:
  `AppRuntimeConfig.UpdateManifestSignaturePublicKeyXml`.

### 2) Build manifest for each release
```powershell
.\tools\Build-UpdateManifest.ps1 `
  -Version "0.0.2.0" `
  -ZipPath ".\RealmLauncher-v0.0.2.0.zip" `
  -DownloadUrl "https://github.com/<owner>/<repo>/releases/download/v0.0.2.0/RealmLauncher-v0.0.2.0.zip" `
  -OutPath ".\update-manifest.json" `
  -Changelog "Fixes and improvements"
```

### 3) Sign manifest
```powershell
.\tools\Sign-UpdateManifest.ps1 `
  -ManifestPath ".\update-manifest.json" `
  -PrivateKeyXmlPath ".\keys\update-sign-private.xml"
```

### 4) Enable strict verification in launcher
Set in `AppRuntimeConfig`:
- `RequireSignedUpdateManifest = true`

### 5) Publish
Upload signed `update-manifest.json` and release zip via HTTPS.

## B) Optional: Authenticode signing for `.exe` (paid cert)
