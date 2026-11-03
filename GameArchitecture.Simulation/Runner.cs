using System;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

public enum RunnerState
{
    /// <summary>On a roof</summary>
    Running,
    /// <summary>Jumping, or falling after running off a roof or letting go of the web</summary>
    Airborne,
    /// <summary>Hanging from a web anchor</summary>
    Swinging,
    /// <summary>The run is over</summary>
    Dead,
}

/// <summary>
/// What the players asked the runner to do this tick (combined from every player's command).
/// There is only one button: it jumps, and held in the air it swings.
/// </summary>
public readonly record struct RunnerInput(bool JumpPressed, bool JumpDown);

/// <summary>
/// Spider-Man, as the simulation sees him: where his feet are vertically, and what he is doing.
/// Horizontally he never moves: the <see cref="RooftopTrack"/> scrolls past him.
/// </summary>
/// <remarks>
/// <para>
/// One button does everything. Press it on a roof to jump: the longer you hold it, the higher
/// you go (letting go early cuts the jump short). A brief grace period after running off an
/// edge, and a brief memory of a press just before landing, keep it feeling fair.
/// </para>
/// <para>
/// Keep holding it in the air, once you start to fall, and Spider-Man shoots a web at a point in
/// the sky just ahead of him and swings from it. There are no fixed anchors: he can swing from
/// anywhere.
/// </para>
/// <para>
/// The swing is a real pendulum, and it keeps his momentum: he enters with the speed he already
/// had, speeds up as he swoops through the bottom of the arc, and leaves with that speed (the
/// city then settles back to cruising speed gradually). Since he stands still on screen, his
/// swing is what moves the city: the track is scrolled to wherever the pendulum puts him. The
/// rope is longer the faster he goes, so a swing takes about the same time at any speed. Let go,
/// or swing as far round as where he started, and he drops. Pressing again in the air swings
/// again. Hit the side of a building, or fall into a gap, and the run ends.
/// </para>
/// </remarks>
public sealed class Runner
{
    // Tuning, in world units and seconds
    public const float Gravity = 1900f;
    public const float JumpSpeed = 720f;
    /// <summary>Letting go of jump early keeps only this much of the upward speed</summary>
    public const float JumpCut = 0.45f;
    /// <summary>How far below his hands his feet hang while swinging</summary>
    public const float HandHeight = 60f;
    /// <summary>How far below a roof's surface the runner may be and still get up onto it</summary>
    public const float StepTolerance = 28f;
    public const float CoyoteSeconds = 0.09f;
    public const float JumpBufferSeconds = 0.10f;
    // The web sticks to a point ahead of and above his hands. How far ahead is however far he'd
    // travel in this many seconds, so a swing lasts about the same time at any speed.
    // Raise WebAheadSeconds for wider, longer swings; the rope is WebAboveRatio times as high as it is far.
    public const float WebAheadSeconds = 0.4f;
    public const float WebAboveRatio = 1.4f;
    public const float MinWebAhead = 120f;

    /// <summary>
    /// Gravity while swinging. Higher means a snappier, faster pendulum with a bigger rush of
    /// speed at the bottom of the arc.
    /// </summary>
    public const float SwingGravity = 1500f;

    /// <summary>A small push along the swing while he moves forward, so swings build speed</summary>
    public const float SwingDrive = 150f;

    /// <summary>How much faster than he was going he may be flung into a swing, at the most</summary>
    public const float MaxEntryBoost = 1.5f;

    /// <summary>Letting go of the web keeps only this much of any upward speed, so he doesn't fly high</summary>
    public const float ReleaseUpFraction = 0.4f;
    public const float RestartDelaySeconds = 0.8f;

    // Where on his body roofs are checked for: a little behind and a little in front of his middle
    private const float FootBack = 10f;
    private const float FootFront = 20f;

    private EventQueue<GameEvent> _events;
    private long _tick;
    private float _jumpBuffer;
    private float _coyoteSeconds;
    private bool _canCoyote;
    private bool _jumpHeld;
    private bool _webLocked;
    private float _deadSeconds;

    // The pendulum: the rope's angle from straight down (negative: he's behind the anchor)
    // and how fast it is changing
    private float _theta;
    private float _omega;
    private float _endTheta;

    /// <param name="track">The track he starts on (he stands on its first roof)</param>
    public Runner(RooftopTrack track)
    {
        FeetY = track.GroundYAt(track.RunnerTrackX);
    }

    public RunnerState State { get; private set; } = RunnerState.Running;

    /// <summary>The y position of his feet</summary>
    public float FeetY { get; private set; }

    /// <summary>Vertical speed, in world units per second (negative is up)</summary>
    public float VelocityY { get; private set; }

    /// <summary>Seconds since he left the ground (for animation)</summary>
    public float AirSeconds { get; private set; }

    /// <summary>The anchor he is swinging from, if any</summary>
    public WebAnchor? Anchor { get; private set; }

    public float RopeLength { get; private set; }

    /// <summary>Seconds since he shot his web (for drawing the web flying out)</summary>
    public float SwingSeconds { get; private set; }

    /// <summary>The y position of his hands, where the web leaves him</summary>
    public float HandY => FeetY - HandHeight;

    public RunEndReason? EndReason { get; private set; }

    /// <summary>
    /// The angle of the rope from straight up, in degrees. Positive when the anchor is ahead.
    /// </summary>
    public float RopeAngleDegrees(RooftopTrack track)
    {
        if (Anchor is not WebAnchor anchor)
            return 0;
        double ahead = anchor.X - track.RunnerTrackX;
        return MathF.Atan2((float)ahead, HandY - anchor.Y) * 180f / MathF.PI;
    }

    /// <summary>
    /// Advances the runner by one tick. Returns true if the players asked to start a new run.
    /// </summary>
    public bool Update(in RunnerInput input, RooftopTrack track, float dt, EventQueue<GameEvent> events, long tick)
    {
        _events = events;
        _tick = tick;

        _jumpBuffer = input.JumpPressed ? JumpBufferSeconds : MathF.Max(0, _jumpBuffer - dt);
        if (!input.JumpDown)
            _webLocked = false;

        if (State == RunnerState.Dead)
        {
            // Tumble down off the screen, then wait for the players to ask for another go
            _deadSeconds += dt;
            VelocityY += Gravity * dt;
            FeetY += VelocityY * dt;
            return input.JumpPressed && _deadSeconds >= RestartDelaySeconds;
        }

        switch (State)
        {
            case RunnerState.Running:
                UpdateRunning(input, track, dt);
                break;
            case RunnerState.Airborne:
                UpdateAirborne(input, track, dt);
                break;
            case RunnerState.Swinging:
                UpdateSwinging(input, track, dt);
                break;
        }
        return false;
    }

    private void UpdateRunning(in RunnerInput input, RooftopTrack track, float dt)
    {
        float? roof = RoofUnderFoot(track);
        if (roof is null)
        {
            // Ran off the edge. There's a moment to jump anyway.
            State = RunnerState.Airborne;
            VelocityY = 0;
            AirSeconds = 0;
            _coyoteSeconds = 0;
            _canCoyote = true;
            UpdateAirborne(input, track, dt);
            return;
        }

        // Hop up (or step down) to the roof, rather than snapping
        float ease = 1f - MathF.Exp(-18f * dt);
        FeetY += (roof.Value - FeetY) * ease;

        if (_jumpBuffer > 0)
            Jump();
    }

    private void UpdateAirborne(in RunnerInput input, RooftopTrack track, float dt)
    {
        AirSeconds += dt;

        if (_canCoyote)
        {
            _coyoteSeconds += dt;
            if (_coyoteSeconds > CoyoteSeconds)
                _canCoyote = false;
            else if (_jumpBuffer > 0)
                Jump();
        }

        // Letting go of jump early makes a shorter hop
        if (_jumpHeld && !input.JumpDown && AirSeconds > 0.06f)
        {
            _jumpHeld = false;
            if (VelocityY < 0)
                VelocityY *= JumpCut;
        }

        // Still holding the button once he starts to fall (or pressing it again in the air):
        // shoot a web and swing
        if (input.JumpDown && !_webLocked && VelocityY >= 0)
        {
            AttachWeb(track);
            return;
        }

        VelocityY += Gravity * dt;
        FeetY += VelocityY * dt;
        Resolve(track);
    }

    private void UpdateSwinging(in RunnerInput input, RooftopTrack track, float dt)
    {
        AirSeconds += dt;
        SwingSeconds += dt;
        var anchor = Anchor.Value;

        if (!input.JumpDown)
        {
            ReleaseWeb(track);
            return;
        }

        // The pendulum: gravity swings the rope back toward hanging straight down, and a little
        // push keeps him building speed while he's moving forward. Small steps keep it accurate.
        const int steps = 4;
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float alpha = -(SwingGravity / RopeLength) * MathF.Sin(_theta);
            if (_omega > 0)
                alpha += SwingDrive / RopeLength;
            _omega += alpha * h;
            _theta += _omega * h;
        }

        // Where the pendulum puts him, and how fast he is moving
        float sin = MathF.Sin(_theta), cos = MathF.Cos(_theta);
        double handX = anchor.X + RopeLength * sin;
        float handY = anchor.Y + RopeLength * cos;
        float speedX = RopeLength * _omega * cos;
        VelocityY = -RopeLength * _omega * sin;
        FeetY = handY + HandHeight;

        // He stays put on screen, so move the whole city to where the swing puts him
        track.DriveScroll(handX - RooftopTrack.RunnerX, speedX);

        Resolve(track);
        if (State != RunnerState.Swinging)
            return;

        // The swing is over when he's round to where he started (on the far side), or has stalled
        if (_theta >= _endTheta || _omega <= 0.05f)
            ReleaseWeb(track);
    }

    private void Jump()
    {
        State = RunnerState.Airborne;
        VelocityY = -JumpSpeed;
        AirSeconds = 0;
        _jumpBuffer = 0;
        _canCoyote = false;
        _jumpHeld = true;
        _events.Raise(new RunnerJumped(_tick));
    }

    private void AttachWeb(RooftopTrack track)
    {
        float speedX = track.Speed;

        // The web sticks to a point ahead of him and above his hands
        float ahead = MathF.Max(MinWebAhead, speedX * WebAheadSeconds);
        float above = ahead * WebAboveRatio;
        Anchor = new WebAnchor(track.RunnerTrackX + ahead, HandY - above);
        RopeLength = MathF.Sqrt(ahead * ahead + above * above);

        // He's behind the anchor, so the rope's angle starts negative, and he swings round to the
        // same angle on the other side
        _theta = MathF.Atan2(-ahead, above);
        _endTheta = -_theta;

        // The web redirects the speed he already has along the arc, without losing any forward
        // speed (that's what makes it feel smooth rather than like hitting a rope)
        float cos = MathF.Cos(_theta);
        float speed = MathF.Sqrt(speedX * speedX + VelocityY * VelocityY);
        float tangentSpeed = MathF.Min(MathF.Max(speed, speedX / cos), MaxEntryBoost * speedX / cos);
        _omega = tangentSpeed / RopeLength;

        State = RunnerState.Swinging;
        SwingSeconds = 0;
        track.ScrollDrivenByRunner = true;
        _canCoyote = false;
        _jumpHeld = false;
        _events.Raise(new WebAttached(_tick));
    }

    private void ReleaseWeb(RooftopTrack track)
    {
        State = RunnerState.Airborne;
        Anchor = null;
        track.ScrollDrivenByRunner = false;     // the city keeps the speed the swing gave it
        _canCoyote = false;
        _webLocked = true;       // Let go of the button, then press it again, to swing again
        if (VelocityY < 0)
            VelocityY *= ReleaseUpFraction;
        _events.Raise(new WebReleased(_tick));
    }

    /// <summary>
    /// The higher of the roofs just behind and just in front of his feet (null over a gap)
    /// </summary>
    private float? RoofUnderFoot(RooftopTrack track)
    {
        double x = track.RunnerTrackX;
        float? back = track.RoofAt(x - FootBack);
        float? front = track.RoofAt(x + FootFront);
        if (back is null) return front;
        if (front is null) return back;
        return MathF.Min(back.Value, front.Value);
    }

    /// <summary>
    /// Lands on a roof, or crashes into the side of one, or falls out of the world
    /// </summary>
    private void Resolve(RooftopTrack track)
    {
        float? roof = RoofUnderFoot(track);
        if (roof is float surface && FeetY > surface)
        {
            float depth = FeetY - surface;
            if (depth > StepTolerance)
            {
                Die(track, RunEndReason.HitWall);
                return;
            }
            if (VelocityY >= 0)
            {
                Land(track, surface);
                return;
            }
        }

        if (FeetY > RooftopTrack.Rows * RooftopTrack.TileSize + 140)
            Die(track, RunEndReason.Fell);
    }

    private void Land(RooftopTrack track, float surface)
    {
        float fallSpeed = VelocityY;
        State = RunnerState.Running;
        FeetY = surface;
        VelocityY = 0;
        AirSeconds = 0;
        Anchor = null;
        track.ScrollDrivenByRunner = false;
        _webLocked = true;       // Landing with the button still held doesn't swing; let go first
        _events.Raise(new RunnerLanded(_tick, fallSpeed));
    }

    private void Die(RooftopTrack track, RunEndReason reason)
    {
        State = RunnerState.Dead;
        EndReason = reason;
        Anchor = null;
        track.ScrollDrivenByRunner = false;
        _deadSeconds = 0;
        track.IsScrolling = false;
        if (reason == RunEndReason.HitWall)
            VelocityY = -250f;       // bounce off the wall
        _events.Raise(new RunEnded(_tick, track.DistanceMeters, reason));
    }
}
