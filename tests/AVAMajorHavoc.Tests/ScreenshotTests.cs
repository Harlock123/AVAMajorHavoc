using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMajorHavoc.Engine;
using AVAMajorHavoc.Views;
using Controls = AVAMajorHavoc.Engine.Controls;

namespace AVAMajorHavoc.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set HAVOC_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamajorhavoc-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("HAVOC_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMajorHavoc-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }


    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("REX", 148250, 7);
        g.HighScores.Insert("AVA", 101800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_fleet()
    {
        var (w, c, g) = Open(2);
        g.Go(3);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Space && g.PartTime > 4 && g.Fleet.Any(e => e.Diving && e.Y > 350) && g.Shots.Count(s => s.Mine) > 0 && g.Fighter.Alive, 60));
        Shot(w, c, "fleet");
    }

    [AvaloniaFact]
    public void Docking()
    {
        var (w, c, g) = Open(3);
        g.Go(1);
        g.SetLives(20);
        g.GoDock();
        Assert.True(PlayUntil(g, c, g => g.Lander.Y > 420 && g.Lander.Thrusting, 30));
        Shot(w, c, "docking");
    }

    [AvaloniaFact]
    public void The_maze()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(20);
        g.GoMaze();
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Maze && g.Havoc.X > 700 && g.Maze.Hazards.Count(z => Math.Abs(z.X - g.Havoc.X) < 450) >= 2 && !g.Havoc.OnGround, 120));
        Shot(w, c, "maze");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Maze && g.Havoc.Shielded && g.Havoc.Alive, 120));
        c.Advance(0.05);
        Shot(w, c, "shield");
    }

    [AvaloniaFact]
    public void The_core_and_the_escape()
    {
        var (w, c, g) = Open(5);
        g.Go(1);
        g.SetLives(20);
        g.GoMaze();
        Assert.True(PlayUntil(g, c, g => g.CoreSet && g.PhaseTime > 0 && g.Countdown < g.CountdownFor - 1.2, 180));
        Shot(w, c, "core");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Escape && g.PhaseTime > 0.9, 180));
        Shot(w, c, "escape");
    }

    [AvaloniaFact]
    public void Lost()
    {
        var (w, c, g) = Open(6);
        g.Go(2);
        g.SetLives(3);
        g.GoMaze();
        Assert.True(PlayUntil(g, c, g => g.Havoc.X > 400 && g.Havoc.OnGround && g.Havoc.Safe <= 0, 60));
        g.Havoc.Air = 0.01;
        g.Idle(30);
        Assert.Equal(Phase.Dying, g.Phase);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
