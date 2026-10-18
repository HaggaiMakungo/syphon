# CPython native source review â€” current Windows x64 input

The locked Beta16 input `python-3.13.16-embed-amd64.zip` has SHA-256 `97dae5274cc54867065e8d5a3226e48c35017ed332a0fdb0e27d5b5821961297`. The [official embed SPDX](https://www.python.org/ftp/python/3.13.16/python-3.13.16-embed-amd64.zip.spdx.json) names that exact package and digest. Its bytes are identical to the previously collected `python-embed-sbom.json`; treating this document as an installer-only SBOM was incorrect.

The engineering source collection is retained at `closure/python`. The adjacent `python-native-source-review.win-x64.json` records hashes, archive locations, license members, native payload hashes, and build recipe hashes. Source archives were inspected as data; no downloaded build code was executed.

## Exact source evidence

The [official source SPDX](https://www.python.org/ftp/python/3.13.16/Python-3.13.16.tgz.spdx.json) identifies [Python-3.13.16.tgz](https://www.python.org/ftp/python/3.13.16/Python-3.13.16.tgz) with SHA-256 `cfac63bddf956deafb1172ca131ae5dcaafd6f95056086e233fca205593ed427`. The existing source archive matches. All 4,863 file SHA-256 entries in that official source SPDX were checked against the archive and match. This includes the actual vendored sources, platform changes, licenses, `PCbuild` recipes, and Windows packaging scripts; an upstream version label alone is not the evidence.

Nine existing dependency source archives and the newly collected HACL* commit archive match every corresponding SHA-256 in the exact embed SPDX. The retained source archives contain their license texts and upstream build machinery. The CPython archive contains its platform adaptations and preparation scripts, including `prepare_ssl.bat`, `openssl.vcxproj`, `prepare_libffi.bat`, `get_externals.bat`, and `python.props`.

| Native source | Version | Runtime / recipe mapping |
| --- | --- | --- |
| bzip2 | 1.0.8 | `_bz2.pyd`; `PCbuild/_bz2.vcxproj` |
| Expat | 2.8.5 | `pyexpat.pyd`, `_elementtree.pyd`; vendored `Modules/expat` and Windows projects |
| HACL* | `bb3d0dc8d9d15a5cd51094d5b69e70aa09005ff0` | Built into `python313.dll`; vendored source and `Modules/_hacl/refresh.sh` retain the exact revision and transformations |
| BLAKE2 / libb2 context | 0.98.1 | Actual Windows source is retained in `Modules/_blake2` and compiled by `pythoncore.vcxproj`; external libb2 archive is upstream provenance context, not a separately linked DLL |
| libffi | 3.4.4 | `_ctypes.pyd`, `libffi-8.dll`; `libffi.props`, `prepare_libffi.bat` |
| mpdecimal | 4.0.0 | `_decimal.pyd`; `python.props` and `_decimal.vcxproj` select the external 4.0.0 sources |
| OpenSSL | 3.5.9 | `libssl-3.dll`, `libcrypto-3.dll`, `_ssl.pyd`, `_hashlib.pyd`; OpenSSL recipes and preparation scripts |
| SQLite | 3.50.4.0 | `sqlite3.dll`, `_sqlite3.pyd`; SQLite Windows projects |
| xz / liblzma | 5.2.5 | `_lzma.pyd`; Windows liblzma sources and project |
| zlib | 1.3.1 | Built into `python313.dll`; `pythoncore.vcxproj` lists external zlib sources |

The embed SPDX has broad `DEPENDS_ON` edges to all 14 listed dependency packages. Those edges establish review candidates; actual zip membership and the verified Windows recipes establish platform relevance. Tcl/Tk are absent from the exact embed payload (`_tkinter.pyd` and Tcl/Tk DLLs are absent). Macholib is macOS Python utility context. mpdecimal 2.5.1 is cross-platform vendored context; the Windows project selects 4.0.0. These are explicitly distinguished rather than silently treating every package in the SPDX as a Windows native component.

The two OpenSSL DLLs also match byte-for-byte the [immutable Python binary-deps commit](https://github.com/python/cpython-bin-deps/tree/ddb46a415b4995440723183e644f8d2c346567a2/amd64); `libffi-8.dll` matches the [immutable libffi binary-deps commit](https://github.com/python/cpython-bin-deps/tree/94cb9a1c7feb608adf2b9f8fe2dbd6925ffbf90d/amd64). The exact DLL copies, hashes, resolved tag metadata, and libffi binary archive are retained externally. The optional full OpenSSL multi-platform binary archive timed out; the two relevant DLLs were separately collected and matched successfully.

## Build declarations and limits

The source retains Visual Studio/MSBuild toolset selection (`v143` for VS 17/18), Windows SDK selection (minimum `10.0.10586.0`, with newer installed SDK selection), OpenSSL NASM 2.11.06 / Perl requirements, and libffi MSVC plus Cygwin/autotools preparation. These are upstream recipe declarations, not proof of the precise compiler, SDK patch level, or command line used by Python's release builder.

This closes collection of the identified open source runtime source archives and CPython platform recipes for the locked embed input. It does not establish a bit-for-bit rebuild or complete historical build execution provenance. The exact MSVC compiler/SDK patch versions and individual release build logs are not supplied by these SPDX documents. The two bundled Microsoft runtime DLLs are inventoried by exact hashes; their proprietary sources are not claimed to be collected. Their redistribution terms require separate consideration.

The permissive licenses and retained license texts do not themselves constitute GPL compatibility approval, legal certification, or approval to distribute a particular combined executable. Source collection and a distribution decision remain distinct.
