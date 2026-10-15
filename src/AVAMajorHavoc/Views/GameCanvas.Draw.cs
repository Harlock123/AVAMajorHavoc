using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMajorHavoc.Engine;
using Effect = AVAMajorHavoc.Engine.Effect;

namespace AVAMajorHavoc.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMajorHavoc/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private const uint HavocWhite = 0xFFF0F4FF, FighterCyan = 0xFF6AE8FF, RayPink = 0xFFFF6AE0, SentryGreen = 0xFF8AFF6A, EmberOrange = 0xFFFF9A3A,
        LaserRed = 0xFFFF3A4A, WallBlue = 0xFF4A8AFF, AirCyan = 0xFF6AFFF0, CoreMagenta = 0xFFE05AFF, Gold = 0xFFFFD84A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>Whether the screen shows the maze (where places are in the maze, and the camera moves).</summary>
    private bool InMaze => Engine.Phase == Phase.Maze || (Engine.Phase == Phase.Dying && Engine.LostIn == Phase.Maze);

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private static void Ring(DrawingContext dc, Point c, double rx, double ry, uint colour, double alpha = 1, double width = 2)
    {
        dc.DrawEllipse(null, Pen(Alpha(colour, 0.22 * alpha), width * 3.2), c, rx, ry);
        dc.DrawEllipse(null, Pen(Alpha(colour, alpha), width), c, rx, ry);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000004), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top)))
            {
                var view = g.Phase is Phase.Dying ? g.LostIn : g.Phase;
                switch (view)
                {
                    case Phase.Start or Phase.Space: DrawSpace(dc); break;
                    case Phase.Dock: DrawDock(dc); break;
                    case Phase.Maze: DrawMaze(dc); break;
                    case Phase.Escape: DrawEscape(dc); break;
                }
                double cam = InMaze ? g.Camera : 0;
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                    Glow(dc, new Point(d.X - cam - c, d.Y - s), new Point(d.X - cam + c, d.Y + s), d.Colour, a, 1.6);
                }
                foreach (var e in g.Effects) DrawEffect(dc, e, cam);
            }
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 380), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── space ─────────────────────────────

    /// <summary>Stars streaming out from the middle, as if flying in towards the station.</summary>
    private void DrawStarfield(DrawingContext dc, double speed)
    {
        for (int k = 0; k < 140; k++)
        {
            double a = Hash(k, 1, 3) % 6283 / 1000.0;
            double z = (((Hash(k, 2, 3) % 1000) / 1000.0 + _clock * speed) % 1 + 1) % 1;
            double r = 20 + Math.Pow(z, 2.2) * 700;
            var p = new Point(512 + Math.Cos(a) * r, 330 + Math.Sin(a) * r * 0.7);
            var q = new Point(512 + Math.Cos(a) * r * 0.97, 330 + Math.Sin(a) * r * 0.7 * 0.97);
            Glow(dc, q, p, k % 5 == 0 ? 0xFFA0C0FF : 0xFFFFFFFF, 0.25 + 0.75 * z, 1.2);
        }
    }

    /// <summary>The station far off, turning slowly: a wheel with spokes and a hub.</summary>
    private void DrawStation(DrawingContext dc, Point at, double k, double alpha)
    {
        double turn = _clock * 0.3;
        Ring(dc, at, 90 * k, 34 * k, WallBlue, alpha);
        Ring(dc, at, 60 * k, 22 * k, WallBlue, alpha * 0.6);
        for (int i = 0; i < 6; i++)
        {
            double a = turn + i * Math.PI / 3;
            Glow(dc, new Point(at.X + Math.Cos(a) * 16 * k, at.Y + Math.Sin(a) * 6 * k), new Point(at.X + Math.Cos(a) * 90 * k, at.Y + Math.Sin(a) * 34 * k), WallBlue, alpha * 0.8);
        }
        Ring(dc, at, 16 * k, 16 * k, CoreMagenta, alpha);
        Glow(dc, new Point(at.X, at.Y - 16 * k), new Point(at.X, at.Y - 60 * k), WallBlue, alpha);
    }

    private void DrawSpace(DrawingContext dc)
    {
        var g = Engine;
        DrawStarfield(dc, 0.12);
        DrawStation(dc, new Point(512, 250), 0.6 + 0.4 * Math.Min(1, g.PartTime / 40), 0.5);
        foreach (var e in g.Fleet)
        {
            // Nearer the bottom, nearer the fighter: bigger.
            double k = 0.7 + Math.Clamp((e.Y - 150) / 600, 0, 0.8);
            double tilt = e.Diving ? Math.Atan2(e.Vy, e.Vx + Math.Sin(e.T * 5) * 120) - Math.PI / 2 : Math.Sin(e.T * 2 + e.Slot) * 0.2;
            DrawStingray(dc, e.X, e.Y, tilt, k, e.Diving);
        }
        foreach (var s in g.Shots)
        {
            if (s.Mine) Glow(dc, new Point(s.X, s.Y), new Point(s.X, s.Y + 22), 0xFFFFF4C0, 1, 2.4);
            else Shape(dc, s.X, s.Y, _clock * 9, 1, 0xFFFF4A4A, 1, (0, -5), (4, 0), (0, 5), (-4, 0));
        }
        var f = g.Fighter;
        if (f.Alive && (f.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawFighter(dc, f.X, Fighter.Y, 0, 1, false);
    }

    /// <summary>A Stingray: a manta-winged raider, flapping; diving, it glows hotter.</summary>
    private void DrawStingray(DrawingContext dc, double x, double y, double angle, double k, bool diving)
    {
        double flap = Math.Sin(_clock * 6 + x * 0.01) * 4;
        uint col = diving ? 0xFFFF9A6A : RayPink;
        Shape(dc, x, y, angle, k, col, 1, (0, 12), (8, 2), (22, -4 + flap), (10, -8), (0, -14), (-10, -8), (-22, -4 + flap), (-8, 2));
        Shape(dc, x, y, angle, k, col, 0.7, (0, 4), (4, -4), (-4, -4));
        Glow(dc, new Point(x, y + 12 * k), new Point(x, y + 20 * k), col, 0.6);
    }

    /// <summary>Major Havoc's fighter, from behind: a swept delta with twin fins; docking, a flame under it.</summary>
    private void DrawFighter(DrawingContext dc, double x, double y, double angle, double k, bool flame)
    {
        Shape(dc, x, y, angle, k, FighterCyan, 1, (0, -18), (8, -4), (24, 8), (8, 6), (0, 12), (-8, 6), (-24, 8), (-8, -4));
        Shape(dc, x, y, angle, k, FighterCyan, 0.7, (0, -10), (4, 0), (-4, 0));
        Glow(dc, new Point(x - 8 * k, y - 2 * k), new Point(x - 10 * k, y - 14 * k), FighterCyan, 0.8);
        Glow(dc, new Point(x + 8 * k, y - 2 * k), new Point(x + 10 * k, y - 14 * k), FighterCyan, 0.8);
        if (flame)
        {
            double f = 10 + 7 * Math.Sin(_clock * 40);
            Shape(dc, x, y, angle, k, 0xFFFF8A3A, 1, (-5, 12), (0, 12 + f), (5, 12));
        }
    }

    // ───────────────────────────── docking ─────────────────────────────

    private void DrawDock(DrawingContext dc)
    {
        var g = Engine;
        DrawStarfield(dc, 0.02);
        // The station's hull either side of the bay, and its lights.
        var (l0, r0) = Bay.Walls(Bay.Mouth);
        var (l1, r1) = Bay.Walls(Bay.Floor);
        Glow(dc, new Point(0, Bay.Mouth), new Point(l0, Bay.Mouth), WallBlue, 1, 2.5);
        Glow(dc, new Point(r0, Bay.Mouth), new Point(Field.Width, Bay.Mouth), WallBlue, 1, 2.5);
        Glow(dc, new Point(l0, Bay.Mouth), new Point(l1, Bay.Floor), WallBlue, 1, 2.5);
        Glow(dc, new Point(r0, Bay.Mouth), new Point(r1, Bay.Floor), WallBlue, 1, 2.5);
        Glow(dc, new Point(l1, Bay.Floor), new Point(r1, Bay.Floor), WallBlue, 1, 2.5);
        for (int i = 1; i < 6; i++)
        {
            double y = Bay.Mouth + i * (Bay.Floor - Bay.Mouth) / 6;
            var (l, r) = Bay.Walls(y);
            Glow(dc, new Point(l - 40 - i * 20, y), new Point(l, y), WallBlue, 0.35);
            Glow(dc, new Point(r, y), new Point(r + 40 + i * 20, y), WallBlue, 0.35);
            bool lit = (int)(_clock * 3 - i) % 6 == 0;
            dc.FillRectangle(Brush(lit ? Yellow : 0xFF3A3A10), new Rect(l + 6, y - 2, 4, 4));
            dc.FillRectangle(Brush(lit ? Yellow : 0xFF3A3A10), new Rect(r - 10, y - 2, 4, 4));
        }
        for (int i = 0; i < 6; i++)
            Glow(dc, new Point(60 + i * 40, Bay.Mouth), new Point(30 + i * 40, Field.Height), WallBlue, 0.15);
        // The pad, its lights chasing.
        Glow(dc, new Point(Bay.PadLeft, Bay.Floor - 2), new Point(Bay.PadRight, Bay.Floor - 2), Gold, 1, 3);
        for (int i = 0; i < 6; i++)
        {
            bool on = (int)(_clock * 8) % 6 == i;
            dc.FillRectangle(Brush(on ? 0xFFFFFFFF : Gold), new Rect(Bay.PadLeft + 6 + i * 20, Bay.Floor + 4, 6, 4));
        }
        var l2 = g.Lander;
        if (l2.Alive) DrawFighter(dc, l2.X, l2.Y, 0, 0.9, l2.Thrusting);
    }

    // ───────────────────────────── the maze ─────────────────────────────

    private void DrawMaze(DrawingContext dc)
    {
        var g = Engine;
        var m = g.Maze;
        double cam = g.Camera;
        int c0 = Math.Max(0, (int)(cam / Maze.Tile) - 1), c1 = Math.Min(m.Columns - 1, (int)((cam + Field.Width) / Maze.Tile) + 1);
        uint wall = g.CoreSet && (int)(_clock * 4) % 2 == 0 ? 0xFFFF6A6A : WallBlue;
        // The edges of the solid rock: wherever solid meets open.
        for (int c = c0; c <= c1; c++)
            for (int r = 0; r < Maze.Rows; r++)
            {
                if (!m.Solid[c, r]) continue;
                double x = c * Maze.Tile - cam, y = Maze.TopOf(r);
                if (!m.IsSolid(c, r - 1) && r > 0) Glow(dc, new Point(x, y), new Point(x + Maze.Tile, y), wall, 1, 2);
                if (r + 1 < Maze.Rows && !m.IsSolid(c, r + 1)) Glow(dc, new Point(x, y + Maze.Tile), new Point(x + Maze.Tile, y + Maze.Tile), wall, 1, 2);
                if (c > 0 && !m.IsSolid(c - 1, r)) Glow(dc, new Point(x, y), new Point(x, y + Maze.Tile), wall, 1, 2);
                if (c + 1 < m.Columns && !m.IsSolid(c + 1, r)) Glow(dc, new Point(x + Maze.Tile, y), new Point(x + Maze.Tile, y + Maze.Tile), wall, 1, 2);
                // A faint cross-hatch inside, so it reads as solid.
                if ((c + r) % 2 == 0) Glow(dc, new Point(x + 10, y + 16), new Point(x + 22, y + 16), wall, 0.15, 1);
            }
        // Spikes along the pits.
        for (int c = c0; c <= c1; c++)
        {
            if (!m.Spikes[c]) continue;
            double x = c * Maze.Tile - cam, y = Maze.TopOf(Maze.Rows - 2);
            for (int i = 0; i < 4; i++)
            {
                Glow(dc, new Point(x + i * 8, y), new Point(x + i * 8 + 4, y - 12), Red, 1, 1.6);
                Glow(dc, new Point(x + i * 8 + 4, y - 12), new Point(x + i * 8 + 8, y), Red, 1, 1.6);
            }
        }
        DrawShipLanded(dc, m.ShipX - cam, m.ShipY);
        DrawCore(dc, m.CoreX - cam, m.CoreY, g.CoreSet);
        foreach (var k in m.Canisters.Where(k => !k.Taken)) DrawCanister(dc, k.X - cam, k.Y);
        foreach (var z in m.Hazards) DrawHazard(dc, z, cam);
        foreach (var b in g.Bolts)
            Glow(dc, new Point(b.X - cam - Math.Sign(b.Vx) * 14, b.Y), new Point(b.X - cam, b.Y), 0xFFFFE04A, 1, 2.5);
        var h = g.Havoc;
        if (h.Alive && (h.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawHavoc(dc, h.X - cam, h.Y, h.Facing, h.OnGround ? Math.Abs(h.Vx) / GameEngine.RunSpeed : 0, !h.OnGround, h.Shielded, 1);
    }

    /// <summary>Major Havoc: a spaceman in a round helmet, legs going as he runs; his shield a ring of light round him.</summary>
    private void DrawHavoc(DrawingContext dc, double x, double feet, int facing, double running, bool airborne, bool shielded, double k)
    {
        Point P(double px, double py) => new(x + px * facing * k, feet + py * k);
        double swing = airborne ? 0.6 : Math.Sin(_clock * 14) * 0.7 * running;
        // Legs and arms swinging opposite.
        Glow(dc, P(0, -18), P(Math.Sin(swing) * 12, -2), HavocWhite);
        Glow(dc, P(0, -18), P(-Math.Sin(swing) * 12, -2), HavocWhite);
        Glow(dc, P(Math.Sin(swing) * 12, -2), P(Math.Sin(swing) * 12 + 5, 0), HavocWhite);
        Glow(dc, P(-Math.Sin(swing) * 12, -2), P(-Math.Sin(swing) * 12 + 5, 0), HavocWhite);
        Glow(dc, P(0, -18), P(0, -30), HavocWhite);
        Glow(dc, P(0, -28), P(-Math.Sin(swing) * 10, -19), HavocWhite);
        Glow(dc, P(0, -28), P(Math.Sin(swing) * 10 + 4, -20), HavocWhite);
        // The helmet and its visor.
        Ring(dc, P(0, -35), 6 * k, 6 * k, HavocWhite);
        Glow(dc, P(1, -36), P(5, -36), FighterCyan, 1, 2.5);
        // The air tank on his back.
        Glow(dc, P(-4, -29), P(-4, -21), AirCyan, 0.8);
        if (shielded)
        {
            double p = 1 + 0.06 * Math.Sin(_clock * 30);
            Ring(dc, new Point(x, feet - 20 * k), 22 * k * p, 26 * k * p, 0xFF6AE0FF, 0.9, 2.5);
        }
    }

    private void DrawShipLanded(DrawingContext dc, double x, double floor)
    {
        if (x < -80 || x > Field.Width + 80) return;
        // The fighter on its legs, side on, nose to the left.
        Shape(dc, x, floor - 34, 0, 1, FighterCyan, 1, (-44, 4), (-20, -10), (20, -14), (40, -4), (36, 8), (-30, 10));
        Glow(dc, new Point(x - 10, floor - 30), new Point(x + 10, floor - 40), FighterCyan, 0.8);
        Glow(dc, new Point(x - 20, floor - 25), new Point(x - 28, floor), FighterCyan, 0.8);
        Glow(dc, new Point(x + 22, floor - 25), new Point(x + 30, floor), FighterCyan, 0.8);
        if (Engine.CoreSet) Text(dc, "SHIP", new Point(x, floor - 70), 22, (int)(_clock * 3) % 2 == 0 ? Yellow : FighterCyan, center: true);
    }

    /// <summary>The station's core: rings turning about a glowing heart; once set, it pulses red.</summary>
    private void DrawCore(DrawingContext dc, double x, double floor, bool set)
    {
        if (x < -120 || x > Field.Width + 120) return;
        var c = new Point(x, floor - 60);
        uint col = set ? ((int)(_clock * 6) % 2 == 0 ? Red : 0xFFFFE04A) : CoreMagenta;
        Glow(dc, new Point(x - 30, floor), new Point(x - 18, floor - 30), col, 0.7);
        Glow(dc, new Point(x + 30, floor), new Point(x + 18, floor - 30), col, 0.7);
        for (int i = 0; i < 3; i++)
        {
            double a = _clock * (1 + i * 0.6);
            Ring(dc, c, 40 * Math.Abs(Math.Cos(a)) + 4, 40, col, 0.7);
        }
        double p = 12 + 3 * Math.Sin(_clock * 8);
        dc.DrawEllipse(Brush(Alpha(col, 0.6)), null, c, p, p);
        if (!set) Text(dc, "CORE", new Point(x, floor - 120), 22, col, center: true);
    }

    private void DrawCanister(DrawingContext dc, double x, double y)
    {
        if (x < -20 || x > Field.Width + 20) return;
        double bob = Math.Sin(_clock * 3 + x) * 3;
        Shape(dc, x, y + bob, 0, 1, AirCyan, 1, (-6, -10), (6, -10), (6, 10), (-6, 10));
        Glow(dc, new Point(x - 3, y + bob - 14), new Point(x + 3, y + bob - 14), AirCyan);
        Text(dc, "O2", new Point(x, y + bob), 14, AirCyan, center: true, shadow: false);
    }

    private void DrawHazard(DrawingContext dc, Hazard z, double cam)
    {
        double x = z.X - cam;
        if (x < -60 || x > Field.Width + 60) return;
        switch (z.Kind)
        {
            case HazardKind.Sentry:
            {
                // A boxy robot on two stumping legs, its eye sweeping the way it faces.
                double step = Math.Sin(z.T * 10) * 3;
                double y = z.Y;
                Shape(dc, x, y - 22, 0, 1, SentryGreen, 1, (-12, -10), (12, -10), (12, 8), (-12, 8));
                Glow(dc, new Point(x - 6, y - 14), new Point(x - 6 + step, y), SentryGreen);
                Glow(dc, new Point(x + 6, y - 14), new Point(x + 6 - step, y), SentryGreen);
                Glow(dc, new Point(x, y - 32), new Point(x, y - 38), SentryGreen);
                dc.DrawEllipse(Brush(Red), null, new Point(x + z.Facing * 6, y - 25), 3, 3);
                break;
            }
            case HazardKind.Ember:
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = z.T * 8 + i * Math.PI / 3;
                    Glow(dc, new Point(x, z.Y), new Point(x + Math.Cos(a) * 12, z.Y + Math.Sin(a) * 12), i % 2 == 0 ? EmberOrange : Yellow, 1, 2);
                }
                break;
            }
            default:
            {
                // The emitters top and bottom, and the beam between them when it's on.
                Shape(dc, x, z.Min + 6, 0, 1, LaserRed, 0.8, (-8, -6), (8, -6), (4, 6), (-4, 6));
                Shape(dc, x, z.Max - 6, 0, 1, LaserRed, 0.8, (-4, -6), (4, -6), (8, 6), (-8, 6));
                double into = (z.T + z.Phase) % 2.8;
                if (z.On)
                    for (int i = 0; i < 2; i++)
                    {
                        double wob = Math.Sin(_clock * 40 + i) * 2;
                        Glow(dc, new Point(x + wob, z.Min + 12), new Point(x - wob, z.Max - 12), LaserRed, 0.9, 2.2);
                    }
                else if (2.8 - into < 0.4)
                    Glow(dc, new Point(x, z.Min + 12), new Point(x, z.Max - 12), LaserRed, 0.25, 1);
                break;
            }
        }
    }

    // ───────────────────────────── the escape ─────────────────────────────

    private void DrawEscape(DrawingContext dc)
    {
        var g = Engine;
        double t = g.PhaseTime;
        DrawStarfield(dc, -0.08);
        var at = new Point(512, 330);
        if (t < 0.8) DrawStation(dc, at, 1.6 - t * 0.6, 1);
        // The blast: rings going out, and lines flying.
        for (int i = 0; i < 4; i++)
        {
            double r = Math.Max(0, (t - i * 0.25) * 320);
            if (r > 0) Ring(dc, at, r * 1.5, r * 0.8, i % 2 == 0 ? 0xFFFFE04A : CoreMagenta, Math.Max(0, 1 - t / 3));
        }
        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI / 12 + Hash(i, 3, 5) % 10 * 0.05, d = t * (200 + Hash(i, 1, 7) % 200);
            var p = new Point(at.X + Math.Cos(a) * d * 1.4, at.Y + Math.Sin(a) * d * 0.8);
            Glow(dc, p, new Point(p.X + Math.Cos(a + t * 4) * 18, p.Y + Math.Sin(a + t * 4) * 18), i % 3 == 0 ? WallBlue : 0xFFFFE04A, Math.Max(0, 1 - t / 4));
        }
        DrawFighter(dc, 512, 700 - t * 80, 0, 1 - t * 0.15, true);
    }

    private void DrawEffect(DrawingContext dc, Effect e, double cam)
    {
        double t = e.Age / e.Life, x = e.X - cam;
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 40 * t) * e.Size;
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6 + e.X;
                    Glow(dc, new Point(x + Math.Cos(a) * r * 0.5, e.Y + Math.Sin(a) * r * 0.5), new Point(x + Math.Cos(a) * r, e.Y + Math.Sin(a) * r), e.Colour, 1 - t, 1.5);
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(x, e.Y - t * 24), 26, Alpha(e.Colour, 1 - t * t), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(x, e.Y - t * 30), 32, Alpha(e.Colour, 1 - t * t), center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void Meter(DrawingContext dc, string name, double x, double y, double value, uint colour)
    {
        Text(dc, name, new Point(x - 8, y), 20, Dim, right: true);
        dc.DrawRectangle(null, Pen(Dim, 1), new Rect(x, y - 7, 150, 14));
        dc.FillRectangle(Brush(colour), new Rect(x + 2, y - 5, 146 * Math.Clamp(value / 100, 0, 1), 10));
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 36, Yellow);
        Text(dc, "HI", new Point(1008, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(1008, 44), 30, Dim, right: true);
        if (!InGame) return;
        int spare = Math.Max(0, g.Lives - (g.Phase == Phase.Dying ? 0 : 1));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawFighter(dc, 26 + i * 30, 74, 0, 0.55, false);
        Text(dc, $"STATION {g.Wave}", new Point(512, 18), 28, White, center: true);
        var view = g.Phase is Phase.Dying ? g.LostIn : g.Phase;
        switch (view)
        {
            case Phase.Start or Phase.Space:
                Text(dc, $"FLEET {g.Fleet.Count}", new Point(512, 52), 24, RayPink, center: true);
                break;
            case Phase.Dock:
            {
                var l = g.Lander;
                Meter(dc, "FUEL", 330, 52, l.Fuel, l.Fuel < 25 ? Red : 0xFF6AFF8A);
                bool down = l.Vy <= Bay.SafeDown, across = Math.Abs(l.Vx) <= Bay.SafeAcross;
                Text(dc, $"DOWN {l.Vy,4:0}", new Point(560, 52), 24, down ? 0xFF6AFF8A : Red);
                Text(dc, $"ACROSS {Math.Abs(l.Vx),3:0}", new Point(680, 52), 24, across ? 0xFF6AFF8A : Red);
                break;
            }
            case Phase.Maze:
            {
                var h = g.Havoc;
                Meter(dc, "AIR", 330, 50, h.Air, h.Air < 25 ? ((int)(_clock * 4) % 2 == 0 ? Red : 0xFF6A1A1A) : AirCyan);
                Meter(dc, "SHIELD", 330, 72, h.Shield, 0xFF6AE0FF);
                if (g.CoreSet)
                {
                    uint c = g.Countdown < 8 && (int)(_clock * 4) % 2 == 0 ? Red : Yellow;
                    Text(dc, $"{g.Countdown:0.0}", new Point(640, 60), 44, c, center: true);
                    Text(dc, "TO GET OUT", new Point(740, 62), 20, c);
                }
                else Text(dc, "FIND THE CORE", new Point(660, 60), 24, CoreMagenta, center: true);
                break;
            }
        }
        dc.DrawLine(Pen(0xFF203048, 1.5), new Point(0, Field.Top - 2), new Point(1024, Field.Top - 2));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = 512, cy = 200;
        var (w, h) = (Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xA0000010), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 44 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 512;
        DrawStarfield(dc, 0.05);
        Text(dc, "AVA MAJOR HAVOC", new Point(cx, 120), 104, Gold, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 186), 28, Dim, center: true);
        // The three parts of every station.
        double y = 300;
        DrawStingray(dc, cx - 330, y - 30, 0, 1.2, false);
        DrawStingray(dc, cx - 290, y - 10, 0, 1, true);
        DrawFighter(dc, cx - 310, y + 40, 0, 1, false);
        Text(dc, "1. SHOOT DOWN THE FLEET", new Point(cx - 310, y + 90), 22, RayPink, center: true);
        Glow(dc, new Point(cx - 40, y - 40), new Point(cx - 25, y + 40), WallBlue);
        Glow(dc, new Point(cx + 40, y - 40), new Point(cx + 25, y + 40), WallBlue);
        Glow(dc, new Point(cx - 25, y + 40), new Point(cx + 25, y + 40), Gold, 1, 3);
        DrawFighter(dc, cx, y + 10 + Math.Sin(_clock * 2) * 12, 0, 0.8, true);
        Text(dc, "2. DOCK GENTLY", new Point(cx, y + 90), 22, FighterCyan, center: true);
        DrawHavoc(dc, cx + 270, y + 46, 1, 1, false, false, 1.3);
        DrawCore(dc, cx + 350, y + 46, false);
        Text(dc, "3. SET THE CORE AND RUN", new Point(cx + 310, y + 90), 22, CoreMagenta, center: true);
        // In the maze.
        y = 470;
        double[] xs = [cx - 360, cx - 180, cx, cx + 180, cx + 360];
        var sentry = new Hazard { Kind = HazardKind.Sentry, X = xs[0], Y = y + 12, T = _clock, Facing = 1 };
        DrawHazard(dc, sentry, 0);
        DrawHazard(dc, new Hazard { Kind = HazardKind.Ember, X = xs[1], Y = y - 8, T = _clock }, 0);
        DrawHazard(dc, new Hazard { Kind = HazardKind.Laser, X = xs[2], Min = y - 40, Max = y + 20, T = _clock }, 0);
        DrawCanister(dc, xs[3], y - 8);
        Ring(dc, new Point(xs[4], y - 8), 20, 24, 0xFF6AE0FF, 0.9, 2.5);
        string[] names = ["SENTRY", "EMBER", "LASER", $"AIR {GameEngine.CanisterPoints}", "SHIELD"];
        uint[] colours = [SentryGreen, EmberOrange, LaserRed, AirCyan, 0xFF6AE0FF];
        for (int i = 0; i < 5; i++) Text(dc, names[i], new Point(xs[i], y + 46), 22, colours[i], center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 590), 46, Yellow, center: true);
        Text(dc, "ARROWS: MOVE    UP / Z: JUMP OR THRUST    SPACE: FIRE OR SHIELD    P: PAUSE", new Point(cx, 648), 23, Dim, center: true);
        Text(dc, $"EXTRA SHIP AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}    M: SOUND", new Point(cx, 682), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "HALL OF HEROES", new Point(cx, 150), 72, Gold, center: true);
        Text(dc, "RANK NAME    SCORE   STATION", new Point(cx, 216), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 260 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 220), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 290), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 370), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 460), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 500, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 580), 26, White, center: true);
    }
}
