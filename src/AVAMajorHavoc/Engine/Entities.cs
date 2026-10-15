namespace AVAMajorHavoc.Engine;

/// <summary>Left and right (Dx), up and down (Dy), jump or thrust, and fire (in the maze, the shield).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Jump = false);

public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The top of the play area, under the score bar.</summary>
    public const double Top = 88;
}

// ───────────────────────────── space ─────────────────────────────

/// <summary>Major Havoc's fighter: along the bottom of the screen in space, under its own thrust when docking.</summary>
public sealed class Fighter
{
    public const double Y = 700, HalfWidth = 22;
    public double X = 512, FireCool, Safe;
    public bool Alive = true;
}

/// <summary>One of the fleet guarding the station: it wheels round in the ring, and now and then dives.</summary>
public sealed class Stingray
{
    /// <summary>Its place round the ring.</summary>
    public double Slot;
    public double X, Y, Vx, Vy, T, FireIn = 2;
    /// <summary>Still flying in to take its place; diving at the fighter.</summary>
    public bool Arriving = true, Diving, Dead;
    public const double Radius = 18;
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine, Dead;
}

// ───────────────────────────── docking ─────────────────────────────

/// <summary>The fighter coming down into the station's bay: thrust against the pull, and touch down gently on the pad.</summary>
public sealed class Lander
{
    public const double HalfWidth = 18, HalfHeight = 14;
    public double X = 512, Y = 120, Vx, Vy, Fuel = 100;
    public bool Thrusting, Alive = true;
}

/// <summary>The station's landing bay: a shaft narrowing down to the pad.</summary>
public static class Bay
{
    public const double Mouth = 220, Floor = 724, PadLeft = 452, PadRight = 572;
    public const double Gravity = 90, Thrust = 230, Side = 150, FuelBurn = 16, SafeDown = 85, SafeAcross = 55;

    /// <summary>The bay's walls at a height: wide at the mouth, narrowing to the pad.</summary>
    public static (double Left, double Right) Walls(double y)
    {
        if (y < Mouth) return (double.NegativeInfinity, double.PositiveInfinity);
        double t = Math.Clamp((y - Mouth) / (Floor - Mouth), 0, 1);
        return (352 + 70 * t, 672 - 70 * t);
    }
}

// ───────────────────────────── the maze ─────────────────────────────

/// <summary>A stretch of floor through the station, from one column to another at one height.</summary>
public readonly record struct Segment(int From, int To, int Floor);

public enum HazardKind
{
    /// <summary>A robot pacing its stretch of floor, firing along it.</summary>
    Sentry,
    /// <summary>A fireball bouncing about the passage.</summary>
    Ember,
    /// <summary>A beam across the passage, on and off.</summary>
    Laser,
}

public sealed class Hazard
{
    public HazardKind Kind;
    /// <summary>A place in the maze, in pixels.</summary>
    public double X, Y, Vx, Vy, T, FireIn = 2;
    /// <summary>A Sentry's stretch of floor; a Laser's top and bottom.</summary>
    public double Min, Max;
    /// <summary>A Laser's place in its cycle.</summary>
    public double Phase;
    public int Facing = 1;

    /// <summary>A Laser is on for 1.4 seconds, then off for 1.4.</summary>
    public bool On => Kind != HazardKind.Laser || (T + Phase) % 2.8 < 1.4;
}

/// <summary>An air canister: a breath of oxygen, and points.</summary>
public sealed class Canister
{
    public double X, Y;
    public bool Taken;
}

/// <summary>Major Havoc on foot: he runs, jumps, and falls, and can raise a shield for a moment.</summary>
public sealed class Runner
{
    public const double HalfWidth = 9, Height = 40;
    /// <summary>His middle, and his feet.</summary>
    public double X, Y, Vx, Vy;
    public bool OnGround, Shielded, Alive = true;
    public int Facing = 1;
    public double Air = 100, Shield = 100, Safe;
}

/// <summary>
/// One station's maze: a passage of floors stepping up and down, with pits, a few platforms to climb, the
/// ship at one end and the core at the other. It's made from the level number, so a level's maze is always
/// the same, and every step up or down is one that can be climbed back, so the way out is always there.
/// </summary>
public sealed class Maze
{
    public const int Tile = 32, Rows = 21;
    public int Columns { get; }
    public bool[,] Solid { get; }
    /// <summary>Spikes along the bottom of a pit, by column.</summary>
    public bool[] Spikes { get; }
    public List<Segment> Floors { get; } = [];
    public List<Hazard> Hazards { get; } = [];
    public List<Canister> Canisters { get; } = [];
    public double ShipX, ShipY, CoreX, CoreY;

    public double PixelWidth => Columns * Tile;

    public static double TopOf(int row) => Field.Top + row * Tile;
    public static int RowAt(double y) => (int)Math.Floor((y - Field.Top) / Tile);
    public static int ColumnAt(double x) => (int)Math.Floor(x / Tile);

    public bool IsSolid(int c, int r) => c < 0 || c >= Columns || r < 0 || r >= Rows || Solid[c, r];
    public bool IsSolidAt(double x, double y) => IsSolid(ColumnAt(x), RowAt(y));

    public Maze(int level)
    {
        var rng = new Random(9100 + level * 37);
        int stretch = Math.Min(level - 1, 4);
        Columns = 64 + 8 * stretch;
        Solid = new bool[Columns, Rows];
        Spikes = new bool[Columns];
        for (int c = 0; c < Columns; c++)
        {
            Solid[c, 0] = Solid[c, Rows - 1] = true;
        }
        for (int r = 0; r < Rows; r++) Solid[0, r] = Solid[Columns - 1, r] = true;

        // The floors: steps of at most two up or down, so the way back can always be climbed; now and then a
        // pit between two floors at much the same height.
        int x = 1, h = 17;
        int ceilingOf(int floor, int extra) => Math.Max(1, floor - 7 - extra);
        void Lay(int from, int to, int floor, int extra)
        {
            for (int c = from; c <= to; c++)
            {
                for (int r = floor; r < Rows; r++) Solid[c, r] = true;
                for (int r = 1; r < ceilingOf(floor, extra); r++) Solid[c, r] = true;
            }
            Floors.Add(new Segment(from, to, floor));
        }
        Lay(x, x + 7, h, 0);
        x += 8;
        while (x < Columns - 14)
        {
            int len = 5 + rng.Next(5);
            int delta = rng.Next(5) - 2;
            int next = Math.Clamp(h + delta, 10, 17);
            if (Math.Abs(next - h) <= 1 && rng.NextDouble() < 0.35 + 0.05 * stretch)
            {
                // A pit, spikes at the bottom.
                int gap = 2 + rng.Next(2);
                int roof = Math.Min(ceilingOf(h, 0), ceilingOf(next, 0));
                for (int c = x; c < x + gap; c++)
                {
                    Solid[c, Rows - 2] = true;
                    Spikes[c] = true;
                    for (int r = 1; r < roof; r++) Solid[c, r] = true;
                }
                x += gap;
            }
            int extra = rng.Next(4);
            Lay(x, x + len - 1, next, extra);
            // A platform to jump up on, with a canister on it, where the roof is high enough.
            if (extra >= 2 && len >= 6)
            {
                int pc = x + 2 + rng.Next(len - 5), pr = next - 3;
                for (int c = pc; c < pc + 3; c++) Solid[c, pr] = true;
                Canisters.Add(new Canister { X = (pc + 1.5) * Tile, Y = TopOf(pr) - 14 });
            }
            else if (rng.NextDouble() < 0.6)
                Canisters.Add(new Canister { X = (x + len / 2.0) * Tile, Y = TopOf(next) - 14 });
            x += len;
            h = next;
        }
        // The last stretch, with the core at its end.
        Lay(x, Columns - 2, h, 2);
        CoreX = (Columns - 4) * Tile;
        CoreY = TopOf(h);
        ShipX = 3.5 * Tile;
        ShipY = TopOf(17);

        // What guards the way: Sentries pacing the longer floors, Lasers across some, Embers in the high places.
        double sentries = 0.25 + 0.1 * stretch, lasers = 0.2 + 0.08 * stretch, embers = 0.15 + 0.08 * stretch;
        foreach (var f in Floors.Skip(1).Take(Floors.Count - 2))
        {
            double left = (f.From + 0.5) * Tile, right = (f.To + 0.5) * Tile, floor = TopOf(f.Floor);
            if (f.To - f.From >= 6 && rng.NextDouble() < sentries)
                Hazards.Add(new Hazard { Kind = HazardKind.Sentry, X = (left + right) / 2, Y = floor, Min = left + 8, Max = right - 8, Facing = rng.Next(2) * 2 - 1, FireIn = 1 + rng.NextDouble() * 2 });
            else if (rng.NextDouble() < lasers)
            {
                int c = f.From + 1 + rng.Next(Math.Max(1, f.To - f.From - 1));
                int top = f.Floor - 1;
                while (top > 0 && !Solid[c, top - 1]) top--;
                Hazards.Add(new Hazard { Kind = HazardKind.Laser, X = (c + 0.5) * Tile, Min = TopOf(top), Max = floor, Phase = rng.NextDouble() * 2.8 });
            }
            if (rng.NextDouble() < embers)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                Hazards.Add(new Hazard { Kind = HazardKind.Ember, X = (left + right) / 2, Y = floor - 90, Vx = Math.Cos(a) * 140, Vy = Math.Sin(a) * 140 });
            }
        }
    }

    /// <summary>The floor under a place: the height of the first solid tile at or below it.</summary>
    public double FloorBelow(double x, double y)
    {
        int c = ColumnAt(x);
        for (int r = Math.Max(0, RowAt(y)); r < Rows; r++)
            if (IsSolid(c, r)) return TopOf(r);
        return TopOf(Rows);
    }
}

// ───────────────────────────── bits and pieces ─────────────────────────────

/// <summary>A line of something destroyed, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    /// <summary>On the screen in space and docking; in the maze, a place in the maze.</summary>
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
