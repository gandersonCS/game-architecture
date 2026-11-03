using System.Collections.Generic;
using System.Numerics;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// Everything that exists in the game world.
/// </summary>
/// <remarks>
/// The world is measured in its own units, not in screen pixels: how it appears on screen
/// (its scale, which part is visible, split screen, etc.) is up to the views that draw it.
/// </remarks>
public sealed class World
{
    /// <summary>
    /// The width of the world, in world units
    /// </summary>
    public const float Width = 800f;

    /// <summary>
    /// The height of the world, in world units
    /// </summary>
    public const float Height = 480f;

    // Where the first player appears; each later player appears a little further down
    private static readonly Vector2 SpawnPosition = new(100, 100);
    private static readonly Vector2 SpawnSpacing = new(0, 80);

    // A list (rather than a dictionary) keeps the players in a predictable order
    private readonly List<PlayerAvatar> _players = new();

    /// <summary>
    /// The random number generator for everything in the world.
    /// </summary>
    public DeterministicRandom Random { get; }

    /// <summary>
    /// The players' avatars, in the order they joined
    /// </summary>
    public IReadOnlyList<PlayerAvatar> Players => _players;

    /// <summary>
    /// The starter's wandering placeholder. Not drawn anymore, I just left it in so the starter tests still pass
    /// </summary>
    public Wanderer Wanderer { get; }

    /// <summary>
    /// The endless rooftops scrolling past
    /// </summary>
    public RooftopTrack Track { get; private set; }

    /// <summary>
    /// Spider-Man himself
    /// </summary>
    public Runner Runner { get; private set; }

    /// <summary>
    /// Spider-Essence orbs for the current run
    /// </summary>
    public EssenceField Essence { get; private set; }

    /// <summary>Most continues a single run can use</summary>
    public const int MaxContinues = 4;

    /// <summary>What the first continue of a run costs, in Spider-Essence</summary>
    public const int FirstContinueCost = 500;

    /// <summary>How much more each continue in the same run costs than the one before</summary>
    public const int ContinueCostStep = 100;

    /// <summary>Continues used so far this run</summary>
    public int ContinuesUsed { get; private set; }

    /// <summary>Continues left this run</summary>
    public int ContinuesLeft => MaxContinues - ContinuesUsed;

    /// <summary>What the next continue costs (500, 600, 700, 800)</summary>
    public int NextContinueCost => FirstContinueCost + ContinueCostStep * ContinuesUsed;

    /// <summary>True if he's dead and the run still has continues left (the player may not be able to afford one)</summary>
    public bool CanContinue => Runner.State == RunnerState.Dead && ContinuesUsed < MaxContinues;

    public World(ulong seed)
    {
        Random = new DeterministicRandom(seed);
        Wanderer = new Wanderer(new Vector2(100, 100), new Vector2(Width / 2, 200));
        Track = new RooftopTrack(Random);
        Runner = new Runner(Track);

        // orbs get their own RNG (based off the seed) so adding them didn't change what city a
        // seed makes
        Essence = new EssenceField(new DeterministicRandom(seed ^ EssenceSeedMix));
    }

    private const ulong EssenceSeedMix = 0x9E3779B97F4A7C15;

    /// <summary>
    /// Finds the avatar belonging to the player, or null if they have none
    /// </summary>
    public PlayerAvatar FindPlayer(PlayerId player)
        => _players.Find(p => p.Owner == player);

    /// <summary>
    /// Picks the run back up after a death: same city, same distance, same essence collected, with
    /// him standing on a safe roof. Doesn't check that the player can pay (that's up to the game,
    /// which has their saved essence) - it just counts the continue and raises
    /// <see cref="RunContinued"/> so the game can charge for it.
    /// </summary>
    internal bool Continue(EventQueue<GameEvent> events, long tick)
    {
        if (!CanContinue)
            return false;

        int cost = NextContinueCost;
        ContinuesUsed++;
        Track.Resume();
        Runner.Revive(Track);
        Essence.Resume();
        events.Raise(new RunContinued(tick, ContinuesUsed, cost, Track.DistanceMeters));
        return true;
    }

    /// <summary>
    /// Starts a new run with a fresh city and puts Spider-Man back on the first roof. Uses the same
    /// random stream so it's still deterministic
    /// </summary>
    internal void Restart()
    {
        Track = new RooftopTrack(Random);
        Runner = new Runner(Track);
        ContinuesUsed = 0;
        ulong essenceSeed = ((ulong)Random.NextUInt() << 32) | Random.NextUInt();
        Essence = new EssenceField(new DeterministicRandom(essenceSeed ^ EssenceSeedMix));
    }

    internal PlayerAvatar AddPlayer(PlayerId player)
    {
        var avatar = new PlayerAvatar(player, SpawnPosition + SpawnSpacing * _players.Count);
        _players.Add(avatar);
        return avatar;
    }

    internal void RemovePlayer(PlayerId player)
        => _players.RemoveAll(p => p.Owner == player);
}
