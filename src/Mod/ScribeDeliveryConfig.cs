using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Reads the admin <c>DeliveryMode</c>/radius settings from <c>worldconfig.json</c>'s
/// <c>worldConfigAttributes</c>. Read fresh on every call (never cached) so a live <c>/worldconfig set</c>
/// takes effect immediately. The server uses it to gate sends and run the range check; the client reads its
/// own <see cref="IWorldAccessor.Config"/> copy to decide whether the Create Assignments tab shows the
/// delivery-mode toggle/notice slots.
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
