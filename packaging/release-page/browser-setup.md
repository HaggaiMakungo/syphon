# Connect Syphon to Chrome or Edge

Companion version @@EXTENSION_VERSION@@ · Matches the Beta @@BETA@@ installer payload.

Syphon is installed, but your browser still needs an introduction. This is the one-time folder-loading step that connects the two. After that, the companion's Check connection button can confirm they are talking.

## Recommended: use the installed extension folder

1. Install Syphon and enable **Browser integration** in setup.
2. On the final setup screen, open **Browser extension setup**, then **Open extension folder**. Keep that folder available. The default location is `C:\Program Files\Syphon\Extensions`; a custom install uses its own Extensions folder.
3. In Chrome, enter `chrome://extensions` in the address bar. In Edge, enter `edge://extensions`.
4. Turn on **Developer mode**, choose **Load unpacked**, and select the **Extensions folder containing manifest.json**. Select the folder itself, not a ZIP or a file inside it.
5. Open **Syphon this** from the browser's Extensions menu and choose **Check connection**. Syphon may open to respond. Refresh existing video tabs after installing or reloading the extension.

The installer registers the included native host; it cannot silently install or enable the browser extension. Chrome Web Store and Edge Add-ons listings are not available yet. Do not download unrelated extensions using a similar name.

## Separate ZIP, if needed

`@@EXTENSION@@` contains the exact extension files from this installer, with manifest.json at the ZIP root. Extract it to a permanent folder before choosing Load unpacked. Keep the extracted directory in place; deleting or moving it can break the loaded extension. Using the installed Extensions folder avoids a second copy to maintain.

ZIP SHA-256: `@@EXTENSION_SHA256@@`.

## Optional features

Right-click a supported link and choose **Syphon this** for a manual handoff. Automatic download capture and video detection are optional switches in the popup; each requests its needed browser permissions. Browser sign-in sharing is also optional and only supports eligible direct-file downloads. Website/media compatibility varies.

## If it cannot connect

- Confirm Syphon is installed and Browser integration was enabled. Re-run setup to repair the installed integration if needed.
- Confirm you loaded the folder containing manifest.json, not its parent. Use the included extension, not an unrelated copy.
- After an update, use **Reload** on its browser extension card, refresh the page and retry **Check connection**.
- If a development checkout previously registered Syphon, its connection script can point the browser at the checkout instead of the installed app. Use installed setup to restore the intended registration.
- On a managed browser, extension loading may be restricted. Check with its administrator rather than changing browser policies.

Remove the companion through the browser's extension-management page. Syphon uninstall does not automatically remove browser extensions or delete downloaded files.
