# DepotDownloaderMod 🚀

[![.NET Version](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Powered by BlueStar](https://img.shields.io/badge/Integrated%20in-BlueStar-00d2ff?logo=steam&logoColor=white)](https://github.com/Coronitaa/BlueStar)

An enhanced, high-performance Steam depot and workshop downloader built on top of [SteamKit2](https://github.com/SteamRE/SteamKit). **DepotDownloaderMod** extends the capabilities of standard depot downloaders with support for external `.manifest` files (bypassing `GetManifestRequestCode` restrictions), custom depot decryption keys, access tokens, and an **embeddable C# engine architecture** designed for modern graphical game managers.

---

## 🌟 Key Features

- 🔑 **Depot Keys Support**: Supply external depot decryption keys via file (`-depotkeys`) in `depotId;hexKey` format.
- 📦 **External Manifest Bypassing**: Download depots using locally provided `.manifest` files (`-manifestfile`) without requiring Steam server manifest code verification.
- 🎟️ **Access Tokens**: Support for `-apptoken` and `-packagetoken` for authorized depot queries.
- 🧩 **Steam Workshop Support**: Download UGC items and Published Files with a single command (`-pubfile` or `-ugc`).
- ⚡ **High-Throughput Concurrent Engine**: Multi-threaded chunk downloading with configurable connection concurrency (`-max-downloads`) and local LanCache routing (`-use-lancache`).
- 🖥️ **Embeddable Library Engine**: Includes `IDepotDownloadEngine`, `DepotDownloadRequest`, and real-time progress callbacks for seamless integration into C# / .NET desktop applications.
- 🌐 **Multi-Platform**: Full support for Windows, Linux, and macOS with OS and architecture-specific depot filtering.

---

## 🎮 BlueStar Integration

**DepotDownloaderMod** serves as the core downloading and depot staging engine for **[BlueStar](https://github.com/Coronitaa/BlueStar)** — the modern open-source desktop game manager and launcher.

### What BlueStar provides on top of DepotDownloaderMod:
- 🖥️ **Modern Desktop GUI**: Manage your library with real-time download progress, pause/resume, and queue orchestration.
- 📦 **1-Click Depot ZIP Package Importer**: Drag & drop `.zip` depot archives (from cs.rin.ru, DepotBox, etc.) to automatically extract manifests, configure depot keys, and detect game Build IDs.
- 🎮 **Game Build & Branch Versioning**: Seamlessly switch between historical game versions (e.g. for compatibility with specific game fixes, bypasses, or mod layers).
- 🔓 **Automated Emulator & DLC Layers**: Integrated deployment for CreamAPI, SmokeAPI, Goldberg, and custom game fixes.

👉 Check out the full desktop client at **[Coronitaa/BlueStar](https://github.com/Coronitaa/BlueStar)**!

---

## 🚀 Quick Start

### Requirements
- [.NET 8.0 Runtime or SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0))

### Building from Source

```bash
# Clone the repository
git clone https://github.com/Coronitaa/DepotDownloaderMod.git
cd DepotDownloaderMod

# Build Release binary
dotnet build DepotDownloaderMod.sln -c Release
```

The executable will be located in `DepotDownloader/bin/Release/net8.0/`.

---

## 📖 Usage Examples

### 1. Download Depots with External Manifest and Keys
Download a specific depot using an external `.manifest` file and a `depotkeys.txt` decryption file:

```bash
dotnet DepotDownloader.dll -app <AppID> -depot <DepotID> -manifest <ManifestID> -manifestfile <path/to/manifest.manifest> -depotkeys <path/to/depotkeys.txt>
```

**Example:**
```bash
dotnet DepotDownloader.dll -app 730 -depot 731 -manifest 7617088375292372759 -manifestfile 730_7617088375292372759.manifest -depotkeys keys.txt -dir ./csgo_depot
```

### 2. Download Game Depots with Authenticated Account
For apps requiring ownership verification, log in with your Steam credentials (supports Steam Mobile 2FA and QR Code):

```bash
# Interactive password & mobile 2FA prompt
dotnet DepotDownloader.dll -app 730 -username your_username -remember-password

# Login with Steam Mobile QR code
dotnet DepotDownloader.dll -app 730 -username your_username -qr
```

### 3. Download a Specific Branch or Beta
```bash
dotnet DepotDownloader.dll -app 730 -branch beta_branch -branchpassword branch_pass
```

### 4. Download Steam Workshop Content
Download items from the Steam Community Workshop using either Pubfile ID or UGC ID:

```bash
# Using Published File ID
dotnet DepotDownloader.dll -app 730 -pubfile 1885082371

# Using UGC ID
dotnet DepotDownloader.dll -app 730 -ugc 770604181014286929
```

### 5. Filter by OS, Architecture, or Language
```bash
# Download only Windows 64-bit English depots
dotnet DepotDownloader.dll -app 730 -os windows -osarch 64 -language english
```

### 6. Filter Specific Files with a Regex Filelist
```bash
# Download only executables and DLLs
dotnet DepotDownloader.dll -app 730 -filelist regex:\.(exe|dll)$
```

---

## 🛠️ Using DepotDownloaderMod as a C# Library

DepotDownloaderMod provides a high-level API designed for C# applications:

```csharp
using BlueStar.DepotDownloader;

var engine = new DepotDownloadEngine();

var request = new DepotDownloadRequest
{
    AppId = 730,
    DepotId = 731,
    ManifestId = 7617088375292372759,
    ManifestFilePath = @"C:\manifests\730_7617088375292372759.manifest",
    DepotKeysFilePath = @"C:\keys\depotkeys.txt",
    InstallDirectory = @"C:\Games\CSGO",
    MaxDownloads = 16,
    VerifyFiles = true
};

var progress = new Progress<DownloadProgressInfo>(info =>
{
    Console.WriteLine($"Progress: {info.Percentage:F1}% | Speed: {info.FormattedSpeed} | Downloaded: {info.FormattedBytesDownloaded}");
});

var result = await engine.DownloadAsync(request, progress, cancellationToken);

if (result.Success)
{
    Console.WriteLine("Depot downloaded and verified successfully!");
}
```

---

## 📋 Command-Line Parameters Reference

| Parameter | Description |
| :--- | :--- |
| `-app <#>` | The Steam Application ID (AppID) to download. |
| `-depot <#>` | Specific Depot ID to download (if omitted, downloads all relevant depots). |
| `-manifest <id>` | Target Manifest ID to download (requires `-depot`). |
| `-manifestfile <file>` | **(Mod Feature)** Path to a local `.manifest` file to bypass Steam server verification. |
| `-depotkeys <file>` | **(Mod Feature)** Path to a decryption keys file (`depotId;hexKey` per line). |
| `-apptoken <token>` | **(Mod Feature)** Specify an App Access Token for restricted queries. |
| `-packagetoken <token>` | **(Mod Feature)** Specify a Package Access Token. |
| `-dir <path>` | Destination folder for downloaded and staged files. |
| `-username <user>` | Steam account username for restricted/licensed content. |
| `-password <pass>` | Steam account password (prompted interactively if omitted). |
| `-remember-password` | Persists login authentication token for subsequent runs without re-entering credentials. |
| `-qr` | Displays a login QR code in the terminal to scan with the Steam Mobile App. |
| `-no-mobile` | Prefer entering numerical 2FA code instead of mobile prompt. |
| `-pubfile <#>` | PublishedFileId of a Steam Workshop item to download. |
| `-ugc <#>` | UGC ID of a Steam Workshop item to download. |
| `-branch <name>` | Branch / beta to download from (default: `public`). |
| `-branchpassword <p>` | Password for protected private branches. |
| `-os <os>` | Target operating system (`windows`, `macos`, or `linux`). |
| `-osarch <arch>` | Target architecture (`32` or `64`). |
| `-all-platforms` | Download depots for all operating systems. |
| `-all-languages` | Download depots for all available languages. |
| `-language <lang>` | Preferred language (default: `english`). |
| `-filelist <file>` | Path to a text file containing file filters (supports `regex:` prefix). |
| `-validate` | Validates file integrity and checksums of existing downloaded files. |
| `-manifest-only` | Generates human-readable depot manifests without downloading content chunks. |
| `-max-downloads <#>` | Maximum concurrent chunk download threads (default: `8`). |
| `-use-lancache` | Routes chunk downloads through a local network LanCache instance. |
| `-loginid <#>` | Unique 32-bit integer Steam LogonID (useful when running multiple concurrent sessions). |
| `-debug` | Enables verbose debug logging output. |
| `-V`, `--version` | Displays program version and runtime information. |

---

## 🔑 Depot Keys Format

When using `-depotkeys <file>`, the file should contain one entry per line formatted as:

```text
<DepotID>;<DecryptionKeyInHex>
```

**Example `depotkeys.txt`:**
```text
731;4E2B1832049BA4E08B39B25032F0A3BC148C01DF91F0314488F7EA9C12822B7A
732;A718BC39904FE0019C3381B27014498AEB38401C0219EF890473921820468BEF
```

---

## 🐍 Helper Scripts

The `Scripts/` directory contains helper tools for managing community manifests and depot key databases:
- `Scripts/storage_depotdownloadermod.py`: Generates batch scripts for downloading multi-depot collections from manifest repositories.

---

## ❓ Frequently Asked Questions

### 1. How do I avoid entering Steam 2FA codes every time?
Use `-username <your_user> -remember-password`. Once authenticated, SteamKit2 securely caches login tokens so subsequent commands run seamlessly.

### 2. Can I download while logged into the Steam desktop client?
Yes. Steam will disconnect duplicate sessions sharing the same LogonID. To download simultaneously without conflict, specify a custom LogonID, for example `-loginid 1234`.

### 3. Why are download speeds slow for older game builds?
Older builds may not be cached on edge CDN nodes. Increase the concurrency factor with `-max-downloads 16` or `-max-downloads 32` to maximize throughput.

---

## ⚖️ License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See the [LICENSE](LICENSE) file for details.

Steam is a registered trademark of Valve Corporation. This project is not affiliated with or endorsed by Valve Corporation.
