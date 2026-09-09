# Update security

## Trust root

The embedded Ed25519 public key is the update authenticity trust root. The committed key is loaded from `PrintableBook.UpdateSecurity`; the private 32-byte seed is never committed.

## Threat model

The Ed25519-signed manifest authenticates the update-critical product, version, runtime, archive filename/size/SHA256, and checksum filename/size/SHA256. It detects tampering with the manifest, archive, checksum sidecar, or a mutually replaced archive/checksum pair when the attacker does not possess the production private signing seed.

GitHub release display and transport metadata that is not present in the signed manifest remains outside the signature trust boundary. This includes the release title/name, release notes/body, publish time, release page URL, download URLs, GitHub release identifiers, and unrelated release metadata. The application must continue to treat those values as untrusted display or transport data rather than authenticated release claims.

Release title and release notes are not authentication inputs. If shown in the Desktop UI they must remain escaped/text-rendered and must never be treated as trusted HTML or executable bridge content merely because the update package itself has a valid manifest signature.

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
