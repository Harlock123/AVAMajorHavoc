using AVAMajorHavoc.Engine;

namespace AVAMajorHavoc.Tests;

/// <summary>How the computer player gets on, station by station (set HAVOC_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("HAVOC_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "havoc-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var parts = new Dictionary<string, double>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                parts[$"{g.Phase}"] = parts.GetValueOrDefault($"{g.Phase}") + 1 / 60.0;
                if (g.Phase == Phase.Maze) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"S{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"S{g.Wave} {g.Cause}") + 1;
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: station {g.Wave} after {t / 60:0}s score {g.Score}");
            output.WriteLine("   time " + string.Join(", ", parts.Select(p => $"{p.Key} {p.Value:0}s")));
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   maze " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
