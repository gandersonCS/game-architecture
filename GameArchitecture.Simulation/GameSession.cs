using CIS580.Simulation;

namespace GameArchitecture.Simulation;

/// <summary>
/// The game simulation: the single source of truth for the state of a game in progress,
/// and the rules that change it.
/// </summary>
/// <remarks>
/// A SessionRunner advances the session one fixed-length tick at a time, supplying every
/// player's command. Everything else (screens, views, audio) only reads the session's state
/// and reacts to its <see cref="Events"/>; nothing outside the simulation changes its state.
/// This lets the same session run in a game client, a headless server, or a unit test.
/// </remarks>
public sealed class GameSession : ISimulation
{
    /// <summary>
    /// Everything that exists in the game world
    /// </summary>
    public World World { get; }

    /// <summary>
    /// Events raised by the simulation, for the rest of the game to react to
    /// </summary>
    public EventQueue<GameEvent> Events { get; } = new();

    /// <summary>
    /// The number of ticks simulated so far
    /// </summary>
    public long TickCount { get; private set; }

    /// <summary>
    /// Creates a new game session
    /// </summary>
    /// <param name="seed">
    /// The seed for the session's random numbers. Sessions with the same seed, given the same
    /// commands, will play out identically.
    /// </param>
    public GameSession(ulong seed)
    {
        World = new World(seed);
    }

    /// <inheritdoc/>
    public void AddPlayer(PlayerId player)
    {
        World.AddPlayer(player);
        Events.Raise(new PlayerJoined(TickCount, player));
    }

    /// <inheritdoc/>
    public void RemovePlayer(PlayerId player)
    {
        World.RemovePlayer(player);
        Events.Raise(new PlayerLeft(TickCount, player));
    }

    /// <inheritdoc/>
    public void Tick(in TickContext context)
    {
        float dt = context.DeltaSeconds;

        foreach (var input in context.Inputs)
            World.FindPlayer(input.Player)?.ApplyInput(input, dt);

        World.Wanderer.Update(World, dt);

        // every player's input drives the same runner, so player 2 can help or take over
        bool jumpPressed = false, jumpDown = false;
        foreach (var input in context.Inputs)
        {
            jumpPressed |= input.WasPressed(GameAction.Jump);
            jumpDown |= input.IsDown(GameAction.Jump);
        }

        World.Track.Advance(dt);
        var runnerInput = new RunnerInput(jumpPressed, jumpDown);
        bool restart = World.Runner.Update(runnerInput, World.Track, dt, Events, context.Tick);

        // do this after the runner moves so he picks up what he actually went through
        World.Essence.Update(World.Track, World.Runner, Events, context.Tick);

        if (restart)
            World.Restart();

        TickCount = context.Tick + 1;
    }
}
