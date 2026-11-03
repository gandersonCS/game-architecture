using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Views;
using GameArchitecture.Settings;
using GameArchitecture.Simulation;

namespace GameArchitecture.Views;

/// <summary>
/// Draws the endless runner: a parallax skyline, the scrolling tile-map rooftops, the
/// Spider-Essence orbs, and Spider-Man (with his web, when he swings), plus the distance and
/// Spider-Essence counters and the end-of-run summary.
/// </summary>
/// <remarks>
/// This is purely a view: it reads the <see cref="GameSession"/> and draws it, but never changes
/// it. The camera never moves; the simulation's <see cref="RooftopTrack"/> scrolls, and each
/// thing is drawn at (its position along the track - the scroll distance).
/// </remarks>
public class WorldView : WorldLayer
{
    private static readonly RectangleF WorldArea = new(0, 0, World.Width, World.Height);

    // The city tileset is a grid of 16 pixel tiles. A tile is named by its (column, row).
    private const int Src = 16;
    private static Rectangle Tile(int column, int row) => new(column * Src, row * Src, Src, Src);

    // Each building style is a 3 column wide panel. A building is drawn in "3 slices": its left
    // edge uses the panel's left column, its right edge the right column, and everything between
    // repeats the middle column. Rows: the roof, the row under it, and a plain row for the body.
    private readonly record struct Panel(int FirstColumn, int RoofRow, int SecondRow, int BodyRow);
    private static readonly Panel[] Panels =
    {
        new(2, 1, 2, 2),     // Blue:  columns 2-4
        new(8, 1, 2, 5),     // Green: columns 8-10
        new(12, 1, 2, 5),    // Red:   columns 12-14
    };

    // The 2x2 tile window (32x32 pixels) in the tileset
    private static readonly Rectangle WindowSource = new(32, 160, 32, 32);

    // One tileset pixel is two world units (so a 16 pixel tile is TileSize = 32 units)
    private const float PixelScale = RooftopTrack.TileSize / (float)Src;

    // The skyline is 320x192 pixels, drawn at 2.5x so it is exactly one world (800x480) wide.
    // It repeats without a seam, and scrolls at a fraction of the rooftops' speed (parallax).
    private const float SkylineScale = World.Height / 192f;
    private const float SkylineWidth = 320 * SkylineScale;
    private const float SkylineParallax = 0.2f;

    private static readonly Color RopeColor = new(235, 240, 245);
    private static readonly Color NewBestColor = new(255, 220, 70);
    private static readonly Color SavedColor = new(150, 235, 160);
    private static readonly Color NotSavedColor = new(255, 140, 130);

    // Spider-Essence orbs, in world units: each orb's core radius, and how they bob and spin
    private const float OrbRadius = 5f;
    private const float OrbBob = 3f;
    private const float OrbSpinSpeed = 4f;

    // The little burst when an orb is collected
    private const float BurstSeconds = 0.35f;
    private readonly record struct Burst(double X, float Y, float Start);
    private readonly List<Burst> _bursts = new();

    // The Spider-Essence icon next to the counter, under the distance
    private static readonly Vector2 EssenceIconCenter = new(30, 68);
    private const float EssenceIconRadius = 7f;

    private readonly GameSession _session;
    private readonly SpriteFont _font;
    private readonly Texture2D _tileset;
    private readonly Texture2D _skyline;
    private readonly Texture2D _spidey;
    private EssenceArt _essenceArt;
    private RunRecord? _runRecord;
    private Runner _recordedRunner;
    private float _clock;

    /// <param name="session">The game to draw</param>
    /// <param name="font">The font to draw with</param>
    /// <param name="tileset">Textures/tileset_city_free</param>
    /// <param name="skyline">Textures/background_tileset_city_free</param>
    /// <param name="spidey">Textures/spiderman (the sprite sheet, with a transparent background)</param>
    public WorldView(GameSession session, SpriteFont font, Texture2D tileset, Texture2D skyline, Texture2D spidey)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _font = font;
        _tileset = tileset;
        _skyline = skyline;
        _spidey = spidey;

        // The orange of the skyline's sky, which fills any space beyond the edges of the world
        BackgroundColor = new Color(212, 128, 77);

        Transition.OnDuration = TimeSpan.FromSeconds(1.5);
        Transition.OffDuration = TimeSpan.FromSeconds(0.5);
    }

    protected override void Load()
    {
        base.Load();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
    }

    /// <summary>
    /// Reacts to something that happened in the simulation: a burst where an orb was collected.
    /// (A flash for a boss would go here too.)
    /// </summary>
    public void OnGameEvent(GameEvent gameEvent)
    {
        if (gameEvent is EssenceCollected collected)
            _bursts.Add(new Burst(collected.X, collected.Y, _clock));
    }

    /// <summary>
    /// Shows how the run that just ended was saved (new best, Spider-Essence banked) on the
    /// end-of-run screen, until the next run starts
    /// </summary>
    public void ShowRunRecord(RunRecord record)
    {
        _runRecord = record;
        _recordedRunner = _session.World.Runner;
    }

    public override void Update(GameTime gameTime)
    {
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
        _bursts.RemoveAll(b => _clock - b.Start > BurstSeconds);

        // A new run has started: forget the last one's summary
        if (_runRecord != null && _session.World.Runner != _recordedRunner)
            _runRecord = null;

        base.Update(gameTime);
    }

    protected override void UpdateCameras(GameTime gameTime)
    {
        // The camera stays put and shows the whole world, as large as fits
        foreach (var camera in Cameras)
            camera.FitToArea(WorldArea);
    }

    protected override void DrawWorld(Camera2D camera, GameTime gameTime)
    {
        // Pixel art must not be smoothed: restart the batch with point sampling
        SpriteBatch.End();
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.Transform);

        // What part of the world is visible? (More than the world if the window is a different shape.)
        float halfWidth = camera.Viewport.Width / 2f / camera.Zoom;
        float halfHeight = camera.Viewport.Height / 2f / camera.Zoom;
        float left = camera.Position.X - halfWidth;
        float right = camera.Position.X + halfWidth;
        float bottom = camera.Position.Y + halfHeight;

        var track = _session.World.Track;
        var runner = _session.World.Runner;

        DrawSkyline(track, left, right);
        DrawBuildings(track, left, right, bottom);
        DrawEssence(camera, track, left, right);
        DrawSpiderMan(track, runner);
        DrawText(camera, track, runner);
    }

    /// <summary>
    /// Ends the current batch and begins another with the camera's transform, for switching
    /// between pixel art (point sampling) and smooth glowing orbs (linear sampling)
    /// </summary>
    private void Rebegin(Camera2D camera, BlendState blend, SamplerState sampler)
    {
        SpriteBatch.End();
        SpriteBatch.Begin(blendState: blend, samplerState: sampler, transformMatrix: camera.Transform);
    }

    private void DrawEssence(Camera2D camera, RooftopTrack track, float left, float right)
    {
        var orbs = _session.World.Essence.Orbs;
        if (_essenceArt == null || (orbs.Count == 0 && _bursts.Count == 0))
            return;

        // The glows (and collection bursts) brighten what's behind them...
        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        foreach (var orb in orbs)
        {
            if (OrbOnScreen(orb, track, left, right, out var center, out float spin))
                _essenceArt.DrawGlow(SpriteBatch, center, OrbRadius, spin, pulse: 1 + 0.12f * MathF.Sin(_clock * 6 + spin));
        }
        foreach (var burst in _bursts)
        {
            float t = (_clock - burst.Start) / BurstSeconds;
            var center = new Vector2((float)(burst.X - track.ScrollX), burst.Y);
            _essenceArt.DrawGlow(SpriteBatch, center, OrbRadius * (1 + 2.5f * t), _clock * 12, alpha: 1 - t);
        }

        // ...then the orbs themselves go on top
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);
        foreach (var orb in orbs)
        {
            if (OrbOnScreen(orb, track, left, right, out var center, out float spin))
                _essenceArt.DrawCores(SpriteBatch, center, OrbRadius, spin);
        }

        // Back to pixel art for Spider-Man
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// Where an orb is drawn (bobbing gently) and how far its pair has spun, if it's on screen
    /// </summary>
    private bool OrbOnScreen(EssenceOrb orb, RooftopTrack track, float left, float right, out Vector2 center, out float spin)
    {
        float x = (float)(orb.X - track.ScrollX);

        // Each orb has its own phase (from its position), so a pack shimmers rather than moving in lockstep
        float phase = (float)(orb.X * 0.05 % (2 * Math.PI));
        spin = _clock * OrbSpinSpeed + phase;
        center = new Vector2(x, orb.Y + OrbBob * MathF.Sin(_clock * 3 + phase));
        return x > left - 20 && x < right + 20;
    }

    private void DrawSkyline(RooftopTrack track, float left, float right)
    {
        float offset = (float)(track.ScrollX * SkylineParallax % SkylineWidth);
        float x = MathF.Floor((left + offset) / SkylineWidth) * SkylineWidth - offset;
        for (; x < right; x += SkylineWidth)
        {
            var position = new Vector2(MathF.Round(x), 0);
            SpriteBatch.Draw(_skyline, position, null, Color.White, 0, Vector2.Zero, SkylineScale, SpriteEffects.None, 0);
        }
    }

    private void DrawBuildings(RooftopTrack track, float left, float right, float bottom)
    {
        const int size = RooftopTrack.TileSize;
        int lastRow = (int)MathF.Ceiling(bottom / size);

        foreach (var building in track.Buildings)
        {
            // Where the building's left edge is on screen. Rounded to whole world units so
            // neighboring tiles never leave hairline gaps.
            float x0 = MathF.Round((float)(building.StartColumn * (double)size - track.ScrollX));
            if (x0 > right || x0 + building.WidthTiles * size < left)
                continue;

            var panel = Panels[(int)building.Style];

            // Only the columns on screen
            int firstColumn = Math.Max(0, (int)MathF.Floor((left - x0) / size));
            int lastColumn = Math.Min(building.WidthTiles - 1, (int)MathF.Ceiling((right - x0) / size));

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                // Left edge, middle, or right edge of the panel
                int panelColumn = panel.FirstColumn + (column == 0 ? 0 : column == building.WidthTiles - 1 ? 2 : 1);

                for (int row = building.TopRow; row <= lastRow; row++)
                {
                    int panelRow = row == building.TopRow ? panel.RoofRow
                                 : row == building.TopRow + 1 ? panel.SecondRow
                                 : panel.BodyRow;

                    var destination = new Rectangle((int)x0 + column * size, row * size, size, size);
                    SpriteBatch.Draw(_tileset, destination, Tile(panelColumn, panelRow), Color.White);
                }
            }

            foreach (var window in building.Windows)
            {
                var destination = new Rectangle((int)x0 + window.Column * size, window.Row * size, size * 2, size * 2);
                SpriteBatch.Draw(_tileset, destination, WindowSource, Color.White);
            }
        }
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        var d = to - from;
        SpriteBatch.Draw(Stack.BlankTexture, from, null, color, MathF.Atan2(d.Y, d.X),
            new Vector2(0, 0.5f), new Vector2(d.Length(), width), SpriteEffects.None, 0);
    }

    private void DrawSpiderMan(RooftopTrack track, Runner runner)
    {
        var feet = new Vector2(RooftopTrack.RunnerX, MathF.Round(runner.FeetY));

        switch (runner.State)
        {
            case RunnerState.Running:
            {
                // The running animation follows the ground: one frame per stretch of track passed
                // under his feet, so he runs faster as the city speeds up
                const double unitsPerFrame = 30;
                int index = (int)(track.ScrollX / unitsPerFrame) % SpiderFrames.Running.Length;
                DrawFrame(SpiderFrames.Running[index], feet);
                break;
            }

            case RunnerState.Airborne:
            {
                if (runner.VelocityY < -250)
                {
                    // Rising: a tumbling flip
                    int index = (int)(runner.AirSeconds / 0.07f) % SpiderFrames.Flip.Length;
                    DrawFrame(SpiderFrames.Flip[index], feet);
                }
                else if (runner.VelocityY < 250)
                    DrawFrame(SpiderFrames.Apex, feet);
                else
                    DrawFrame(SpiderFrames.Falling, feet);
                break;
            }

            case RunnerState.Swinging:
            {
                var anchor = runner.Anchor.Value;
                var hand = new Vector2(RooftopTrack.RunnerX, runner.HandY);
                var anchorOnScreen = new Vector2((float)(anchor.X - track.ScrollX), anchor.Y);

                // The web shoots out to the anchor in a moment, then he hangs from it. His pose
                // follows the rope's angle.
                var tip = Vector2.Lerp(hand, anchorOnScreen, MathF.Min(1f, runner.SwingSeconds / 0.07f));
                DrawLine(hand, tip, Color.Black * 0.35f, 5);
                DrawLine(hand, tip, RopeColor, 2.5f);
                DrawFrame(SpiderFrames.SwingFor(runner.RopeAngleDegrees(track)), hand);
                break;
            }

            case RunnerState.Dead:
                DrawFrame(SpiderFrames.Dead, feet);
                break;
        }
    }

    private void DrawFrame(SpiderFrames.Frame frame, Vector2 position, float rotation = 0)
        => SpriteBatch.Draw(_spidey, position, frame.Source, Color.White, rotation, frame.Origin,
            PixelScale, SpriteEffects.None, 0);

    private void DrawText(Camera2D camera, RooftopTrack track, Runner runner)
    {
        if (_font == null)
            return;

        SpriteBatch.DrawString(_font, $"{track.DistanceMeters:0} m", new Vector2(16, 12), Color.White);

        // This run's Spider-Essence, with its icon, right under the distance
        int essence = _session.World.Essence.CollectedThisRun;
        var countPosition = new Vector2(EssenceIconCenter.X + EssenceIconRadius + 12, EssenceIconCenter.Y);
        var countOrigin = new Vector2(0, _font.MeasureString("0").Y / 2);
        SpriteBatch.DrawString(_font, essence.ToString(), countPosition + new Vector2(2, 2), Color.Black * 0.45f, 0,
            countOrigin, 0.75f, SpriteEffects.None, 0);
        SpriteBatch.DrawString(_font, essence.ToString(), countPosition, Color.White, 0, countOrigin, 0.75f,
            SpriteEffects.None, 0);
        DrawEssenceIcon(camera, EssenceIconCenter, EssenceIconRadius);

        // A reminder of the controls, at the start of a run
        if (runner.State != RunnerState.Dead && track.DistanceMeters < 80)
        {
            const string help = "SPACE / A: jump (hold = higher)     keep holding in the air to swing";
            DrawCentered(help, new Vector2(World.Width / 2, World.Height - 28), Color.White * 0.9f, 0.6f);
        }

        if (runner.State == RunnerState.Dead)
        {
            string why = runner.EndReason == RunEndReason.Fell ? "You fell!" : "You hit a building!";
            DrawCentered(why, new Vector2(World.Width / 2, 130), Color.White, 1.6f);
            DrawCentered($"{track.DistanceMeters:0} m", new Vector2(World.Width / 2, 182), Color.White, 1.1f);

            if (_runRecord is RunRecord record)
                DrawRunSummary(camera, record);

            DrawCentered("Press Jump to try again", new Vector2(World.Width / 2, 350), Color.White * 0.9f, 0.8f);
        }
    }

    /// <summary>
    /// The end-of-run summary: the best distance, the Spider-Essence banked, and that it was saved
    /// </summary>
    private void DrawRunSummary(Camera2D camera, RunRecord record)
    {
        const float centerX = World.Width / 2;

        if (record.NewHighScore)
            DrawCentered("NEW BEST!", new Vector2(centerX, 220), NewBestColor, 0.8f);
        else
            DrawCentered($"Best: {record.HighScoreMeters} m", new Vector2(centerX, 220), Color.White * 0.85f, 0.6f);

        // "+12 Spider-Essence (340 total)", with the icon in front
        string essence = $"+{record.EssenceCollected} Spider-Essence  ({record.TotalEssence} total)";
        const float essenceScale = 0.6f;
        float textWidth = _font.MeasureString(essence).X * essenceScale;
        const float iconRadius = 6f, gap = 10f;
        float startX = centerX - (iconRadius * 2 + gap + textWidth) / 2;
        DrawCentered(essence, new Vector2(startX + iconRadius * 2 + gap + textWidth / 2, 258), Color.White, essenceScale);

        string saved = record.Saved ? "Progress saved" : "Couldn't save progress";
        DrawCentered(saved, new Vector2(centerX, 292), record.Saved ? SavedColor : NotSavedColor, 0.5f);

        DrawEssenceIcon(camera, new Vector2(startX + iconRadius, 258), iconRadius);
    }

    /// <summary>
    /// Draws the Spider-Essence icon (a slowly turning pair of orbs). Leaves the batch as it found
    /// it: point sampled, alpha blended.
    /// </summary>
    private void DrawEssenceIcon(Camera2D camera, Vector2 center, float radius)
    {
        if (_essenceArt == null)
            return;

        float spin = _clock * 2;
        Rebegin(camera, BlendState.Additive, SamplerState.LinearClamp);
        _essenceArt.DrawGlow(SpriteBatch, center, radius, spin, alpha: 0.9f);
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);
        _essenceArt.DrawCores(SpriteBatch, center, radius, spin);
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    private void DrawCentered(string text, Vector2 position, Color color, float scale)
    {
        var size = _font.MeasureString(text);
        var origin = size / 2;
        // A dark outline keeps the text readable over any color
        foreach (var offset in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2) })
            SpriteBatch.DrawString(_font, text, position + offset, Color.Black * 0.7f, 0, origin, scale, SpriteEffects.None, 0);
        SpriteBatch.DrawString(_font, text, position, color, 0, origin, scale, SpriteEffects.None, 0);
    }
}

/// <summary>
/// Where Spider-Man's frames are on Textures/spiderman.png. Each source rectangle is a tight box
/// around one frame (so frames have different widths).
/// </summary>
public static class SpiderFrames
{
    /// <summary>A frame, and the point in it (in sheet pixels) that is drawn at the position</summary>
    public readonly record struct Frame(Rectangle Source, Vector2 Origin)
    {
        /// <summary>Anchored at the middle of the bottom edge: his feet</summary>
        public static Frame Feet(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height));

        /// <summary>Anchored in the middle of the frame (for a tumbling flip)</summary>
        public static Frame Middle(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height / 2f + 14));

        /// <summary>Anchored at the middle of the top edge: where the web leaves him</summary>
        public static Frame Hanging(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, 0));
    }

    /// <summary>The sheet row (y) of Spider-Man's feet in the running frames</summary>
    private const int RunBaseline = 364;

    private static Frame Run(int x, int y, int width, int height)
        => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, RunBaseline - y));

    // Anchored on a shared baseline, so his feet stay level through the stride
    public static readonly Frame[] Running =
    {
        Run(3, 330, 40, 34), Run(59, 334, 34, 30), Run(106, 336, 44, 28),
        Run(166, 333, 54, 30), Run(233, 333, 48, 30), Run(295, 334, 33, 30),
        Run(342, 334, 44, 30), Run(397, 334, 52, 30), Run(466, 332, 46, 31),
    };

    public static readonly Frame[] Flip =
    {
        Frame.Middle(16, 511, 24, 21), Frame.Middle(67, 491, 21, 24), Frame.Middle(126, 482, 24, 21),
    };

    public static readonly Frame Apex = Frame.Feet(172, 493, 39, 34);
    public static readonly Frame Falling = Frame.Feet(230, 509, 27, 32);
    public static readonly Frame Dead = Frame.Middle(242, 2533, 35, 29);

    // Hanging from the web, from the rope pointing far ahead (first) to straight up or behind (last)
    private static readonly Frame[] Swing =
    {
        Frame.Hanging(221, 1974, 45, 35),
        Frame.Hanging(295, 2268, 42, 34),
        Frame.Hanging(459, 2268, 42, 34),
        Frame.Hanging(44, 2427, 43, 33),
    };

    /// <summary>The pose for a rope at an angle from straight up (positive: the anchor is ahead)</summary>
    public static Frame SwingFor(float ropeAngleDegrees)
    {
        if (ropeAngleDegrees > 30) return Swing[0];
        if (ropeAngleDegrees > 15) return Swing[1];
        if (ropeAngleDegrees > 0) return Swing[2];
        return Swing[3];
    }
}
