# Supported private yt-dlp runtime

Beta 10 replaces the upstream Windows x64 executable with a clearly identified **Syphon-local build of stable yt-dlp 2026.08.19**, using Python **3.13.16** and OpenSSL **3.5.9**. End users still install no Python dependency. FFmpeg, FFprobe and Node inputs are unchanged. The upstream stable and current nightly Windows x64 recipes still select Python 3.10; changing release channels would not resolve the runtime support issue.

## Reviewed inputs and identity

`packaging/yt-dlp-build.win-x64.json` locks all 22 build inputs: the official Python.org private embeddable package, the full upstream yt-dlp source archive and 20 Windows wheels. Python's archive matches its published SHA-256; wheel hashes match the retained tagged upstream requirements. `packaging/yt-dlp-build-record.win-x64.json` records the exact local output and runtime. The binary remains at upstream code version 2026.08.19 and release code head 594bd50c2c78ac432f81600d309fdc4e0a92d82c; its source archive is the tag commit 3a08beaf031ab68f966401ead017ac81fe8486cf.

Local modifications are confined to version.py: VARIANT is syphon_win_x64_py313, ORIGIN is HaggaiMakungo/syphon, and UPDATE_HINT directs users to Syphon setup. CHANNEL remains stable to avoid an invalid-channel startup error. This does not create an upstream release or enable tool self-update. All other source files come from the retained tag; lazy extractors and the executable are generated with the upstream scripts. The builder uses direct script entry because the isolated interpreter does not add the current directory to sys.path.

The reviewed output is 21,971,777 bytes, SHA-256 7cc1dd43e07ec25b625b9d1851070f06d8993d45d68ba184833b9a0c578fc283. The dependency lock now names that exact local binary and build variant. Old beta 9 lock/source manifests remain historical snapshots; the 44-archive baseline must not be relabelled as proof of this new binary's complete source set.

## Offline build

Stage the files named by the build lock in a dedicated input directory. Downloads are a separate explicit operation; the builder never resolves latest or installs into system Python. Then:

```powershell
python tests/yt_dlp_build_test.py
python packaging/build_ytdlp.py --inputs D:/Syphon-build-inputs --work D:/new-dedicated-build-directory
```

Every input's hash/length is checked before workspace creation or code execution. A fresh work directory is mandatory. Archive links, absolute paths and traversal are rejected. The private interpreter's _pth isolates build dependencies; wheel contents are unpacked into that interpreter without global installation. Upstream source/build code is intentionally executed only after input verification. The work directory retains build.log, build-record.json, runtime-inspection.txt and exact notice files. Source evidence is indexed in packaging/yt-dlp-runtime-source-evidence.json. This is partial evidence, not source-distribution approval.

The official Python embeddable package DLLs also retain their publisher signatures; package hashes alone are not a substitute for publisher authentication. Cross-machine byte-for-byte reproducibility, GPG/Sigstore verification and complete native source coverage are not claimed.

## Runtime and packaging gates

`packaging/media_runtime.py` first verifies the executable against the release lock, then invokes it with user configuration/plugins/cache disabled and a deliberately unsupported local scheme. Expected exit 1 is checked along with Python >=3.13.16, OpenSSL >=3.5.9 and the local variant. This probe makes no remote media request. Older Python/OpenSSL and absent runtime output are rejected by regression tests. Installer builds run both the test suites and this runtime gate before publishing.

The local runtime includes websockets 17.0.1 rather than the old Python <3.11 branch's 16.1.1. Runtime reports SQLite 3.50.4. Crypto, curl, Brotli, requests and EJS remain present. Original upstream aggregate notices are preserved, with exact Python/wheel notices under licenses/yt-dlp-runtime and both build-record/input manifests in the installer. OpenSSL runtime now belongs to the 3.x family described by the aggregate; exact supplemental runtime notices accompany it. Curl's BoringSSL is a separate dependency.

## Release limits

This runtime is used in the locally self-signed evaluation installer. Exact FFmpeg vendor linked/transitive sources, patches and toolchain remain outstanding. New Python/native component source coverage and redistribution review remain incomplete. Source collection and runtime support are separate release gates. No public binary release, system Python installation, app installation, registry or user download/history modification is part of this change.

References: [upstream stable release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19), [Python 3.13.16](https://www.python.org/downloads/release/python-31316/), [upstream build recipe](https://github.com/yt-dlp/yt-dlp/blob/3a08beaf031ab68f966401ead017ac81fe8486cf/.github/workflows/build.yml).
