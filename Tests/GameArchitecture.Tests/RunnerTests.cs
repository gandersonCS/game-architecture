using System;
using System.Linq;
using System.Numerics;
using CIS580.Simulation;
using GameArchitecture.Simulation;

namespace GameArchitecture.Tests;

public class RunnerTests
{
    private static readonly PlayerId One = new(1);

    private static (GameSession session, SessionRunner runner) Start(ulong seed, Func<long, ActionSet> actionsOnTick)
    {
        var session = new GameSession(seed);
        var runner = new SessionRunner(session);
        runner.AddPlayer(One, new DelegateCommandSource(tick => new PlayerCommand { Actions = actionsOnTick(tick) }));
        return (session, runner);
    }

    private static ActionSet Holding(params GameAction[] actions)
    {
        var set = default(ActionSet);
        foreach (var action in actions)
            set = set.With(action);
        return set;
    }

    [Fact]
    public void StartsRunningOnARoof()
    {
        var (session, runner) = Start(1, _ => default);
        runner.Step();

        Assert.Equal(RunnerState.Running, session.World.Runner.State);
    }

    [Fact]
    public void Jump_LeavesTheRoof_ThenLandsOnIt()
    {
        // one jump at tick 10, held the whole time. first roof is long enough to land back on it
        var (session, runner) = Start(1, tick => tick >= 10 ? Holding(GameAction.Jump) : default);
        for (int i = 0; i < 12; i++)
            runner.Step();
        float restingY = session.World.Runner.FeetY;

        var peak = restingY;
        bool wasAirborne = false;
        for (int i = 0; i < 60; i++)
        {
            runner.Step();
            if (session.World.Runner.State == RunnerState.Airborne)
                wasAirborne = true;
            peak = MathF.Min(peak, session.World.Runner.FeetY);
        }

        Assert.True(wasAirborne);
        Assert.True(restingY - peak > 80, "The jump should rise well above the roof");
    }

    [Fact]
    public void ReleasingJumpEarly_MakesAShorterJump()
    {
        float PeakHeight(Func<long, ActionSet> input)
        {
            var (session, runner) = Start(1, input);
            for (int i = 0; i < 12; i++)
                runner.Step();
            float rest = session.World.Runner.FeetY;
            float peak = rest;
            for (int i = 0; i < 50; i++)
            {
                runner.Step();
                peak = MathF.Min(peak, session.World.Runner.FeetY);
            }
            return rest - peak;
        }

        float full = PeakHeight(tick => tick >= 10 ? Holding(GameAction.Jump) : default);
        float tap = PeakHeight(tick => tick is 10 or 11 or 12 ? Holding(GameAction.Jump) : default);

        Assert.True(tap < full, $"tap {tap} should be lower than full {full}");
    }

    [Fact]
    public void DoingNothing_EventuallyEndsTheRun()
    {
        var (session, runner) = Start(1, _ => default);
        bool ended = false;
        for (int i = 0; i < 60 * 90 && !ended; i++)
        {
            runner.Step();
            ended = session.World.Runner.State == RunnerState.Dead;
        }

        Assert.True(ended);
        Assert.Contains(session.Events.Pending, e => e is RunEnded);
    }

    [Fact]
    public void AfterTheRunEnds_TheCityStopsScrolling()
    {
        var (session, runner) = Start(1, _ => default);
        while (session.World.Runner.State != RunnerState.Dead)
            runner.Step();

        double scroll = session.World.Track.ScrollX;
        for (int i = 0; i < 30; i++)
            runner.Step();

        Assert.Equal(scroll, session.World.Track.ScrollX);
    }

    // does nothing until the run ends, then presses jump (down, up, down, up...) to ask for a continue
    private static (GameSession session, SessionRunner runner) StartAndSpamJumpWhenDead()
    {
        GameSession session = null;
        var (s, runner) = Start(1, tick =>
            session?.World.Runner.State == RunnerState.Dead && tick % 2 == 0
                ? Holding(GameAction.Jump)
                : default);
        session = s;
        return (session, runner);
    }

    [Fact]
    public void PressingJumpAfterTheRunEnds_ContinuesWhereYouDied()
    {
        var (session, runner) = StartAndSpamJumpWhenDead();

        double distanceAtDeath = 0;
        int essenceAtDeath = 0;
        bool died = false;
        for (int i = 0; i < 60 * 120 && session.World.ContinuesUsed == 0; i++)
        {
            runner.Step();
            if (!died && session.World.Runner.State == RunnerState.Dead)
            {
                died = true;
                distanceAtDeath = session.World.Track.DistanceMeters;
                essenceAtDeath = session.World.Essence.CollectedThisRun;
            }
        }

        Assert.True(died);
        Assert.Equal(1, session.World.ContinuesUsed);
        Assert.Equal(RunnerState.Running, session.World.Runner.State);
        Assert.True(session.World.Track.IsScrolling);
        Assert.Equal(distanceAtDeath, session.World.Track.DistanceMeters, 6);     // same score
        Assert.Equal(essenceAtDeath, session.World.Essence.CollectedThisRun);     // same essence
        Assert.NotNull(session.World.Track.RoofAt(session.World.Track.RunnerTrackX));   // standing on a roof
        Assert.Contains(session.Events.Pending, e => e is RunContinued);
    }

    [Fact]
    public void ContinueCosts_Start500_AndGoUp100EachTime_ForAtMostFourContinues()
    {
        var (session, runner) = StartAndSpamJumpWhenDead();

        // keep dying and continuing; once the continues run out, keep going a while longer to
        // check it really stays over
        int ticksSinceLast = 0;
        int lastCount = 0;
        for (int i = 0; i < 60 * 600 && ticksSinceLast < 60 * 10; i++)
        {
            runner.Step();
            if (session.World.ContinuesUsed != lastCount)
            {
                lastCount = session.World.ContinuesUsed;
                ticksSinceLast = 0;
            }
            else
                ticksSinceLast++;
        }

        var costs = session.Events.Pending.OfType<RunContinued>().Select(c => c.Cost).ToArray();
        Assert.Equal(new[] { 500, 600, 700, 800 }, costs);
        Assert.Equal(World.MaxContinues, session.World.ContinuesUsed);
        Assert.Equal(RunnerState.Dead, session.World.Runner.State);    // no more chances
        Assert.False(session.World.CanContinue);
    }

    [Fact]
    public void ANewWorld_StartsWithFullContinues()
    {
        var world = new World(1);

        Assert.Equal(0, world.ContinuesUsed);
        Assert.Equal(World.MaxContinues, world.ContinuesLeft);
        Assert.Equal(500, world.NextContinueCost);
        Assert.False(world.CanContinue);       // he's alive
    }

    [Fact]
    public void HoldingJumpThroughTheAirStartsASwing_AndLettingGoEndsIt()
    {
        // hold from tick 10 to 90 then release
        var (session, runner) = Start(1, tick => tick is >= 10 and < 90 ? Holding(GameAction.Jump) : default);

        bool swung = false, anchoredWhileSwinging = true;
        for (int i = 0; i < 80; i++)
        {
            runner.Step();
            var state = session.World.Runner;
            if (state.State == RunnerState.Swinging)
            {
                swung = true;
                anchoredWhileSwinging &= state.Anchor is not null;
            }
        }

        Assert.True(swung, "Holding jump in the air should start a swing");
        Assert.True(anchoredWhileSwinging);
        Assert.Contains(session.Events.Pending, e => e is WebAttached);
    }

    [Fact]
    public void ATapJump_NeverSwings()
    {
        // tap for 3 ticks then release
        var (session, runner) = Start(1, tick => tick is >= 10 and < 13 ? Holding(GameAction.Jump) : default);

        for (int i = 0; i < 90; i++)
        {
            runner.Step();
            Assert.NotEqual(RunnerState.Swinging, session.World.Runner.State);
        }
    }

    [Fact]
    public void Swinging_BuildsSpeedThroughTheBottomOfTheArc()
    {
        // hold from tick 10 through the jump and the swing
        var (session, runner) = Start(1, tick => tick >= 10 ? Holding(GameAction.Jump) : default);

        float fastest = 0;
        float speedWhenGrabbing = 0;
        bool swinging = false;
        for (int i = 0; i < 120; i++)
        {
            runner.Step();
            var state = session.World.Runner.State;
            if (state == RunnerState.Swinging)
            {
                if (!swinging)
                    speedWhenGrabbing = session.World.Track.Speed;
                swinging = true;
                fastest = MathF.Max(fastest, session.World.Track.Speed);
            }
            else if (swinging)
                break;
        }

        Assert.True(swinging);
        Assert.True(fastest > speedWhenGrabbing * 1.2f,
            $"Should speed up swooping through the bottom ({speedWhenGrabbing} -> {fastest})");
    }

    [Fact]
    public void Swinging_NeverStopsTheCity()
    {
        var (session, runner) = Start(1, tick => tick >= 10 ? Holding(GameAction.Jump) : default);

        for (int i = 0; i < 120; i++)
        {
            runner.Step();
            if (session.World.Runner.State == RunnerState.Swinging)
                Assert.True(session.World.Track.Speed > RooftopTrack.BaseSpeed * 0.5f);
        }
    }
}
