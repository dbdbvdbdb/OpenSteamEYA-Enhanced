# Third-party notices

本文件是旧路径兼容入口。**当前完整审查及发布阻断状态以 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 为准。** 正式打包使用该新文件；下文为历史本地开发说明，其中 WinUI 2.2.1 已改为 2.3.9，不能作为旧包可公开再分发的依据。

OpenSteamEYA base source: https://github.com/hvh-software/OpenSteamEYA at e68ae7d24ac116826d791902bbaace6076f1936b. MIT, Copyright (c) 2026 tuntun1337. The complete original LICENSE is retained.

SteamAccountManager was inspected only as an interaction reference at 50e9ee4640e8ca52bca4dace803b3cc60668f4f5. No code, icons or assets from that repository are distributed in this enhanced project.

Microsoft.WindowsAppSDK.WinUI 2.2.1 and its transitive packages use their own package licenses and notices. The packaging script copies LICENSE/NOTICE texts from the resolved NuGet packages into `third-party-licenses`. The self-contained .NET runtime's LICENSE and THIRD-PARTY-NOTICES are also included. Do not infer these licenses from this repository's MIT LICENSE.

The existing `SteamEyaWinUI/Assets/weapons` images came from Juknum/counter-strike-icons and originate in Valve's Counter-Strike assets. The project's actual license distinguishes the tooling's MIT license from Valve-owned game assets and restricts commercial use without Valve permission. The complete declaration is retained in `third-party-licenses/counter-strike-icons-LICENSE.txt`. Source: https://github.com/Juknum/counter-strike-icons/blob/main/LICENSE

The existing `steam_api64.dll` is the Steamworks redistributable library included by the base project for CS2 cloud synchronization. It has not been modified and is not relicensed as MIT. Steamworks documentation identifies redistributable_bin as API redistributable binaries: https://partner.steamgames.com/doc/sdk . Steamworks usage is governed by Valve's applicable terms; the base repository does not include the complete SDK license. This local development deliverable does not certify rights for a commercial or public distribution of Valve assets. Public distribution needs a review of those terms and provenance before publishing.

Steam and Counter-Strike are Valve trademarks. This enhancement does not claim affiliation with Valve or official Steam support.
