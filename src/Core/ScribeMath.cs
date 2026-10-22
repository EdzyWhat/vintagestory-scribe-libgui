namespace Scribe.Core;

/// <summary>Small numeric helpers shared by the Mod's animation and lighting code.</summary>
public static class ScribeMath
{
    /// <summary>Clamps <paramref name="v"/> to [0, 1].</summary>
    public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}
