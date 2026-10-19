using System.Runtime.CompilerServices;

// Lets Integration.Tests exercise Mod-layer `internal` helpers (e.g. ScribeReadViewFilter) without
// booting an Atlas world.
[assembly: InternalsVisibleTo("Integration.Tests")]
