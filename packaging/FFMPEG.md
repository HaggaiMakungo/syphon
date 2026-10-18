# Building FFmpeg and FFprobe

The Windows source build uses `ffmpeg-build.win-x64.json` and `build_ffmpeg.py`. It does not replace the release dependency cache or discover third-party media libraries from PATH.

Use Windows x64 and Python 3.11+. Collect the exact files named in the lock into a dedicated input folder. The lock records upstream URLs, byte lengths, hashes and the authentication performed for each input. Downloads are separate; the builder performs no package installation or download.

```powershell
python tests/ffmpeg_build_test.py
python packaging/build_ffmpeg.py --inputs D:/Syphon-FFmpeg-inputs --work D:/Syphon-FFmpeg-build --jobs 2
```

The work directory must be new and outside the input directory. Allow several GiB of free space and use a short path. Two workers are the default; a six-worker run on the development PC exhausted available memory. Every locked input is verified before extraction or code execution. The build extracts its portable w64devkit compiler and NASM, then compiles zlib, libxml2, libwebp, LAME, x264 and FFmpeg. No system compiler installation is needed. The legacy LAME configuration uses explicit Windows target and executable/path conventions. Its install tool is named explicitly. Windows zlib metadata is adjusted to reference the static archive it actually installs.

External-library autodetection is disabled in FFmpeg. The recipe explicitly selects Windows Schannel, zlib, libxml2, libwebp, LAME and x264; GPL and version-3 licensing are enabled, and nonfree is not enabled. Native FFmpeg codecs and containers remain broadly enabled. Schannel provides Windows HTTPS support; network certificate behavior requires separate runtime verification.

Successful builds produce `output/ffmpeg.exe`, `output/ffprobe.exe`, a build record, command log and version reports. Input archives include the compiler's source bundle and recipe, NASM source, exact media-library sources and FFmpeg's detached release signature/key. A locally calculated hash is not independent publisher authentication; the lock distinguishes those cases.

The pinned x264 configuration script receives a guarded shell-syntax patch for BusyBox. The build retains the original, modified script and unified diff under `patches/`; encoder source is unchanged. Executing recipe snapshots are retained under `recipes/`.

Keep the exact input archives, recipe, logs, configure outputs and notices with the candidate. The source build is a local candidate until import inspection, media compatibility and corresponding-source inventory review pass. It does not by itself clear public redistribution of every bundled dependency. Cross-machine byte-for-byte reproducibility is not claimed.

The existing release dependency lock continues to identify the shipped candidate until an explicit reviewed update. Do not replace its binaries with an untested source build or relabel an old installer's source records.

## Current candidate

The fresh native CLI build produced FFmpeg 9.0.2-syphon-source-build and FFprobe from the locked inputs on 2026-10-18. All five external libraries and both executables were built in a new work directory with two workers, without manual continuation or repair. Their exact identities are retained in `ffmpeg-build-record.win-x64.json`.

The clean source archive identified by `ffmpeg-source-evidence.win-x64.json` contains the exact inputs, portable compiler source/build context, executing recipe snapshots, patches, generated configuration, complete command/build logs, notices and compatibility evidence. Its top-level `packaging/`, `tests/` and `inputs/` layout supports the build commands above without rearranging files. `SHA256SUMS` covers every other archive member. The archive README distinguishes standalone build prerequisites from the checkout-dependent application test harness.

Independent engineering source-material review passed with no critical or important findings; see `ffmpeg-source-review.win-x64.json`. All 94 archive checksums and the outer archive hash were verified. The archive retains the earlier conditional review as historical evidence; the separate final verdict closes its clean-build conditions. All 17 recipe tests also passed using only files extracted from the archive, without checkout imports. This review does not establish legal/patent clearance or clearance for other installer dependencies.

Earlier incremental development evidence remains in a separate historical archive. Its executable identities and test results must not be substituted for this clean pair. Output bytes can differ with build paths; cross-machine byte-for-byte reproducibility is not claimed. The shipped release lock remains unchanged until a separate installer integration step.

## Application compatibility

The clean candidate pair passed 13 engine media checks, 18 YouTube adapter checks, eight size-discovery checks and three album-art scenarios. Only ordinary Windows DLL imports were observed. Live Big Buck Bunny downloads through Syphon completed as MP4 (AV1/AAC) and MP3 with JPEG artwork, with FFprobe validation. The first live MP4 succeeded but MP3 did not complete; test cleanup reported only “Paused,” obscuring its cause. The identical live test passed on the second attempt. Both logs are retained; this is successful compatibility evidence, not a claim of uninterrupted live reliability. Exact candidate hashes and results are recorded in `ffmpeg-compatibility.win-x64.json`.

HTTPS inspection succeeded against a valid public certificate. The same local media fixture succeeded with verification disabled as a diagnostic control, then failed with verification enabled against its untrusted certificate. No certificate was added to Windows trust stores. This is one-PC evidence, not clean-machine certification or an exhaustive website-compatibility claim. The shipped dependency cache and installer remain unchanged.
