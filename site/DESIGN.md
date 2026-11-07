# Design: PromptForge project page

This file is the single source for how the page looks and reads. If the page and this file disagree, fix whichever is wrong in the same change. The values below are also in `assets/style.css` as CSS variables.

## Direction

- **Product:** a free Windows tray app that rewrites a rough prompt into a clearer one, shown for review before anything is pasted.
- **Character:** exact, calm, checkable. Not "clean, modern, sleek".
- **World:** a text editor. Selection highlight, a diff, key caps, a plain window.
- **Moments:** one big one: the draft is selected and rewritten as you scroll. One small one: the review window with Replace and Keep original at equal weight.
- **Signature:** the marker-yellow highlight. It appears only where text is selected or changed.
- **Swap test:** put another product's name in the hero and the scroll scene (selection, key caps, review window, nothing auto-submitted) no longer fits it.

How the direction was chosen: from the project brief, the project's own docs, and the rules in the `frontend-design` skill and the `heydamir/unslop` repo. No real websites were compared (UXKIN was not connected).

## Version 2: what the reference study changed

Studied (animated, 2025 and 2026): the Scrolltide template library (Nightjar, Lacuna, Mobula, Parrot), plus the live pages of Cursor, Lovable, Linear, Raycast and Resend, and six reference images from the owner. Ideas only; nothing is copied.

What the good ones do, and what this page now does:

- **The hero is the product working.** Nightjar streams an answer word by word; Lacuna types a headline and crosses a word out. Here the headline is a rough draft that edits itself into the real sentence: wrong words are struck in yellow, the right ones grow in and flash. It plays once (about 4 seconds), then the text, lead and button settle. A "Replay the edit" link plays it again.
- **One focal point, few words.** References put 25 to 40 words in the first screen (Lacuna about 35, Lovable 38). The hero here is the headline, one lead of about 25 words, one button.
- **A signature texture instead of a photo.** Nightjar and Lacuna use 1-bit dither as their image. This page has no photo, so a dithered dot field (chalk dots on ink, drawn on a canvas) sits behind the hero and is heated by the pointer. It is texture, not glow, and it uses no new colour.
- **Product UI is the image.** Mobula and Cursor frame a real-looking window. The scroll scene below keeps the review-window mock.
- **Typography:** large, short, one family. References use a big weight-500 to 600 sans, with mono for the working text. This page uses weight 500 only for headings and mono for drafts.
- **Not taken from the owner's images:** photo-led layouts (no photo exists), purple and blue glow gradients (images 4 to 6; banned), all-caps tracked labels, and a brush underline on one headline word.

Rules changed by v2: the hero may animate once on load and has one ambient dither field (see Motion).

## Colour (three colours)

| Token | Role | Hex |
| --- | --- | --- |
| `--ink` (primary) | Dark sections; text on chalk | `#10212E` |
| `--chalk` (secondary) | Light sections; text on ink | `#EBF0F2` |
| `--mark` (accent) | Selection highlight; download button fill | `#FFD23F` |

Derived, not new colours: muted text (`#AEB6BB` on ink, `#4D5B65` on chalk) and hairlines (ink or chalk at 14% opacity).

| Pair | Contrast | WCAG AA |
| --- | --- | --- |
| Chalk on ink | 14.3 | Pass |
| Muted `#AEB6BB` on ink | 8.0 | Pass |
| Muted `#4D5B65` on chalk | 6.1 | Pass |
| Ink text on mark (button, highlight) | 11.4 | Pass |
| Mark on ink (focus ring) | 11.4 | Pass |
| Mark on chalk | 1.3 | Fail |

- Mark is never text or a border on chalk. On chalk it is only a fill behind ink text.
- No gradients, glows, tinted shadows or colour washes. No purple.
- No dark-mode switch: the page already alternates ink and chalk sections.

## Typography

- Sans: `"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`. The audience is on Windows. No downloaded fonts.
- Mono (draft text, key caps, commands): `"Cascadia Mono", Consolas, ui-monospace, monospace`.
- Weights: 400 and 500 only. Headings are 500; size does the work. No italics, no gradient text.
- Scale (ratio 1.25): 14, 17 (body), 21, 26, 33, 41. H1: `clamp(34px, 5vw, 60px)` (the first line is a full sentence).
- Line height: body 1.55, headings 1.15. Lines 62 characters or fewer.
- Sentence case. No all-caps tracked labels. No eyebrow above every heading.

## Spacing and layout

- 4px grid: 4, 8, 12, 16, 24, 32, 48, 64, 96.
- Sections: 96px padding on desktop, 64px on mobile. Content max width 1120px. 12 columns. 16px side gutter on mobile, 32px from 720px.
- Left-aligned. Text on 5 columns, the scene on 7. No stacked centred hero, no row of identical cards.
- Equal things get equal size: Replace and Keep original are the same size.

## Shape and depth

- Radius: 0, 4, 8. Inner radius is the outer radius minus the padding.
- 1px hairline borders. No shadows. Depth comes from ink against chalk.

## Components

- **Primary button:** mark fill, ink text, 44px high, 4px radius. Hover: 8% darker. Press: 1px down. One per view.
- **Link:** underlined always, in the section's text colour.
- **Key cap:** mono 14px, hairline border, 4px radius, thicker bottom border.
- **Code block:** mono, hairline, with a copy button.
- **Table:** a real `<table>`, hairline row dividers, no zebra, no cards.
- **Focus ring:** 3px, mark on ink sections, ink on chalk sections, 2px offset.
- **Highlight:** mark fill behind ink text. The signature. Never on a heading.

## Motion

- One easing: `cubic-bezier(0.22, 1, 0.36, 1)`. Durations: 120ms (press), 200ms (state), 500ms (hero entrance).
- Allowed: the scroll-linked scene (the scroll is the person's action); button press; copy-button state; the hero headline editing itself once on load; one ambient dither field in the hero only (stops when off screen or the tab is hidden, drawn once if motion is reduced).
- Animate only colour, background, opacity and transform.
- Not allowed: other loops, shimmer, glow, float, parallax, `transition: all`, hover scale, per-section entrances.
- The default state of every element is the final, readable state. Motion is added on top.
- Reduced motion, narrow screens (under 900px), short screens and browsers without CSS scroll-driven animation get the same layout without pinning or movement.

## Imagery and icons

- No stock images, illustration, emoji or icon set. One texture: the chalk dither field in the hero.
- The scene is an HTML/CSS mock, labelled as an example and not a recorded run. Real screenshots only if the owner supplies them.
- Favicon: a new ink and mark "P" (`assets/favicon.svg`). The app icon is not used.

## Voice

- Plain verbs, active voice, sentence case. No exclamation marks. No "AI-powered", "seamless", "unlock", "supercharge".
- Buttons say what happens: "Download PromptForge.exe".
- Every claim traces to `README.md`, `docs/design/rewrite-intent.md` or the code. No testimonials, logos, counts or benchmark numbers.

## Do not

- Purple, gradients, glass, glow, emoji bullets, identical card rows, gradient text.
- Third-party requests, analytics, cookies, external fonts or scripts.
- Mark yellow as text or border on chalk.
- More than one scroll-linked scene.
