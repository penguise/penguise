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
        path.Add(Ground(640f, 0f, 0f));
        path.Add(Ground(590f, 0f, SillHeight));
        path.Add(Inside(TunnelFront, SillHeight, 0f));
        path.Add(Inside(522f, SillHeight, 0.2f));
        path.Add(Inside(500f, 0f, 0.3f));
        path.Add(Inside(330f, 0f, 1f));
        return path;
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
        float t = Mathf.Clamp01(Vector2.Dot(-a, ab) / ab.sqrMagnitude);
        return (a + ab * t).magnitude;
    }
}
