# Native source-material closure

The previously identified native source/archive/build-recipe gaps are closed for the current isolated downloader build: SHA-256 `d7a74ab51f7d599c7de22678cb7cb327e21b00e7d9a5453e15828269fde7059b` (20,944,536 bytes). All22 pinned source/Python/wheel inputs are unchanged. The accepted Beta16 installer and its historical receipts retain their original identities; the replacement is packaged in Beta17.

## What is verified

- [CPython](PYTHON-NATIVE-SOURCE.md): the exact embed SPDX matches the pinned ZIP; source and all4,863 official source-file hashes match; linked source, licenses and Windows adaptation/build recipes are retained.
- [Custom PyInstaller](PYINSTALLER-SOURCE.md): publisher asset digests, actual successful workflow/source revisions, bootloader/build sources and bundled zlib are matched.
- [curl_cffi and curl](CURL-NATIVE-SOURCE.md): the wheel DLL byte-matches the publisher's Windows asset; all eight native source archives match the immutable CMake recipe. Full patches and build scripts are retained.
- [Other native wheels](NATIVE-WHEELS-SOURCE.md): all47 runtime wheel-native files map to pinned wheel bytes and retained author/native sources/recipes. Build-only launcher EXEs are explicitly excluded from runtime claims.

Direct CArchive inspection of the current hash-verified executable matches **all75 native files** to pinned Python/wheel inputs, with zero unclassified files. This supplements the earlier build-graph evidence and does not import or execute archived code.

## Host DLL collection fixed

The old downloader unintentionally collected43 Windows UCRT/API-set DLLs from an unrelated build-machine search path. Their exact supplier distribution evidence was insufficiently pinned. The replacement build uses only private Python and Windows directories in PATH, a fresh private PyInstaller cache, and rejects any native build-graph input outside the extracted pinned runtime. The old build fails that gate; the fresh build passes. None of those43 DLLs remains. [Historical Windows runtime review](WINDOWS-RUNTIME.md) records the original finding and explains Windows10/11 system UCRT behavior. No extra user-installed dependency is introduced.

## Source companion

`native-source-bundle.win-x64.json` identifies the complete companion archive and checksum. It contains the historical baseline material clearly labelled as such, exact current native sources and patches, notices, platform build recipes, the isolated downloader builder/input lock, current review records and an integrity verifier. Historical Windows runtime DLL copies are deliberately excluded; their hashes/review evidence are retained. The separate reviewed FFmpeg source/build companion remains required.

After extracting the source ZIP, rebuild the downloader with `python packaging/build_ytdlp.py --inputs baseline/inputs --work <new-directory>`. Inputs must match the retained lock; the builder makes no downloads or global installations. Verify the original ZIP with `python packaging/verify_native_sources.py <source-zip> --receipt <downloaded-native-source-bundle.win-x64.json>`. The release-lock identity uses canonical JSON to survive Git checkout formatting; every archive member's bytes are checked independently.

Precise historical compiler/runner patch versions and bit-identical cross-machine builds remain unproven. Those reproducibility limits are separate from missing source. This is engineering source evidence, not legal/patent certification or a public release. Original third-party terms and proprietary Microsoft runtime notices remain applicable; no proprietary runtime source is claimed.
