using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using GameArchitecture.Simulation;

namespace GameArchitecture.Views;

/// <summary>Which version of the city skyline is showing</summary>
public enum TimeOfDay { Day, Dusk, Night }

/// <summary>
/// The parallax city behind the rooftops. It has a full set of layers for each
/// <see cref="TimeOfDay"/> and cycles Day -> Dusk -> Night -> Day every
/// <see cref="MetersPerPeriod"/>, cross-fading over the last <see cref="FadeMeters"/> before each
/// change.
/// </summary>
/// <remarks>
/// Each set is a stack of 576x324 layers, drawn back (sky) to front (closest buildings). Layers
/// further back scroll slower, which is what makes the depth effect. Everything is based on the
/// distance run, so it's the same every run and pausing/dying just freezes it.
/// </remarks>
public sealed class CityBackdrop
{
    /// <summary>How far you run before the time of day changes</summary>
    public const double MetersPerPeriod = 500;

    /// <summary>How many meters the fade into the next time of day takes</summary>
    public const double FadeMeters = 40;

    // source art is 576x324, scaled so it's as tall as the world (800x480)
    private const int LayerWidth = 576;
    private const int LayerHeight = 324;
    private const float Scale = World.Height / (float)LayerHeight;
    private const float ScaledWidth = LayerWidth * Scale;

    // scroll speed (compared to the rooftops) of the sky, then the farthest and nearest buildings.
    // building layers in between are spaced evenly
    private const float SkyParallax = 0.02f;
    private const float FarParallax = 0.06f;
    private const float NearParallax = 0.4f;

    private readonly record struct Layer(Texture2D Texture, float Parallax);

    /// <summary>One time of day: its layers and a color to fill anything outside the world with</summary>
    private sealed record LayerSet(Layer[] Layers, Color Fill);

    private static readonly TimeOfDay[] Cycle = { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night };

    private readonly LayerSet[] _sets = new LayerSet[Cycle.Length];

    /// <summary>
    /// Loads all of the layer textures
    /// </summary>
    /// <remarks>
    /// dawn7.png is a flattened preview of the whole dusk scene, not a layer, so it isn't used.
    /// day1-6 are recolored copies of dawn1-6.
    /// </remarks>
    public CityBackdrop(ContentManager content)
    {
        _sets[(int)TimeOfDay.Day] = LoadSet(content, "day", 6, new Color(35, 50, 75));
        _sets[(int)TimeOfDay.Dusk] = LoadSet(content, "dawn", 6, new Color(14, 26, 30));
        _sets[(int)TimeOfDay.Night] = LoadSet(content, "night", 5, new Color(18, 18, 40));
    }

    private static LayerSet LoadSet(ContentManager content, string prefix, int count, Color fill)
    {
        var layers = new Layer[count];
        for (int i = 0; i < count; i++)
        {
            // layer 0 is the sky, the rest are buildings from far to near
            float parallax = i == 0
                ? SkyParallax
                : MathHelper.Lerp(FarParallax, NearParallax, (i - 1) / (float)Math.Max(1, count - 2));
            layers[i] = new Layer(content.Load<Texture2D>($"Textures/{prefix}{i + 1}"), parallax);
        }
        return new LayerSet(layers, fill);
    }

    /// <summary>
    /// Works out which time of day it is at a distance
    /// </summary>
    /// <param name="meters">Distance run</param>
    /// <param name="current">The time of day that's showing</param>
    /// <param name="next">The time of day that's coming next</param>
    /// <returns>How far into the fade to <paramref name="next"/> it is (0 = not fading yet, 1 = done)</returns>
    public static float PeriodAt(double meters, out TimeOfDay current, out TimeOfDay next)
    {
        meters = Math.Max(0, meters);
        int period = (int)(meters / MetersPerPeriod);
        current = Cycle[period % Cycle.Length];
        next = Cycle[(period + 1) % Cycle.Length];

        // fade happens at the end of the period, so the switch is finished right at the 500 m mark
        double intoPeriod = meters - period * MetersPerPeriod;
        double fadeStart = MetersPerPeriod - FadeMeters;
        float fade = (float)Math.Clamp((intoPeriod - fadeStart) / FadeMeters, 0, 1);

        // ease in and out so the fade doesn't start or stop abruptly
        return fade * fade * (3 - 2 * fade);
    }

    /// <summary>
    /// The color for anything outside the world (e.g. above/below it if the window is taller
    /// than the world)
    /// </summary>
    public Color FillColor(double meters)
    {
        float fade = PeriodAt(meters, out var current, out var next);
        return Color.Lerp(_sets[(int)current].Fill, _sets[(int)next].Fill, fade);
    }

    /// <summary>
    /// Draws the skyline. Expects the batch to already be started with point sampling and alpha
    /// blending.
    /// </summary>
    /// <param name="scrollX">How far the rooftops have scrolled (world units)</param>
    /// <param name="meters">Distance run, picks the time of day</param>
    /// <param name="left">Left edge of the visible area</param>
    /// <param name="right">Right edge of the visible area</param>
    public void Draw(SpriteBatch spriteBatch, double scrollX, double meters, float left, float right)
    {
        float fade = PeriodAt(meters, out var current, out var next);

        // draw the current set, then the next one on top of it, see-through by how far the fade is.
        // (textures are premultiplied, so Color.White * fade fades the whole layer)
        if (fade < 1)
            DrawSet(spriteBatch, _sets[(int)current], scrollX, left, right, Color.White);
        if (fade > 0)
            DrawSet(spriteBatch, _sets[(int)next], scrollX, left, right, Color.White * fade);
    }

    private static void DrawSet(SpriteBatch spriteBatch, LayerSet set, double scrollX, float left, float right, Color tint)
    {
        foreach (var layer in set.Layers)
        {
            // repeat the layer across the screen, shifted by its share of the scrolling
            float offset = (float)(scrollX * layer.Parallax % ScaledWidth);
            float x = MathF.Floor((left + offset) / ScaledWidth) * ScaledWidth - offset;
            for (; x < right; x += ScaledWidth)
            {
                // rounded to whole units so the pixel art doesn't shimmer as it scrolls. the +1
                // width hides any 1-unit seam between copies
                var destination = new Rectangle((int)MathF.Round(x), 0, (int)MathF.Ceiling(ScaledWidth) + 1, (int)World.Height);
                spriteBatch.Draw(layer.Texture, destination, tint);
            }
        }
    }
}
