using System;
using System.Collections.Generic;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// One Spider-Essence pickup, in track space (X is the distance along the whole track; Y is
/// world units from the top of the screen)
/// </summary>
/// <param name="Pack">Which pack it was spawned in (packs are numbered from 0, in order)</param>
public readonly record struct EssenceOrb(double X, float Y, int Pack);

/// <summary>
/// The shape a pack of Spider-Essence is laid out in
/// </summary>
public enum EssencePackShape { Line, Circle }

/// <summary>
/// The Spider-Essence currency: packs of 3 to 7 orbs, laid out in a line or a tight circle,
/// spawned ahead of the runner either sitting on a rooftop or floating in the air where he
/// can jump or swing through them.
/// </summary>
/// <remarks>
/// Like the <see cref="RooftopTrack"/>, this is simulation state: it only changes in
/// <see cref="Update"/> (once per tick, after the runner has moved), and gets its randomness
/// from its own seeded generator, so the same seed always spawns the same orbs (and adding
/// orbs didn't change the city a seed builds). Orbs are removed when collected, and each
/// collection raises an <see cref="EssenceCollected"/> event for the views and sounds.
/// </remarks>
public sealed class EssenceField
{
    public const int MinPackSize = 3;
    public const int MaxPackSize = 7;

    /// <summary>Where the first pack may start, along the track (just off the right of the screen)</summary>
    public const double FirstPackX = 900;

    // The space between the end of one pack and the start of the next, in world units
    public const float MinPackSpacing = 450f;
    public const float MaxPackSpacing = 950f;

    /// <summary>The chance a pack tries to sit on a roof (it floats in the air if there's no flat roof long enough)</summary>
    public const float RoofChance = 0.5f;

    /// <summary>The chance a pack is a circle rather than a line</summary>
    public const float CircleChance = 0.45f;

    // Layout, in world units
    public const float LineSpacing = 34f;
    public const float CircleSpacing = 28f;
    public const float MinCircleRadius = 24f;

    /// <summary>How high above a roof a line of orbs floats: in reach while running</summary>
    public const float LineAboveRoof = 30f;

    /// <summary>
    /// How high above a roof the middle of a circle is: the bottom orbs can be run through, the
    /// top ones need a jump
    /// </summary>
    public const float CircleAboveRoof = 75f;

    // Packs in the air float this far above the highest roof around them: in reach of a jump or a swing
    public const float MinAirAboveRoof = 100f;
    public const float MaxAirAboveRoof = 175f;

    /// <summary>The steepest a line of orbs in the air may slope (rise over run)</summary>
    public const float MaxAirSlope = 0.35f;

    /// <summary>No orb is placed higher than this (nearer the top of the screen)</summary>
    public const float MinOrbY = 40f;

    /// <summary>How close to Spider-Man's body (feet to hands) an orb must come to be collected</summary>
    public const float CollectRadius = 24f;

    // Orbs are forgotten once this far behind the left edge of the screen
    private const double DiscardMargin = 200;

    private static readonly double MaxPackLength = (MaxPackSize - 1) * LineSpacing;

    private readonly DeterministicRandom _random;
    private readonly List<EssenceOrb> _orbs = new();
    private double _nextPackX = FirstPackX;
    private int _packCount;

    // Where the runner was last tick, so a fast swing can't skip over an orb between ticks
    private bool _hasLast;
    private double _lastRunnerX;
    private float _lastFeetY;

    public EssenceField(DeterministicRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>The orbs not yet collected (ahead of the runner, or just behind)</summary>
    public IReadOnlyList<EssenceOrb> Orbs => _orbs;

    /// <summary>How many orbs Spider-Man has collected this run</summary>
    public int CollectedThisRun { get; private set; }

    /// <summary>How many packs have been spawned so far</summary>
    public int PackCount => _packCount;

    /// <summary>
    /// Spawns packs ahead of the runner, collects the ones he touches, and forgets the ones
    /// left behind. Call once per tick, after the runner has moved.
    /// </summary>
    public void Update(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick)
    {
        Generate(track);
        if (runner.State != RunnerState.Dead)
            Collect(track, runner, events, tick);
        Discard(track);
    }

    private void Generate(RooftopTrack track)
    {
        // Only lay out a pack once the buildings under all of it exist
        double coveredTo = track.Buildings[^1].EndColumn * (double)RooftopTrack.TileSize;
        while (_nextPackX + MaxPackLength < coveredTo)
            AddPack(track);
    }

    private void AddPack(RooftopTrack track)
    {
        int count = _random.NextInt(MinPackSize, MaxPackSize + 1);
        var shape = _random.Chance(CircleChance) ? EssencePackShape.Circle : EssencePackShape.Line;

        // A tight circle: just big enough that its orbs don't overlap
        float radius = MathF.Max(MinCircleRadius, count * CircleSpacing / (2 * MathF.PI));
        double start = _nextPackX;
        double length = shape == EssencePackShape.Line ? (count - 1) * LineSpacing : 2 * radius;
        double end = start + length;

        bool wantsRoof = _random.Chance(RoofChance);
        float airAbove = _random.NextFloat(MinAirAboveRoof, MaxAirAboveRoof);
        float slope = _random.NextFloat(-MaxAirSlope, MaxAirSlope);

        int pack = _packCount++;
        if (wantsRoof && FlatRoof(track, start - CollectRadius, end + CollectRadius) is float roof)
        {
            // Sitting on the roof
            if (shape == EssencePackShape.Line)
                AddLine(pack, count, start, roof - LineAboveRoof, 0);
            else
                AddCircle(pack, count, start + radius, roof - CircleAboveRoof, radius);
        }
        else
        {
            // Floating in the air, above the highest roof around it (it may be over a gap)
            float highestRoof = HighestRoof(track, start - 200, end);
            float centerY = highestRoof - airAbove;

            if (shape == EssencePackShape.Line)
            {
                float halfRise = MathF.Abs(slope) * (float)length / 2;
                centerY = MathF.Max(centerY, MinOrbY + halfRise);
                AddLine(pack, count, start, centerY - slope * (float)length / 2, slope);
            }
            else
            {
                centerY = MathF.Max(centerY, MinOrbY + radius);
                AddCircle(pack, count, start + radius, centerY, radius);
            }
        }

        _nextPackX = end + _random.NextFloat(MinPackSpacing, MaxPackSpacing);
    }

    private void AddLine(int pack, int count, double startX, float startY, float slope)
    {
        for (int i = 0; i < count; i++)
        {
            float along = i * LineSpacing;
            _orbs.Add(new EssenceOrb(startX + along, startY + slope * along, pack));
        }
    }

    private void AddCircle(int pack, int count, double centerX, float centerY, float radius)
    {
        // Start at the top and go round
        for (int i = 0; i < count; i++)
        {
            float angle = -MathF.PI / 2 + i * 2 * MathF.PI / count;
            _orbs.Add(new EssenceOrb(centerX + radius * MathF.Cos(angle), centerY + radius * MathF.Sin(angle), pack));
        }
    }

    /// <summary>
    /// The roof's y position if one flat roof (no gaps, no steps) covers the whole stretch, otherwise null
    /// </summary>
    private static float? FlatRoof(RooftopTrack track, double from, double to)
    {
        float? first = track.RoofAt(from);
        if (first is null)
            return null;

        for (double x = from; x < to; x += RooftopTrack.TileSize / 2)
        {
            if (track.RoofAt(x) != first)
                return null;
        }
        return track.RoofAt(to) == first ? first : null;
    }

    /// <summary>
    /// The y position of the highest (smallest y) roof over a stretch, or the lowest possible
    /// roof if it is all gap
    /// </summary>
    private static float HighestRoof(RooftopTrack track, double from, double to)
    {
        float highest = RooftopTrack.MaxTopRow * RooftopTrack.TileSize;
        for (double x = from; x <= to; x += RooftopTrack.TileSize / 2)
        {
            if (track.RoofAt(x) is float roof)
                highest = MathF.Min(highest, roof);
        }
        return highest;
    }

    private void Collect(RooftopTrack track, Runner runner, EventQueue<GameEvent> events, long tick)
    {
        double x = track.RunnerTrackX;
        float feetY = runner.FeetY;
        if (!_hasLast)
        {
            _lastRunnerX = x;
            _lastFeetY = feetY;
            _hasLast = true;
        }

        // The box Spider-Man's body (feet to hands) swept through since last tick, plus the reach
        double left = Math.Min(_lastRunnerX, x) - CollectRadius;
        double right = Math.Max(_lastRunnerX, x) + CollectRadius;
        float top = MathF.Min(_lastFeetY, feetY) - Runner.HandHeight - CollectRadius;
        float bottom = MathF.Max(_lastFeetY, feetY) + CollectRadius;

        for (int i = 0; i < _orbs.Count; i++)
        {
            var orb = _orbs[i];
            if (orb.X < left || orb.X > right || orb.Y < top || orb.Y > bottom)
                continue;

            _orbs.RemoveAt(i--);
            CollectedThisRun++;
            events.Raise(new EssenceCollected(tick, orb.X, orb.Y, CollectedThisRun));
        }

        _lastRunnerX = x;
        _lastFeetY = feetY;
    }

    private void Discard(RooftopTrack track)
    {
        double behind = track.ScrollX - DiscardMargin;
        _orbs.RemoveAll(orb => orb.X < behind);
    }
}
