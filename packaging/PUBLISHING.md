# Publishing a Syphon beta

The installer, unpacked-extension ZIP and source archives are GitHub Release assets. GitHub Pages contains only the lightweight static site and links to those assets. Users need the installer; source downloads are optional.

## Preserve build identity

Use the clean application revision recorded by the installer build, not an arbitrary later documentation commit, for the release tag and application source archive. Keep the installer unchanged after signing. Match its size and SHA-256 against the versioned build receipt.

Keep the native and FFmpeg source companions named in that receipt alongside the installer. Export the application archive with `git -c core.autocrlf=false archive` from the recorded revision. Verify ZIP file contents against Git blob identities, not just the archive filename.

Create `SHA256SUMS.txt` covering each uploaded asset except itself. Avoid including private keys, local credentials, user downloads, private research or inspiration material.

## Release first

Create a draft marked as a prerelease. Upload all selected assets. Compare GitHub's recorded SHA-256 digest and size to the local inventory for every upload. Reuse an existing upload only when both match; do not silently replace a different published artifact.

Publish only after uploads and release notes are checked. Explain self-signing, optional source downloads, manual browser-extension setup and remaining acceptance limits. Keep historical build-time receipts unchanged and describe later publication in a separate release record.

## Build the Pages site

`prepare_public_page.py` requires a published matching prerelease record from GitHub. It validates release-asset identities, replaces local download links with their actual GitHub URLs, removes preview labels and adds the application source link. The site's `release.json` records the published URLs.

Example (paths are local staging directories):

```text
python packaging/prepare_public_page.py --preview <verified-preview> --release-files <published-assets> --release-record <github-release.json> --output <new-site-directory>
```

Publish only that small directory to the `gh-pages` branch root, including `.nojekyll`. Configure Pages to deploy from that branch. Keep binaries and source archives out of the Pages branch.

Run `tests/release_page_browser_test.mjs` against the site with its fourth argument set to `public`. It checks desktop/mobile layout, keyboard access, fonts/images and release download responses. Check the anonymous release API as well as the public page before reporting publication complete.

## Scope of evidence

A published prerelease is not clean-machine certification, trusted code signing, legal certification or a promise of byte-identical dependency rebuilds. Preserve the exact acceptance status in the release notes. Browser store publication is a separate task.
