using AVAMajorHavoc.Engine;

namespace AVAMajorHavoc.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamajorhavoc-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A station's fleet in play, calm: nothing dives or fires unless a test makes it.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    /// <summary>Havoc in the maze, standing at the start of a floor.</summary>
    private static GameEngine OnFoot(int wave = 1)
    {
        var g = Game(wave);
        g.GoMaze();
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── space ─────────────────────────────

    [Fact]
    public void The_fighter_slides_along_the_bottom_and_fires_three_at_a_time()
    {
        var g = Game();
        g.Run(200, new Controls(Dx: 1));
        Assert.Equal(Field.Width - 60, g.Fighter.X);
        g.Run(60, FireButton);
        Assert.InRange(g.Shots.Count(s => s.Mine), 1, 3);
        Assert.All(g.Shots, s => Assert.True(s.Vy < 0));
    }

    [Fact]
    public void Shooting_the_fleet_and_more_for_a_diver()
    {
        var g = Game();
        g.Settle();
        var e = g.Fleet[0];
        g.Fighter.X = e.X;
        g.Tick(FireButton);
        for (int t = 0; t < 60 && !e.Dead; t++) { g.Tick(default); g.Fighter.X = e.X; }
        Assert.DoesNotContain(e, g.Fleet);
        Assert.Equal(GameEngine.StingrayPoints, g.Score);
        var d = g.Fleet[0];
        d.Diving = true;
        d.Vy = 0;
        d.Y = 500;
        g.Fighter.X = d.X;
        g.Tick(FireButton);
        g.Idle(15);
        Assert.Equal(GameEngine.StingrayPoints + GameEngine.DiverPoints, g.Score);
    }

    [Fact]
    public void The_fleet_wheels_round_and_dives_and_fires()
    {
        var g = Game(3);
        g.Settle();
        double x = g.Fleet[0].X;
        g.Idle(30);
        Assert.NotEqual(x, g.Fleet[0].X, 0);
        g.Calm = false;
        bool dived = false, fired = false;
        for (int t = 0; t < 60 * 10 && g.Phase == Phase.Space; t++)
        {
            g.Tick(default);
            dived |= g.Fleet.Any(e => e.Diving);
            fired |= g.Shots.Any(s => !s.Mine);
        }
        Assert.True(dived && fired);
    }

    [Fact]
    public void Shot_down_in_space_the_fleet_carries_on_where_it_was()
    {
        var g = Game();
        g.SetLives(3);
        int left = g.Fleet.Count - 1;
        g.Fleet.RemoveAt(0);
        g.Shots.Add(new Shot { X = g.Fighter.X, Y = Fighter.Y, Vy = 10, Life = 1 });
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT DOWN", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(Phase.Space, g.Phase);
        Assert.Equal(left, g.Fleet.Count);
        Assert.Equal(2, g.Lives);
    }

    [Fact]
    public void The_whole_fleet_down_and_on_to_dock()
    {
        var g = Game();
        g.Calm = false;
        g.Fleet.Clear();
        g.Tick(default);
        Assert.Equal(Phase.Dock, g.Phase);
    }

    // ───────────────────────────── docking ─────────────────────────────

    [Fact]
    public void Docking_the_pull_and_the_thrust()
    {
        var g = Game();
        g.GoDock();
        g.Lander.Vy = 0;
        g.Idle(60);
        Assert.InRange(g.Lander.Vy, 80, 100);
        double fuel = g.Lander.Fuel;
        g.Run(60, new Controls(Jump: true));
        Assert.True(g.Lander.Vy < 20);
        Assert.True(g.Lander.Fuel < fuel);
    }

    [Theory]
    [InlineData(512, 40, 0, true, null)]
    [InlineData(512, 150, 0, false, "LANDED TOO HARD")]
    [InlineData(512, 40, 90, false, "LANDED TOO HARD")]
    [InlineData(430, 40, 0, false, null)]
    public void Docking_gently_on_the_pad_or_not(double x, double vy, double vx, bool docks, string? cause)
    {
        var g = Game();
        g.GoDock();
        g.Lander.X = x;
        g.Lander.Y = Bay.Floor - Lander.HalfHeight - 0.3;
        g.Lander.Vy = vy;
        g.Lander.Vx = vx;
        g.Tick(default);
        if (docks)
        {
            Assert.Equal(Phase.Dock, g.Phase);
            Assert.True(g.Bonus > 0);
            g.Idle((int)(GameEngine.DockedSeconds * 60) + 5);
            Assert.Equal(Phase.Maze, g.Phase);
        }
        else
        {
            Assert.Equal(Phase.Dying, g.Phase);
            if (cause != null) Assert.Equal(cause, g.Cause);
        }
    }

    [Fact]
    public void Touching_the_bay_walls_is_a_crash()
    {
        var g = Game();
        g.GoDock();
        var (left, _) = Bay.Walls(500);
        g.Lander.X = left + Lander.HalfWidth - 4;
        g.Lander.Y = 500;
        g.Lander.Vx = g.Lander.Vy = 0;
        g.Tick(default);
        Assert.Equal("CRASHED INTO THE BAY", g.Cause);
    }

    // ───────────────────────────── the maze ─────────────────────────────

    [Fact]
    public void Every_maze_can_be_walked_both_ways()
    {
        for (int level = 1; level <= 8; level++)
        {
            var m = new Maze(level);
            for (int i = 1; i < m.Floors.Count; i++)
                Assert.InRange(m.Floors[i].Floor - m.Floors[i - 1].Floor, -2, 2);
            Assert.True(m.CoreX > m.ShipX + 1000);
            // Room to stand and jump everywhere along the floors.
            foreach (var f in m.Floors)
                for (int c = f.From; c <= f.To; c++)
                    for (int r = f.Floor - 2; r < f.Floor; r++) Assert.False(m.Solid[c, r] && r < f.Floor - 3, $"level {level} col {c}");
        }
        Assert.Equal(new Maze(3).Columns, new Maze(3).Columns);
    }

    [Fact]
    public void Havoc_runs_and_jumps_about_three_tiles_high()
    {
        var g = OnFoot();
        double y = g.Havoc.Y, x = g.Havoc.X;
        g.Run(30, new Controls(Dx: 1));
        Assert.True(g.Havoc.X > x + 80);
        double top = y;
        g.Tick(new Controls(Jump: true));
        for (int t = 0; t < 60; t++) { g.Tick(default); top = Math.Min(top, g.Havoc.Y); }
        Assert.InRange(y - top, 100, 125);
        Assert.True(g.Havoc.OnGround);
        Assert.Equal(y, g.Havoc.Y);
    }

    [Fact]
    public void Havoc_cannot_walk_through_rock()
    {
        var g = OnFoot();
        g.Run(400, new Controls(Dx: -1));
        Assert.True(g.Havoc.X >= Maze.Tile + Runner.HalfWidth - 0.5);
    }

    [Fact]
    public void Air_runs_out_unless_he_finds_more()
    {
        var g = OnFoot();
        g.Idle(120);
        Assert.Equal(100 - GameEngine.AirBurn * 2, g.Havoc.Air, 1);
        var can = g.Maze.Canisters.First();
        g.Havoc.Air = 40;
        g.PutHavoc(can.X, can.Y + 14);
        g.Tick(default);
        Assert.True(can.Taken);
        Assert.InRange(g.Havoc.Air, 64, 65.1);
        Assert.Equal(GameEngine.CanisterPoints, g.Score);
        g.Havoc.Air = 0.01;
        g.Tick(default);
        Assert.Equal("OUT OF AIR", g.Cause);
    }

    private static Hazard Put(GameEngine g, HazardKind kind)
    {
        var z = g.Maze.Hazards.FirstOrDefault(z => z.Kind == kind) ?? new Maze(4).Hazards.First(z => z.Kind == kind);
        if (!g.Maze.Hazards.Contains(z)) g.Maze.Hazards.Add(z);
        return z;
    }

    [Fact]
    public void A_Sentry_is_deadly_unless_the_shield_is_up_and_the_shield_burns_then_recharges()
    {
        var g = OnFoot(4);
        var z = Put(g, HazardKind.Sentry);
        g.PutHavoc(z.X, z.Y);
        g.Run(10, FireButton);
        Assert.True(g.Havoc.Alive);
        Assert.InRange(g.Havoc.Shield, 85, 95);
        double low = g.Havoc.Shield;
        g.PutHavoc(z.X + 600, z.Y);
        g.Maze.Hazards.Clear();
        g.Idle(60);
        Assert.True(g.Havoc.Shield > low);
        g.Maze.Hazards.Add(z);
        g.PutHavoc(z.X, z.Y);
        g.Tick(default);
        Assert.Equal("CAUGHT BY A SENTRY", g.Cause);
    }

    [Fact]
    public void A_Sentry_fires_along_its_floor_at_Havoc_in_front()
    {
        var g = OnFoot(4);
        g.Calm = false;
        var z = Put(g, HazardKind.Sentry);
        g.Maze.Hazards.RemoveAll(h => h != z);
        z.FireIn = 0;
        z.Facing = 1;
        z.Min = z.X - 1;
        z.Max = z.X + 4;
        g.PutHavoc(z.X + 80, z.Y);
        g.Havoc.OnGround = true;
        g.Tick(default);
        Assert.Single(g.Bolts);
        g.Idle(60);
        Assert.Equal("SHOT BY A SENTRY", g.Cause);
    }

    [Fact]
    public void A_Laser_cuts_only_while_it_is_on()
    {
        var g = OnFoot(4);
        var z = Put(g, HazardKind.Laser);
        g.Maze.Hazards.RemoveAll(h => h != z);
        z.Phase = 0;
        z.T = 1.5;
        Assert.False(z.On);
        g.PutHavoc(z.X, z.Max);
        g.Tick(default);
        Assert.True(g.Havoc.Alive);
        z.T = 0.2;
        g.Tick(default);
        Assert.Equal("CUT BY A LASER", g.Cause);
    }

    [Fact]
    public void An_Ember_bounces_about_its_passage()
    {
        var g = OnFoot(4);
        var z = Put(g, HazardKind.Ember);
        g.PutHavoc(g.Maze.ShipX + 30, g.Maze.ShipY);
        var (vx, vy) = (z.Vx, z.Vy);
        bool turned = false;
        for (int t = 0; t < 600; t++)
        {
            g.Tick(default);
            Assert.False(g.Maze.IsSolidAt(z.X, z.Y));
            if (Math.Sign(z.Vx) != Math.Sign(vx) || Math.Sign(z.Vy) != Math.Sign(vy)) turned = true;
            g.Havoc.Air = 100;
        }
        Assert.True(turned);
    }

    [Fact]
    public void Spikes_kill_even_through_the_shield()
    {
        var g = OnFoot(4);
        int c = Array.IndexOf(g.Maze.Spikes, true);
        Assert.True(c > 0);
        g.PutHavoc((c + 0.5) * Maze.Tile, Maze.TopOf(Maze.Rows - 2) - 20);
        g.Run(30, FireButton);
        Assert.Equal("SPIKED", g.Cause);
    }

    [Fact]
    public void Setting_the_core_then_back_to_the_ship_for_the_bonus_and_the_next_station()
    {
        var g = OnFoot();
        var m = g.Maze;
        g.PutHavoc(m.CoreX, m.CoreY);
        g.Tick(default);
        Assert.True(g.CoreSet);
        Assert.Equal(g.CountdownFor, g.Countdown, 0);
        Assert.Equal(GameEngine.CorePoints, g.Score);
        g.PutHavoc(m.ShipX, m.ShipY);
        g.Tick(default);
        Assert.Equal(Phase.Escape, g.Phase);
        Assert.True(g.Bonus > 1000);
        g.Idle((int)(GameEngine.EscapeSeconds * 60) + 5);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Phase.Start, g.Phase);
    }

    [Fact]
    public void Too_slow_and_the_core_blows()
    {
        var g = OnFoot();
        g.Run(40, new Controls(Dx: 1));
        g.SetCountdown(0.05);
        g.Idle(5);
        Assert.Equal("THE CORE BLEW", g.Cause);
    }

    [Fact]
    public void Lost_in_the_maze_he_comes_back_at_the_last_floor_reached()
    {
        var g = OnFoot();
        g.SetLives(5);
        var f = g.Maze.Floors[3];
        g.PutHavoc((f.From + 0.5) * Maze.Tile, Maze.TopOf(f.Floor) - 1);
        g.Idle(5);
        g.Havoc.Air = 0.01;
        g.Idle(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(Phase.Maze, g.Phase);
        Assert.Equal((f.From + 0.5) * Maze.Tile, g.Havoc.X, 0);
        Assert.InRange(g.Havoc.Air, 99, 100);
    }

    [Fact]
    public void Later_stations_have_bigger_fleets_and_longer_mazes()
    {
        Assert.True(Real(4).Fleet.Count > Real(1).Fleet.Count);
        Assert.True(new Maze(4).Columns > new Maze(1).Columns);
    }

    [Fact]
    public void An_extra_ship_at_20000()
    {
        var g = OnFoot();
        g.SetScore(19950);
        int lives = g.Lives;
        var can = g.Maze.Canisters.First();
        g.PutHavoc(can.X, can.Y + 14);
        g.Tick(default);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_player_destroys_the_first_station()
    {
        var g = Real();
        g.SetLives(8);
        for (int i = 0; i < 60 * 300 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still at station {g.Wave}: {g.Phase}, {g.Cause}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 60; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Phase} {g.Havoc.X:0.00} {g.Lander.Y:0.00} {g.Fleet.Count}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Fighter.X, ring = g.RingAngle;
        g.Pause();
        g.Idle(120);
        Assert.Equal((x, ring), (g.Fighter.X, g.RingAngle));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
