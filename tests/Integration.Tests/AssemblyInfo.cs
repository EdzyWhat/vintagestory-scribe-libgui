using Atlas.XUnit;
using Xunit;

// Atlas hosts at most one live server per process; scenario classes must run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// The mod itself is staged via the ProjectReference AtlasMod=true sugar in the .csproj
// (see atlas-mods.generated.txt, written at build time), so no path is declared here.

// Scribe has a HARD dependency on the `gui` (LibGUI) mod, which Atlas does not otherwise stage
// (it never sees the player's installed Mods folder). Without it the mod loader skips scribe and
// every scenario fails at SetBlock("scribe:scribelectern"). The .csproj copies the installed
// gui_3.1.0.zip into the test output dir; this stages it by the resulting relative name (AtlasMods
// paths resolve against the test assembly's directory). See Integration.Tests.csproj.
[assembly: AtlasMods("gui_3.1.0.zip")]
