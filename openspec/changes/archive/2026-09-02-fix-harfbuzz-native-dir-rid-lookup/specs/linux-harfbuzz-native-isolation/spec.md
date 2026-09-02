## ADDED Requirements

### Requirement: Native-asset directory resolution matches the actual on-disk RID naming
Scribe SHALL locate the bundled `libHarfBuzzSharp.so`'s native-asset directory using the
same RID-naming convention `gui`'s own native-library loader uses for the identical asset
layout, rather than relying on `RuntimeInformation.RuntimeIdentifier`, which is not
guaranteed to match the flat RID the asset folder is actually named on disk.

#### Scenario: Standard x64 Linux/glibc client
- **WHEN** Scribe resolves the native-asset directory on a Linux/glibc x64 client
- **THEN** it looks under `native/linux-x64/native/`, matching the folder `gui`'s own
  loader resolves for the same assembly

#### Scenario: ARM64 Linux client
- **WHEN** Scribe resolves the native-asset directory on a Linux ARM64 client
- **THEN** it looks under `native/linux-arm64/native/`, matching the folder `gui`'s own
  loader resolves for the same assembly

#### Scenario: Directory still not found after correcting the RID mapping
- **WHEN** the corrected native-asset directory still doesn't exist or doesn't contain
  `libHarfBuzzSharp.so`
- **THEN** Scribe falls back exactly as it does today — logging a warning and letting
  `gui`'s original, unisolated loader run, never crashing or regressing below the unpatched
  state
