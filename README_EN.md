<div align="center">

# OpenSteamEYA Enhanced

**Steam account management · CS2 status queries · Shared game settings**

[![Latest release](https://img.shields.io/github/v/release/dbdbvdbdb/OpenSteamEYA-Enhanced?label=release&color=3278f6)](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/latest)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4)](docs/RUNNING.zh-CN.md)
[![MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**[Download ↗](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/latest) · [Changelog ↗](CHANGELOG.md)**

<img src="docs/screenshots/0.2.5/classification-white.png" width="960" alt="White account cards in a real v0.2.5-beta WinUI QA build, using fictional accounts and status data">

<sub>v0.2.5-beta WinUI QA screenshot with synthetic data; this is not a new v0.2.9 capture.</sub>

</div>

| **Account management** | **CS2 status queries** |
| --- | --- |
| Search, groups, notes, favorites and batch operations. | View Premier rating, level, cooldown and supported status information. |
| **Shared settings** | **Import and backups** |
| Edit one template, then confirm deployment to eligible local accounts. | Batch entry, TXT / CSV / JSON import and export, and management-data backups. |

OpenSteamEYA Enhanced is a Windows tool for managing multiple Steam accounts. Its white / black account categories separate account sources; they do not indicate ban status. Accounts can be moved between categories.

Existing Steam/EYA login, history, cached accounts, loadouts, personalization and Workshop cleanup remain available. Login and online queries require valid existing credentials; saving a username and password alone does not create an EYA credential.

[简体中文](README.md) · [User guide (Chinese)](docs/RUNNING.zh-CN.md)

## Screenshots

**Black account management** — a real v0.2.5-beta WinUI QA screenshot with fictional account data.

<img src="docs/screenshots/0.2.5/classification-black.png" width="960" alt="Black account list in v0.2.5-beta with synthetic account names and Steam IDs">

**CS2 shared settings** — a real v0.2.9 WinUI CI screenshot with an isolated template. A rendered settings page does not establish in-game effectiveness.

<img src="docs/screenshots/0.2.9/cs2-unified-video-advanced-dark.png" width="960" alt="v0.2.9 CS2 shared settings page showing advanced video controls in an isolated QA environment">

[More screenshots and provenance](docs/screenshots/README.md)

## Quick start

1. Download the Windows x64 ZIP or 7z from [this repository's Releases](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/latest).
2. **Extract the entire archive.** Keep all runtime files and licenses; do not copy only the EXE.
3. Run `SteamEyaWinUI.exe`.

Requires Windows 10 1809 (build 17763) or later, x64. Steam is needed for login and local account configuration. The portable package does not require a separate .NET Runtime installation.

Back up your management data before upgrading, then extract the new version to a new folder. Management ZIP backups exclude original EYA tokens, Steam sessions and quick-login caches; they do not migrate a complete login session. See [backup and restore details (Chinese)](docs/FORMATS-AND-BACKUP.zh-CN.md).

## Safety and limitations

- This is unofficial software, with no official affiliation or endorsement from Valve, Steam or upstream developers.
- **Management passwords are stored in plain text.** Exports and backups may contain passwords. Privacy mode and password dots hide the UI only; they do not encrypt files.
- Do not publish real databases, tokens, backups or credential screenshots. Copying a password does not automatically clear the clipboard.
- CS2 shared settings affect **local files and do not automatically push to Steam Cloud**. Cloud or other configurations may override them. Close CS2 before applying settings.
- Some options depend on the client, display or driver and still require in-game checks. A successful file write does not prove an actual game effect.

[Security policy](SECURITY.md) · [CS2 usage and limitations (Chinese)](docs/CS2-UNIFIED-CONFIG.zh-CN.md) · [End-user terms](END_USER_TERMS.txt)

## Recent update

**v0.2.9** adds a persistent CS2 template, import/export, backup recovery and confirmed batch deployment. Saving a template is separate from applying it to accounts, and does not push it to the cloud.

[Full changelog (Chinese)](CHANGELOG.md) · [v0.2.9 release notes](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/tag/v0.2.9)

## License and acknowledgements

Based on [OpenSteamEYA](https://github.com/hvh-software/OpenSteamEYA), retaining the original copyright of tuntun1337. Thanks to [SteamAccountManager](https://github.com/Sakiora/SteamAccountManager) for interaction references; its code and images were not copied. Thanks also to Microsoft and other dependency maintainers.

Project code uses the [MIT License](LICENSE). Valve assets, original branding and Microsoft runtime components retain their own terms; MIT does not relicense them. Read [third-party notices](THIRD_PARTY_NOTICES.md) and the bundled licenses. Contact the maintainer through this repository about copyright concerns.

[Issues](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/issues) · [Contributing](CONTRIBUTING.md)

<details>
<summary>Developer documentation</summary>

- [Build, test and archive verification (Chinese)](docs/BUILDING.zh-CN.md)
- [Formats and backup boundaries (Chinese)](docs/FORMATS-AND-BACKUP.zh-CN.md)
- [Startup update checks (Chinese)](docs/UPDATES.zh-CN.md) — this repository's releases, including beta; no automatic installation or file replacement.
- [Archived technical changelog (Chinese)](docs/archive/CHANGELOG-technical.zh-CN.md)

</details>
