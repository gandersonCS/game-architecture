using System;
using Microsoft.Xna.Framework;
using GameArchitecture.Views;

namespace GameArchitecture.UI;

/// <summary>
/// The animated Spider-Man on the main menu. Idles for a few seconds, does a random attack
/// (jab, upward kick or uppercut), goes back to idle, and repeats.
/// </summary>
/// <remarks>
/// This class just picks the frame, <see cref="MainMenuLayer"/> does the actual drawing.
/// </remarks>
public sealed class MenuSpiderMan
{
    /// <summary>An animation - list of frames, how long each one shows, and whether it loops</summary>
    private sealed record Clip(SpiderFrames.Frame[] Frames, float FrameSeconds, bool Loops)
    {
        public float Length => Frames.Length * FrameSeconds;
    }

    private static readonly Clip Idle = new(MenuSpiderFrames.Standing, 0.12f, Loops: true);

    private static readonly Clip[] Fights =
    {
        new(MenuSpiderFrames.JabPunch, 0.07f, Loops: false),
        new(MenuSpiderFrames.UpwardKick, 0.08f, Loops: false),
        new(MenuSpiderFrames.Uppercut, 0.07f, Loops: false),
    };

    // idle time between attacks, in seconds
    private const float MinIdleSeconds = 1.5f;
    private const float MaxIdleSeconds = 3.5f;

    private readonly Random _random = new();
    private Clip _clip = Idle;
    private int _lastFight = -1;
    private float _clipTime;
    private float _idleLeft;

    public MenuSpiderMan()
    {
        _idleLeft = NextIdleSeconds();
    }

    /// <summary>True during an attack, false while idling</summary>
    public bool IsFighting => !ReferenceEquals(_clip, Idle);

    /// <summary>Which frame to draw right now</summary>
    public SpiderFrames.Frame CurrentFrame
    {
        get
        {
            int index = (int)(_clipTime / _clip.FrameSeconds);
            index = _clip.Loops ? index % _clip.Frames.Length : Math.Min(index, _clip.Frames.Length - 1);
            return _clip.Frames[index];
        }
    }

    public void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _clipTime += dt;

        if (ReferenceEquals(_clip, Idle))
        {
            _idleLeft -= dt;
            if (_idleLeft <= 0)
                Play(Fights[NextFight()]);
        }
        else if (_clipTime >= _clip.Length)
        {
            Play(Idle);
            _idleLeft = NextIdleSeconds();
        }
    }

    private void Play(Clip clip)
    {
        _clip = clip;
        _clipTime = 0;
    }

    private float NextIdleSeconds()
        => MinIdleSeconds + (float)_random.NextDouble() * (MaxIdleSeconds - MinIdleSeconds);

    // pick a random attack, but don't repeat the last one
    private int NextFight()
    {
        int next;
        if (_lastFight < 0)
            next = _random.Next(Fights.Length);
        else
        {
            next = _random.Next(Fights.Length - 1);
            if (next >= _lastFight)
                next++;
        }
        _lastFight = next;
        return next;
    }
}

/// <summary>
/// Frame rectangles for the menu idle/attack animations on Textures/spiderman.png. Same idea as
/// <see cref="SpiderFrames"/>: each rect is cropped tight around a frame, and every frame in an
/// animation uses the same baseline (where his feet are) so he doesn't bounce around.
/// </summary>
public static class MenuSpiderFrames
{
    // origin = middle of the frame horizontally, at the baseline vertically
    private static SpiderFrames.Frame OnBaseline(int baseline, int x, int y, int width, int height)
        => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, baseline - y));

    private static SpiderFrames.Frame Stand(int x, int y, int w, int h) => OnBaseline(284, x, y, w, h);
    private static SpiderFrames.Frame Jab(int x, int y, int w, int h) => OnBaseline(1213, x, y, w, h);
    private static SpiderFrames.Frame Kick(int x, int y, int w, int h) => OnBaseline(1462, x, y, w, h);
    private static SpiderFrames.Frame Upper(int x, int y, int w, int h) => OnBaseline(1607, x, y, w, h);
    private static SpiderFrames.Frame Land(int x, int y, int w, int h) => OnBaseline(1687, x, y, w, h);

    /// <summary>"Standing" row on the sheet, loops</summary>
    public static readonly SpiderFrames.Frame[] Standing =
    {
        Stand(8, 251, 29, 33), Stand(51, 251, 30, 33), Stand(96, 252, 30, 31), Stand(137, 252, 30, 31),
        Stand(183, 252, 30, 32), Stand(227, 252, 30, 32), Stand(268, 252, 30, 32), Stand(312, 252, 30, 32),
    };

    /// <summary>"Jab Punch" row</summary>
    public static readonly SpiderFrames.Frame[] JabPunch =
    {
        Jab(8, 1179, 36, 34), Jab(61, 1180, 50, 34), Jab(123, 1180, 49, 34), Jab(181, 1180, 48, 33),
        Jab(240, 1181, 41, 32), Jab(295, 1183, 37, 30), Jab(346, 1180, 30, 33),
    };

    /// <summary>"Upward Kick" row</summary>
    public static readonly SpiderFrames.Frame[] UpwardKick =
    {
        Kick(12, 1423, 35, 38), Kick(68, 1412, 34, 28), Kick(128, 1392, 41, 50), Kick(209, 1383, 35, 58),
        Kick(276, 1386, 31, 54), Kick(337, 1409, 40, 38), Kick(405, 1424, 32, 39),
    };

    /// <summary>"Uppercut" rows - jump part, then landing part</summary>
    public static readonly SpiderFrames.Frame[] Uppercut =
    {
        Upper(7, 1574, 43, 33), Upper(62, 1572, 46, 35), Upper(144, 1542, 34, 63), Upper(206, 1525, 28, 75),
        Upper(267, 1525, 29, 65), Upper(328, 1515, 30, 68), Upper(391, 1510, 30, 75), Upper(454, 1515, 31, 72),
        Upper(510, 1529, 29, 66), Upper(561, 1547, 31, 51),
        Land(15, 1637, 35, 48), Land(74, 1659, 48, 28), Land(133, 1659, 41, 28), Land(185, 1658, 37, 29),
        Land(234, 1656, 30, 31),
    };
}
