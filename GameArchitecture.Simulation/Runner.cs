using System;
using CIS580.Simulation;

namespace GameArchitecture.Simulation;

public enum RunnerState
{
    /// <summary>On a roof, running</summary>
    Running,
    /// <summary>In the air - jumped, ran off a ledge, or let go of a web</summary>
    Airborne,
    /// <summary>Hanging off a web</summary>
    Swinging,
    /// <summary>Game over</summary>
    Dead,
}

/// <summary>
/// Input for the runner this tick, merged from all players. It's a one-button game: tap to jump,
/// keep holding in the air to swing.
/// </summary>
public readonly record struct RunnerInput(bool JumpPressed, bool JumpDown);

/// <summary>
/// Spider-Man in the simulation. We only track him vertically (feet height + state) since he
/// never actually moves sideways - the <see cref="RooftopTrack"/> scrolls under him instead.
/// </summary>
/// <remarks>
/// <para>
/// Controls: press on a roof to jump, hold for a higher jump, release early for a short hop.
/// I added coyote time and a jump buffer so edge jumps don't feel unfair.
/// </para>
/// <para>
/// Holding the button once he starts falling shoots a web at a spot up and ahead of him and he
/// swings from it. No fixed anchor points, you can swing from anywhere.
/// </para>
/// <para>
/// The swing is an actual pendulum sim. He keeps whatever speed he came in with, picks up speed
/// at the bottom, and keeps it when he lets go (the city slows back down to cruising speed over
/// time). Because he's fixed on screen, the swing moves the track instead of him. Rope length
/// scales with speed so swings take roughly the same time no matter how fast you're going.
/// Releasing, or swinging up to the same angle you started at, drops you. Hitting a wall or
/// falling in a gap ends the run.
/// </para>
/// </remarks>
public sealed class Runner
{
    // tuning values (world units / seconds)
    public const float Gravity = 1900f;
    public const float JumpSpeed = 720f;
    /// <summary>How much upward speed you keep if you let go of jump early</summary>
    public const float JumpCut = 0.45f;
    /// <summary>Distance from hands down to feet while hanging on the web</summary>
    public const float HandHeight = 60f;
    /// <summary>How far below a roof he can be and still step up onto it instead of hitting the wall</summary>
    public const float StepTolerance = 28f;
    public const float CoyoteSeconds = 0.09f;
    public const float JumpBufferSeconds = 0.10f;
    // web anchor goes up and ahead of his hands. "ahead" = how far he'd travel in WebAheadSeconds,
    // which keeps swing timing about the same at any speed. bump WebAheadSeconds up for bigger swings.
    // WebAboveRatio = how much higher the anchor is than it is far ahead
    public const float WebAheadSeconds = 0.4f;
    public const float WebAboveRatio = 1.4f;
    public const float MinWebAhead = 120f;

    /// <summary>
    /// Gravity used during the swing. Turning it up makes the pendulum snappier and gives more of a
    /// speed boost at the bottom.
    /// </summary>
    public const float SwingGravity = 1500f;

    /// <summary>Small forward push during the swing so you gain speed over multiple swings</summary>
    public const float SwingDrive = 150f;

    /// <summary>Cap on how much faster than your current speed a swing can launch you</summary>
    public const float MaxEntryBoost = 1.5f;

    /// <summary>Fraction of upward speed kept on release, otherwise he rockets off the top of the screen</summary>
    public const float ReleaseUpFraction = 0.4f;
    public const float RestartDelaySeconds = 0.8f;

    // spots on his body we check for roofs - slightly behind and slightly in front of center
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

    // pendulum state. theta = rope angle from straight down (negative means he's behind the anchor),
    // omega = how fast it's changing
    private float _theta;
    private float _omega;
    private float _endTheta;

    /// <param name="track">Track to start on - he spawns on the first roof</param>
    public Runner(RooftopTrack track)
    {
        FeetY = track.GroundYAt(track.RunnerTrackX);
    }

    public RunnerState State { get; private set; } = RunnerState.Running;

    /// <summary>Y of his feet</summary>
    public float FeetY { get; private set; }

    /// <summary>Vertical speed in units/sec, negative = up</summary>
    public float VelocityY { get; private set; }

    /// <summary>Time since he left the ground, used by the animations</summary>
    public float AirSeconds { get; private set; }

    /// <summary>Current web anchor, null if not swinging</summary>
    public WebAnchor? Anchor { get; private set; }

    public float RopeLength { get; private set; }

    /// <summary>Time since the web was shot, so the view can animate it flying out</summary>
    public float SwingSeconds { get; private set; }

    /// <summary>Y of his hands (where the web comes out)</summary>
    public float HandY => FeetY - HandHeight;

    public RunEndReason? EndReason { get; private set; }

    /// <summary>
    /// Rope angle from straight up in degrees. Positive if the anchor is in front of him.
    /// </summary>
    public float RopeAngleDegrees(RooftopTrack track)
    {
        if (Anchor is not WebAnchor anchor)
            return 0;
        double ahead = anchor.X - track.RunnerTrackX;
        return MathF.Atan2((float)ahead, HandY - anchor.Y) * 180f / MathF.PI;
    }

    /// <summary>
    /// Steps the runner forward one tick. Returns true when the player wants to restart.
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
            // fall off screen, then wait for a button press to restart
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
            // ran off the edge - still give them a few frames to jump (coyote time)
            State = RunnerState.Airborne;
            VelocityY = 0;
            AirSeconds = 0;
            _coyoteSeconds = 0;
            _canCoyote = true;
            UpdateAirborne(input, track, dt);
            return;
        }

        // ease up/down to the roof height instead of snapping
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

        // short hop if they let go early
        if (_jumpHeld && !input.JumpDown && AirSeconds > 0.06f)
        {
            _jumpHeld = false;
            if (VelocityY < 0)
                VelocityY *= JumpCut;
        }

        // still holding when he starts falling (or pressed again mid-air) -> web swing
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

        // pendulum update: gravity pulls him back toward hanging straight down, plus a small push
        // while moving forward. split into substeps so it doesn't drift
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

        // get his position/velocity from the pendulum
        float sin = MathF.Sin(_theta), cos = MathF.Cos(_theta);
        double handX = anchor.X + RopeLength * sin;
        float handY = anchor.Y + RopeLength * cos;
        float speedX = RopeLength * _omega * cos;
        VelocityY = -RopeLength * _omega * sin;
        FeetY = handY + HandHeight;

        // he doesn't move on screen, so scroll the city to match the swing
        track.DriveScroll(handX - RooftopTrack.RunnerX, speedX);

        Resolve(track);
        if (State != RunnerState.Swinging)
            return;

        // done once he's swung round to the mirror of his starting angle, or he's basically stopped
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

        // stick the web up and ahead of him
        float ahead = MathF.Max(MinWebAhead, speedX * WebAheadSeconds);
        float above = ahead * WebAboveRatio;
        Anchor = new WebAnchor(track.RunnerTrackX + ahead, HandY - above);
        RopeLength = MathF.Sqrt(ahead * ahead + above * above);

        // he starts behind the anchor so theta is negative, and the swing ends at the same angle
        // on the other side
        _theta = MathF.Atan2(-ahead, above);
        _endTheta = -_theta;

        // turn his current speed into swing speed without losing forward momentum. without this
        // it feels like slamming into a rope
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
        track.ScrollDrivenByRunner = false;     // city keeps the swing's speed
        _canCoyote = false;
        _webLocked = true;       // have to release and press again to swing again
        if (VelocityY < 0)
            VelocityY *= ReleaseUpFraction;
        _events.Raise(new WebReleased(_tick));
    }

    /// <summary>
    /// Whichever roof is higher between the spot behind his feet and the spot in front. Null if both are gaps.
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
    /// Handles landing on a roof, crashing into a wall, or falling off the bottom
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
        _webLocked = true;       // don't auto-swing if they land still holding the button
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
            VelocityY = -250f;       // little bounce off the wall
        _events.Raise(new RunEnded(_tick, track.DistanceMeters, reason));
    }
}
