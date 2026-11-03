using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameArchitecture.Views;

/// <summary>
/// The placeholder look of Spider-Essence: a glowing red orb and a glowing blue orb circling
/// each other. The textures are made in code, so there's no art to load; swap in a sprite
/// later by changing <see cref="DrawGlow"/> and <see cref="DrawCores"/>.
/// </summary>
/// <remarks>
/// Draw an orb in two passes, so the glow brightens whatever is behind it:
/// <list type="number">
/// <item>the glows, in a batch begun with <see cref="BlendState.Additive"/></item>
/// <item>the cores, in a normal (alpha blended) batch</item>
/// </list>
/// Use <see cref="SamplerState.LinearClamp"/> for both, so the circles are smooth.
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

        // A solid disc with a soft (anti-aliased) edge
        Disc = MakeTexture(device, d => Math.Clamp((1 - d) * TextureSize / 3f, 0, 1));
    }

    /// <summary>The device the textures belong to</summary>
    public GraphicsDevice Device { get; }

    /// <summary>A soft white glow, fading to nothing at its edge</summary>
    public Texture2D Glow { get; }

    /// <summary>A white disc</summary>
    public Texture2D Disc { get; }

    /// <summary>
    /// The shared art for a graphics device (made the first time it's asked for)
    /// </summary>
    public static EssenceArt For(GraphicsDevice device)
    {
        if (_shared == null || _shared.Device != device || _shared.Glow.IsDisposed)
            _shared = new EssenceArt(device);
        return _shared;
    }

    /// <summary>
    /// Where the two orbs of a pair are, around its center
    /// </summary>
    /// <param name="center">The middle of the pair</param>
    /// <param name="orbRadius">The radius of each orb's core</param>
    /// <param name="spin">How far round the orbs have circled, in radians</param>
    public static (Vector2 Red, Vector2 Blue) OrbPositions(Vector2 center, float orbRadius, float spin)
    {
        // Circling on a squashed ellipse, so it looks like they orbit in depth
        var offset = new Vector2(MathF.Cos(spin), MathF.Sin(spin) * 0.45f) * orbRadius * 1.1f;
        return (center + offset, center - offset);
    }

    /// <summary>
    /// Draws the glow around a pair of orbs (in an additive batch)
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
    /// Draws the cores of a pair of orbs (in an alpha blended batch). The orb in front is drawn last.
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

        // A bright, slightly off-center middle makes it look lit from within
        var highlight = position + new Vector2(-0.2f, -0.25f) * radius;
        batch.Draw(Disc, highlight, null, Color.Lerp(color, Color.White, 0.75f) * alpha, 0, origin, scale * 0.45f,
            SpriteEffects.None, 0);
    }

    /// <summary>
    /// A white texture whose opacity at each pixel is <paramref name="shape"/>(distance from the
    /// middle, 0 to 1 at the edge). The color is premultiplied by the opacity, as SpriteBatch expects.
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
