using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameArchitecture.Views;

/// <summary>
/// Placeholder art for Spider-Essence: a red orb and a blue orb that glow and circle each other.
/// The textures are generated in code so there's nothing to load. If I get a real sprite later,
/// just change <see cref="DrawGlow"/> and <see cref="DrawCores"/>.
/// </summary>
/// <remarks>
/// Orbs get drawn in two passes so the glow lightens whatever's behind it:
/// <list type="number">
/// <item>glows, in a batch started with <see cref="BlendState.Additive"/></item>
/// <item>cores, in a regular alpha-blended batch</item>
/// </list>
/// Use <see cref="SamplerState.LinearClamp"/> for both or the circles look jagged.
/// </remarks>
public sealed class EssenceArt
{
    public static readonly Color Red = new(255, 55, 70);
    public static readonly Color Blue = new(50, 140, 255);

    private const int TextureSize = 64;

    private static EssenceArt _shared;

    private EssenceArt(GraphicsDevice device)
    {
        Device = device;
        Glow = MakeTexture(device, d => MathF.Pow(MathF.Max(0, 1 - d), 2.2f));

        // solid circle with an anti-aliased edge
        Disc = MakeTexture(device, d => Math.Clamp((1 - d) * TextureSize / 3f, 0, 1));
    }

    /// <summary>Graphics device these textures were made on</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Soft white glow that fades out toward the edge</summary>
    public Texture2D Glow { get; }

    /// <summary>Plain white circle</summary>
    public Texture2D Disc { get; }

    /// <summary>
    /// Gets the shared art for a graphics device (creates it the first time)
    /// </summary>
    public static EssenceArt For(GraphicsDevice device)
    {
        if (_shared == null || _shared.Device != device || _shared.Glow.IsDisposed)
            _shared = new EssenceArt(device);
        return _shared;
    }

    /// <summary>
    /// Positions of the two orbs around the pair's center
    /// </summary>
    /// <param name="center">Center of the pair</param>
    /// <param name="orbRadius">Radius of each orb's core</param>
    /// <param name="spin">How far they've rotated, in radians</param>
    public static (Vector2 Red, Vector2 Blue) OrbPositions(Vector2 center, float orbRadius, float spin)
    {
        // orbit on a flattened ellipse so it looks 3D-ish
        var offset = new Vector2(MathF.Cos(spin), MathF.Sin(spin) * 0.45f) * orbRadius * 1.1f;
        return (center + offset, center - offset);
    }

    /// <summary>
    /// Draws the glow for a pair (use an additive batch)
    /// </summary>
    public void DrawGlow(SpriteBatch batch, Vector2 center, float orbRadius, float spin, float alpha = 1, float pulse = 1)
    {
        var (red, blue) = OrbPositions(center, orbRadius, spin);
        float scale = orbRadius * 3.2f * pulse / (TextureSize / 2f);
        var origin = new Vector2(TextureSize / 2f);
        batch.Draw(Glow, red, null, Red * (0.85f * alpha), 0, origin, scale, SpriteEffects.None, 0);
        batch.Draw(Glow, blue, null, Blue * (0.85f * alpha), 0, origin, scale, SpriteEffects.None, 0);
    }

    /// <summary>
    /// Draws the orb cores (use a normal alpha batch). Whichever orb is in front gets drawn last.
    /// </summary>
    public void DrawCores(SpriteBatch batch, Vector2 center, float orbRadius, float spin, float alpha = 1)
    {
        var (red, blue) = OrbPositions(center, orbRadius, spin);
        bool redInFront = MathF.Sin(spin) > 0;
        if (redInFront)
        {
            DrawCore(batch, blue, Blue, orbRadius, alpha);
            DrawCore(batch, red, Red, orbRadius, alpha);
        }
        else
        {
            DrawCore(batch, red, Red, orbRadius, alpha);
            DrawCore(batch, blue, Blue, orbRadius, alpha);
        }
    }

    private void DrawCore(SpriteBatch batch, Vector2 position, Color color, float radius, float alpha)
    {
        var origin = new Vector2(TextureSize / 2f);
        float scale = radius / (TextureSize / 2f);
        batch.Draw(Disc, position, null, color * alpha, 0, origin, scale, SpriteEffects.None, 0);

        // bright spot a little off center so it looks lit from inside
        var highlight = position + new Vector2(-0.2f, -0.25f) * radius;
        batch.Draw(Disc, highlight, null, Color.Lerp(color, Color.White, 0.75f) * alpha, 0, origin, scale * 0.45f,
            SpriteEffects.None, 0);
    }

    /// <summary>
    /// Makes a white texture where each pixel's alpha = <paramref name="shape"/>(distance from center,
    /// 0 to 1 at the edge). Color is premultiplied by alpha since that's what SpriteBatch wants.
    /// </summary>
    private static Texture2D MakeTexture(GraphicsDevice device, Func<float, float> shape)
    {
        var data = new Color[TextureSize * TextureSize];
        float half = TextureSize / 2f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float a = Math.Clamp(shape(MathF.Sqrt(dx * dx + dy * dy)), 0, 1);
                data[y * TextureSize + x] = new Color(a, a, a, a);
            }
        }

        var texture = new Texture2D(device, TextureSize, TextureSize);
        texture.SetData(data);
        return texture;
    }
}
