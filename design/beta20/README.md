# Beta 20 visual exploration

Four generated visual concepts inspired by the supplied VLC Redesign community reference. These are concept images with illustrative data, not screenshots of implemented behavior. Existing application code is unchanged.

1. **Violet Studio:** graphite surfaces, lavender emphasis, category navigation, spacious queue and horizontal monitor. Closest continuation of Syphon's identity.
2. **Graphite & Amber:** warm black, amber controls, top filters and a download transport strip. Closest interpretation of the VLC reference.
3. **Soft Light:** pearl surfaces, ink text, iris actions and a clean queue table. Explores readability and a lighter desktop presence.
4. **Midnight Current:** navy surfaces, ice-blue accents, queue beside an integrated transfer inspector, circular progress and a portrait monitor. Most substantial layout departure.

## Proposed sizing

Explore 15–16 logical-pixel body text, 13–14 secondary labels, 40–44px controls, and 64–80px queue rows. Validate in a real 1280×800 window at the owner's Windows scaling before choosing final dimensions. Image presentations do not establish actual display scale.

## Proposed motion

- Navigation: 140–180ms opacity transition with at most 6px translation; keep the shell still.
- Transfer inspector: 180–220ms slide and fade; keep selected queue position stable.
- Progress: interpolate known progress over 200–300ms without inventing intermediate network samples. Unknown totals remain indeterminate.
- Pause/resume: immediate button feedback and a short state crossfade; avoid moving rows during progress refreshes.
- Floating monitor: 180ms anchored expansion; no bounce. Reduced motion uses immediate state changes or a brief fade.

Motion is proposed here, not demonstrated by the static images. Some concept navigation, category counts and inspector treatments are exploratory rather than commitments to new functionality.

## Generation

Created with the built-in image generation tool. The supplied Player and Settings images were visual references only; extracted reference files are in `vlc-reference/`.

Prompt direction: high-fidelity Windows download-manager mockups showing a large Downloads window, New Download dialog and floating transfer monitor; borrow VLC's integrated titlebar, flat layered surfaces, quiet broad content zones, icon navigation and bottom transport controls. Retain file/video/audio transfer workflows, pause/resume, destination, speed limits, queue and history. Prioritize readable type, generous controls and flat rows. Explore four genuinely different layouts and palettes as described above. Avoid deep neumorphic shadows, neon borders, nested cards and tiny text.
