# Migrating from `Span` to `TextSpan`

The document viewer's reference and span collections are moving from the text editor's `Microsoft.VisualStudio.Text.Span` to dnSpy's own `dnSpy.Contracts.Decompiler.TextSpan`. This lets them live in assemblies that don't depend on the Visual Studio text editor, so hosts without a UI (such as `dnSpy.Console`) can use them.

The `Span` members still work but are marked `[Obsolete]`. **They will be removed in dnSpy 7.0.** Extensions that keep using them will get warnings until then, and will have to be recompiled against the new members before they run on 7.0.

## What to change

| Obsolete member | Replacement |
|---|---|
| `SpanData<TData>.Span` | `SpanData<TData>.TextSpan` |
| `new SpanData<TData>(Span, TData)` | `new SpanData<TData>(TextSpan, TData)` |
| `SpanDataCollection<TData>.Find(Span)` | `SpanDataCollection<TData>.Find(TextSpan)` |
| `SpanDataCollectionBuilder<TData>.Add(Span, TData)` | `SpanDataCollectionBuilder<TData>.Add(TextSpan, TData)` |
| `TextReference.Span` | `TextReference.TextSpan` |
| `new TextReference(object?, Span)` | `new TextReference(object?, TextSpan)` |
| `new TextReference(object?, DecompilerReferenceFlags, Span)` | `new TextReference(object?, DecompilerReferenceFlags, TextSpan)` |
| `ReferenceInfo.ToTextReference(Span)` | `ReferenceInfo.ToTextReference(TextSpan)` |

`TextSpan` has the same `Start`, `End`, `Length`, `IsEmpty`, `Contains(int)` and `IntersectsWith` members as `Span`, so most code only needs `.Span` renamed to `.TextSpan`. It's in the `dnSpy.Contracts.Decompiler` namespace.

## Code that talks to the text editor

Text editor APIs (`SnapshotSpan`, `ITextSnapshot.CreateTrackingSpan()`, classification and tagger spans) still use `Span`. Convert at that boundary with the extension methods in `dnSpy.Contracts.Text.TextSpanConversions`:

- `TextSpan.ToVsSpan()` returns a `Span`
- `Span.ToDsTextSpan()` returns a `TextSpan`

```csharp
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

// Before
var snapshotSpan = new SnapshotSpan(snapshot, spanData.Span);
foreach (var info in documentViewer.Content.ReferenceCollection.Find(span.Span)) { ... }

// After
var snapshotSpan = new SnapshotSpan(snapshot, spanData.TextSpan.ToVsSpan());
foreach (var info in documentViewer.Content.ReferenceCollection.Find(span.Span.ToDsTextSpan())) { ... }
```

The helpers are deliberately not called `ToSpan()` / `ToTextSpan()`. `dnSpy.Roslyn` already has methods with those names for Roslyn's `Microsoft.CodeAnalysis.Text.TextSpan`, and sharing the names would make calls ambiguous in files that import both namespaces. For the same reason, if a file imports both `dnSpy.Contracts.Decompiler` and `Microsoft.CodeAnalysis.Text`, use a `using` alias for whichever `TextSpan` it needs.

## Behaviour differences

- `TextSpan.ToString()` prints `[start,end)` where `Span` printed `[start..end)`, so `SpanData<TData>.ToString()` changes the same way. It's only meant for debugging.
- Everything else behaves the same: both types reject a negative start or length, and `IntersectsWith` treats spans that only touch as intersecting, like `Span.IntersectsWith`.
