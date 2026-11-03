using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.Input;
using CIS580.UI;
using CIS580.UI.Widgets;
using CIS580.Views;

namespace GameArchitecture.UI;

/// <summary>
/// The main menu. Buttons are listed on the left, and on the right Spider-Man stands on a rooftop
/// and randomly switches between idling and fighting. This layer only shows the options - the
/// state that opened it decides what each one does.
/// </summary>
public class MainMenuLayer : MenuLayer
{
    // colors I pulled from the mockup
    private static readonly Color PanelColor = new(13, 19, 32);
    private static readonly Color ButtonColor = new(201, 211, 227);
    private static readonly Color HighlightColor = new(232, 72, 60);
    private static readonly Color HintColor = new(125, 138, 163);
    private static readonly Color RoofColor = new(26, 35, 54);
    private static readonly Color RoofEdgeColor = new(46, 59, 88);

    // little description for each button, shown under the list (same order as the buttons)
    private static readonly string[] Hints =
    {
        "Start a new run",
        "Your stats, Spider-Essence and costumes",
        "Audio, controls, display",
        "Quit to desktop",
    };


    private readonly Action _onExit;
    private readonly MenuSpiderMan _spiderMan = new();
    private readonly float[] _fade;
    private Texture2D _skyline;
    private Texture2D _spidey;
    private SpriteFont _buttonFont;
    private float _clock;

    /// <param name="onPlay">Runs when someone picks Run, and gets told who picked it</param>
    /// <param name="onExit">Called when the player confirms they want to exit</param>
    public MainMenuLayer(Action<InputSource> onPlay, Action onExit)
        : base("")
    {
        _onExit = onExit;
        FitToScreen = false;

        Menu.Add(new ButtonItem("Run", onPlay))
            .Add(new ButtonItem("The Closet", _ => Open(new ClosetLayer())))
            .Add(new ButtonItem("Options", _ => Open(new OptionsLayer())))
            .Add(new ButtonItem("Exit Game", OnCancel));

        _fade = new float[Menu.Items.Count];
    }

    protected override void Load()
    {
        base.Load();
        _skyline = Game.Content.Load<Texture2D>("Textures/background_tileset_city_free");
        _spidey = Game.Content.Load<Texture2D>("Textures/spiderman");
        _buttonFont = Game.Content.Load<SpriteFont>("Fonts/buttonfont");    // Action Comics Black
    }

    /// <summary>
    /// Cancelling the main menu asks the player to confirm they want to exit
    /// </summary>
    protected override void OnCancel(InputSource source)
    {
        var prompt = new PromptLayer("Are you sure you want to exit?");
        prompt.Accepted += _ => _onExit();
        Open(prompt);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
        _spiderMan.Update(gameTime);

        // highlight fades in/out as you move the selection
        float step = (float)gameTime.ElapsedGameTime.TotalSeconds * 8;
        for (int i = 0; i < _fade.Length; i++)
        {
            bool selected = HasFocus && i == Menu.SelectedIndex;
            _fade[i] = Math.Clamp(_fade[i] + (selected ? step : -step), 0f, 1f);
        }
    }

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float width = viewport.Width, height = viewport.Height;
        float alpha = Transition.Alpha;

        // menu on the left half, Spider-Man's stage on the right
        float panelWidth = MathF.Max(380, width * 0.45f);    // wide enough for the button font
        float roofTop = MathF.Round(height * 0.82f);
        float stageCenter = panelWidth + (width - panelWidth) / 2;

        // --- pixel art (point sampling so it doesn't get blurry) ---
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawSkyline(width, height, alpha);
        Fill(new RectangleF(0, roofTop, width, height - roofTop), RoofColor * alpha);
        Fill(new RectangleF(0, roofTop, width, 4), RoofEdgeColor * alpha);
        DrawSpiderMan(new Vector2(MathF.Round(stageCenter), roofTop), height, alpha);

        SpriteBatch.End();

        // --- menu panel + text ---
        SpriteBatch.Begin();

        // panel slides in from the left when the menu opens
        float slide = -Transition.Offset * panelWidth;
        DrawPanel(panelWidth, height, slide, alpha);
        DrawTitlePlaceholder(new RectangleF(48 + slide, height * 0.10f, panelWidth - 96, height * 0.17f), alpha);
        DrawButtons(new Vector2(48 + slide, height * 0.38f), panelWidth - 96, alpha);

        SpriteBatch.End();
    }

    private void DrawSkyline(float width, float height, float alpha)
    {
        // skyline image is 320x192. scale it to the screen height, tile it horizontally, and scroll
        // it slowly so the menu doesn't look static
        float scale = height / _skyline.Height;
        float tileWidth = _skyline.Width * scale;
        float offset = _clock * 12 % tileWidth;
        var tint = new Color(alpha * 0.8f, alpha * 0.8f, alpha * 0.9f);    // darken it a bit so it looks like night

        for (float x = -offset; x < width; x += tileWidth)
            SpriteBatch.Draw(_skyline, new Vector2(MathF.Round(x), 0), null, tint, 0, Vector2.Zero, scale,
                SpriteEffects.None, 0);
    }

    private void DrawSpiderMan(Vector2 feet, float height, float alpha)
    {
        // stick to whole number scales so the pixels stay square (3x at the default 800x480)
        float scale = MathF.Max(2, MathF.Round(height / 160));
        var frame = _spiderMan.CurrentFrame;

        // soft shadow under his feet
        var shadowSize = new Vector2(30 * scale, 3 * scale);
        Fill(new RectangleF(feet.X - shadowSize.X / 2, feet.Y - shadowSize.Y / 2, shadowSize.X, shadowSize.Y),
            Color.Black * (0.35f * alpha));

        SpriteBatch.Draw(_spidey, feet, frame.Source, Color.White * alpha, 0, frame.Origin, scale,
            SpriteEffects.None, 0);
    }

    private void DrawPanel(float panelWidth, float height, float slide, float alpha)
    {
        // solid color behind the buttons, then fades out over the skyline
        const float fadeWidth = 120;
        float solid = panelWidth - fadeWidth;
        Fill(new RectangleF(slide, 0, solid, height), PanelColor * alpha);

        const int steps = 24;
        float stepWidth = fadeWidth / steps;
        for (int i = 0; i < steps; i++)
        {
            float opacity = 1 - (i + 0.5f) / steps;
            Fill(new RectangleF(slide + solid + i * stepWidth, 0, stepWidth + 1, height), PanelColor * (opacity * alpha));
        }
    }

    private void DrawTitlePlaceholder(RectangleF box, float alpha)
    {
        // TODO: swap this for the title logo when it's done
        var color = new Color(58, 71, 96) * alpha;
        Fill(new RectangleF(box.X, box.Y, box.Width, 2), color);
        Fill(new RectangleF(box.X, box.Y + box.Height - 2, box.Width, 2), color);
        Fill(new RectangleF(box.X, box.Y, 2, box.Height), color);
        Fill(new RectangleF(box.X + box.Width - 2, box.Y, 2, box.Height), color);

        const string text = "[ TITLE LOGO ]";
        var font = Theme.TextFont;
        var size = font.MeasureString(text) * 0.8f;
        var position = new Vector2(box.X + (box.Width - size.X) / 2, box.Y + (box.Height - size.Y) / 2);
        SpriteBatch.DrawString(font, text, position, HintColor * alpha, 0, Vector2.Zero, 0.8f, SpriteEffects.None, 0);
    }

    private void DrawButtons(Vector2 topLeft, float width, float alpha)
    {
        var font = _buttonFont;
        float rowHeight = font.LineSpacing + 14;
        var position = topLeft;

        for (int i = 0; i < Menu.Items.Count; i++)
        {
            var item = Menu.Items[i];
            float fade = _fade[i];

            // selected button shifts right a bit and gets a tinted row + red marker
            float nudge = 8 * fade;
            var row = new RectangleF(position.X + nudge, position.Y, width, rowHeight - 6);
            Fill(row, HighlightColor * (0.16f * fade * alpha));
            Fill(new RectangleF(row.X + 8, row.Y + 8, 5, row.Height - 16), HighlightColor * (fade * alpha));

            var color = !item.IsEnabled ? Theme.DisabledColor : Color.Lerp(ButtonColor, Color.White, fade);
            float textHeight = font.LineSpacing;
            var textPosition = new Vector2(row.X + 26, row.Y + (row.Height - textHeight) / 2);
            // The button font only has capital letters
            SpriteBatch.DrawString(font, item.Text.ToUpperInvariant(), textPosition, color * alpha);

            position.Y += rowHeight;
        }

        // description of whatever button is selected
        int selected = Menu.SelectedIndex;
        if (selected >= 0 && selected < Hints.Length)
        {
            var hintPosition = new Vector2(topLeft.X + 8, position.Y + 12);
            SpriteBatch.DrawString(Theme.TextFont, Hints[selected], hintPosition, HintColor * alpha, 0, Vector2.Zero, 0.85f,
                SpriteEffects.None, 0);
        }
    }

    private void Fill(RectangleF area, Color color)
        => SpriteBatch.Draw(Stack.BlankTexture, new Vector2(area.X, area.Y), null, color, 0, Vector2.Zero,
            new Vector2(area.Width, area.Height), SpriteEffects.None, 0);
}
