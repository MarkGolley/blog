# AislePilot Carousel Redesign Spec

## Goals

- Replace the unstable day bar with a single clear pattern: a day-selector chip row.
- Make mobile feel deliberate: full-width primary card, day selector above the card, no shape-shifting dots.
- Keep desktop focused: centered feature card, quieter neighboring previews, cleaner selector alignment.
- Reduce ornamental chrome so the meal card and photography carry the interface.

## Interaction Rules

- The day selector is always a labelled chip row, never a hybrid of dots and pills.
- Active state changes color and emphasis only. Chip width stays stable.
- Mobile keeps swipe and arrows for navigation, but the day chip row sits above the card for quick jumping.
- Desktop keeps the selector below the stage and aligns it to the card width rather than the full carousel shell.
- Day labels use short visible names (`Mon`, `Tue`, etc.) with full accessible labels on the buttons.

## Mobile Layout

- Show a single primary day card at a time.
- Remove visible side peeks and ghost previews on narrow screens.
- Keep the status line compact and the chip row horizontally scrollable if needed.
- All day chips remain fully labelled and tappable.

## Desktop Layout

- Keep one centered feature card with muted adjacent previews.
- Tone down stage glow, border noise, and active-state lift.
- Use a contained chip rail with consistent spacing and equal visual rhythm.

## Polish Rules

- No width or label reveal animation on the day selector.
- Respect reduced motion by avoiding decorative chip animations.
- Prefer subtle shadow/elevation changes over extra gradients and underlines.
