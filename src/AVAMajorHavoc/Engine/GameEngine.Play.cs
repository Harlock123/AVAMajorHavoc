namespace AVAMajorHavoc.Engine;

public sealed partial class GameEngine
{
    // Space.
    public const double FighterSpeed = 420, ShotSpeed = 900, FireRepeat = 0.18, EnemyShotSpeed = 280;
    // On foot.
    public const double RunSpeed = 210, Gravity = 1500, JumpSpeed = 600, AirBurn = 1.5, ShieldBurn = 45, ShieldRecharge = 6, AirPerCanister = 25;
    public const double StartSeconds = 2.2, DyingSeconds = 2.2, SafeSeconds = 2, EscapeSeconds = 4, DockedSeconds = 1.5;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 40000;
    public const int StingrayPoints = 150, DiverPoints = 300, CanisterPoints = 100, CorePoints = 1000, StationPoints = 2000;

    /// <summary>The station reached: each is a fleet, a docking, and a maze.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>The part of the level that was being played when the ship (or Havoc) was lost.</summary>
    public Phase LostIn { get; private set; }
    public double PartTime { get; private set; }
    /// <summary>The bonus last paid: for the fuel left after docking, or the time left on the way out.</summary>
    public int Bonus { get; private set; }
    /// <summary>For tests: the fleet doesn't dive or fire, and nothing in the maze fires.</summary>
    internal bool Calm { get; set; }

    public Fighter Fighter { get; } = new();
    public List<Stingray> Fleet { get; } = [];
    public List<Shot> Shots { get; } = [];
    public Lander Lander { get; } = new();
    public Maze Maze { get; private set; } = new(1);
    public Runner Havoc { get; } = new();
    public List<Shot> Bolts { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    /// <summary>The core is set to blow: how long until it does.</summary>
    public double Countdown { get; private set; }
    public bool CoreSet { get; private set; }
    /// <summary>The fleet's ring: how far round it has turned, and how wide it is.</summary>
    public double RingAngle { get; private set; }
    public double RingRadius => 170 + 50 * Math.Sin(PartTime * 0.7);

    private int _nextExtra;
    /// <summary>Where Havoc comes back after being lost in the maze: the start of the furthest floor he's reached.</summary>
    private double _checkX, _checkY;
    private double _diveIn, _thrustSound, _alarm;
    private bool _jumpPrev;
    private bool _docked;

    public double Pace => Math.Min(1.8, 1 + 0.1 * (Wave - 1));
    /// <summary>The left edge of the screen in the maze.</summary>
    public double Camera => Math.Clamp(Havoc.X - Field.Width * 0.45, 0, Math.Max(0, Maze.PixelWidth - Field.Width));
    public int FleetSize => Math.Min(20, 10 + 2 * (Wave - 1));
    public double CountdownFor => Math.Max(20, Maze.PixelWidth / RunSpeed * 1.5 + 8 - 1.5 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        SetPhase(Phase.Start);
    }

    /// <summary>A new station: its name, then the fleet.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Maze = new Maze(wave);
        Havoc.Shield = 100;
        EnterSpace(fresh: true);
        SetPhase(Phase.Start);
        Say($"STATION {wave}", "SHOOT DOWN ITS FLEET", StartSeconds + 0.4);
        Play(Sfx.LevelStart);
    }

    private void EnterSpace(bool fresh)
    {
        Fighter.Alive = true;
        Fighter.X = 512;
        Fighter.Safe = SafeSeconds;
        Shots.Clear();
        Bolts.Clear();
        if (fresh)
        {
            Fleet.Clear();
            for (int i = 0; i < FleetSize; i++)
                Fleet.Add(new Stingray { Slot = i * Math.PI * 2 / FleetSize, X = 512 + (_rng.NextDouble() - 0.5) * 900, Y = Field.Top - 40 - _rng.NextDouble() * 300 });
        }
        else
            foreach (var s in Fleet) { s.Diving = false; s.Arriving = true; s.Y = Field.Top - 40 - _rng.NextDouble() * 200; }
        _diveIn = 3;
        PartTime = 0;
        SetPhase(Phase.Space);
    }

    private void EnterDock()
    {
        var l = Lander;
        l.X = 512 + (_rng.NextDouble() - 0.5) * 300;
        l.Y = 120;
        l.Vx = (_rng.NextDouble() - 0.5) * 80;
        l.Vy = 20;
        l.Fuel = 100;
        l.Alive = true;
        l.Thrusting = false;
        Shots.Clear();
        Bolts.Clear();
        _docked = false;
        PartTime = 0;
        SetPhase(Phase.Dock);
        Say("DOCK WITH THE STATION", "LAND GENTLY ON THE PAD", 2.5);
    }

    private void EnterMaze()
    {
        _checkX = Maze.ShipX + 30;
        _checkY = Maze.ShipY;
        CoreSet = false;
        Countdown = 0;
        foreach (var c in Maze.Canisters) c.Taken = false;
        Say("FIND THE CORE", "AND SET IT TO BLOW", 2.5);
        Respawn();
    }

    /// <summary>Havoc back on his feet at the last checkpoint, with fresh air.</summary>
    private void Respawn()
    {
        var h = Havoc;
        h.X = _checkX;
        h.Y = _checkY;
        h.Vx = h.Vy = 0;
        h.OnGround = true;
        h.Alive = true;
        h.Shielded = false;
        h.Facing = 1;
        h.Air = 100;
        h.Safe = SafeSeconds;
        if (CoreSet) Countdown = Math.Max(Countdown, 15);
        Bolts.Clear();
        PartTime = 0;
        SetPhase(Phase.Maze);
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        PartTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Space);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Space:
                FlySpace(c, dt);
                break;
            case Phase.Dock:
                FlyDock(c, dt);
                break;
            case Phase.Maze:
                RunMaze(c, dt);
                break;
            case Phase.Escape:
                if (PhaseTime > EscapeSeconds) BeginWave(Wave + 1);
                break;
            case Phase.Dying:
                MoveShots(dt);
                if (LostIn == Phase.Maze) MoveHazards(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    switch (LostIn)
                    {
                        case Phase.Space: EnterSpace(fresh: false); break;
                        case Phase.Dock: EnterDock(); break;
                        default: Respawn(); break;
                    }
                    if (State == GameState.Playing) Music(true);
                }
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── space ─────────────────────────────

    private void FlySpace(Controls c, double dt)
    {
        var f = Fighter;
        RingAngle += 0.45 * Pace * dt;
        if (f.Alive)
        {
            f.Safe = Math.Max(0, f.Safe - dt);
            f.X = Math.Clamp(f.X + Math.Sign(c.Dx) * FighterSpeed * dt, 60, Field.Width - 60);
            f.FireCool -= dt;
            if (c.Fire && f.FireCool <= 0 && Shots.Count(s => s.Mine) < 3)
            {
                f.FireCool = FireRepeat;
                Shots.Add(new Shot { X = f.X, Y = Fighter.Y - 20, Vy = -ShotSpeed, Life = 1, Mine = true });
                Play(Sfx.Fire);
            }
        }
        MoveFleet(dt);
        MoveShots(dt);
        if (Phase != Phase.Space) return;
        // Shot down.
        foreach (var s in Shots.Where(s => s.Mine))
            foreach (var e in Fleet)
            {
                if (e.Dead || s.Dead || Math.Abs(s.X - e.X) > Stingray.Radius || Math.Abs(s.Y - e.Y) > Stingray.Radius) continue;
                s.Dead = true;
                e.Dead = true;
                int points = e.Diving ? DiverPoints : StingrayPoints;
                AddScore(points);
                Shatter(e.X, e.Y, 7, 0xFFFF6AE0, 16);
                if (e.Diving) Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 20, Text = points.ToString() });
                Play(Sfx.Explode);
            }
        Fleet.RemoveAll(e => e.Dead);
        Shots.RemoveAll(s => s.Dead);
        if (f.Alive && f.Safe <= 0)
        {
            foreach (var b in Shots.Where(s => !s.Mine))
                if (Math.Abs(b.X - f.X) < Fighter.HalfWidth && Math.Abs(b.Y - Fighter.Y) < 14) { Lose(Phase.Space, "SHOT DOWN", f.X, Fighter.Y); return; }
            foreach (var e in Fleet)
                if (Math.Abs(e.X - f.X) < Fighter.HalfWidth + 12 && Math.Abs(e.Y - Fighter.Y) < 24) { e.Dead = true; Fleet.Remove(e); Lose(Phase.Space, "RAMMED", f.X, Fighter.Y); return; }
        }
        if (Fleet.Count == 0 && !Calm)
        {
            AddScore(StationPoints / 4);
            Play(Sfx.Landed);
            EnterDock();
        }
    }

    /// <summary>
    /// The fleet wheels round in a ring above the fighter, the ring breathing in and out; every so often one
    /// peels off and dives, weaving and firing, then climbs back to its place.
    /// </summary>
    private void MoveFleet(double dt)
    {
        var f = Fighter;
        double r = RingRadius;
        foreach (var e in Fleet)
        {
            e.T += dt;
            double a = e.Slot + RingAngle;
            double hx = 512 + Math.Cos(a) * r * 1.6, hy = 330 + Math.Sin(a) * r * 0.75;
            if (e.Diving)
            {
                e.Vy = Math.Min(300 + 15 * Wave, e.Vy + 400 * dt);
                e.Vx += Math.Clamp((f.X - e.X) * 2 - e.Vx, -500 * dt, 500 * dt);
                e.X += (e.Vx + Math.Sin(e.T * 5) * 120) * dt;
                e.Y += e.Vy * dt;
                if (!Calm && f.Alive)
                {
                    e.FireIn -= dt;
                    if (e.FireIn <= 0 && e.Y < Fighter.Y - 120)
                    {
                        e.FireIn = Math.Max(0.5, 1.1 - 0.06 * Wave);
                        double dx = f.X - e.X, dy = Fighter.Y - e.Y, d = Math.Sqrt(dx * dx + dy * dy);
                        Shots.Add(new Shot { X = e.X, Y = e.Y, Vx = dx / d * EnemyShotSpeed * Pace, Vy = dy / d * EnemyShotSpeed * Pace, Life = 3 });
                        Play(Sfx.EnemyFire);
                    }
                }
                // Off the bottom: round to the top, and back to its place.
                if (e.Y > Field.Height + 30)
                {
                    e.Diving = false;
                    e.Arriving = true;
                    e.Y = Field.Top - 30;
                }
                continue;
            }
            if (e.Arriving)
            {
                double dx = hx - e.X, dy = hy - e.Y, d = Math.Sqrt(dx * dx + dy * dy);
                double step = Math.Min(d, 380 * dt);
                e.X += dx / Math.Max(d, 1e-6) * step;
                e.Y += dy / Math.Max(d, 1e-6) * step;
                if (d < 4) e.Arriving = false;
                continue;
            }
            e.X = hx;
            e.Y = hy;
        }
        if (Calm || !f.Alive) return;
        _diveIn -= dt;
        if (_diveIn <= 0)
        {
            _diveIn = Math.Max(0.7, 2.6 - 0.18 * Wave) * (0.7 + 0.6 * _rng.NextDouble());
            int already = Fleet.Count(e => e.Diving);
            var pick = Fleet.Where(e => !e.Diving && !e.Arriving).OrderBy(_ => _rng.Next()).FirstOrDefault();
            if (pick != null && already < 1 + Wave / 3)
            {
                pick.Diving = true;
                pick.Vx = 0;
                pick.Vy = -120;
                pick.FireIn = 0.4;
                Play(Sfx.Dive);
            }
        }
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (s.Life <= 0 || s.Y < Field.Top || s.Y > Field.Height) s.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);
    }

    // ───────────────────────────── docking ─────────────────────────────

    /// <summary>Down into the bay against its pull, steering clear of the walls, and gently onto the pad.</summary>
    private void FlyDock(Controls c, double dt)
    {
        var l = Lander;
        if (_docked)
        {
            if (PhaseTime > DockedSeconds) EnterMaze();
            return;
        }
        l.Thrusting = c.Jump && l.Fuel > 0;
        l.Vy += Bay.Gravity * dt;
        if (l.Thrusting)
        {
            l.Vy -= Bay.Thrust * dt;
            l.Fuel = Math.Max(0, l.Fuel - Bay.FuelBurn * dt);
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        }
        if (c.Dx != 0 && l.Fuel > 0)
        {
            l.Vx += Math.Sign(c.Dx) * Bay.Side * dt;
            l.Fuel = Math.Max(0, l.Fuel - Bay.FuelBurn * 0.4 * dt);
        }
        l.X += l.Vx * dt;
        l.Y += l.Vy * dt;
        l.Y = Math.Max(Field.Top + 20, l.Y);
        // Off the sides of the screen, it comes back the other way.
        if (l.X < 20 || l.X > Field.Width - 20) { l.Vx = -l.Vx * 0.5; l.X = Math.Clamp(l.X, 20, Field.Width - 20); }
        var (left, right) = Bay.Walls(l.Y + Lander.HalfHeight);
        if (l.Y + Lander.HalfHeight > Bay.Mouth && (l.X - Lander.HalfWidth < left || l.X + Lander.HalfWidth > right))
        {
            Lose(Phase.Dock, "CRASHED INTO THE BAY", l.X, l.Y);
            return;
        }
        if (l.Y + Lander.HalfHeight >= Bay.Floor)
        {
            bool onPad = l.X - Lander.HalfWidth >= Bay.PadLeft - 6 && l.X + Lander.HalfWidth <= Bay.PadRight + 6;
            if (!onPad || l.Vy > Bay.SafeDown || Math.Abs(l.Vx) > Bay.SafeAcross)
            {
                Lose(Phase.Dock, onPad ? "LANDED TOO HARD" : "MISSED THE PAD", l.X, l.Y);
                return;
            }
            l.Y = Bay.Floor - Lander.HalfHeight;
            l.Vx = l.Vy = 0;
            l.Thrusting = false;
            _docked = true;
            Bonus = (int)l.Fuel * 10;
            AddScore(Bonus);
            SetPhase(Phase.Dock);
            Say("DOCKED", $"FUEL BONUS {Bonus}", DockedSeconds);
            Play(Sfx.Landed);
        }
    }

    // ───────────────────────────── the maze ─────────────────────────────

    private void RunMaze(Controls c, double dt)
    {
        var h = Havoc;
        var m = Maze;
        MoveHazards(dt);
        MoveBolts(dt);
        h.Safe = Math.Max(0, h.Safe - dt);
        // Air runs out as he goes; the shield burns its own charge while it's up.
        h.Air = Math.Max(0, h.Air - AirBurn * dt);
        h.Shielded = c.Fire && h.Shield > 0;
        if (h.Shielded)
        {
            if (h.Shield > 0 && PhaseTime % 0.5 < dt) Play(Sfx.Shield);
            h.Shield = Math.Max(0, h.Shield - ShieldBurn * dt);
        }
        else h.Shield = Math.Min(100, h.Shield + ShieldRecharge * dt);
        if (h.Air <= 0) { Lose(Phase.Maze, "OUT OF AIR", h.X, h.Y - 20); return; }
        if (CoreSet)
        {
            Countdown -= dt;
            _alarm -= dt;
            if (_alarm <= 0) { _alarm = Countdown < 8 ? 0.5 : 1; Play(Sfx.Alarm); }
            if (Countdown <= 0) { Lose(Phase.Maze, "THE CORE BLEW", h.X, h.Y - 20); return; }
        }

        // Running and jumping.
        if (c.Dx != 0) h.Facing = Math.Sign(c.Dx);
        double want = Math.Sign(c.Dx) * RunSpeed;
        h.Vx += Math.Clamp(want - h.Vx, -2400 * dt, 2400 * dt);
        bool jump = c.Jump && !_jumpPrev;
        _jumpPrev = c.Jump;
        if ((jump || c.Jump) && h.OnGround)
        {
            h.Vy = -JumpSpeed;
            h.OnGround = false;
            Play(Sfx.Jump);
        }
        h.Vy = Math.Min(900, h.Vy + Gravity * dt);
        // Across, then up or down, stopping at whatever is solid.
        double nx = h.X + h.Vx * dt;
        if (Blocked(nx, h.Y)) { h.Vx = 0; nx = h.X; }
        h.X = nx;
        double ny = h.Y + h.Vy * dt;
        bool was = h.OnGround;
        h.OnGround = false;
        if (h.Vy > 0)
        {
            int row = Maze.RowAt(ny);
            if (m.IsSolid(Maze.ColumnAt(h.X - Runner.HalfWidth + 1), row) || m.IsSolid(Maze.ColumnAt(h.X + Runner.HalfWidth - 1), row))
            {
                ny = Maze.TopOf(row);
                if (!was && h.Vy > 300) Play(Sfx.Land);
                h.Vy = 0;
                h.OnGround = true;
            }
        }
        else if (h.Vy < 0)
        {
            int row = Maze.RowAt(ny - Runner.Height);
            if (m.IsSolid(Maze.ColumnAt(h.X - Runner.HalfWidth + 1), row) || m.IsSolid(Maze.ColumnAt(h.X + Runner.HalfWidth - 1), row))
            {
                ny = Maze.TopOf(row + 1) + Runner.Height;
                h.Vy = 0;
            }
        }
        h.Y = ny;
        // A checkpoint at the start of each new floor reached: the furthest on the way in, the nearest the ship on
        // the way out.
        if (h.OnGround)
            foreach (var f in m.Floors)
            {
                double fx = (CoreSet ? f.To : f.From) * Maze.Tile + Maze.Tile / 2.0;
                if (Math.Abs(fx - h.X) > 12 || Math.Abs(Maze.TopOf(f.Floor) - h.Y) > 1) continue;
                if (CoreSet ? fx < _checkX : fx > _checkX) { _checkX = fx; _checkY = h.Y; }
            }

        // Air.
        foreach (var can in m.Canisters)
        {
            if (can.Taken || Math.Abs(can.X - h.X) > 20 || Math.Abs(can.Y - (h.Y - 20)) > 30) continue;
            can.Taken = true;
            h.Air = Math.Min(100, h.Air + AirPerCanister);
            AddScore(CanisterPoints);
            Effects.Add(new Effect { Kind = EffectKind.Score, X = can.X, Y = can.Y - 20, Text = CanisterPoints.ToString(), Colour = 0xFF6AE0FF });
            Play(Sfx.Air);
        }
        // The core.
        if (!CoreSet && Math.Abs(h.X - m.CoreX) < 30 && Math.Abs(h.Y - m.CoreY) < 40)
        {
            CoreSet = true;
            Countdown = CountdownFor;
            _alarm = 0;
            AddScore(CorePoints);
            Effects.Add(new Effect { Kind = EffectKind.Score, X = m.CoreX, Y = m.CoreY - 90, Text = CorePoints.ToString(), Colour = 0xFFFFE04A });
            Say("THE CORE IS SET", "BACK TO THE SHIP!", 2.5);
            Play(Sfx.CoreSet);
        }
        // Away.
        if (CoreSet && Math.Abs(h.X - m.ShipX) < 34 && Math.Abs(h.Y - m.ShipY) < 40)
        {
            Escape();
            return;
        }
        // Harm: spikes always; Sentries, Embers, Lasers and shots unless the shield is up.
        if (Maze.RowAt(h.Y - 1) == Maze.Rows - 3 && m.Spikes[Math.Clamp(Maze.ColumnAt(h.X), 0, m.Columns - 1)] && h.OnGround)
        {
            Lose(Phase.Maze, "SPIKED", h.X, h.Y - 20);
            return;
        }
        if (h.Safe > 0 || h.Shielded) return;
        foreach (var z in m.Hazards)
        {
            bool hit = z.Kind switch
            {
                HazardKind.Sentry => Math.Abs(z.X - h.X) < 22 && h.Y > z.Y - 34 && h.Y - Runner.Height < z.Y,
                HazardKind.Ember => Math.Abs(z.X - h.X) < 18 && z.Y > h.Y - Runner.Height - 8 && z.Y < h.Y + 8,
                _ => z.On && Math.Abs(z.X - h.X) < Runner.HalfWidth + 3 && h.Y > z.Min && h.Y - Runner.Height < z.Max,
            };
            if (!hit) continue;
            Lose(Phase.Maze, z.Kind switch { HazardKind.Sentry => "CAUGHT BY A SENTRY", HazardKind.Ember => "BURNED BY AN EMBER", _ => "CUT BY A LASER" }, h.X, h.Y - 20);
            return;
        }
        foreach (var b in Bolts)
            if (Math.Abs(b.X - h.X) < Runner.HalfWidth + 4 && b.Y > h.Y - Runner.Height && b.Y < h.Y)
            {
                Lose(Phase.Maze, "SHOT BY A SENTRY", h.X, h.Y - 20);
                return;
            }
    }

    /// <summary>Whether Havoc, at a place, would be inside something solid.</summary>
    private bool Blocked(double x, double feet)
    {
        var m = Maze;
        for (double y = feet - 1; y > feet - Runner.Height; y -= 12)
            if (m.IsSolidAt(x - Runner.HalfWidth, y) || m.IsSolidAt(x + Runner.HalfWidth, y)) return true;
        return m.IsSolidAt(x - Runner.HalfWidth, feet - Runner.Height + 1) || m.IsSolidAt(x + Runner.HalfWidth, feet - Runner.Height + 1);
    }

    private void MoveHazards(double dt)
    {
        var m = Maze;
        var h = Havoc;
        foreach (var z in m.Hazards)
        {
            z.T += dt;
            switch (z.Kind)
            {
                case HazardKind.Sentry:
                    z.X += z.Facing * 70 * Pace * dt;
                    if (z.X < z.Min) { z.X = z.Min; z.Facing = 1; }
                    if (z.X > z.Max) { z.X = z.Max; z.Facing = -1; }
                    if (Calm || Phase != Phase.Maze || !h.Alive) break;
                    z.FireIn -= dt;
                    // It fires along its floor when Havoc is on it, in front.
                    if (z.FireIn <= 0 && Math.Abs(h.Y - z.Y) < 10 && Math.Sign(h.X - z.X) == z.Facing && Math.Abs(h.X - z.X) < 420)
                    {
                        z.FireIn = Math.Max(1.2, 2.6 - 0.15 * Wave);
                        Bolts.Add(new Shot { X = z.X + z.Facing * 16, Y = z.Y - 22, Vx = z.Facing * 250, Life = 2 });
                        Play(Sfx.EnemyFire);
                    }
                    break;
                case HazardKind.Ember:
                {
                    // Bouncing off whatever it meets.
                    double nx = z.X + z.Vx * Pace * dt, ny = z.Y + z.Vy * Pace * dt;
                    if (m.IsSolidAt(nx + Math.Sign(z.Vx) * 8, z.Y)) z.Vx = -z.Vx; else z.X = nx;
                    if (m.IsSolidAt(z.X, ny + Math.Sign(z.Vy) * 8)) z.Vy = -z.Vy; else z.Y = ny;
                    break;
                }
                case HazardKind.Laser:
                    if (z.On && (z.T + z.Phase - dt) % 2.8 >= 1.4 && Math.Abs(z.X - h.X) < 600) Play(Sfx.Zap);
                    break;
            }
        }
    }

    private void MoveBolts(double dt)
    {
        foreach (var b in Bolts)
        {
            b.X += b.Vx * dt;
            b.Life -= dt;
            if (b.Life <= 0 || Maze.IsSolidAt(b.X, b.Y)) b.Dead = true;
        }
        Bolts.RemoveAll(b => b.Dead);
    }

    /// <summary>Back aboard with the core set: away, and the station goes up behind.</summary>
    private void Escape()
    {
        Bonus = (int)Countdown * 100 + (int)Havoc.Air * 10;
        AddScore(Bonus + StationPoints * Wave);
        SetPhase(Phase.Escape);
        Music(false);
        Say($"STATION {Wave} DESTROYED", $"TIME AND AIR BONUS {Bonus}", EscapeSeconds);
        Play(Sfx.StationBoom);
    }

    // ───────────────────────────── losing ─────────────────────────────

    private void Lose(Phase part, string cause, double x, double y)
    {
        Cause = cause;
        LostIn = part;
        Lives--;
        Fighter.Alive = false;
        Lander.Alive = false;
        Havoc.Alive = false;
        Havoc.Shielded = false;
        Shatter(x, y, 14, 0xFFFFE070, 18);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 1, Size = 2, Colour = 0xFFFFE070 });
        Shots.RemoveAll(s => !s.Mine);
        Bolts.Clear();
        SetPhase(Phase.Dying);
        Music(false);
        Play(part == Phase.Dock ? Sfx.Crash : Sfx.Death);
    }

    private void Shatter(double x, double y, int lines, uint colour, double size)
    {
        for (int i = 0; i < lines; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 50 + _rng.NextDouble() * 150;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Sin(a) * v, Vy = Math.Cos(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 10, Length = size * (0.4 + _rng.NextDouble() * 0.6), Life = 0.8 + _rng.NextDouble() * 0.5, Colour = colour });
        }
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a station's fleet, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Space);
        Message = SubMessage = null;
        Fighter.Safe = 0;
    }

    /// <summary>The fleet in its ring, already in place.</summary>
    internal void Settle()
    {
        foreach (var e in Fleet) e.Arriving = false;
        MoveFleet(0);
    }

    internal void GoDock() { EnterDock(); Message = null; }
    internal void GoMaze() { EnterMaze(); Message = null; Havoc.Safe = 0; }
    internal void PutHavoc(double x, double feet) { Havoc.X = x; Havoc.Y = feet; Havoc.Vx = Havoc.Vy = 0; Havoc.OnGround = false; }
    internal void SetCountdown(double s) { CoreSet = true; Countdown = s; }
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void Begin(int wave) => BeginWave(wave);
}
