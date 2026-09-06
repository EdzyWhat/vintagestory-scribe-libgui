using System.Runtime.CompilerServices;

// Lets Integration.Tests exercise Mod-layer `internal` helpers directly (e.g.
// ScribeReadViewFilter's pure filter/collapse logic) without a full Atlas world boot, mirroring
// how Core.Tests already needs no such grant (Core has no `internal` surface these tests touch).
[assembly: InternalsVisibleTo("Integration.Tests")]
