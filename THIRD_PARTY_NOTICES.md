# Third-party acknowledgements

This project uses the community's reverse-engineered Nothing Bluetooth protocol and contains a new C# implementation of the documented framing, commands, and data formats.

- **Ear (web)** — https://github.com/radiance-project/ear-web — GNU Affero General Public License v3. Protocol references: `res/js/bluetooth_socket.js`, `hoppip.js`, `gestures_common.js`, `ear_config_file.json`. Credit to RapidZapper and the radiance-project contributors. Reference revision: `d076e1371572fbf0a14b441f9b3cafbf9a237e04`.
- **swift-nothing-ear** — https://github.com/bestK1ngArthur/swift-nothing-ear — GNU General Public License v3. Used as a protocol cross-reference for command IDs and device capabilities. Credit to bestK1ngArthur and contributors. Reference revision: `b56c15e5749996f8f0e4fda54622079ea395686a`.

The application source is licensed under AGPL v3. The pixel-art app icon and dot lettering are original artwork. Nothing and related product names belong to their respective owners.

`Core/device-controls.json` is a converted Nothing-only subset of Ear (web)'s `res/js/ear_config_file.json`, preserving its firmware variants and control operation definitions. It is included under the application source license with the upstream attribution above.

The portable Windows release bundles Microsoft .NET and Windows Desktop Runtime 10.0.13. Their MIT license and the runtime's third-party notices are preserved under `licenses/`. These components are separate from the application's AGPL license. `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` are supplied by Microsoft's `Microsoft.Windows.SDK.NET.Ref` targeting pack (10.0.19041.57); its package identifies Microsoft Corporation as copyright owner and links the [Windows SDK license](https://aka.ms/WinSDKLicenseURL). No Microsoft SDK package archives are bundled with the application source.

This distribution includes official Nothing audio product photographs in `Assets/Devices/` and the original Headphone (a) photo in `Assets/headphone-a-product.png`, obtained from Nothing's product pages and CDNs at the user's request. Copyright belongs to Nothing and/or its licensors; photographs are not covered by the application's AGPL license, and no redistribution license is claimed. See `Assets/README.md` and `Assets/Devices/sources.json` for exact sources. This app is unofficial and is not affiliated with or endorsed by Nothing.

