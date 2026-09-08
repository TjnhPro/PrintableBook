# Update security

## Trust root

The embedded Ed25519 public key is the update authenticity trust root. The committed key is loaded from `PrintableBook.UpdateSecurity`; the private 32-byte seed is never committed.

## Threat model

Signed metadata detects a tampered manifest, archive, checksum sidecar, an archive/checksum pair replaced together, and GitHub release metadata altered without the private signing key.

## Manifest schema

Schema v1 binds product `PrintableBook`, a strict three-part version, runtime `win-x64`, and archive/checksum names, byte sizes and lowercase SHA256 hashes. Unknown JSON members are rejected. It intentionally has no release URL, GitHub ID, timestamp, download URL or arbitrary metadata.

## Signature verification order

The client bounds manifest and signature downloads, strictly decodes the 64-byte Base64 signature, verifies Ed25519 over the raw manifest bytes using the embedded key, and only then parses and validates JSON.

## Artifact verification order

The download path validates signed hashes, hashes the checksum file, parses its exact single-line format, requires its declaration to equal the signed archive hash, then hashes the archive. The ZIP must contain only the approved Desktop, Updater and Frontend payload contract.

## Private key handling

The production private seed is stored only as GitHub Actions secret `PRINTABLEBOOK_UPDATE_SIGNING_PRIVATE_KEY`. Packaging and the release tool read it from the environment, never a CLI argument or repository file. The public key is safe to commit.

## CI boundary

- PR/push workflow: no private key.
- `release-candidate` workflow on `main`: private key, artifact only.
- Tag release workflow: private key and release publication.

## Bootstrap release

`v0.1.1` has no updater. The planned first updater-enabled release is `v0.2.0`, installed manually once; later releases can use the authenticated update path.

## Key compromise

If the production private signing seed is compromised, an attacker can create manifests accepted by clients. Revoke access and design a transition release before resuming publication.

## Key rotation

Key rotation requires a separately designed transition release and is not silently automatic.

## Out of scope

PR5 does not add Windows Authenticode signing. A compromised already-running process or host OS is outside this update protocol's protection.
