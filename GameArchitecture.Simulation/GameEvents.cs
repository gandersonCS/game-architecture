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
/// Fired when Spider-Man jumps
/// </summary>
public sealed record RunnerJumped(long Tick) : GameEvent(Tick);

/// <summary>
/// Fired when he lands on a roof
/// </summary>
/// <param name="FallSpeed">Fall speed when he hit (units/sec), used to make hard landings feel heavier</param>
public sealed record RunnerLanded(long Tick, float FallSpeed) : GameEvent(Tick);

/// <summary>
/// Fired when his web sticks
/// </summary>
public sealed record WebAttached(long Tick) : GameEvent(Tick);

/// <summary>
/// Fired when he lets go of the web
/// </summary>
public sealed record WebReleased(long Tick) : GameEvent(Tick);

/// <summary>
/// How the run ended
/// </summary>
public enum RunEndReason { Fell, HitWall }

/// <summary>
/// Run's over. The game saves the distance when it sees this, along with the essence collected
/// (<see cref="EssenceField.CollectedThisRun"/>).
/// </summary>
public sealed record RunEnded(long Tick, double DistanceMeters, RunEndReason Reason) : GameEvent(Tick);

/// <summary>
/// The player continued after dying and picked the run back up. The game takes <paramref name="Cost"/>
/// out of the player's total Spider-Essence when it sees this.
/// </summary>
/// <param name="ContinuesUsed">How many continues this run has used, including this one</param>
/// <param name="Cost">Spider-Essence this continue costs</param>
/// <param name="DistanceMeters">Distance when he died (it carries on from here)</param>
public sealed record RunContinued(long Tick, int ContinuesUsed, int Cost, double DistanceMeters) : GameEvent(Tick);

/// <summary>
/// Fired when he grabs a Spider-Essence orb
/// </summary>
/// <param name="X">Orb's x along the track</param>
/// <param name="Y">Orb's y from the top of the screen</param>
/// <param name="CollectedThisRun">Total collected this run, including this one</param>
/// <param name="Pack">Which pack the orb came from (see <see cref="EssenceOrb.Pack"/>), so audio can tell a
/// streak through one pack from a new pack</param>
public sealed record EssenceCollected(long Tick, double X, float Y, int CollectedThisRun, int Pack) : GameEvent(Tick);
