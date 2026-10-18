# Custom PyInstaller source review (Windows x64)

The unchanged Syphon downloader build uses the custom `pyinstaller-6.22.0-py3-none-win_amd64.whl` locked in `yt-dlp-build.win-x64.json`. Its SHA256 `294099ecb5fdd2a13ae4c29006d4e335b697a63b6a16cf052b90ec2b40bef05a` matches both the retained wheel and the GitHub release asset digest. This is not the PyPI wheel/source pairing.

The [immutable custom release](https://github.com/yt-dlp/Pyinstaller-Builds/releases/tag/2026.08.19.215425) identifies PyInstaller source commit `70fc17210920bce17f4ab09bbf8104b0dbd45338`. Its custom source asset (3,527,013 bytes; SHA256 `2fadbed5d951d53f003ed899312823a07dbba59990a223cbe538933e1c42a168`) matches the release metadata. All 157 bootloader/build configuration files compared between that source distribution and the complete commit archive match exactly. All 558 wheel Python files match the commit after CRLF/LF normalization. No custom source patches occur in the retained workflow. The source archive, rather than the partial Windows extraction, is authoritative; three unrelated symlinks could not be extracted by Windows tar.

The [successful workflow run](https://github.com/yt-dlp/Pyinstaller-Builds/actions/runs/32306212512) was a workflow_dispatch at build-repository commit `5eb86b862d61adc3df0f44e6e0b815b7d74fa3f5`. The jobs API confirms successful x64 job `96239539520` on `windows-2025`. The retained workflow at that exact revision configures MINGW64, the x86_64 GCC toolchain and MSYS2 Python, and runs:

```text
cd pyinstaller/bootloader
python ./waf --target-arch="64bit" --"gcc" distclean all
```

It installs hash-locked Python build dependencies from the retained `requirements/pip.txt` and `requirements/build.txt`, then runs `python -m build --no-isolation --wheel --outdir=dist .` with `PYI_WHEEL_TAG=win_amd64` and `PYI_PLATFORM=Windows-64bit-intel`. The workflow archive includes pinned action revisions, both requirement locks and `uv.lock`. The complete PyInstaller commit archive includes `bootloader/waf`, `waflib`, `wscript`, C sources and configuration. `bootloader/zlib` includes the bundled zlib 1.3.2 source and its LICENSE; Windows uses static zlib linkage. Retain this bundled source, rather than substituting the separate CPython zlib dependency.

`COPYING.txt` licenses PyInstaller under GPL-2.0-or-later with a bootloader exception covering `bootloader/` and `PyInstaller/loader` embedded in combined executables. The exception grants distribution permission for those combinations; GPL restrictions still apply in other respects, including modifications and distribution outside a combined executable. Runtime hooks have separate Apache-2.0 terms. The exception does not blanket-relicense every dependency. Preserve `COPYING.txt`, bundled zlib LICENSE and per-file notices when distributing source. This review records engineering provenance, not legal approval.

Evidence is retained at `closure/pyinstaller`: source/wheel archives, release metadata, runs/jobs API responses, source comparison, and the review scripts. `pyinstaller-source-review.win-x64.json` records material and bootloader hashes. Its wheel Python comparison normalizes CRLF/LF; its sdist build-source comparison is byte-for-byte.

The logs API returned HTTP 403. Exact GCC/MSYS2/Python package versions and hosted runner image revision remain unproven because the workflow updates MSYS2 packages at build time. No downloaded source was executed. This closes the source and build-recipe mapping for the existing custom asset; it does not claim cross-machine byte reproducibility or replace the working downloader.
