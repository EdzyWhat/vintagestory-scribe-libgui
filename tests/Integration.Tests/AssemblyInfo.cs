using Atlas.XUnit;
using Xunit;

// Atlas hosts at most one live server per process; scenario classes must run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]


// Scribe hard-depends on the `gui` (LibGUI) mod, which Atlas does not stage on its own; without it
// every scenario fails at SetBlock("scribe:scribelectern"). The .csproj copies gui_3.1.0.zip into the
// test output dir and this stages it by relative name (resolved against the test assembly's directory).
[assembly: AtlasMods("gui_3.1.0.zip")]
