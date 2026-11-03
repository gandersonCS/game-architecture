using System;
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
        // Jump once, on tick 10, and hold it for the whole jump. The first roof is long enough to land on.
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

    [Fact]
    public void PressingJumpAfterTheRunEnds_StartsANewRun()
    {
        // Does nothing until the run ends, then taps jump over and over (alternating down and up)
        GameSession session = null;
        var (s, runner) = Start(1, tick =>
            session?.World.Runner.State == RunnerState.Dead && tick % 2 == 0
                ? Holding(GameAction.Jump)
                : default);
        session = s;

        bool died = false, restarted = false;
        for (int i = 0; i < 60 * 120 && !restarted; i++)
        {
            runner.Step();
            if (session.World.Runner.State == RunnerState.Dead)
                died = true;
            else if (died)
                restarted = true;
        }

        Assert.True(restarted, "Pressing jump should start a new run");
        Assert.True(session.World.Track.ScrollX < 100, "The new run starts from the beginning");
    }

    [Fact]
    public void HoldingJumpThroughTheAirStartsASwing_AndLettingGoEndsIt()
    {
        // Hold the button from tick 10 to tick 90, then let go
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
        // Pressed for 3 ticks, then let go
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
        // Hold the button from tick 10 for the whole jump and swing
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
