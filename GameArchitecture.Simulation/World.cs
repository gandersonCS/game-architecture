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
    /// The placeholder wandering entity (no longer drawn; kept so the starter's tests still run)
    /// </summary>
    public Wanderer Wanderer { get; }

    /// <summary>
    /// The endless rooftops that scroll past the runner
    /// </summary>
    public RooftopTrack Track { get; private set; }

    /// <summary>
    /// Spider-Man
    /// </summary>
    public Runner Runner { get; private set; }

    /// <summary>
    /// The Spider-Essence orbs to collect on this run
    /// </summary>
    public EssenceField Essence { get; private set; }

    public World(ulong seed)
    {
        Random = new DeterministicRandom(seed);
        Wanderer = new Wanderer(new Vector2(100, 100), new Vector2(Width / 2, 200));
        Track = new RooftopTrack(Random);
        Runner = new Runner(Track);

        // The orbs have their own random numbers (derived from the seed), so spawning them
        // doesn't change the city a seed builds
        Essence = new EssenceField(new DeterministicRandom(seed ^ EssenceSeedMix));
    }

    private const ulong EssenceSeedMix = 0x9E3779B97F4A7C15;

    /// <summary>
    /// Finds the avatar belonging to the player, or null if they have none
    /// </summary>
    public PlayerAvatar FindPlayer(PlayerId player)
        => _players.Find(p => p.Owner == player);

    /// <summary>
    /// Starts a new run: a new city (from the same random stream, so it is still deterministic)
    /// and Spider-Man back on its first roof
    /// </summary>
    internal void Restart()
    {
        Track = new RooftopTrack(Random);
        Runner = new Runner(Track);
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
