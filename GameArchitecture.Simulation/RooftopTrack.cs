using System;
using System.Collections.Generic;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// Building color scheme - each one matches a panel in the city tileset.
/// </summary>
public enum BuildingStyle { Blue = 0, Green = 1, Red = 2 }

/// <summary>
/// A window on a building. Column is relative to the building, row is the actual tile row.
/// Windows are 2x2 tiles.
/// </summary>
public readonly record struct WindowSlot(int Column, int Row);

/// <summary>
/// Web anchor point in track space. X is distance along the whole track (not screen x), Y is
/// from the top of the screen and can go negative if the anchor is above the screen.
/// </summary>
public readonly record struct WebAnchor(double X, float Y);

/// <summary>
/// A single building: a run of columns with a flat roof at <see cref="TopRow"/> that goes down
/// off the bottom of the screen.
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

    /// <summary>First column, counted along the whole track (not the screen)</summary>
    public int StartColumn { get; }
    public int WidthTiles { get; }
    /// <summary>One past the last column</summary>
    public int EndColumn => StartColumn + WidthTiles;
    /// <summary>Roof tile row. Lower number = taller building (row 0 is the top of the screen).</summary>
    public int TopRow { get; }
    public BuildingStyle Style { get; }
    public IReadOnlyList<WindowSlot> Windows { get; }
}

/// <summary>
/// The endless city. The runner doesn't actually move forward - the track scrolls past him and
/// we keep generating random-height buildings (sometimes with gaps) just off the right side.
/// </summary>
/// <remarks>
/// This is part of the simulation, so it only changes inside <see cref="Advance"/> (once per tick),
/// uses the tick's dt, and pulls randomness from the world's seeded RNG. Same seed = same city
/// every time. No MonoGame stuff in here, the views just read from it to draw.
///
/// Units: a tile is <see cref="TileSize"/> world units and the screen is <see cref="Rows"/> tiles
/// tall. Positions are in "track space" (distance along the whole track); the view subtracts
/// <see cref="ScrollX"/> to get screen coordinates.
/// </remarks>
public sealed class RooftopTrack
{
    /// <summary>World units per tile. Tiles are 16px in the art, drawn at 2x.</summary>
    public const int TileSize = 32;

    /// <summary>Rows of tiles on screen (15 * 32 = 480, same as the world height)</summary>
    public const int Rows = 15;

    // roof height range in tile rows. 6 = tall building, 11 = short
    public const int MinTopRow = 6;
    public const int MaxTopRow = 11;

    /// <summary>Max a roof can go up or down compared to the last building (in tiles)</summary>
    public const int MaxStepRows = 2;

    public const int MinWidthTiles = 6;
    public const int MaxWidthTiles = 16;

    // gap sizes in tiles. each new building rolls one of:
    //   no gap     - buildings touch
    //   small gap  - you can just jump it
    //   big gap    - too far to jump at starting speed, you need to jump and then swing
    public const float NoGapChance = 0.35f;
    public const float SmallGapChance = 0.35f;     // whatever's left over = big gap
    public const int SmallGapMin = 2;
    public const int SmallGapMax = 4;
    public const int BigGapMin = 9;
    public const int BigGapMax = 13;

    /// <summary>Runner's x on screen (world units from the left edge)</summary>
    public const float RunnerX = 200f;

    /// <summary>How far ahead of the runner we generate buildings. Bigger than any window size.</summary>
    public const float LookAhead = 1800f;

    // drop buildings once they're this far off the left side
    private const double DiscardMargin = 600;

    // cruising speed (units/sec) and how quickly it ramps up over a run
    public const float BaseSpeed = 360f;
    public const float MaxSpeed = 720f;
    public const float Acceleration = 4f;

    // a swing can leave the city going faster than cruising speed. it slows back down gradually
    // so momentum carries over. if it's slower it speeds back up at SpeedRecovery
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

    /// <summary>Buildings currently on screen or coming up, left to right</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>Total scroll distance in world units. Double so long runs don't get jittery.</summary>
    public double ScrollX { get; private set; }

    /// <summary>Runner's position along the track (he's fixed on screen, the track moves)</summary>
    public double RunnerTrackX => ScrollX + RunnerX;

    /// <summary>
    /// Normal speed of the city, slowly goes up the longer you survive
    /// </summary>
    public float CruiseSpeed => MathF.Min(MaxSpeed, BaseSpeed + Acceleration * (float)_elapsed);

    /// <summary>
    /// Current scroll speed in units/sec. Normally the cruising speed, but during (and right after)
    /// a swing it's whatever speed the swing gave him.
    /// </summary>
    public float Speed { get; private set; } = BaseSpeed;

    /// <summary>Distance run in meters (1 tile = 1 meter)</summary>
    public double DistanceMeters => ScrollX / TileSize;

    /// <summary>Set to false when the run ends so the city stops</summary>
    public bool IsScrolling { get; internal set; } = true;

    /// <summary>
    /// True while swinging. The runner controls the scroll position then (see <see cref="DriveScroll"/>)
    /// instead of the track moving on its own.
    /// </summary>
    public bool ScrollDrivenByRunner { get; internal set; }

    /// <summary>
    /// Lets the runner set the scroll position and speed directly. Used while swinging since the
    /// swing decides the speed.
    /// </summary>
    internal void DriveScroll(double scrollX, float speed)
    {
        ScrollX = scrollX;
        Speed = speed;
    }

    /// <summary>
    /// Scrolls the track and spawns new buildings ahead. Call this once per tick.
    /// </summary>
    public void Advance(float deltaSeconds)
    {
        if (IsScrolling)
        {
            _elapsed += deltaSeconds;

            // go back to cruising speed - slowly if we're going fast (keeps momentum), faster if we're slow
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
    /// Roof y at a given spot on the track, or null if it's a gap
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

    /// <summary>Roof y, or the bottom of the screen if it's a gap</summary>
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
            // first roof is long and medium height so you don't die right away
            gap = 0;
            width = 24;
            top = 9;
        }
        else
        {
            gap = PickGap();
            width = _random.NextInt(MinWidthTiles, MaxWidthTiles + 1);

            // random height but within MaxStepRows of the last roof
            int previous = _buildings[^1].TopRow;
            int lowest = Math.Max(MinTopRow, previous - MaxStepRows);
            int highest = Math.Min(MaxTopRow, previous + MaxStepRows);
            top = _random.NextInt(lowest, highest + 1);
        }

        var style = (BuildingStyle)_random.NextInt(3);

        // windows are 2x2, placed on a grid (every 4 columns, every 3 rows)
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
