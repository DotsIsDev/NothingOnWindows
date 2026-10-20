# Nothing on Windows v0.1.0

First public release of an unofficial native Windows companion for Nothing audio devices.

## Download

- **NothingOnWindows-v0.1.0-win-x64.zip**: portable Windows x64 app with .NET 10.0.13 included. Extract the entire ZIP and open `NothingOnWindows/NothingOnWindows.exe`. Requires Windows 10 build 19041 or newer and Bluetooth support. No separate runtime installation is needed.
- **NothingOnWindows-v0.1.0-source.zip**: corresponding application, tests, documentation, assets, and packaging source. Also included as `Source.zip` inside the portable package.
- **SHA256SUMS.txt**: SHA-256 checksums for both archives.

## Included

- Black glass interface, compact resizable portrait window, pixel-art app icon, custom window buttons, and system tray support.
- Model-specific sound, EQ, bass, spatial audio, low lag, wearing detection, and gesture controls where available.
- Simple and eight-band advanced EQ, local custom profiles, and Nothing Playground community profiles.
- Device notifications and read-only polling to refresh settings changed using physical controls.
- Eleven Nothing audio profiles, each with a distinct official product photo; automatic recognition, disconnected previews, and manual selection for renamed devices.
- README with six software screenshots and a detailed device/firmware capability matrix.

## Compatibility and validation

**Headphone (a), firmware 1.0.1.51, is the only hardware-tested model. The other ten profiles are experimental.** See [device support](https://github.com/DotsIsDev/NothingOnWindows/blob/main/DEVICE_SUPPORT.md) for firmware requirements and feature limits.

Validation: 321 offline core checks and 314 offline UI/window/tray checks. Packaging and screenshot capture send no headphone writes. Earlier Headphone (a) hardware tests verified setting writes/readbacks, including two custom EQ profiles, and restored the original settings.

The app is unsigned. Firmware updates, automatic reconnect, Nothing X QR sharing, phone-only integrations, Personal Sound, Auracast, and proprietary Headphone (1) Pro spatial/driver processing are not included. Broader physical-device and button-synchronization testing is still needed.

Code is AGPL-3.0-or-later. Nothing names and product photographs belong to their respective owners and are separate from the code license. This project is not affiliated with or endorsed by Nothing. See the bundled license and third-party notices.
