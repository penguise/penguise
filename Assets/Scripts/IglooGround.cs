using System.Collections.Generic;
using UnityEngine;

// Ground plane of the igloo art, which is an orthographic view of a 3D model (camera 23.6 degrees up).
// Works in the art's canvas pixels around the dome's base center, screen y up. Door frame: d along the
// tunnel (out of the dome, turned 25 degrees right of the view), s across it (right), h up.
public static class IglooGround
{
    public const float CanvasPixels = 1254f;
    // Dome base center as a texture fraction of the igloo art, y up.
    public static readonly Vector2 DomeCenter = new Vector2(0.505582f, 0.366826f);
    // Ground radius around the dome center that holds the dome, the tunnel and its threshold.
    public const float Clearance = 660f;
    public const float TunnelFront = 568f;
    public const float SillHeight = 18f;
    private const float TunnelYaw = 25f * Mathf.Deg2Rad;
    private const float CameraElevation = 23.6f * Mathf.Deg2Rad;

    public struct Waypoint
    {
        public Vector2 Pixels;   // screen offset from the dome center, canvas pixels, y up
        public Vector3 Frame;    // the same point in the door frame (d, s, h), canvas pixels
        public bool Masked;      // past the doorway plane: only visible through the opening
        public float Shade;      // 0 at the doorway, 1 deep in the tunnel
        public bool DoorClear;   // in front of the door, beyond the open leaf's swing
    }

    // Screen offset of a door-frame point.
    public static Vector2 ToScreen(float d, float s, float h)
    {
        float x = d * Mathf.Sin(TunnelYaw) + s * Mathf.Cos(TunnelYaw);
        float z = d * Mathf.Cos(TunnelYaw) - s * Mathf.Sin(TunnelYaw);
        return new Vector2(x, -z * Mathf.Sin(CameraElevation) + h * Mathf.Cos(CameraElevation));
    }

    // Door-frame (d, s) of a screen offset taken as a point on the ground.
    public static Vector2 ToFrame(Vector2 pixels)
    {
        float x = pixels.x, z = -pixels.y / Mathf.Sin(CameraElevation);
        return new Vector2(x * Mathf.Sin(TunnelYaw) + z * Mathf.Cos(TunnelYaw),
            x * Mathf.Cos(TunnelYaw) - z * Mathf.Sin(TunnelYaw));
    }

    // Route on the ground from `start` into the tunnel. Points that would cross the dome or the tunnel go
    // around them on the Clearance circle; the last approach stays clear of the door leaf (hinged on the
    // right edge, swinging out to about d 770, s 210). `others` are the other igloos' dome centers (screen
    // offsets from this one, canvas pixels): of the two ways around, the shorter one that keeps
    // NeighborClearance from every other dome wins.
    public static List<Waypoint> PathToDoor(Vector2 start, IReadOnlyList<Vector2> others = null)
    {
        Vector2 begin = ToFrame(start);
        if (begin.x >= 800f)
            return Route(start, begin, 0f);
        List<Waypoint> best = null;
        float bestLength = float.PositiveInfinity, bestClearance = float.NegativeInfinity;
        foreach (float side in new[] { begin.y >= 0f ? 1f : -1f, begin.y >= 0f ? -1f : 1f })
        {
            List<Waypoint> route = Route(start, begin, side);
            float clearance = Mathf.Min(NeighborClearance, ClearanceFrom(route, others));
            float length = Length(route);
            if (clearance > bestClearance + 0.5f || (clearance > bestClearance - 0.5f && length < bestLength))
            {
                best = route;
                bestLength = length;
                bestClearance = clearance;
            }
        }
        return best;
    }

    // Ground distance a route must keep from other igloos' dome centers (dome radius 512 plus a margin).
    public const float NeighborClearance = 550f;

    // Ground length of a route, canvas pixels.
    public static float Length(List<Waypoint> route)
    {
        float length = 0f;
        for (int i = 1; i < route.Count; i++)
            length += Vector3.Distance(route[i - 1].Frame, route[i].Frame);
        return length;
    }

    private static float ClearanceFrom(List<Waypoint> route, IReadOnlyList<Vector2> others)
    {
        float nearest = float.PositiveInfinity;
        if (others == null)
            return nearest;
        foreach (Vector2 other in others)
        {
            Vector2 center = ToFrame(other);
            for (int i = 1; i < route.Count; i++)
                if (!route[i].Masked)
                    nearest = Mathf.Min(nearest, SegmentDistance((Vector2)route[i - 1].Frame - center,
                        (Vector2)route[i].Frame - center));
        }
        return nearest;
    }

    // side: +1 around the right of the dome (positive s), -1 around the left, 0 straight to the door.
    private static List<Waypoint> Route(Vector2 start, Vector2 begin, float side)
    {
        var path = new List<Waypoint> { new Waypoint { Pixels = start, Frame = begin } };
        if (side != 0f)
        {
            var corner = new Vector2(850f, 330f * side);
            if (SegmentDistance(begin, corner) < Clearance && begin.magnitude > Clearance)
            {
                float from = Mathf.Atan2(begin.y, begin.x) - side * Mathf.Acos(Clearance / begin.magnitude);
                float to = Mathf.Atan2(corner.y, corner.x) + side * Mathf.Acos(Clearance / corner.magnitude);
                float span = Mathf.Repeat(side * (from - to), 2f * Mathf.PI);
                int steps = Mathf.CeilToInt(span / (10f * Mathf.Deg2Rad));
                for (int i = 0; i <= steps; i++)
                {
                    float angle = from - side * span * i / Mathf.Max(1, steps);
                    path.Add(Ground(Clearance * Mathf.Cos(angle), Clearance * Mathf.Sin(angle), 0f));
                }
            }
            path.Add(Ground(corner.x, corner.y, 0f));
        }
        var clear = Ground(820f, -10f, 0f);
        clear.DoorClear = true;
        path.Add(clear);
        AddThreshold(path);
        return path;
    }

    // From in front of the door over the sill to deep in the tunnel.
    private static void AddThreshold(List<Waypoint> path)
    {
        path.Add(Ground(640f, 0f, 0f));
        path.Add(Ground(590f, 0f, SillHeight));
        path.Add(Inside(TunnelFront, SillHeight, 0f));
        path.Add(Inside(522f, SillHeight, 0.2f));
        path.Add(Inside(500f, 0f, 0.3f));
        path.Add(Inside(330f, 0f, 1f));
    }

    // Visit approach points in front of the door, from straight ahead to its left: outside the open leaf's
    // swing (hinge near d 568, s 125, leaf 200 long, swinging out to the right) and the dome clearance,
    // so from each one the way to the door is straight.
    private static readonly Vector2[] FrontApproaches =
    {
        new Vector2(900f, -40f), new Vector2(850f, -150f), new Vector2(760f, -250f),
    };

    // Visit approaches of an igloo into its tunnel: from the right around the open leaf's tip (as Route
    // does), and straight to the door from each front point. A visit takes whichever is shortest.
    private static List<List<Waypoint>> Approaches()
    {
        List<Waypoint> right = Route(Vector2.zero, new Vector2(1000f, 0f), 1f);
        right.RemoveAt(0);
        var approaches = new List<List<Waypoint>> { right };
        foreach (Vector2 point in FrontApproaches)
        {
            Waypoint clear = Ground(point.x, point.y, 0f);
            clear.DoorClear = true;
            var path = new List<Waypoint> { clear };
            AddThreshold(path);
            approaches.Add(path);
        }
        return approaches;
    }

    // Ground clearance a visit keeps from the two igloos whose doors it uses (dome, tunnel and threshold)
    // and from every other dome; route corners sit a little outside, every 10 degrees.
    private const float VisitStep = 10f * Mathf.Deg2Rad;
    private const float NodeMargin = 15f;

    // Route on the ground from igloo `from` to the tunnel of igloo `to`. `domes` are every igloo's dome
    // center as screen offsets (canvas pixels) from `from`'s dome center, which the result's Pixels are
    // relative to too; Frame is the ground point in one frame shared by every igloo. `start` null leaves
    // from deep in `from`'s tunnel (its door approach backwards); otherwise it starts at that ground point
    // (screen offset, e.g. the penguin's slot). `end` set ends there (screen offset, e.g. beside the ice
    // hole) instead of in `to`'s tunnel; `to` still keeps the full Clearance. In between it takes the
    // shortest way around every dome (Clearance from both igloos, NeighborClearance from the others),
    // turning only at points whose screen offset lies in `area`. `obstacles` are more round ground areas to
    // keep out of (screen offset of the center, ground radius; e.g. the ice hole); either end may lie inside
    // one and only moves away from its center. Null when no such way exists.
    public static List<Waypoint> PathBetween(Vector2? start, int from, int to, IReadOnlyList<Vector2> domes, Rect area,
        IReadOnlyList<(Vector2 Center, float Radius)> obstacles = null, Vector2? end = null)
    {
        int extra = obstacles?.Count ?? 0;
        var centers = new Vector2[domes.Count + extra];
        var radii = new float[centers.Length];
        for (int i = 0; i < domes.Count; i++)
        {
            centers[i] = ToFrame(domes[i]);
            radii[i] = i == from || i == to ? Clearance : NeighborClearance;
        }
        for (int i = 0; i < extra; i++)
        {
            centers[domes.Count + i] = ToFrame(obstacles[i].Center);
            radii[domes.Count + i] = obstacles[i].Radius;
        }

        // Nodes: sources (leaving), targets (arriving), then corners around every dome.
        var nodes = new List<Vector2>();
        var startCost = new List<float>();
        var lead = new List<List<Waypoint>>();
        if (start.HasValue)
        {
            nodes.Add(ToFrame(start.Value));
            startCost.Add(0f);
            lead.Add(new List<Waypoint> { new Waypoint { Pixels = start.Value, Frame = ToFrame(start.Value) } });
        }
        else
            foreach (List<Waypoint> leave in Approaches())
            {
                leave.Reverse();
                nodes.Add(leave[leave.Count - 1].Frame);
                startCost.Add(Length(leave));
                lead.Add(leave);
            }
        int sources = nodes.Count;
        var tails = new List<List<Waypoint>>();
        if (end.HasValue)
        {
            nodes.Add(ToFrame(end.Value));
            tails.Add(new List<Waypoint> { new Waypoint { Pixels = end.Value, Frame = ToFrame(end.Value) } });
        }
        else
            foreach (List<Waypoint> arrive in Approaches())
            {
                for (int k = 0; k < arrive.Count; k++)
                {
                    Waypoint point = arrive[k];
                    point.Frame += (Vector3)centers[to];
                    point.Pixels += domes[to];
                    arrive[k] = point;
                }
                nodes.Add(arrive[0].Frame);
                tails.Add(arrive);
            }
        int endpoints = nodes.Count;
        for (int i = 0; i < centers.Length; i++)
        {
            float radius = radii[i] + NodeMargin;
            for (float angle = 0f; angle < 2f * Mathf.PI - 1e-3f; angle += VisitStep)
            {
                Vector2 node = centers[i] + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 screen = ToScreen(node.x, node.y, 0f);
                if (area.Contains(screen) && Within(node, centers, radii) < 0)
                    nodes.Add(node);
            }
        }

        // Dijkstra over straight segments that keep clear of every dome. A start inside a clearance (a slot
        // close to its own igloo) only moves away from the domes around it.
        int n = nodes.Count;
        var cost = new float[n];
        var previous = new int[n];
        var done = new bool[n];
        for (int i = 0; i < n; i++)
        {
            cost[i] = i < sources ? startCost[i] : float.PositiveInfinity;
            previous[i] = -1;
        }
        while (true)
        {
            int current = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && !float.IsInfinity(cost[i]) && (current < 0 || cost[i] < cost[current]))
                    current = i;
            if (current < 0)
                break;
            done[current] = true;
            for (int next = 0; next < n; next++)
            {
                if (done[next] || next < sources)
                    continue;
                float total = cost[current] + Vector2.Distance(nodes[current], nodes[next]);
                if (total >= cost[next] || !Clear(nodes[current], nodes[next], current < endpoints, next < endpoints,
                        centers, radii))
                    continue;
                cost[next] = total;
                previous[next] = current;
            }
        }

        int best = -1;
        float bestCost = float.PositiveInfinity;
        for (int t = 0; t < tails.Count; t++)
        {
            float total = cost[sources + t] + Length(tails[t]);
            if (total < bestCost)
            {
                bestCost = total;
                best = t;
            }
        }
        if (best < 0)
            return null;

        var middle = new List<int>();
        int step = previous[sources + best];
        while (step >= sources)
        {
            middle.Add(step);
            step = previous[step];
        }
        middle.Reverse();
        var path = new List<Waypoint>(lead[step]);
        foreach (int i in middle)
            path.Add(new Waypoint { Pixels = ToScreen(nodes[i].x, nodes[i].y, 0f), Frame = nodes[i] });
        path.AddRange(tails[best]);
        return path;
    }

    private static bool Clear(Vector2 a, Vector2 b, bool aEnd, bool bEnd, Vector2[] centers, float[] radii)
    {
        for (int i = 0; i < centers.Length; i++)
        {
            float limit = radii[i];
            if (aEnd)
                limit = Mathf.Min(limit, (a - centers[i]).magnitude);
            if (bEnd)
                limit = Mathf.Min(limit, (b - centers[i]).magnitude);
            if (SegmentDistance(a - centers[i], b - centers[i]) < limit - 0.5f)
                return false;
        }
        return true;
    }

    // First dome whose clearance holds `point`, or -1.
    private static int Within(Vector2 point, Vector2[] centers, float[] radii)
    {
        for (int i = 0; i < centers.Length; i++)
            if ((point - centers[i]).magnitude < radii[i])
                return i;
        return -1;
    }

    private static Waypoint Ground(float d, float s, float h) =>
        new Waypoint { Pixels = ToScreen(d, s, h), Frame = new Vector3(d, s, h) };

    private static Waypoint Inside(float d, float h, float shade) => new Waypoint
    {
        Pixels = ToScreen(d, 0f, h), Frame = new Vector3(d, 0f, h), Masked = true, Shade = shade,
    };

    // Shortest distance from the dome center (frame origin) to the segment a-b.
    private static float SegmentDistance(Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        if (ab.sqrMagnitude < 1e-6f)
            return a.magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(-a, ab) / ab.sqrMagnitude);
        return (a + ab * t).magnitude;
    }
}
