namespace AVAMajorHavoc.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). In space it slides under the nearest Stingray
/// (a diving one first) and fires, stepping aside from anything coming down at it. Docking, it steers over the
/// pad and lets the ship down slower the nearer it gets. In the maze it runs for the core, then back to the
/// ship: jumping up steps, over pits and Sentries, waiting out the Lasers, and raising the shield when an
/// Ember or a shot is about to reach it.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    public Controls Decide(GameEngine g) => g.Phase switch
    {
        Phase.Space => Space(g),
        Phase.Dock => Dock(g),
        Phase.Maze => OnFoot(g),
        _ => default,
    };

    private Controls Space(GameEngine g)
    {
        _tick++;
        var f = g.Fighter;
        if (!f.Alive) return default;
        // Anything about to come down on it.
        double danger = double.NaN;
        foreach (var s in g.Shots.Where(s => !s.Mine && s.Vy > 0))
        {
            double t = (Fighter.Y - s.Y) / s.Vy;
            if (t < 0 || t > 0.8) continue;
            double x = s.X + s.Vx * t;
            if (Math.Abs(x - f.X) < Fighter.HalfWidth + 14) { danger = x; break; }
        }
        foreach (var e in g.Fleet.Where(e => e.Diving && e.Y > Fighter.Y - 220))
            if (Math.Abs(e.X - f.X) < 60) { danger = e.X; break; }
        if (!double.IsNaN(danger))
        {
            Doing = "dodge";
            int away = danger > f.X ? -1 : 1;
            if (f.X < 120) away = 1;
            if (f.X > Field.Width - 120) away = -1;
            return new Controls(away, 0, Fire: _tick % 2 == 0);
        }
        var target = g.Fleet.Where(e => !e.Arriving).MinBy(e => Math.Abs(e.X - f.X) * (e.Diving ? 0.4 : 1) + (Fighter.Y - e.Y) * 0.3);
        if (target == null) { Doing = "wait"; return default; }
        Doing = "attack";
        double lead = (Fighter.Y - target.Y) / GameEngine.ShotSpeed;
        double aim = target.X + (target.Diving ? target.Vx : -Math.Sin(target.Slot + g.RingAngle) * g.RingRadius * 1.6 * 0.45 * g.Pace) * lead;
        int dx = Math.Abs(aim - f.X) > 8 ? Math.Sign(aim - f.X) : 0;
        return new Controls(dx, 0, Fire: Math.Abs(aim - f.X) < 22 && _tick % 2 == 0);
    }

    private Controls Dock(GameEngine g)
    {
        var l = g.Lander;
        if (!l.Alive) return default;
        Doing = "dock";
        double padX = (Bay.PadLeft + Bay.PadRight) / 2;
        // Across: towards the pad, easing off as it gets there.
        double wantVx = Math.Clamp((padX - l.X) * 0.9, -110, 110);
        int dx = Math.Abs(wantVx - l.Vx) > 6 ? Math.Sign(wantVx - l.Vx) : 0;
        // Down: quick at first, gentle at the end; and held up until it's lined up with the mouth.
        double above = Bay.Floor - Lander.HalfHeight - l.Y;
        double wantVy = Math.Clamp(above * 0.4, 30, 150);
        if (l.Y > Bay.Mouth - 80 && Math.Abs(l.X - padX) > 40) wantVy = Math.Min(wantVy, 10);
        bool thrust = l.Vy > wantVy;
        return new Controls(dx, 0, Jump: thrust);
    }

    private Controls OnFoot(GameEngine g)
    {
        var h = g.Havoc;
        var m = g.Maze;
        if (!h.Alive) return default;
        double goal = g.CoreSet ? m.ShipX : m.CoreX;
        int dir = Math.Sign(goal - h.X);
        if (Math.Abs(goal - h.X) < 6) dir = 0;
        bool shield = false, jump = false;
        int dx = dir;
        Doing = g.CoreSet ? "escape" : "to core";

        // Embers and shots about to reach him: the shield.
        foreach (var z in m.Hazards.Where(z => z.Kind == HazardKind.Ember))
        {
            double px = z.X + z.Vx * 0.25, py = z.Y + z.Vy * 0.25;
            if (Math.Abs(px - h.X) < 40 && py > h.Y - Runner.Height - 30 && py < h.Y + 20) shield = true;
        }
        foreach (var b in g.Bolts)
            if ((Math.Sign(h.X - b.X) == Math.Sign(b.Vx) || Math.Abs(h.X - b.X) < 30) && Math.Abs(h.X - b.X) < 120 && Math.Abs(b.Y - (h.Y - 22)) < 30) shield = true;
        if (h.Safe > 0.3) shield = false;
        if (shield && h.Shield <= 0)
        {
            // No shield left: jump the shot, or back off from the Ember.
            shield = false;
            jump = true;
        }

        if (dir != 0 && h.OnGround)
        {
            double front = h.X + dir * (Runner.HalfWidth + 4);
            int c = Maze.ColumnAt(front + dir * 14), feetRow = Maze.RowAt(h.Y - 1) + 1;
            // A step up in front.
            if (m.IsSolid(c, feetRow - 1) || m.IsSolid(c, feetRow - 2)) jump = true;
            // A drop or a pit: walk off a short drop, jump a pit.
            if (!m.IsSolid(c, feetRow))
            {
                int drop = 0;
                while (drop < 6 && !m.IsSolid(c, feetRow + drop)) drop++;
                bool spikes = m.Spikes[Math.Clamp(c, 0, m.Columns - 1)];
                if (spikes || drop > 2)
                {
                    jump = true;
                    // Not onto a Sentry: wait at the edge till the far side is clear.
                    double land = h.X + dir * 130;
                    foreach (var z in m.Hazards.Where(z => z.Kind == HazardKind.Sentry && Math.Abs(z.Y - h.Y) < 70))
                        if (Math.Abs(z.X - land) < 110 && !(Math.Sign(z.X - land) == dir && z.Facing == dir)) { jump = false; dx = 0; Doing = "wait"; }
                }
            }
            // A Sentry coming the other way on this floor: over it.
            foreach (var z in m.Hazards.Where(z => z.Kind == HazardKind.Sentry && Math.Abs(z.Y - h.Y) < 10))
            {
                double ahead = (z.X - h.X) * dir;
                if (ahead <= -26 || ahead >= 75) continue;
                // Still passing through it: keep the shield up.
                if (ahead <= 0) { if (h.Shield > 0) shield = true; continue; }
                // Over it if there's room above; otherwise the shield, or back off till it turns.
                bool room = true;
                for (double d = -Runner.HalfWidth; d <= 100; d += 8)
                    for (double up = 4; up <= 84; up += 16)
                        if (m.IsSolidAt(h.X + dir * d, h.Y - Runner.Height - up)) room = false;
                // Too close to jump it now, or a jump would come down in a pit: the shield.
                if ((ahead < 34 || !SafeLanding(m, h, dir)) && h.Shield > 8) { shield = ahead < 40; jump = false; }
                else if (room && SafeLanding(m, h, dir)) jump = true;
                else
                {
                    // No room to jump it: slip past behind the shield as it comes, rather than chase it.
                    jump = false;
                    if (z.Facing == dir && ahead > 30) { dx = 0; Doing = "wait"; }
                    else if (ahead < 40 && h.Shield > 8) shield = true;
                    else if (h.Shield <= 8) { dx = -dir; Doing = "back off"; }
                }
            }
            // A Laser ahead: wait for it to be off for long enough to get through.
            foreach (var z in m.Hazards.Where(z => z.Kind == HazardKind.Laser && h.Y > z.Min && h.Y - Runner.Height < z.Max))
            {
                double ahead = (z.X - h.X) * dir;
                if (ahead <= -Runner.HalfWidth - 4 || ahead > 60) continue;
                // Through only if it'll stay off long enough to get clear.
                double into = (z.T + z.Phase) % 2.8;
                double needed = (ahead + Runner.HalfWidth + 8) / GameEngine.RunSpeed + 0.15;
                bool safe = into >= 1.4 && 2.8 - into > needed;
                if (ahead < 0) safe = true;
                if (!safe)
                {
                    if (g.CoreSet && g.Countdown < 6 && h.Shield > 25) shield = true;
                    else { dx = 0; jump = false; Doing = "wait"; }
                }
            }
        }
        // Carried into a Laser (in the air, or too late to stop): the shield.
        foreach (var z in m.Hazards.Where(z => z.Kind == HazardKind.Laser && h.Y > z.Min && h.Y - Runner.Height < z.Max))
        {
            double ahead = (z.X - h.X) * Math.Sign(h.Vx == 0 ? h.Facing : h.Vx);
            if (ahead < -Runner.HalfWidth - 4 || ahead > 26) continue;
            double into = (z.T + z.Phase) % 2.8;
            if ((into < 1.4 || 2.8 - into < 0.25) && h.Shield > 3) shield = true;
        }
        // An air canister just above: jump up for it.
        if (h.OnGround && m.Canisters.Any(k => !k.Taken && Math.Abs(k.X - h.X) < 40 && k.Y < h.Y - 50 && k.Y > h.Y - 140)
            && !m.IsSolidAt(h.X - Runner.HalfWidth, h.Y - Runner.Height - 40) && !m.IsSolidAt(h.X + Runner.HalfWidth, h.Y - Runner.Height - 40)) jump = true;
        // In the air over a pit, keep going.
        if (!h.OnGround && dx == 0) dx = dir;
        return new Controls(dx, 0, Fire: shield, Jump: jump);
    }

    /// <summary>Whether a running jump from here would come down on a floor, rather than in a pit.</summary>
    private static bool SafeLanding(Maze m, Runner h, int dir)
    {
        double land = h.X + dir * 150;
        int feet = Maze.RowAt(h.Y - 1) + 1;
        foreach (double x in new[] { land - Runner.HalfWidth, land + Runner.HalfWidth })
        {
            int c = Maze.ColumnAt(x);
            if (c < 0 || c >= m.Columns || m.Spikes[c]) return false;
            bool floor = false;
            for (int r = feet - 2; r <= feet + 2; r++) if (m.IsSolid(c, r) && !m.IsSolid(c, r - 1)) floor = true;
            if (!floor) return false;
        }
        return true;
    }
}
