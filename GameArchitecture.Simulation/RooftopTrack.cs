using System;
using System.Collections.Generic;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// Which color scheme a building uses (each is a panel in the city tileset).
/// </summary>
public enum BuildingStyle { Blue = 0, Green = 1, Red = 2 }

/// <summary>
/// Where a window sits on a building: a column local to the building, and an absolute tile row.
/// A window is 2x2 tiles.
/// </summary>
public readonly record struct WindowSlot(int Column, int Row);

/// <summary>
/// A point in the sky the runner is swinging from, in track space (X is the distance along the
/// whole track, not the screen; Y is world units from the top of the screen, and can be
/// negative: above the top of the screen).
/// </summary>
public readonly record struct WebAnchor(double X, float Y);

/// <summary>
/// One rooftop: a block of columns, with a flat roof at <see cref="TopRow"/>, that extends
/// down past the bottom of the screen.
/// </summary>
public sealed class Building
{
    public Building(int startColumn, int widthTiles, int topRow, BuildingStyle style, IReadOnlyList<WindowSlot> windows)
    {
        StartColumn = startColumn;
        WidthTiles = widthTiles;
        TopRow = topRow;
        Style = style;
        Windows = windows;
    }

    /// <summary>First tile column, counted along the whole endless track (not the screen)</summary>
    public int StartColumn { get; }
    public int WidthTiles { get; }
    /// <summary>One past the last tile column</summary>
    public int EndColumn => StartColumn + WidthTiles;
    /// <summary>The tile row of the roof. Smaller means taller (row 0 is the top of the screen).</summary>
    public int TopRow { get; }
    public BuildingStyle Style { get; }
    public IReadOnlyList<WindowSlot> Windows { get; }
}

/// <summary>
/// The endless city the runner runs along. The runner never really moves forward: the track
/// scrolls past, and new rooftops of random height (with gaps between some of them) are
/// generated just ahead of the screen.
/// </summary>
/// <remarks>
/// This is simulation state, so it follows the same rules as the rest of the simulation: it
/// only changes in <see cref="Advance"/> (called once per tick), takes time from the tick's
/// delta, and gets its randomness from the world's seeded generator. The same seed therefore
/// always builds the same city. It has no MonoGame types, so the views draw it by reading it.
///
/// Units: one tile is <see cref="TileSize"/> world units, and the screen is
/// <see cref="Rows"/> tiles tall. Everything is positioned in "track space" (the distance
/// along the whole track); the view subtracts <see cref="ScrollX"/> to get a screen position.
/// </remarks>
public sealed class RooftopTrack
{
    /// <summary>World units per tile. The art is 16 pixel tiles drawn at 2x.</summary>
    public const int TileSize = 32;

    /// <summary>Tile rows on screen (15 x 32 = 480, the world's height)</summary>
    public const int Rows = 15;

    // Roof heights, as tile rows: row 6 is a tall building, row 11 a short one
    public const int MinTopRow = 6;
    public const int MaxTopRow = 11;

    /// <summary>The most a roof may rise or drop from the previous building, in tiles</summary>
    public const int MaxStepRows = 2;

    public const int MinWidthTiles = 6;
    public const int MaxWidthTiles = 16;

    // Gaps between buildings, in tiles. A roll picks one of three kinds:
    //   none         (the next building touches this one),
    //   small gaps   (a jump clears them),
    //   big gaps     (too wide to jump at the starting speed: jump, then swing across them).
    public const float NoGapChance = 0.35f;
    public const float SmallGapChance = 0.35f;     // the rest are big gaps
    public const int SmallGapMin = 2;
    public const int SmallGapMax = 4;
    public const int BigGapMin = 9;
    public const int BigGapMax = 13;

    /// <summary>Where the runner stands, in world units from the left of the screen</summary>
    public const float RunnerX = 200f;

    /// <summary>How far past the runner (world units) buildings are generated. Wider than any window.</summary>
    public const float LookAhead = 1800f;

    // Buildings are forgotten once this far behind the left edge of the screen
    private const double DiscardMargin = 600;

    // The cruising scroll speed, in world units per second, and how fast it ramps up
    public const float BaseSpeed = 360f;
    public const float MaxSpeed = 720f;
    public const float Acceleration = 4f;

    // After a swing the city may be going faster than cruising speed. It settles back gradually
    // (so momentum carries), and picks up again at this rate if it was slower.
    public const float SpeedDecaySeconds = 1.6f;
    public const float SpeedRecovery = 500f;

    private readonly DeterministicRandom _random;
    private readonly List<Building> _buildings = new();
    private int _nextColumn;
    private double _elapsed;

    public RooftopTrack(DeterministicRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        Generate();
    }

    /// <summary>The buildings that are on screen or about to be, from left to right</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>How far the track has scrolled, in world units. A double so very long runs stay smooth.</summary>
    public double ScrollX { get; private set; }

    /// <summary>The runner's position along the track (he stands still on screen; the track moves)</summary>
    public double RunnerTrackX => ScrollX + RunnerX;

    /// <summary>
    /// The speed the city settles at, which creeps up the longer the run goes on
    /// </summary>
    public float CruiseSpeed => MathF.Min(MaxSpeed, BaseSpeed + Acceleration * (float)_elapsed);

    /// <summary>
    /// How fast the city is scrolling right now, in world units per second. It is the cruising
    /// speed, except while swinging (and just after), when the runner's momentum sets it.
    /// </summary>
    public float Speed { get; private set; } = BaseSpeed;

    /// <summary>How far the runner has gone, in meters (one tile is one meter)</summary>
    public double DistanceMeters => ScrollX / TileSize;

    /// <summary>False once the run is over: the city stops scrolling</summary>
    public bool IsScrolling { get; internal set; } = true;

    /// <summary>
    /// True while the runner is swinging: then he decides where the city is (see
    /// <see cref="DriveScroll"/>) instead of it scrolling at its own speed.
    /// </summary>
    public bool ScrollDrivenByRunner { get; internal set; }

    /// <summary>
    /// Sets where the city is and how fast it is moving. Used by the runner while swinging,
    /// because a swing's speed comes from the swing.
    /// </summary>
    internal void DriveScroll(double scrollX, float speed)
    {
        ScrollX = scrollX;
        Speed = speed;
    }

    /// <summary>
    /// Scrolls the track and generates new buildings ahead of the screen. Call once per tick.
    /// </summary>
    public void Advance(float deltaSeconds)
    {
        if (IsScrolling)
        {
            _elapsed += deltaSeconds;

            // Ease back to cruising speed: gently if we're fast (momentum), quicker if slow
            float cruise = CruiseSpeed;
            if (Speed < cruise)
                Speed = MathF.Min(cruise, Speed + SpeedRecovery * deltaSeconds);
            else
                Speed = cruise + (Speed - cruise) * MathF.Exp(-deltaSeconds / SpeedDecaySeconds);

            if (!ScrollDrivenByRunner)
                ScrollX += Speed * deltaSeconds;
        }

        Generate();
        Discard();
    }

    /// <summary>
    /// The y position of the roof at a point along the track, or null if there is no building
    /// there (a gap)
    /// </summary>
    public float? RoofAt(double trackX)
    {
        foreach (var building in _buildings)
        {
            if (trackX >= building.StartColumn * (double)TileSize && trackX < building.EndColumn * (double)TileSize)
                return building.TopRow * TileSize;
        }
        return null;
    }

    /// <summary>The roof's y position, or the bottom of the screen if there's a gap there</summary>
    public float GroundYAt(double trackX) => RoofAt(trackX) ?? Rows * TileSize;

    private void Generate()
    {
        double needed = ScrollX + LookAhead;
        while (_nextColumn * (double)TileSize < needed)
            AddBuilding();
    }

    private void Discard()
    {
        while (_buildings.Count > 1 && _buildings[0].EndColumn * (double)TileSize < ScrollX - DiscardMargin)
            _buildings.RemoveAt(0);
    }

    private int PickGap()
    {
        float roll = _random.NextFloat();
        if (roll < NoGapChance)
            return 0;
        if (roll < NoGapChance + SmallGapChance)
            return _random.NextInt(SmallGapMin, SmallGapMax + 1);
        return _random.NextInt(BigGapMin, BigGapMax + 1);
    }

    private void AddBuilding()
    {
        int gap, width, top;

        if (_buildings.Count == 0)
        {
            // A long, middling first roof, so the runner starts on solid ground
            gap = 0;
            width = 24;
            top = 9;
        }
        else
        {
            gap = PickGap();
            width = _random.NextInt(MinWidthTiles, MaxWidthTiles + 1);

            // Random height, but never more than a couple of tiles different from the last roof
            int previous = _buildings[^1].TopRow;
            int lowest = Math.Max(MinTopRow, previous - MaxStepRows);
            int highest = Math.Min(MaxTopRow, previous + MaxStepRows);
            top = _random.NextInt(lowest, highest + 1);
        }

        var style = (BuildingStyle)_random.NextInt(3);

        // Windows are 2x2 tiles, on a grid: a column every 4 tiles, a row every 3
        var windows = new List<WindowSlot>();
        for (int row = top + 2; row + 2 <= Rows; row += 3)
        {
            for (int column = 1; column + 2 <= width - 1; column += 4)
            {
                if (_random.Chance(0.55f))
                    windows.Add(new WindowSlot(column, row));
            }
        }

        int start = _nextColumn + gap;
        _buildings.Add(new Building(start, width, top, style, windows));
        _nextColumn = start + width;
    }
}
