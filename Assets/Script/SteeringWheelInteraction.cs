using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// First-Person Steering Wheel — Grab-Point Tracking with three modes.
///
/// Modes:
///   Drive  — clamped ±maxRotation, springs back to centre on release (steering wheel).
///   Valve  — clamped 0–valveTurns full rotations, no spring (tap / pipe valve).
///   Free   — unclamped, spins endlessly, no spring (ship's wheel, combination lock).
///
/// Interaction:
///   On click the exact rim point is recorded. Each drag frame the camera ray is projected
///   onto the wheel's face plane; the wheel rotates so the grabbed point follows the mouse.
/// </summary>
public class SteeringWheelInteraction : PlayerPovInteractable
{
    // ── Modes ─────────────────────────────────────────────────────────────────
    public enum WheelMode { Drive, Valve, Free }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Child Transform that visually rotates (the rim/spoke mesh).")]
    public Transform wheelMesh;

    [Header("Wheel")]
    [Tooltip("Local axis pointing toward the player (wheel face normal). " +
             "Vector3.forward = +Z faces driver.")]
    public Vector3 wheelNormal = Vector3.forward;

    [Tooltip("Minimum distance from wheel centre a grab point must be (world units). " +
             "Prevents jitter when clicking near the hub.")]
    public float minGrabRadius = 0.05f;

    [Header("Mode")]
    public WheelMode wheelMode = WheelMode.Drive;

    [Header("Drive mode")]
    [Tooltip("Max rotation in either direction (degrees). 450 = 1.25 full turns each way.")]
    public float maxRotation = 450f;

    [Tooltip("Deg/sec the wheel springs back to centre when released. 0 = no spring.")]
    public float returnSpeed = 90f;

    [Header("Valve mode")]
    [Tooltip("How many full turns the valve travels from fully-closed (0) to fully-open (1).")]
    public float valveTurns = 3f;

    [Header("Events")]
    [Tooltip("Fires whenever the angle changes. " +
             "Drive → [-1, 1]  |  Valve → [0, 1]  |  Free → unbounded degrees.")]
    public UnityEvent<float> onValueChanged;

    // ── Public read-outs ──────────────────────────────────────────────────────
    /// <summary>
    /// Raw accumulated rotation in degrees.
    /// Drive:  clamped to [-maxRotation, +maxRotation].
    /// Valve:  clamped to [0, valveTurns * 360].
    /// Free:   unclamped.
    /// </summary>
    public float CurrentAngle { get; private set; }

    /// <summary>
    /// Normalised output value.
    /// Drive → [-1, 1]   Valve → [0, 1]   Free → CurrentAngle (raw degrees).
    /// </summary>
    public float NormalizedValue
    {
        get
        {
            switch (wheelMode)
            {
                case WheelMode.Drive: return Mathf.Clamp(CurrentAngle / maxRotation, -1f, 1f);
                case WheelMode.Valve: return Mathf.Clamp01(CurrentAngle / (valveTurns * 360f));
                default: return CurrentAngle; // Free — caller decides meaning
            }
        }
    }

    public bool IsHeld { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────
    // Grab direction stored in the wheel's "rest frame" (un-rotated by CurrentAngle).
    private Vector2 _grabbedLocalDir;

    // CurrentAngle frozen at grab time; delta is added on top each frame.
    private float _angleAtGrab;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (wheelMesh == null)
            wheelMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[SteeringWheel] No Collider — add one so the raycast can detect it.", this);

        // Valve starts fully closed (angle = 0) which is already the default.
    }

    private void Update()
    {
        if (!IsHeld && wheelMode == WheelMode.Drive)
            HandleSpringReturn();

        ApplyVisualRotation();
    }

    // ── PlayerPovInteractable overrides ───────────────────────────────────────
    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);

        Vector2 rawLocalDir = WorldPointToWheelLocal(point);

        if (rawLocalDir.magnitude < minGrabRadius)
        {
            Debug.Log("[SteeringWheel] Grab point too close to centre — ignored.");
            return;
        }

        // Store in the rest frame so we don't need to compensate for CurrentAngle each frame.
        _grabbedLocalDir = Rotate2D(rawLocalDir.normalized, -CurrentAngle);
        _angleAtGrab = CurrentAngle;
        IsHeld = true;
    }

    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        IsHeld = false;
    }

    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!IsHeld) return;

        if (!RaycastWheelPlane(ray, out Vector3 mouseWorldPoint))
            return;

        Vector2 mouseLocalDir = WorldPointToWheelLocal(mouseWorldPoint);

        if (mouseLocalDir.magnitude < minGrabRadius)
            return;

        mouseLocalDir = mouseLocalDir.normalized;

        // Un-rotate mouse dir into rest frame, then take the signed angle from
        // the stored grab direction. This gives the TOTAL delta from grab — not
        // incremental — so we SET rather than ADD to avoid compounding.
        Vector2 mouseInRestFrame = Rotate2D(mouseLocalDir, -_angleAtGrab);
        float delta = Vector2.SignedAngle(_grabbedLocalDir, mouseInRestFrame);
        float desired = _angleAtGrab - delta;

        float prev = CurrentAngle;
        CurrentAngle = ApplyModeClamp(desired);

        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    // ── Mode clamping ─────────────────────────────────────────────────────────
    private float ApplyModeClamp(float angle)
    {
        switch (wheelMode)
        {
            case WheelMode.Drive: return Mathf.Clamp(angle, -maxRotation, maxRotation);
            case WheelMode.Valve: return Mathf.Clamp(angle, 0f, valveTurns * 360f);
            default: return angle; // Free — no clamp
        }
    }

    // ── Spring return (Drive only) ────────────────────────────────────────────
    private void HandleSpringReturn()
    {
        if (returnSpeed <= 0f || Mathf.Approximately(CurrentAngle, 0f)) return;
        float prev = CurrentAngle;
        CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, returnSpeed * Time.deltaTime);
        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    // ── Visual ────────────────────────────────────────────────────────────────
    private void ApplyVisualRotation()
    {
        wheelMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, wheelNormal);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>Set the wheel to a normalised value [0,1] from code (Drive and Valve).</summary>
    public void SetNormalizedValue(float t)
    {
        t = Mathf.Clamp01(t);
        switch (wheelMode)
        {
            case WheelMode.Drive: CurrentAngle = Mathf.Lerp(-maxRotation, maxRotation, t); break;
            case WheelMode.Valve: CurrentAngle = t * valveTurns * 360f; break;
                // Free: no sensible normalised mapping — use SetAngle instead.
        }
        ApplyVisualRotation();
    }

    /// <summary>Set the wheel to an exact angle in degrees from code.</summary>
    public void SetAngle(float degrees)
    {
        CurrentAngle = ApplyModeClamp(degrees);
        ApplyVisualRotation();
    }

    /// <summary>
    /// Returns the current value as a float in a caller-defined range.
    /// Drive:  maps [-1, 1]  → [min, max]
    /// Valve:  maps [0,  1]  → [min, max]
    /// Free:   maps CurrentAngle directly into [min, max] unclamped
    /// </summary>
    public override float GetValueNormalizedFloat(float min, float max)
    {
        switch (wheelMode)
        {
            case WheelMode.Drive: return Mathf.Lerp(min, max, NormalizedValue * 0.5f + 0.5f); // remap [-1,1]→[0,1] first
            case WheelMode.Valve: return Mathf.Lerp(min, max, NormalizedValue);
            default: return Mathf.Lerp(min, max, CurrentAngle);                   // Free — caller interprets
        }
    }

    /// <summary>
    /// Returns the current value snapped to the nearest integer step in [min, max] (inclusive).
    /// E.g. GetValueNormalizedInt(0, 5) on a Valve wheel returns 0, 1, 2, 3, 4, or 5.
    /// Drive uses the full [-1, 1] → [min, max] mapping (min should usually be negative).
    /// Free maps CurrentAngle linearly; min/max define the range endpoints.
    /// </summary>
    public override int GetValueNormalizedInt(int min, int max)
    {
        float f = GetValueNormalizedFloat(min, max);
        return Mathf.Clamp(Mathf.RoundToInt(f), min, max);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────
    private bool RaycastWheelPlane(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        // World-space face normal and a point on the plane.
        Vector3 planeNormal = wheelMesh.TransformDirection(wheelNormal).normalized;
        Vector3 planeOrigin = wheelMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return false; // Parallel — no intersection.

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return false; // Plane is behind the camera.

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    /// <summary>
    /// Projects a world-space point onto the wheel's local 2-D plane (X = right, Y = up).
    /// The origin is the wheel mesh's pivot.
    /// </summary>
    private Vector2 WorldPointToWheelLocal(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - wheelMesh.position;
        return new Vector2(
            Vector3.Dot(offset, wheelMesh.right),
            Vector3.Dot(offset, wheelMesh.up)
        );
    }

    /// <summary>Rotates a 2-D vector by <paramref name="degrees"/> (counter-clockwise).</summary>
    private static Vector2 Rotate2D(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(cos * v.x - sin * v.y,
                           sin * v.x + cos * v.y);
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (wheelMesh == null) return;

        Gizmos.color = Color.yellow;
        Vector3 faceDir = wheelMesh.TransformDirection(wheelNormal).normalized;
        Gizmos.DrawLine(wheelMesh.position, wheelMesh.position + faceDir * 0.35f);
        Gizmos.DrawSphere(wheelMesh.position + faceDir * 0.35f, 0.02f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(wheelMesh.position, minGrabRadius);
    }
}
