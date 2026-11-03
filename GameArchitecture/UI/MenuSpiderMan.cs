using System;
using Microsoft.Xna.Framework;
using GameArchitecture.Views;

namespace GameArchitecture.UI;

/// <summary>
/// Spider-Man on the main menu: he stands idle for a few seconds, then throws a random attack
/// (jab, upward kick, or uppercut), then goes back to idle, forever.
/// </summary>
/// <remarks>
/// This only decides which frame to show; <see cref="MainMenuLayer"/> draws it.
/// </remarks>
public sealed class MenuSpiderMan
{
    /// <summary>One animation: its frames, how long each is shown, and whether it repeats</summary>
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

    // How long he stays idle between attacks (seconds)
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

    /// <summary>True while an attack is playing (false while idle)</summary>
    public bool IsFighting => !ReferenceEquals(_clip, Idle);

    /// <summary>The frame to draw this frame</summary>
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

    // A random attack, never the same one twice in a row
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
/// Where the menu's idle and attack frames are on Textures/spiderman.png. Like
/// <see cref="SpiderFrames"/>, each source rectangle is a tight box around one frame, and each
/// animation shares a baseline (the sheet row of his feet) so he doesn't jitter up and down.
/// </summary>
public static class MenuSpiderFrames
{
    // Anchored on a shared baseline: the origin is the frame's middle, at the baseline's height
    private static SpiderFrames.Frame OnBaseline(int baseline, int x, int y, int width, int height)
        => new(new Rectangle(x, y, width, height), new Vector2(width / 2f, baseline - y));

    private static SpiderFrames.Frame Stand(int x, int y, int w, int h) => OnBaseline(284, x, y, w, h);
    private static SpiderFrames.Frame Jab(int x, int y, int w, int h) => OnBaseline(1213, x, y, w, h);
    private static SpiderFrames.Frame Kick(int x, int y, int w, int h) => OnBaseline(1462, x, y, w, h);
    private static SpiderFrames.Frame Upper(int x, int y, int w, int h) => OnBaseline(1607, x, y, w, h);
    private static SpiderFrames.Frame Land(int x, int y, int w, int h) => OnBaseline(1687, x, y, w, h);

    /// <summary>The "Standing" row: a looping idle</summary>
    public static readonly SpiderFrames.Frame[] Standing =
    {
        Stand(8, 251, 29, 33), Stand(51, 251, 30, 33), Stand(96, 252, 30, 31), Stand(137, 252, 30, 31),
        Stand(183, 252, 30, 32), Stand(227, 252, 30, 32), Stand(268, 252, 30, 32), Stand(312, 252, 30, 32),
    };

    /// <summary>The "Jab Punch" row</summary>
    public static readonly SpiderFrames.Frame[] JabPunch =
    {
        Jab(8, 1179, 36, 34), Jab(61, 1180, 50, 34), Jab(123, 1180, 49, 34), Jab(181, 1180, 48, 33),
        Jab(240, 1181, 41, 32), Jab(295, 1183, 37, 30), Jab(346, 1180, 30, 33),
    };

    /// <summary>The "Upward Kick" row</summary>
    public static readonly SpiderFrames.Frame[] UpwardKick =
    {
        Kick(12, 1423, 35, 38), Kick(68, 1412, 34, 28), Kick(128, 1392, 41, 50), Kick(209, 1383, 35, 58),
        Kick(276, 1386, 31, 54), Kick(337, 1409, 40, 38), Kick(405, 1424, 32, 39),
    };

    /// <summary>The "Uppercut" rows: the jump, then the landing</summary>
    public static readonly SpiderFrames.Frame[] Uppercut =
    {
        Upper(7, 1574, 43, 33), Upper(62, 1572, 46, 35), Upper(144, 1542, 34, 63), Upper(206, 1525, 28, 75),
        Upper(267, 1525, 29, 65), Upper(328, 1515, 30, 68), Upper(391, 1510, 30, 75), Upper(454, 1515, 31, 72),
        Upper(510, 1529, 29, 66), Upper(561, 1547, 31, 51),
        Land(15, 1637, 35, 48), Land(74, 1659, 48, 28), Land(133, 1659, 41, 28), Land(185, 1658, 37, 29),
        Land(234, 1656, 30, 31),
    };
}
