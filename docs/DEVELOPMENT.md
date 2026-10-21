# Development and validation

This is a native C# / WPF application using Windows Bluetooth RFCOMM APIs. Build on Windows with the .NET 10 SDK. The portable release includes the runtime; source development requires the SDK.

## Checks

```powershell
dotnet build NothingOnWindows.csproj -c Release
dotnet run --project tests/ProtocolChecks.csproj -c Release
```

The current release passes 390 offline core checks and 353 WPF/window/tray checks. Core checks cover framing and CRC, request/notification correlation, EQ serialization/headroom, invalid input, batteries/charging, firmware gates, all 11 identities, model-specific reads and control mappings, and read-only synchronization.

UI checks cover all six views for all 11 profiles at default and minimum size, embedded photos, profile selection, eight resize edges, the portrait ratio, tray lifecycle, single-instance activation, differing preset graphs, deferred state updates, advanced draft preservation, and rejection of unsupported writes. They inject simulated packets and send no headphone writes.

Run UI checks or capture disconnected screenshots with a built application:

```powershell
artifacts/releases/v0.1.4/NothingOnWindows/NothingOnWindows.exe --ui-smoke artifacts/ui-smoke.json
artifacts/releases/v0.1.4/NothingOnWindows/NothingOnWindows.exe --snapshot artifacts/preview.png
```

Snapshot mode exports Sound, Equalizer, Advanced, Controls, Explore, and Connection images. `--live` requests read-only connected-device data; `--catalog` requests the online catalog. `--auto` exercises the normal automatic discovery/connection path, reads settings only, and writes a small connection-status report beside the screenshots. The README uses disconnected previews.

## Hardware validation

Headphone (a), firmware `1.0.1.51`, was tested on October 19, 2026. Tests read battery, firmware, ANC, spatial audio, bass, EQ presets, advanced EQ data/state, low lag, and gestures. Reapplying existing settings produced successful acknowledgements/readbacks. Low lag was briefly changed and restored.

Simple custom EQ was tested with `+1 / -0.5 / +0.5 dB` and `-1 / +0.5 / -0.5 dB` bass/mid/treble profiles. Both read back exactly. The original custom slot, Balanced preset, and advanced EQ state were restored; all nine checked raw setting payloads matched the starting snapshot. Private reports and backups are excluded from public source and packages.

Only Headphone (a) has this hardware verification. Simulated coverage is not proof of compatibility for the other ten profiles. Physical-button synchronization needs further real-device validation.

## Diagnostic commands

- `--probe <report.json>` discovers exactly one recognized paired device and reads its state. It sends no setting writes.
- `--verify <report.json>` is an explicit Headphone (a)-only write/restore test. It reapplies current settings and briefly changes low lag, with restoration in a `finally` block.
- `--verify-simple <report.json>` is an explicit Headphone (a)-only EQ write/restore test. It creates a backup, applies two modest profiles, restores the custom slot/preset/advanced state, and verifies nine original raw responses.

Write tests never run during normal startup, packaging, screenshot capture, or offline UI checks. Review reports before sharing; they can contain device names, settings, and raw responses.

## Release packaging

```powershell
powershell -ExecutionPolicy Bypass -File tools/PublishRelease.ps1 -Version 0.1.4
```

This produces `artifacts/releases/v0.1.4/` with a self-contained Windows x64 application ZIP, matching source ZIP, and `SHA256SUMS.txt`. Packaging refuses to overwrite an existing release folder. Choose a new version or a fresh `-OutputDirectory` for a rebuild. `-RuntimeVersion` selects the bundled .NET patch; the initial release uses 10.0.13. `-SkipUiChecks` is available for environments without a desktop; run UI checks on a Windows desktop before publishing.

The source archive uses Git's tracked and nonignored file list, including working changes. It omits `.git`, runtime caches, build outputs, research downloads, and local artifacts. Keep new local-only files ignored. The portable application includes `Source.zip`, license/attribution files, image provenance, and documentation.

Automatic-connection checks cover last-device preference, ambiguous device selection, no-device startup, later Bluetooth connections, retry cooldowns, overlapping discovery, manual suspension/resume, shutdown or manual activity during discovery, and legacy preference migration. Startup UI checks use a fake registration service; they never modify the real Windows startup entry. Normal diagnostics and screenshots do not start automatic connection unless explicitly passed --auto.


Update checks cover release/architecture/version selection, same-repository HTTPS URLs, SHA-256 metadata and downloaded bytes, truncated downloads, rate limiting, extraction limits, unsafe/duplicate paths, manifest hashes, file replacement, obsolete managed files, user-file preservation, rollback after a real file conflict, and a separate helper process that waits for an old process before restarting the installed executable. UI tests use a fake update service and do not install into the working app.

The portable package includes release-manifest.json with per-file SHA-256 hashes. Keep this manifest intact; updater sessions are prepared outside the app folder. The staged executable runs --apply-update before WPF or Bluetooth starts, waits for the original process to exit, backs up managed files, replaces them, and restarts the app with an update result. No administrator elevation or registry rewrite is performed by the updater.

Run NothingOnWindows.exe --update-probe artifacts/update-check.json to verify the public GitHub endpoint. Add --download to verify and stage the latest package without installing it. This explicit diagnostic uses a baseline version to inspect the latest release regardless of the app version. Normal update checks only offer newer stable releases.




Startup update checks run only from normal app startup, after tray initialization. The saved CheckUpdatesAtStartup preference defaults to false for existing and new settings. Enabling the toggle explicitly checks immediately; normal launch checks once. Duplicate-instance activation, diagnostics, screenshots, and offline test startup do not contact GitHub. Automatic check failures remain in the Updates card and do not change Bluetooth status. A pending check uses the existing window cancellation token, is shared with manual requests, and suppresses late notifications after disabling the preference or closing.

Notification tests use an injected callback rather than displaying fake OS notifications. They verify startup gating, preference persistence, no download/install during automatic checks, hidden-tray behavior, in-app banner navigation, click-to-open Settings, same-release deduplication, quiet current/error responses, concurrency, cancellation, and all six views at minimum size with the update banner visible. Actual tray delivery follows Windows notification settings.

