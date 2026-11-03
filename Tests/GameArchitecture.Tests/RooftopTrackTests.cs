using System;
using System.Linq;
using CIS580.Simulation;
using GameArchitecture.Simulation;

namespace GameArchitecture.Tests;

public class RooftopTrackTests
{
    private const float Dt = 1f / 60f;

    private static RooftopTrack Run(ulong seed, int ticks)
    {
        var track = new RooftopTrack(new DeterministicRandom(seed));
        for (int i = 0; i < ticks; i++)
            track.Advance(Dt);
        return track;
    }

    [Fact]
    public void StartsWithARoofUnderTheRunner()
    {
        var track = new RooftopTrack(new DeterministicRandom(1));

        Assert.NotNull(track.RoofAt(track.RunnerTrackX));
    }

    [Fact]
    public void SameSeed_BuildsTheSameCity()
    {
        var a = Run(seed: 7, ticks: 3000);
        var b = Run(seed: 7, ticks: 3000);

        Assert.Equal(a.ScrollX, b.ScrollX);
        Assert.Equal(a.Buildings.Count, b.Buildings.Count);
        for (int i = 0; i < a.Buildings.Count; i++)
        {
            Assert.Equal(a.Buildings[i].StartColumn, b.Buildings[i].StartColumn);
            Assert.Equal(a.Buildings[i].WidthTiles, b.Buildings[i].WidthTiles);
            Assert.Equal(a.Buildings[i].TopRow, b.Buildings[i].TopRow);
            Assert.Equal(a.Buildings[i].Style, b.Buildings[i].Style);
        }
    }

    [Fact]
    public void DifferentSeeds_BuildDifferentCities()
    {
        var a = Run(seed: 1, ticks: 600);
        var b = Run(seed: 2, ticks: 600);

        Assert.NotEqual(
            a.Buildings.Select(x => (x.WidthTiles, x.TopRow)).ToArray(),
            b.Buildings.Select(x => (x.WidthTiles, x.TopRow)).ToArray());
    }

    [Fact]
    public void Roofs_StayInRange_AndNeighborsAreCloseInHeight()
    {
        var track = Run(seed: 3, ticks: 6000);

        for (int i = 0; i < track.Buildings.Count; i++)
        {
            var building = track.Buildings[i];
            Assert.InRange(building.TopRow, RooftopTrack.MinTopRow, RooftopTrack.MaxTopRow);
            if (i > 0)
                Assert.InRange(Math.Abs(building.TopRow - track.Buildings[i - 1].TopRow), 0, RooftopTrack.MaxStepRows);
        }
    }

    [Fact]
    public void GapsBetweenBuildings_AreNeverOtherThanTheConfiguredSizes()
    {
        var track = Run(seed: 4, ticks: 6000);

        for (int i = 1; i < track.Buildings.Count; i++)
        {
            int gap = track.Buildings[i].StartColumn - track.Buildings[i - 1].EndColumn;
            Assert.True(
                gap == 0
                || (gap >= RooftopTrack.SmallGapMin && gap <= RooftopTrack.SmallGapMax)
                || (gap >= RooftopTrack.BigGapMin && gap <= RooftopTrack.BigGapMax),
                $"Unexpected gap of {gap} tiles");
        }
    }

    [Fact]
    public void BuildingsAlwaysCoverTheScreenAhead()
    {
        var track = new RooftopTrack(new DeterministicRandom(5));
        for (int i = 0; i < 3000; i++)
        {
            track.Advance(Dt);
            double coveredTo = track.Buildings[^1].EndColumn * (double)RooftopTrack.TileSize;
            Assert.True(coveredTo >= track.ScrollX + World.Width, $"Ran out of city at tick {i}");
        }
    }

    [Fact]
    public void ThingsThatScrolledOffAreForgotten()
    {
        var track = Run(seed: 6, ticks: 12000);     // 200 seconds

        Assert.True(track.Buildings.Count < 60, $"{track.Buildings.Count} buildings kept");
        Assert.True(track.Buildings[0].EndColumn * (double)RooftopTrack.TileSize >= track.ScrollX - 1000);
    }

    [Fact]
    public void SpeedsUp_ButNeverPastTheMaximum()
    {
        var early = Run(seed: 1, ticks: 60);
        var late = Run(seed: 1, ticks: 60 * 600);

        Assert.True(late.Speed > early.Speed);
        Assert.Equal(RooftopTrack.MaxSpeed, late.Speed);
    }

    [Fact]
    public void WhenNotScrolling_TheCityStandsStill()
    {
        var track = Run(seed: 1, ticks: 120);
        double before = track.ScrollX;

        track.IsScrolling = false;
        track.Advance(Dt);

        Assert.Equal(before, track.ScrollX);
    }

    [Fact]
    public void Windows_StayInsideTheirBuilding()
    {
        var track = Run(seed: 8, ticks: 3000);

        foreach (var building in track.Buildings)
            foreach (var window in building.Windows)
            {
                Assert.InRange(window.Column, 0, building.WidthTiles - 2);
                Assert.True(window.Row >= building.TopRow + 2);
            }
    }
}
