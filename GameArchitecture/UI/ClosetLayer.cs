using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CIS580.UI;
using CIS580.UI.Widgets;
using CIS580.Views;
using GameArchitecture.Settings;
using GameArchitecture.Views;

namespace GameArchitecture.UI;

/// <summary>
/// The Closet screen. Shows saved stats (best run, last run, essence) and is where suits and
/// collectables are going to go. Right now the shop part is just a placeholder + Back button.
/// </summary>
/// <remarks>
/// Stats are read from the <see cref="SaveFile"/> service, which saves after every run, so
/// they're always current.
/// </remarks>
public class ClosetLayer : MenuLayer
{
    private const string ComingSoon = "Costumes to spend your Spider-Essence on are coming soon";

    // same colors as the main menu
    private static readonly Color PanelColor = new(13, 19, 32);
    private static readonly Color AccentColor = new(232, 72, 60);
    private static readonly Color LabelColor = new(125, 138, 163);
    private static readonly Color ValueColor = new(235, 240, 248);

    private const float PanelWidth = 440;
    private const float RowHeight = 46;
    private const float EssenceIconRadius = 8;

    private SaveFile _save;
    private EssenceArt _essenceArt;
    private float _clock;

    public ClosetLayer() : base("The Closet")
    {
        DimBackground = true;
        FitToScreen = false;
        Menu.Add(new ButtonItem("Back", _ => Close()));
    }

    protected override void Load()
    {
        base.Load();
        _save = Game.Services.GetService<SaveFile>();
        _essenceArt = EssenceArt.For(Game.GraphicsDevice);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _clock = (float)gameTime.TotalGameTime.TotalSeconds;
    }

    public override void Draw(GameTime gameTime)
    {
        var viewport = Game.GraphicsDevice.Viewport;
        float centerX = viewport.Width / 2f;
        float alpha = Transition.Alpha;
        float slide = Transition.Offset * 60;     // panel slides up as the closet opens

        DrawBackdrop(gameTime);

        var data = _save?.Data ?? new SaveData();
        bool hasPlayed = _save?.HasPlayed ?? false;

        // stats panel sits between the title and the Back button
        float panelTop = Theme.TitleTop + 48 + slide;
        var panel = new RectangleF(centerX - PanelWidth / 2, panelTop, PanelWidth, RowHeight * 3 + 20);
        float backTop = MathF.Max(panel.Y + panel.Height + 70, viewport.Height - 90);
        var essenceIconCenter = new Vector2(panel.X + 28 + EssenceIconRadius, panel.Y + 10 + RowHeight * 2.5f);

        SpriteBatch.Begin();

        Fill(panel, PanelColor * (0.88f * alpha));
        Fill(new RectangleF(panel.X, panel.Y, 4, panel.Height), AccentColor * alpha);

        DrawRow(panel, 0, "Best Run", hasPlayed ? $"{data.HighScoreMeters} m" : "--", 0, alpha);
        DrawRow(panel, 1, "Last Run", hasPlayed ? $"{data.LastScoreMeters} m" : "--", 0, alpha);
        DrawRow(panel, 2, "Spider-Essence", data.SpiderEssence.ToString(), EssenceIconRadius * 2 + 10, alpha);

        // thin lines between rows
        for (int i = 1; i < 3; i++)
            Fill(new RectangleF(panel.X + 20, panel.Y + 10 + RowHeight * i, panel.Width - 40, 1), LabelColor * (0.3f * alpha));

        var font = Theme.TextFont;
        var comingSize = font.MeasureString(ComingSoon) * 0.7f;
        SpriteBatch.DrawString(font, ComingSoon, new Vector2(centerX - comingSize.X / 2, panel.Y + panel.Height + 18),
            Theme.DisabledColor * alpha, 0, Vector2.Zero, 0.7f, SpriteEffects.None, 0);

        Menu.Draw(SpriteBatch, Theme, new Vector2(centerX, backTop), Transition, gameTime);

        // title slides down while the menu transitions in, same as the other menus
        var titleFont = Theme.TitleFont;
        var titlePosition = new Vector2(centerX, Theme.TitleTop - Transition.Offset * 100);
        SpriteBatch.DrawString(titleFont, Title, titlePosition, Theme.TitleColor * alpha, 0,
            titleFont.MeasureString(Title) / 2, Theme.TitleScale, SpriteEffects.None, 0);

        SpriteBatch.End();

        DrawEssenceIcon(essenceIconCenter, alpha);
    }

    /// <summary>
    /// A single stats row - label on the left, value on the right
    /// </summary>
    private void DrawRow(RectangleF panel, int row, string label, string value, float labelIndent, float alpha)
    {
        var font = Theme.TextFont;
        float middle = panel.Y + 10 + RowHeight * (row + 0.5f);

        var labelOrigin = new Vector2(0, font.MeasureString(label).Y / 2);
        SpriteBatch.DrawString(font, label, new Vector2(panel.X + 28 + labelIndent, middle), LabelColor * alpha, 0,
            labelOrigin, 1f, SpriteEffects.None, 0);

        const float valueScale = 1.2f;
        var valueSize = font.MeasureString(value);
        SpriteBatch.DrawString(font, value, new Vector2(panel.X + panel.Width - 24, middle), ValueColor * alpha, 0,
            new Vector2(valueSize.X, valueSize.Y / 2), valueScale, SpriteEffects.None, 0);
    }

    private void DrawEssenceIcon(Vector2 center, float alpha)
    {
        if (_essenceArt == null)
            return;

        float spin = _clock * 2;
        SpriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        _essenceArt.DrawGlow(SpriteBatch, center, EssenceIconRadius, spin, alpha * 0.9f);
        SpriteBatch.End();

        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _essenceArt.DrawCores(SpriteBatch, center, EssenceIconRadius, spin, alpha);
        SpriteBatch.End();
    }

    private void Fill(RectangleF area, Color color)
        => SpriteBatch.Draw(Stack.BlankTexture, new Vector2(area.X, area.Y), null, color, 0, Vector2.Zero,
            new Vector2(area.Width, area.Height), SpriteEffects.None, 0);
}
