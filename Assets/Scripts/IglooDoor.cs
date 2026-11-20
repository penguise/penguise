using UnityEngine;

// Igloo door leaf hinged on its right edge, opening outward. The igloo art is an orthographic view of a
// 3D model, so the leaf opened by any angle is the closed leaf's image under one 2D linear map about
// the hinge. Unity transforms cannot shear directly, so the map is split into rotation, non-uniform
// scale, rotation (2x2 SVD) across this object, its "Skew" child and the "Leaf" grandchild.
[ExecuteAlways]
public sealed class IglooDoor : MonoBehaviour
{
    // Opening angle at Open = 1. Beyond 65 degrees the camera sees the back of the leaf.
    public const float MaxAngle = 115f;
    // Art camera: tunnel axis turned 25 degrees right of the view, camera elevated 23.6 degrees.
    private const float TunnelYaw = 25f;
    private const float CameraElevation = 23.6f;

    [SerializeField, Range(0f, 1f)] private float open;
    [SerializeField, HideInInspector] private Sprite front;
    [SerializeField, HideInInspector] private Sprite back;
    [SerializeField, HideInInspector] private Transform skew;
    [SerializeField, HideInInspector] private Transform leaf;
    [SerializeField, HideInInspector] private SpriteRenderer leafRenderer;

    private bool animating;
    private float animationFrom, animationTo, animationDuration, animationElapsed;

    // 0 closed, 1 fully open (MaxAngle).
    public float Open
    {
        get => open;
        set
        {
            animating = false;
            open = Mathf.Clamp01(value);
            Apply();
        }
    }

    public float Angle => open * MaxAngle;
    public bool IsAnimating => animating;
    public bool ShowsFront => FacesCamera(Angle);

    // Builds the leaf hierarchy under `parent`, with the hinge at `hinge` (parent local units).
    public static IglooDoor Create(Transform parent, Vector2 hinge, Sprite front, Sprite back,
        Material material, int sortingOrder)
    {
        var door = new GameObject("Door").AddComponent<IglooDoor>();
        door.transform.SetParent(parent, false);
        door.transform.localPosition = hinge;
        door.skew = new GameObject("Skew").transform;
        door.skew.SetParent(door.transform, false);
        door.leaf = new GameObject("Leaf").transform;
        door.leaf.SetParent(door.skew, false);
        door.leafRenderer = door.leaf.gameObject.AddComponent<SpriteRenderer>();
        door.leafRenderer.sharedMaterial = material;
        door.leafRenderer.sortingOrder = sortingOrder;
        door.front = front;
        door.back = back;
        door.Apply();
        return door;
    }

    // Moves to `target` (0..1) over `seconds` of play time with ease-in-out.
    public void AnimateTo(float target, float seconds)
    {
        animationFrom = open;
        animationTo = Mathf.Clamp01(target);
        animationDuration = Mathf.Max(0f, seconds);
        animationElapsed = 0f;
        animating = true;
        if (animationDuration == 0f)
            Update();
    }

    private void OnValidate() => Apply();

    private void Update()
    {
        if (!animating)
            return;
        animationElapsed += Application.isPlaying ? Time.deltaTime : 0f;
        float t = animationDuration > 0f ? Mathf.Clamp01(animationElapsed / animationDuration) : 1f;
        open = Mathf.Lerp(animationFrom, animationTo, Mathf.SmoothStep(0f, 1f, t));
        animating = t < 1f;
        Apply();
    }

    private void Apply()
    {
        if (skew == null || leaf == null || leafRenderer == null)
            return;
        Decompose(OpenMatrix(Angle), out float first, out Vector2 scale, out float second);
        transform.localRotation = Quaternion.Euler(0f, 0f, first * Mathf.Rad2Deg);
        skew.localScale = new Vector3(scale.x, scale.y, 1f);
        leaf.localRotation = Quaternion.Euler(0f, 0f, second * Mathf.Rad2Deg);
        leafRenderer.sprite = ShowsFront ? front : back;
    }

    // Screen image (y up) of the leaf's 3D axes: across the leaf, along the tunnel, and up.
    private static Vector2 Across => new Vector2(Mathf.Cos(TunnelYaw * Mathf.Deg2Rad),
        Mathf.Sin(TunnelYaw * Mathf.Deg2Rad) * Mathf.Sin(CameraElevation * Mathf.Deg2Rad));
    private static Vector2 Outward => new Vector2(Mathf.Sin(TunnelYaw * Mathf.Deg2Rad),
        -Mathf.Cos(TunnelYaw * Mathf.Deg2Rad) * Mathf.Sin(CameraElevation * Mathf.Deg2Rad));
    private static Vector2 Up => new Vector2(0f, Mathf.Cos(CameraElevation * Mathf.Deg2Rad));

    // The leaf's front faces the camera while its normal, turned `degrees` outward, points toward it.
    public static bool FacesCamera(float degrees) => Mathf.Cos((TunnelYaw + degrees) * Mathf.Deg2Rad) > 0f;

    // Linear map from the closed leaf's image to the leaf opened by `degrees`, about the hinge.
    // Across maps to the turned across-axis; Up stays fixed.
    public static Matrix4x4 OpenMatrix(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 turned = Across * Mathf.Cos(radians) - Outward * Mathf.Sin(radians);
        Vector2 across = Across, up = Up;
        float determinant = across.x * up.y - up.x * across.y;
        // [turned up] * inverse([across up])
        var inverse = new Vector4(up.y, -across.y, -up.x, across.x) / determinant;
        var matrix = Matrix4x4.identity;
        matrix.m00 = turned.x * inverse.x + up.x * inverse.y;
        matrix.m01 = turned.x * inverse.z + up.x * inverse.w;
        matrix.m10 = turned.y * inverse.x + up.y * inverse.y;
        matrix.m11 = turned.y * inverse.z + up.y * inverse.w;
        return matrix;
    }

    // 2x2 SVD: M = R(first) * diag(scale) * R(second); scale.y may be negative (mirrored leaf).
    public static void Decompose(Matrix4x4 m, out float first, out Vector2 scale, out float second)
    {
        float e = (m.m00 + m.m11) / 2f, f = (m.m00 - m.m11) / 2f;
        float g = (m.m10 + m.m01) / 2f, h = (m.m10 - m.m01) / 2f;
        float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
        float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
        first = (a2 + a1) / 2f;
        second = (a2 - a1) / 2f;
        scale = new Vector2(q + r, q - r);
    }
}
