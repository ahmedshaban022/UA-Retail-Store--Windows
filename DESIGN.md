---
name: StoreFlow
description: The ledger ink of a shop — a calm, paper-warm operating surface for a Windows retail counter.
colors:
  paper-canvas: "#F7F5F1"
  paper-raised: "#FFFFFF"
  paper-sunken: "#EFECE6"
  paper-overlay: "#FFFFFF"
  rule-subtle: "#E2DED6"
  rule-strong: "#8B8578"
  ink-primary: "#16181D"
  ink-secondary: "#4A4E57"
  ink-muted: "#666C73"
  ink-inverse: "#FFFFFF"
  petrol: "#0F5E63"
  petrol-deep: "#0C4C50"
  petrol-deeper: "#093D41"
  petrol-wash: "#DCEBEC"
  petrol-ink: "#FFFFFF"
  seal-critical: "#B3261E"
  seal-warning: "#8A5200"
  seal-success: "#2E6B3E"
  seal-info: "#1F5C99"
  seal-critical-wash: "#FBE9E7"
  seal-warning-wash: "#FBF0DC"
  seal-success-wash: "#E4F0E6"
  seal-info-wash: "#E3EEF8"
  focus-ring: "#0F5E63"
  selection-wash: "#DCEBEC"
  selection-ink: "#16181D"
  series-1: "#0F5E63"
  series-2: "#B26B00"
  series-3: "#2E6B3E"
  series-4: "#B3261E"
  series-5: "#1F5C99"
  series-6: "#6B4E9B"
typography:
  display:
    fontFamily: "Segoe UI / Tajawal"
    fontSize: "28px"
    fontWeight: 600
    lineHeight: 1.3
  headline:
    fontFamily: "Segoe UI / Tajawal"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: 1.4
  title:
    fontFamily: "Segoe UI / Tajawal"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.5
  body:
    fontFamily: "Segoe UI / Tajawal"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Segoe UI / Tajawal"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: 1.4
rounded:
  sm: "4px"
  md: "6px"
spacing:
  unit: "8px"
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "24px"
components:
  button-primary:
    backgroundColor: "{colors.petrol}"
    textColor: "{colors.petrol-ink}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "32px"
  button-primary-hover:
    backgroundColor: "{colors.petrol-deep}"
    textColor: "{colors.petrol-ink}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "32px"
  button-secondary:
    backgroundColor: "{colors.paper-raised}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "32px"
  button-danger:
    backgroundColor: "{colors.seal-critical}"
    textColor: "{colors.petrol-ink}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "32px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "32px"
  input-field:
    backgroundColor: "{colors.paper-raised}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.md}"
    padding: "10px 6px"
    height: "32px"
  nav-rail:
    backgroundColor: "{colors.paper-sunken}"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.sm}"
    padding: "10px 8px"
  chip-badge:
    backgroundColor: "{colors.paper-sunken}"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.sm}"
    padding: "2px 8px"
  card-surface:
    backgroundColor: "{colors.paper-raised}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.md}"
    padding: "16px"
---

# Design System: StoreFlow

## Overview

**Creative North Star: "The Ledger Ink"**

StoreFlow is an operating surface, not a showcase. Its world is the shop's own paper ledger: a warm off-white
ground the colour of a receipt, near-black ink for anything the owner must read precisely, and a single deep
petrol accent that appears only where the interface asks for a decision. The visual promise is the promise of
the ledger itself — what is written down is what happened, and it can be trusted to the piastre.

Density is the discipline here. Management screens run compact (32 px rows, 8 px spacing rhythm) so a thousand
SKUs are scannable; the point-of-sale screen runs generous (44 px hit targets, a 34 px grand total) so a
standing cashier reads it under shop lighting without a mouse. Both are the same token system at two
densities, which is why they feel like one product rather than two.

The interface is bilingual by construction rather than by a translation pass, so the layout engine treats
right-to-left as a first-class direction, not a mirror applied at the end. Arabic copy runs in Sakkal Majalla
with more leading than Latin; numeric columns use tabular figures so digits stack vertically in both
directions. Nothing in the interface asks the user to translate meaning — status is always carried by an icon
and a word as well as a colour, and every error states the next action instead of reporting a fault.

The one non-obvious element in the product is the scan chip: after a successful barcode scan, a high-contrast
confirmation chip materialises in the cart's quantity column showing the product name and unit price for
roughly 900 ms while the line's quantity ticks up. It exists so the cashier confirms a scan without leaving
the keypad or looking away from the price column. It is functional, not ornamental, and it is the only place
in the product where an element is allowed to appear without a visible cause.

**Key Characteristics:**

- Warm paper ground, near-black ink, one deep petrol accent used sparingly.
- Zero literal colour in view code — every colour is a semantic role resolved at runtime.
- Compact management tables, generous point-of-sale targets, one density token family.
- Status always carries an icon and a text label; colour never carries meaning alone.
- Tabular figures in every money and quantity column, in both scripts.
- Right-to-left is a layout mode, not a cosmetic flip.

## Colors

A warm-neutral ground carrying exactly one saturated accent, with four reserved "seal" colours that only
ever appear where they carry real meaning.

### Primary

- **Petrol** (#0F5E63): the single accent. Primary actions, focus rings, active navigation, chart series 1.
  Hover deepens to #0C4C50, pressed deepens further to #093D41, and its pale wash (#DCEBEC) marks a selected
  row or a subtle accent fill.
- **Petrol Wash** (#DCEBEC): the selection and subtle-fill colour. It replaces a heavy highlight so a
  selected row still reads as a row, not as a button.

### Secondary

- **Seal Critical** (#B3261E): destructive actions and failed states only.
- **Seal Warning** (#8A5200): warnings and degraded states only.
- **Seal Success** (#2E6B3E): confirmed, reconciled, completed states only.
- **Seal Info** (#1F5C99): informational emphasis only.

Each seal colour has a paired wash (#FBE9E7, #FBF0DC, #E4F0E6, #E3EEF8) used for the filled background of a
state badge, so a status can carry a large area of colour without shouting.

### Neutral

- **Paper Canvas** (#F7F5F1): the application ground.
- **Paper Raised** (#FFFFFF): cards, data grids, raised controls.
- **Paper Sunken** (#EFECE6): sunken panels, table headers, tool surfaces, badges.
- **Paper Overlay** (#FFFFFF): menus, tooltips, popups.
- **Rule Subtle** (#E2DED6): dividers, row separators, card outlines.
- **Rule Strong** (#8B8578): the visible border of an interactive control.
- **Ink Primary** (#16181D): body text and data.
- **Ink Secondary** (#4A4E57): supporting text and column headers.
- **Ink Muted** (#666C73): captions and de-emphasised text.

### Named Rules

**The Semantic-Only Rule.** A view may never contain a literal colour. Every colour in the product is a
semantic role (`surface.canvas`, `text.primary`, `state.critical`) that the theme file fills in. This is why
the whole application repaints when the owner changes the theme, and it is why the rebrand surface is a JSON
file rather than a recompile.

**The One Accent Rule.** Petrol covers no more than roughly a tenth of any screen. Its rarity is the point —
a screen where everything is accented is a screen where nothing is emphasised.

**The State Carries a Word Rule.** No state is communicated by colour alone. Every badge pairs its colour with
an icon and a text label, and every chart series is labelled in the legend.

**The Ring Is Visible Rule.** Keyboard focus is always drawn as a 2 px petrol ring offset 3 px outside the
control, never removed, never the only difference between focus and hover.

## Typography

**Display Font:** Segoe UI (Latin interface), Tajawal (Arabic interface)
**Body Font:** Segoe UI (Latin interface), Tajawal (Arabic interface)
**Label/Mono Font:** Segoe UI, with tabular lining figures for all numeric columns

**Character:** Segoe UI is a workhorse — it is what a cashier can read at arm's length without noticing the
type, and it is already on every Windows 11 machine, so an offline shop PC needs no font install. Tajawal is
the Arabic counterpart: a geometric, low-contrast skeleton with a Kufi-influenced spine that sits cleanly
beside Segoe UI, holds its shape at 12 px in a dense table, and covers both Western and Arabic-Indic
numerals. It is **embedded in the application** rather than installed on the machine, so the interface looks
identical on every shop PC and nothing has to be downloaded at install time. Sakkal Majalla, which ships with
Windows, is the last-resort fallback for any glyph Tajawal does not carry.

**Each language is set in the face designed for it.** The Arabic interface is drawn in Tajawal and the Latin
interface in Segoe UI, and switching the language re-applies the face along with the words. Tajawal carries
a complete and well-drawn Latin of its own, so a product name, a SKU or a figure inside an Arabic line reads
as one voice rather than as two fonts arguing — the same result a composite family would have given, reached
by a route the platform actually supports.

The face is part of the theme: `typography.uiFont` and `typography.arabicFont` name the faces a rebrand
wants, and a theme that names faces installed on the machine is honoured live. The embedded Tajawal is the
default for the Arabic interface, because an embedded face cannot be named by a theme file. See
[ADR-0004](docs/decisions/ADR-0004-typography.md) for why a single composite family carrying both scripts
is not possible on WPF.

The Arabic is the default locale, and the numerals follow the store's setting: Western Arabic numerals by
default, with an Eastern Arabic-Indic toggle.

### Hierarchy

- **Display** (SemiBold, 28 px, 1.3): the largest statement on a screen — the point-of-sale grand total and
  window-level titles. Deliberately rare.
- **Headline / Title** (SemiBold, 20 px, 1.4): screen titles and section headings.
- **Title** (SemiBold, 16 px, 1.5): card titles and sub-section headings.
- **Body** (Regular, 14 px, 1.5): everything else — labels, table cells, message text.
- **Label / Caption** (SemiBold or Regular, 12 px, 1.4): column headers, captions, badge text. Captions use
  muted ink; section labels use secondary ink and SemiBold.

### Named Rules

**The Scale Multiplies Rule.** Typography scale is a single multiplier in the theme file applied to 12 / 14 /
16 / 20 / 28 / 34 px. Changing the scale changes the whole application at once; no screen carries its own
font size.

**The Arabic Leads Rule.** Arabic sets with more leading than Latin because its vertical joins need it. The
default line heights above apply to Latin; Arabic content uses roughly 1.6× leading, and Arabic text must
always render with `TextOptions.TextFormattingMode=Display` — `Ideal` breaks the ligatures.

**The Figure Alignment Rule.** Any column of money, quantity, or percentage uses tabular figures and
right alignment. An aligned column is scannable; a ragged one is not.

## Layout

The application is a shell: a fixed navigation rail on the leading edge, a command bar across the top of the
working area, and the content surface beneath. The rail carries the product identity and the global switches
(locale, theme); the command bar carries screen-level actions; the content surface carries one job.

Spacing follows an 8 px base unit (`spacing.unit = 8`) with a 4 px half-step for intra-component gaps. Card
padding is 16 px, field padding is 10 px vertical / 6 px horizontal, badge padding is 8 px horizontal / 2 px
vertical, table cell padding is 10 px horizontal.

Density is a theme value, not a hard-coded choice:

| Token | Value | Where it applies |
| --- | --- | --- |
| `density.rowHeight` | 32 px | data-grid rows, list rows on management screens |
| `density.controlHeight` | 32 px | buttons, text fields, combo boxes |
| `density.spacingUnit` | 8 px | the spacing rhythm |
| `density.posHitTarget` | 44 px | minimum interactive target on the point-of-sale screen |

Responsive behaviour on a desktop is window-driven, not breakpoint-driven: the shell reflows to the window
width, grids keep a minimum column width and scroll horizontally below it, and the point-of-sale screen keeps
its totals rail pinned to the trailing edge at every width.

Right-to-left mirrors the layout through `FlowDirection` on the root, so the rail moves to the right edge and
the command bar flips its order. Spacing tokens are symmetric by construction because WPF `Margin` and
`Thickness` do not mirror automatically; directional glyphs are mirrored explicitly and logos are never
mirrored.

## Elevation & Depth

This system uses **no shadows**. Depth is conveyed entirely by tonal layering — canvas, sunken, raised,
overlay — plus a single hairline border in rule-subtle or rule-strong. A shop counter screen is read in
seconds, often under glare, and a soft drop shadow on every card turns into visual noise that competes with
the numbers. Elevation is therefore expressed by *how much lighter a surface is than what sits on it*, and by
the border weight: 1 px rule-strong for an interactive control that must read as pressable, 1 px rule-subtle for
a container that must not.

The single exception is the focus ring, which is deliberately a hard 2 px outer border rather than a glow —
it must be unambiguous at a glance and at 200 % text scaling.

### Shadow Vocabulary

- None. There are no shadow tokens in the theme schema, and none may be introduced without a reason that
  tonal layering cannot serve.

### Named Rules

**The Flat-By-Default Rule.** Surfaces are flat at rest. Separation comes from tone and a hairline rule, never
from a drop shadow.

**The Border Means Pressable Rule.** A control with a visible rule-strong border is interactive; a container
with a rule-subtle border is not. The two are never mixed.

## Shapes

Radii are small and consistent: 6 px for controls and cards (`shape.cornerRadius`), 4 px for badges and list
items, and 0 for sharp elements such as dividers and the progress bar. Border width is 1 px throughout
(`shape.borderWidth`).

There is no pill shape, no fully rounded chip, and no circular ornament. The form language is that of a printed
ledger: rectangular fields, ruled lines, a single radius small enough to read as a printed form rather than a
consumer app. Corners are snapped to device pixels so a 1 px rule stays 1 px on a 150 % display.

## Components

### Buttons

- **Shape:** 6 px radius, 1 px border, 32 px minimum height, 16 px horizontal padding.
- **Primary:** petrol fill, petrol-ink text, petrol border. One per screen — the action the screen exists to
  perform.
- **Secondary (default):** raised-paper fill, ink-primary text, rule-strong border. The workhorse.
- **Danger:** seal-critical fill, petrol-ink text. Destructive confirmation only.
- **Ghost:** transparent fill and border, ink-secondary text. Tertiary and toolbar actions.
- **Hover / Focus:** hover drops the fill to 90 % opacity, pressed to 75 %, and keyboard focus reveals the
  2 px petrol focus ring offset 3 px outside the control. Disabled drops to 45 % opacity and resets the cursor.
- Transitions are opacity only. No scale, no lift, no colour tween — a pressed button must still be readable
  as the same button.

### Chips (badges)

- **Style:** 4 px radius, sunken-paper fill, rule-subtle border, 8 × 2 px padding, caption type.
- **State:** a status badge pairs its wash colour with an icon and a word. A badge reading "Paid" or "Low
  stock" is legible in greyscale and to a colour-blind reader.

### Cards / Containers

- **Corner Style:** 6 px radius.
- **Background:** raised paper over canvas.
- **Shadow Strategy:** none — see Elevation & Depth.
- **Border:** 1 px rule-subtle.
- **Internal Padding:** 16 px.

Sunken panels use sunken paper with the same rule-subtle border and no padding of their own; they are used for
table headers and secondary tool surfaces rather than as content cards.

### Inputs / Fields

- **Style:** 1 px rule-strong border, raised-paper fill, 6 px radius, 10 × 6 px padding, 32 px minimum height.
- **Focus:** the 2 px petrol ring appears outside the border; on hover the border itself shifts to petrol.
- **Error / Disabled:** disabled drops to 50 % opacity; error uses seal-critical on the border with the message
  beneath the field in the same colour *and* in words.
- Text is vertically centred and set with `TextFormattingMode=Display` so Arabic shapes correctly.

### Navigation

The rail is a sunken strip on the leading edge with the product name at the top, the screen list beneath, and
the locale and theme switches at the bottom. Each item is 8 px vertical padding with a 4 px radius, ink
secondary by default, petrol when active, and a sunken fill on hover. The rail mirrors wholesale under
right-to-left; the icons are directional only where the meaning is directional.

### Data grid

The grid is the product's main reading surface. Row height 32 px, 10 px horizontal cell padding, horizontal
grid lines only in rule-subtle, vertical grid lines transparent, header on sunken paper in ink secondary with
SemiBold 12 px type. Rows alternate between transparent and sunken paper. Row and column virtualization are
always enabled — a ten-thousand-product list must scroll without allocating. Selection uses the selection
wash and selection ink, never an accent fill.

### The scan chip

The signature element. After a successful scan, a chip materialises in the cart's quantity column carrying the
product name and the unit price, in high contrast against the row, for roughly 900 ms, while the line's
quantity increments. It is the only element in the product that appears without a visible cause, and it earns
that by confirming an event the user caused with their hands rather than with the mouse.

## Do's and Don'ts

### Do:

- **Do** reference a semantic role for every colour, in every view: `{DynamicResource Text.Primary}`, never
  `#16181D`.
- **Do** pair every status colour with an icon and a word.
- **Do** use the 8 px spacing rhythm and the density tokens rather than ad-hoc margins.
- **Do** set tabular figures and right alignment on every money and quantity column.
- **Do** render Arabic with `TextOptions.TextFormattingMode=Display` and mirror the shell through
  `FlowDirection`.
- **Do** keep the primary accent to one action per screen.
- **Do** draw the focus ring on every focusable control, including custom ones.
- **Do** add a new semantic role to the theme schema before using a new colour anywhere.

### Don't:

- **Don't** put a literal colour in a view, a control template, or a converter — it breaks the rebrand surface
  and fails the semantic-only rule.
- **Don't** introduce a shadow, a gradient, a glass effect, or a glow. The ledger has no depth effects.
- **Don't** communicate state with colour alone, and don't rely on hover as the only affordance.
- **Don't** mirror a logo or a non-directional icon under right-to-left.
- **Don't** put a SKU, barcode, phone number, or email inside an Arabic sentence without isolating it
  (U+2066 … U+2069) and forcing left-to-right.
- **Don't** hard-code a font size in a view — change the theme scale instead.
- **Don't** show a raw exception string; state the condition and the next action in the user's language.
- **Don't** round money for display twice — format from minor units once.
