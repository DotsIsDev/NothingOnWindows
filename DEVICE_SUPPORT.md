# Nothing audio device profiles

Profiles cover the 11 Nothing-branded audio models identified in the current product lineup and community protocol references. CMF devices are excluded, as requested. These are implementation profiles, not a claim that every device/firmware has been physically tested on Windows.

| Model | ANC | Spatial controls | Bass levels | Equalizer | Wearing detection |
| --- | --- | --- | --- | --- | --- |
| Ear (1) (B181) | Low, high, transparency, off | — | — | Four presets | Yes |
| Ear (2) (B155) | Low, mid, high, adaptive, transparency, off | — | — | Presets, custom, advanced¹ | Yes |
| Ear (2024) (B171) | All four strengths, transparency, off | — | 1–5 | Presets, custom, advanced | Yes |
| Ear (a) (B162) | All four strengths, transparency, off | — | 1–5 | Presets, custom | Yes |
| Ear (3) (B173) | All four strengths, transparency, off | Fixed, off | 1–5 | Presets, custom, advanced | Yes |
| Ear (3a) (B190) | All four strengths, transparency, off | Fixed, off | — | Presets, custom, advanced | Yes |
| Ear (stick) (B157) | Noise reduction on/off² | — | — | Presets, custom, advanced² | Yes |
| Ear (open) (B174) | — | — | Automatic on device, no manual setting | Presets, custom, advanced | — |
| Headphone (1) (B170) | All four strengths, transparency, off | Fixed, head tracking, off | 1–5 | Presets, custom, advanced | Yes |
| Headphone (1) Pro (B192) | All four strengths, transparency, off | Audiodo scenes not implemented | — | Presets including New Instrument, custom, standard eight-band advanced³ | Yes |
| Headphone (a) (B186) | All four strengths, transparency, off | Concert, cinema, off | 1–2 | Presets, custom, advanced | — |

All models have mapped battery reads, low lag controls where supported, and model-specific gesture assignments. Earbuds show left/right/case batteries; headphones show a single battery. Ear (1) firmware through `1.0.1.74` does not expose low lag. Phone actions remain phone actions, even when their assignments can be changed here.

¹ Ear (2) Advanced EQ requires firmware `1.0.1.95` or newer.

² Ear (stick) Advanced EQ requires `1.0.1.80` or newer. Noise reduction requires `1.0.2.0` or newer and has no transparency mode. Its custom EQ uses a 3500 Hz high shelf and 0.66 mid-band Q; other earbuds use 3400 Hz and 0.7 Q.

³ Headphone (1) Pro's Audiodo scenes, Personal Sound, Auracast, and driver-specific EQ remain outside this implementation. The physical Flat EQ switch is read and blocks incompatible sound writes. Ear (3) and Ear (3a) prevent enabling spatial audio together with Advanced EQ; Ear (3) also prevents spatial/bass conflicts.

## Connection and model selection

The app controls one selected device at a time. Known Bluetooth names and model aliases select the profile automatically. When multiple recognized devices are paired, Connect offers a device chooser. Settings → Device profile previews any model while disconnected, including its product photo and feature layout. Settings → Connect a renamed device lets you select a paired device and explicitly identify its model. A connection must expose Nothing's control service before settings are read or written.

Feature controls are enabled only after a successful read from the connected device. Unsupported commands and gesture addresses/actions are rejected before writing. Writes use a fresh readback to confirm the result. The background synchronization uses the selected profile and queries only capabilities successfully read on that connection.

## Verification status

- **Headphone (a):** core sound, EQ and gesture behavior was previously hardware-tested on Windows with firmware `1.0.1.51`.
- **Other ten models:** experimental Windows support based on the references below. A real owner must confirm connection, readbacks, writes, and physical control notifications for their firmware before hardware verification can be claimed.
- **Software validation:** protocol checks exercise every profile's names, feature masks, battery formats, bass scales, spatial payloads, EQ filters and gesture mappings. UI checks render all six views for all 11 profiles at both window sizes, load every embedded photo, test write rejection and interlocks, and exercise the profile selector. These use simulated packets and send no hardware writes.

## Sources

- Ear (web), `res/js/ear_config_file.json` and Bluetooth handling: https://github.com/radiance-project/ear-web, reference revision `d076e1371572fbf0a14b441f9b3cafbf9a237e04`. `Core/device-controls.json` contains the converted Nothing-only control definitions and firmware variants.
- swift-nothing-ear, model/capability definitions and payload layouts: https://github.com/bestK1ngArthur/swift-nothing-ear, reference revision `b56c15e5749996f8f0e4fda54622079ea395686a`.
- Nothing product images: official product pages and image CDNs, recorded in `Assets/Devices/sources.json`. Photos are embedded for offline use and remain separate from the application's code license.

