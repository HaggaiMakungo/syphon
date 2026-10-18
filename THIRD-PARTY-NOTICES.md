# Third-party materials in Syphon

The root MIT license covers original Syphon code contributed to this project. It does not replace or relicense third-party software, fonts, icons, source archives or other materials carrying their own notices.

The installer retains component notices in `licenses/`, and frontend/font notices alongside the corresponding application assets. The source tree retains the originals under `packaging/licenses`, frontend font directories and generated asset notice files.

| Material | Existing licensing/notice evidence |
|---|---|
| FFmpeg and FFprobe | Beta 14 uses the reviewed Syphon FFmpeg 9.0.2 source build, with GPL/version-3 enabled and nonfree disabled. Exact FFmpeg, LAME, x264, libxml2, zlib, libwebp and build-tool notices are retained under `licenses/ffmpeg-source-build`, alongside build/input, compatibility and engineering-review records. |
| yt-dlp | The project source has its own Unlicense notice. The standalone executable also bundles other code; its upstream release describes the combined executable as GPLv3+. Both the original project and third-party notices remain included. |
| Node.js | Original Node license and bundled third-party notices remain included. |
| .NET runtime and WPF | Original runtime/WPF licenses and third-party notices remain included. |
| WebView2 SDK/bootstrapper | Microsoft SDK license/notice and the original bootstrapper remain subject to their own terms. |
| Frontend libraries, icons and fonts | Existing package/font/icon notices remain with the assets. |

These entries identify retained evidence; they are not an exhaustive application SBOM. Exact bundled binary versions and hashes are recorded in `packaging/dependencies.win-x64.json` and the generated package provenance. Original source materials and unresolved vendor/bundled input gaps are tracked in the source manifests under `packaging/`; coverage remains incomplete.

The reviewed FFmpeg source/build archive is identified in `packaging/ffmpeg-source-evidence.win-x64.json` and retained beside the local Beta 14 installer. It includes exact library sources, patches, compiler/assembler source and build context, configuration, logs and a runnable recipe. See `packaging/FFMPEG.md`. Earlier vendor-build source manifests describe historical binaries and must not be used as the source identity for Beta 14. Engineering source-material review is not legal or patent clearance for this package or other dependencies.

Choosing MIT for Syphon's original code does not close those third-party distribution requirements. The current installer remains a local beta candidate, pending the documented public-release checks.

Upstream references: [FFmpeg](https://www.ffmpeg.org/legal.html), [yt-dlp 2026.08.19](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/README.md#licensing).

Beta 10 uses a Syphon-local yt-dlp build with Python 3.13.16. Exact Python/wheel notices are retained under packaging/licenses/yt-dlp-runtime and included in the installer alongside original upstream notices and build/input records. See packaging/MEDIA-RUNTIME.md for scope and remaining source-material gaps.
