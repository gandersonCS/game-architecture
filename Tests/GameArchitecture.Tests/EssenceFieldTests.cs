using System;
using System.Collections.Generic;
using System.Linq;
using CIS580.Simulation;
using GameArchitecture.Simulation;

namespace GameArchitecture.Tests;

public class EssenceFieldTests
{
    private const float Dt = 1f / 60f;

    /// <summary>
    /// Scrolls a city past a runner who stands still on screen (he isn't updated, so he never
    /// dies), spawning orbs. Calls <paramref name="observe"/> after every tick.
    /// </summary>
    private static (RooftopTrack track, EssenceField field, EventQueue<GameEvent> events) Run(
        ulong seed, int ticks, Action<RooftopTrack, EssenceField> observe = null)
    {
        var track = new RooftopTrack(new DeterministicRandom(seed));
        var runner = new Runner(track);
        var field = new EssenceField(new DeterministicRandom(seed + 100));
        var events = new EventQueue<GameEvent>();
        for (int tick = 0; tick < ticks; tick++)
        {
            track.Advance(Dt);
            field.Update(track, runner, events, tick);
            observe?.Invoke(track, field);
        }
        return (track, field, events);
    }

    [Fact]
    public void Packs_HaveThreeToSevenOrbs()
    {
        // A pack is spawned whole, far ahead of the runner, so the first time it is seen it is complete
        var sizes = new Dictionary<int, int>();
        Run(seed: 1, ticks: 6000, (_, field) =>
        {
            foreach (var group in field.Orbs.GroupBy(o => o.Pack))
                sizes.TryAdd(group.Key, group.Count());
        });

        Assert.True(sizes.Count > 20, $"Only {sizes.Count} packs spawned");
        foreach (var (pack, size) in sizes)
            Assert.InRange(size, EssenceField.MinPackSize, EssenceField.MaxPackSize);
    }

    [Fact]
    public void SameSeed_SpawnsTheSameOrbs()
    {
        var a = Run(seed: 7, ticks: 3000).field;
        var b = Run(seed: 7, ticks: 3000).field;

        Assert.Equal(a.Orbs.ToArray(), b.Orbs.ToArray());
        Assert.Equal(a.PackCount, b.PackCount);
    }

    [Fact]
    public void Orbs_AreNeverInsideABuilding()
    {
        Run(seed: 3, ticks: 6000, (track, field) =>
        {
            foreach (var orb in field.Orbs)
            {
                if (track.RoofAt(orb.X) is float roof)
                    Assert.True(orb.Y < roof, $"Orb at ({orb.X:0}, {orb.Y:0}) is inside a roof at {roof}");
                Assert.True(orb.Y >= EssenceField.MinOrbY - 0.01f, $"Orb at y {orb.Y} is off the top of the screen");
            }
        });
    }

    [Fact]
    public void SomePacksAreOnRoofs_AndSomeAreInTheAir()
    {
        bool onRoof = false, inAir = false;
        Run(seed: 5, ticks: 6000, (track, field) =>
        {
            foreach (var orb in field.Orbs)
            {
                float? roof = track.RoofAt(orb.X);
                if (roof is float r && Math.Abs(r - EssenceField.LineAboveRoof - orb.Y) < 0.01f)
                    onRoof = true;
                if (roof is null || roof.Value - orb.Y >= EssenceField.MinAirAboveRoof - 0.01f)
                    inAir = true;
            }
        });

        Assert.True(onRoof, "No line of orbs was spawned on a roof");
        Assert.True(inAir, "No orbs were spawned in the air");
    }

    [Fact]
    public void OrbsLeftBehind_AreForgotten()
    {
        var (track, field, _) = Run(seed: 6, ticks: 12000);

        Assert.All(field.Orbs, orb => Assert.True(orb.X >= track.ScrollX - 1000));
        Assert.True(field.Orbs.Count < 200, $"{field.Orbs.Count} orbs kept");
    }

    [Fact]
    public void EachCollection_RaisesAnEvent_AndCounts()
    {
        var (_, field, events) = Run(seed: 2, ticks: 6000);

        var collected = events.Pending.OfType<EssenceCollected>().ToList();
        Assert.Equal(field.CollectedThisRun, collected.Count);
        for (int i = 0; i < collected.Count; i++)
            Assert.Equal(i + 1, collected[i].CollectedThisRun);
    }

    [Fact]
    public void ARestartedRun_StartsWithNoEssence()
    {
        // Keep jumping, so the run eventually ends, then press jump to restart
        var session = new GameSession(11);
        var runner = new SessionRunner(session);
        runner.AddPlayer(new PlayerId(1), new DelegateCommandSource(tick =>
            new PlayerCommand { Actions = tick % 40 < 20 ? default(ActionSet).With(GameAction.Jump) : default }));

        var firstRun = session.World.Runner;
        for (int i = 0; i < 60 * 120 && session.World.Runner == firstRun; i++)
            runner.Step();

        if (session.World.Runner != firstRun)
            Assert.Equal(0, session.World.Essence.CollectedThisRun);
    }
}
