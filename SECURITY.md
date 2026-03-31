# Security Policy

## Official distribution channels
- GitHub Releases of this repository.
- The update-manifest URL configured in the launcher code.

## Vulnerability reporting
If you find a vulnerability, please report it privately first.

Contact: **Discord: Landrut**.

Please include:
- reproduction steps,
- launcher version,
- logs/screenshots,
- risk/impact assessment.

## Current protections
- `AllowedRemoteHosts` whitelist for remote URLs.
- SHA-256 verification of downloaded update package.
- Digital signature verification of update manifest (`RSA-SHA256`).

## User recommendations
- Download builds only from official releases.
- Do not run builds from unknown sources.
- Verify release checksums.
