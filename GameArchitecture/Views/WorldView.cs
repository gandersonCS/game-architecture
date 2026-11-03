using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Views;
using GameArchitecture.Settings;
using GameArchitecture.Simulation;

namespace GameArchitecture.Views;

/// <summary>
/// Draws everything in the runner: parallax skyline (<see cref="CityBackdrop"/>), the scrolling tile rooftops, essence orbs,
/// Spider-Man (plus his web when swinging), the distance/essence counters, and the end screen.
/// </summary>
/// <remarks>
/// This is purely a view: it reads the <see cref="GameSession"/> and draws it, but never changes
/// it. The camera doesn't move - the <see cref="RooftopTrack"/> scrolls instead, so everything gets
/// drawn at (track position - scroll distance).
/// </remarks>
public class WorldView : WorldLayer
{
    private static readonly RectangleF WorldArea = new(0, 0, World.Width, World.Height);

    // city tileset = grid of 16px tiles, referenced by (column, row)
    private const int Src = 16;
    private static Rectangle Tile(int column, int row) => new(column * Src, row * Src, Src, Src);

    // each building style is a 3-column panel in the tileset. buildings use 3-slice drawing:
    // left column for the left edge, right column for the right edge, middle column repeated in
    // between. rows are: roof, the row under the roof, and a plain body row.
    private readonly record struct Panel(int FirstColumn, int RoofRow, int SecondRow, int BodyRow);
    private static readonly Panel[] Panels =
    {
        new(2, 1, 2, 2),     // Blue:  columns 2-4
        new(8, 1, 2, 5),     // Green: columns 8-10
        new(12, 1, 2, 5),    // Red:   columns 12-14
    };

    // window is 2x2 tiles (32x32px) in the tileset
    private static readonly Rectangle WindowSource = new(32, 160, 32, 32);

    // 1 tileset pixel = 2 world units, so a 16px tile = TileSize (32)
    private const float PixelScale = RooftopTrack.TileSize / (float)Src;

    private static readonly Color RopeColor = new(235, 240, 245);
    private static readonly Color NewBestColor = new(255, 220, 70);
    private static readonly Color SavedColor = new(150, 235, 160);
    private static readonly Color NotSavedColor = new(255, 140, 130);

    // orb settings in world units: core radius + bob/spin
    private const float OrbRadius = 5f;
    private const float OrbBob = 3f;
    private const float OrbSpinSpeed = 4f;

    // small burst effect when you grab an orb
    private const float BurstSeconds = 0.35f;
    private readonly record struct Burst(double X, float Y, float Start);
    private readonly List<Burst> _bursts = new();

    // essence icon next to the counter (under the distance)
    private static readonly Vector2 EssenceIconCenter = new(30, 68);
    private const float EssenceIconRadius = 7f;

    private readonly GameSession _session;
    private readonly SpriteFont _font;
    private readonly Texture2D _tileset;
    private readonly CityBackdrop _backdrop;
    private readonly Texture2D _spidey;
    private EssenceArt _essenceArt;
    private RunRecord? _runRecord;
    private Runner _recordedRunner;
    private float _clock;

    /// <param name="session">The game to draw</param>
    /// <param name="font">The font to draw with</param>
    /// <param name="tileset">Textures/tileset_city_free</param>
    /// <param name="backdrop">The parallax city behind the rooftops (day/dusk/night)</param>
    /// <param name="spidey">Textures/spiderman (sprite sheet with the background removed)</param>
    public WorldView(GameSession session, SpriteFont font, Texture2D tileset, CityBackdrop backdrop, Texture2D spidey)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _font = font;
        _tileset = tileset;
        _backdrop = backdrop;
        _spidey = spidey;

        // fills anything outside the world; follows the time of day (see Update)
        BackgroundColor = backdrop?.FillColor(0) ?? Color.Black;

        Transition.OnDuration = TimeSpan.FromSeconds(1.5);
        Transition.OffDuration = TimeSpan.FromSeconds(0.5);
    }

    protected override void Load()
    {
        base.Load();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
    }

    /// <summary>
    /// Handles sim events - right now just spawns a burst where an orb was picked up.
    /// (boss flash would go here later)
    /// </summary>
    public void OnGameEvent(GameEvent gameEvent)
    {
        if (gameEvent is EssenceCollected collected)
            _bursts.Add(new Burst(collected.X, collected.Y, _clock));
    }

    /// <summary>
    /// Shows the save results (new best, essence banked) on the end-of-run screen until the next
    /// run starts
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

        // new run started, clear the old summary
        if (_runRecord != null && _session.World.Runner != _recordedRunner)
            _runRecord = null;

        if (_backdrop != null)
            BackgroundColor = _backdrop.FillColor(_session.World.Track.DistanceMeters);

        base.Update(gameTime);
    }

    protected override void UpdateCameras(GameTime gameTime)
    {
        // camera stays still and fits the whole world in the window
        foreach (var camera in Cameras)
            camera.FitToArea(WorldArea);
    }

    protected override void DrawWorld(Camera2D camera, GameTime gameTime)
    {
        // pixel art can't be smoothed, so restart the batch with point sampling
        SpriteBatch.End();
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.Transform);

        // visible area (can be bigger than the world if the window aspect ratio is different)
        float halfWidth = camera.Viewport.Width / 2f / camera.Zoom;
        float halfHeight = camera.Viewport.Height / 2f / camera.Zoom;
        float left = camera.Position.X - halfWidth;
        float right = camera.Position.X + halfWidth;
        float bottom = camera.Position.Y + halfHeight;

        var track = _session.World.Track;
        var runner = _session.World.Runner;

        _backdrop?.Draw(SpriteBatch, track.ScrollX, track.DistanceMeters, left, right);
        DrawBuildings(track, left, right, bottom);
        DrawEssence(camera, track, left, right);
        DrawSpiderMan(track, runner);
        DrawText(camera, track, runner);
    }

    /// <summary>
    /// Ends the batch and starts a new one with the camera transform. Used to switch between
    /// pixel art (point sampling) and the smooth glowy orbs (linear sampling)
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

        // glows + collect bursts first (these brighten what's behind them)...
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

        // ...then draw the actual orbs on top
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.LinearClamp);
        foreach (var orb in orbs)
        {
            if (OrbOnScreen(orb, track, left, right, out var center, out float spin))
                _essenceArt.DrawCores(SpriteBatch, center, OrbRadius, spin);
        }

        // switch back to pixel art for Spider-Man
        Rebegin(camera, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>
    /// Gets where to draw an orb (with the bob) and its spin, returns false if it's off screen
    /// </summary>
    private bool OrbOnScreen(EssenceOrb orb, RooftopTrack track, float left, float right, out Vector2 center, out float spin)
    {
        float x = (float)(orb.X - track.ScrollX);

        // give each orb its own phase based on position so a pack shimmers instead of moving in sync
        float phase = (float)(orb.X * 0.05 % (2 * Math.PI));
        spin = _clock * OrbSpinSpeed + phase;
        center = new Vector2(x, orb.Y + OrbBob * MathF.Sin(_clock * 3 + phase));
        return x > left - 20 && x < right + 20;
    }

    private void DrawBuildings(RooftopTrack track, float left, float right, float bottom)
    {
        const int size = RooftopTrack.TileSize;
        int lastRow = (int)MathF.Ceiling(bottom / size);

        foreach (var building in track.Buildings)
        {
            // building's left edge on screen, rounded to whole units so we don't get tiny gaps
            // between tiles
            float x0 = MathF.Round((float)(building.StartColumn * (double)size - track.ScrollX));
            if (x0 > right || x0 + building.WidthTiles * size < left)
                continue;

            var panel = Panels[(int)building.Style];

            // only draw columns that are on screen
            int firstColumn = Math.Max(0, (int)MathF.Floor((left - x0) / size));
            int lastColumn = Math.Min(building.WidthTiles - 1, (int)MathF.Ceiling((right - x0) / size));

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                // pick left edge / middle / right edge of the panel
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
                // run animation is tied to the ground: next frame every X distance traveled, so his legs
                // speed up as the city speeds up
                const double unitsPerFrame = 30;
                int index = (int)(track.ScrollX / unitsPerFrame) % SpiderFrames.Running.Length;
                DrawFrame(SpiderFrames.Running[index], feet);
                break;
            }

            case RunnerState.Airborne:
            {
                if (runner.VelocityY < -250)
                {
                    // going up: do a flip
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

                // web shoots out to the anchor quickly, then he hangs from it. pose depends on the
                // rope angle
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

        // essence count for this run + icon, right below the distance
        int essence = _session.World.Essence.CollectedThisRun;
        var countPosition = new Vector2(EssenceIconCenter.X + EssenceIconRadius + 12, EssenceIconCenter.Y);
        var countOrigin = new Vector2(0, _font.MeasureString("0").Y / 2);
        SpriteBatch.DrawString(_font, essence.ToString(), countPosition + new Vector2(2, 2), Color.Black * 0.45f, 0,
            countOrigin, 0.75f, SpriteEffects.None, 0);
        SpriteBatch.DrawString(_font, essence.ToString(), countPosition, Color.White, 0, countOrigin, 0.75f,
            SpriteEffects.None, 0);
        DrawEssenceIcon(camera, EssenceIconCenter, EssenceIconRadius);

        // controls hint at the start of a run
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
    /// End-of-run summary: best distance, essence banked, and whether it saved
    /// </summary>
    private void DrawRunSummary(Camera2D camera, RunRecord record)
    {
        const float centerX = World.Width / 2;

        if (record.NewHighScore)
            DrawCentered("NEW BEST!", new Vector2(centerX, 220), NewBestColor, 0.8f);
        else
            DrawCentered($"Best: {record.HighScoreMeters} m", new Vector2(centerX, 220), Color.White * 0.85f, 0.6f);

        // e.g. "+12 Spider-Essence (340 total)" with the icon in front
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
    /// Draws the essence icon (pair of slowly spinning orbs). Puts the batch back the way it was
    /// afterwards (point sampled, alpha blended).
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
        // dark outline so text is readable on any background
        foreach (var offset in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2) })
            SpriteBatch.DrawString(_font, text, position + offset, Color.Black * 0.7f, 0, origin, scale, SpriteEffects.None, 0);
        SpriteBatch.DrawString(_font, text, position, color, 0, origin, scale, SpriteEffects.None, 0);
    }
}

/// <summary>
/// Frame locations on Textures/spiderman.png. Each rect is cropped tight around a frame, so
/// the widths aren't all the same.
/// </summary>
public static class SpiderFrames
{
    /// <summary>A frame plus the point in it (sheet pixels) that lines up with the draw position</summary>
    public readonly record struct Frame(Rectangle Source, Vector2 Origin)
    {
        /// <summary>Origin at bottom center (his feet)</summary>
        public static Frame Feet(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height));

        /// <summary>Origin at the center (for the flip)</summary>
        public static Frame Middle(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, height / 2f + 14));

        /// <summary>Origin at top center (where the web attaches)</summary>
        public static Frame Hanging(int x, int y, int width, int height)
            => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, 0));
    }

    /// <summary>Sheet y of his feet in the running frames</summary>
    private const int RunBaseline = 364;

    private static Frame Run(int x, int y, int width, int height)
        => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, RunBaseline - y));

    // all use the same baseline so his feet don't bob up and down mid-stride
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

    // swinging poses, ordered from rope pointing way ahead (first) to straight up/behind (last)
    private static readonly Frame[] Swing =
    {
        Frame.Hanging(221, 1974, 45, 35),
        Frame.Hanging(295, 2268, 42, 34),
        Frame.Hanging(459, 2268, 42, 34),
        Frame.Hanging(44, 2427, 43, 33),
    };

    /// <summary>Picks the swing pose for a rope angle from straight up (positive = anchor ahead)</summary>
    public static Frame SwingFor(float ropeAngleDegrees)
    {
        if (ropeAngleDegrees > 30) return Swing[0];
        if (ropeAngleDegrees > 15) return Swing[1];
        if (ropeAngleDegrees > 0) return Swing[2];
        return Swing[3];
    }
}
