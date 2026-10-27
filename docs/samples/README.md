# Reference samples

Files kept for reference rather than shipped in the application.

| File | What it is |
|---|---|
| `StoreFlow.CompositeFont.xml` | WPF's composite font format, in the shape the product would use it. It is **not** built into the application — see the note inside the file and [ADR-0004](../decisions/ADR-0004-typography.md) for why. |

A sample lives here rather than in the source tree because the `Fonts` folder of the desktop
assembly is a private font collection: WPF only treats it as one if every file in it can be read,
and a file it cannot read takes the whole collection down with it. See
[ADR-0004](../decisions/ADR-0004-typography.md) for what that costs and
`tests/StoreFlow.Desktop.Tests/PrivateFontCollectionTests.cs` for the test that keeps it true.
