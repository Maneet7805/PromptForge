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
- Scale (ratio 1.25): 14, 17 (body), 21, 26, 33, 41. H1: `clamp(32px, 4.6vw, 52px)` (the first line is a full sentence).
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
- Allowed: the scroll-linked scene (the scroll is the person's action); button press; copy-button state; one short hero entrance on load.
- Animate only colour, background, opacity and transform.
- Not allowed: loops, shimmer, glow, float, parallax, `transition: all`, hover scale, per-section entrances.
- The default state of every element is the final, readable state. Motion is added on top.
- Reduced motion, narrow screens (under 900px), short screens and browsers without CSS scroll-driven animation get the same layout without pinning or movement.

## Imagery and icons

- No stock images, illustration, emoji or icon set.
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
