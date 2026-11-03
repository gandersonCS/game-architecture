using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// Base type for the events the simulation raises for the rest of the game to react to
/// (e.g. by playing a sound). Add your own events by deriving from this record.
/// </summary>
/// <param name="Tick">The tick on which the event happened</param>
public abstract record GameEvent(long Tick);

/// <summary>
/// A player joined the game
/// </summary>
public sealed record PlayerJoined(long Tick, PlayerId Player) : GameEvent(Tick);

/// <summary>
/// A player left the game
/// </summary>
public sealed record PlayerLeft(long Tick, PlayerId Player) : GameEvent(Tick);

/// <summary>
/// Spider-Man jumped
/// </summary>
public sealed record RunnerJumped(long Tick) : GameEvent(Tick);

/// <summary>
/// Spider-Man landed on a roof
/// </summary>
/// <param name="FallSpeed">How fast he was falling (world units per second), for a harder landing effect</param>
public sealed record RunnerLanded(long Tick, float FallSpeed) : GameEvent(Tick);

/// <summary>
/// Spider-Man stuck his web to an anchor
/// </summary>
public sealed record WebAttached(long Tick) : GameEvent(Tick);

/// <summary>
/// Spider-Man let go of his web
/// </summary>
public sealed record WebReleased(long Tick) : GameEvent(Tick);

/// <summary>
/// Why a run ended
/// </summary>
public enum RunEndReason { Fell, HitWall }

/// <summary>
/// The run is over. The game saves the distance (and the Spider-Essence collected, see
/// <see cref="EssenceField.CollectedThisRun"/>) when it sees this.
/// </summary>
public sealed record RunEnded(long Tick, double DistanceMeters, RunEndReason Reason) : GameEvent(Tick);

/// <summary>
/// Spider-Man collected an orb of Spider-Essence
/// </summary>
/// <param name="X">Where the orb was, along the track</param>
/// <param name="Y">Where the orb was, from the top of the screen</param>
/// <param name="CollectedThisRun">How many he has collected this run, counting this one</param>
public sealed record EssenceCollected(long Tick, double X, float Y, int CollectedThisRun) : GameEvent(Tick);
