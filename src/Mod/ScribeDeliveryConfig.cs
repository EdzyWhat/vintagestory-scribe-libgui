using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Reads the server admin `DeliveryMode`/radius settings (`assignment-delivery-mode` capability) off
/// this project's existing world-config mechanism (<c>worldconfig.json</c>'s
/// <c>worldConfigAttributes</c>, the same convention <c>scribeClockmakerRequiresTrait</c> uses) — read
/// fresh on every call, never cached, so a value
/// changed live (e.g. via the <c>/worldconfig set</c> server command) takes effect for the very next
/// read with no restart, per the capability's own requirement. Both sides read identically: the server
/// consults it to gate a send and run the range check; the client consults it (its own copy of
/// <see cref="IWorldAccessor.Config"/>) to decide whether the Create Assignments tab shows the
/// delivery-mode toggle/notice slots at all.
/// </summary>
internal static class ScribeDeliveryConfig
{
    public const string ModeConfigCode = "scribeDeliveryMode";
    public const string RadiusConfigCode = "scribeDeliveryRadius";

    private const string ModeInstant = "instant";
    private const string ModePhysical = "physical";
    private const string ModeHybrid = "hybrid";

    public static ScribeDeliveryMode ReadMode(ICoreAPI api) => api.World.Config.GetString(ModeConfigCode, ModeHybrid) switch
    {
        ModeInstant => ScribeDeliveryMode.AlwaysInstant,
        ModePhysical => ScribeDeliveryMode.AlwaysPhysical,
        _ => ScribeDeliveryMode.Hybrid,
    };

    public static int ReadRadius(ICoreAPI api) =>
        ScribeDeliveryPolicy.ClampRadius(api.World.Config.GetInt(RadiusConfigCode, ScribeDeliveryPolicy.DefaultRadiusBlocks));
}
