# ADR-0004 — Typography: one face per locale, with the Arabic face embedded

- **Status:** Accepted
- **Date:** 2026-10-27
- **Phase:** 0 — Foundation

## Context

The product ships both Arabic (the default) and English, and the theme file is the rebrand
surface, so the faces are theme data: `typography.uiFont` and `typography.arabicFont`.

Two requirements pull in opposite directions:

1. The Arabic face must be **embedded**. A shop PC is not going to install a font, and the
   interface has to look the same on every machine in every shop.
2. Latin text inside an Arabic line — a SKU, a user name, a figure — should not look like it was
   pasted in from another document.

The obvious way to satisfy both is a single WPF *composite* family: Latin first, Arabic second,
with WPF walking the list for every character and using the first family that has the glyph.

## The candidates

**Tajawal** over **Cairo**, decided earlier and unchanged. Both cover Arabic-Indic numerals
(٠-٩) and the Latin range, and both are SIL Open Font License. Tajawal ships **static** weights,
so WPF can render a true bold; Cairo is a **variable** font (`Cairo[slnt,wght].ttf`, 600 KB) and
WPF cannot weight a variable font, so "Cairo Bold" would have been faked by WPF. Tajawal's
regular and bold cuts are 60 KB each.

The embedding mechanism is WPF's **private font collection**: a folder called `Fonts` in the
application's own resources. The faces are addressed by name from that collection.

## What was tried, and what the platform said

**A comma separated family list** (`Segoe UI, Tajawal`). This is WPF's normal way to get a
per-character fallback. It cannot hold an embedded face: the pack URI that addresses one
contains commas itself, and the family's own separator is a comma, so the URI is shredded by the
parser. The list resolves to "Segoe UI" and the URI is dropped.

**A composite font resource** (WPF's composite font format, sample kept in
`docs/samples/StoreFlow.CompositeFont.xml`). Two findings killed it:

1. A composite font names its faces and maps character ranges to those names. It cannot name an
   *embedded* face, because the embedded family only exists inside the private collection.
2. WPF only treats a folder as a private font collection if every file in it can be read. A
   composite font WPF cannot parse takes the **whole collection** down, and the symptom is a face
   that quietly resolves to the platform fallback — indistinguishable, from the outside, from a
   font that failed to load. This was observed directly: one malformed composite file made the
   embedded Tajawal face disappear, and the interface fell back to the system Arabic face without
   a word of complaint.

**The family has to come from the collection.** A hand-written URI
(`pack://application:,,,/Fonts/#Tajawal`) does *not* resolve to the embedded face — it resolves to
the platform fallback. The family object has to be taken from `Fonts.GetFontFamilies`, which
hands out a family whose source is relative to the collection.

## The decision

**Each language is set in the face designed for it.**

- The Arabic interface is set in **Tajawal**, embedded in the application.
- The Latin interface is set in the **Latin face** (`Segoe UI` by default, or whatever the theme
  names), with a system Arabic face behind it so a stray Arabic character still has a home.

Tajawal carries a complete and well-drawn Latin, so a product name or a figure inside an Arabic
line reads as one voice rather than as two fonts arguing — the same outcome the composite was
meant to produce, reached by a route the platform actually supports.

A theme that names other faces is honoured live: the faces are expressed as WPF's native family
list and resolve against the fonts installed on the machine. The embedded face is the default,
not a cage.

## Consequences

- A shop PC needs no font installation, and the Arabic interface looks identical on every
  machine.
- A rebrand that names an installed font works at run time. A rebrand that wants a *different
  embedded* face means shipping a different set of faces in the `Fonts` folder, which is a build
  change rather than a theme change.
- The theme's `uiFont` is honoured in the Latin interface; in the Arabic interface the embedded
  face wins, because an embedded face cannot be named by a theme file.

## Pinned by

- `tests/StoreFlow.Desktop.Tests/EmbeddedFontTests.cs` — the faces travel with the application,
  the embedded face resolves, it carries a regular and a bold cut, and the Arabic interface is
  drawn in it.
- `tests/StoreFlow.Desktop.Tests/PrivateFontCollectionTests.cs` — the private font collection
  holds the embedded face and **nothing else**, so a file WPF cannot read can never take the
  collection down again.
