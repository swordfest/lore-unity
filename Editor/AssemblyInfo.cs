using System.Runtime.CompilerServices;

// Exposes internal types (LoreParse, LoreHistoryEntry) to the EditMode test
// assembly so they can be unit-tested without being part of the public API.
[assembly: InternalsVisibleTo("LoreVcs.Editor.Tests")]
