using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// First-Person Lever Interaction — Grab-Point Tracking
///
/// Mirrors SteeringWheelInteraction's approach: on grab, records the hit point on the
/// lever's rotation plane, then each drag frame projects the camera ray onto that same
/// plane and drives the angle so the grabbed point follows the mouse.
///
/// Modes (set 'leverMode' in Inspector):
///   FreeSlide  — lever tracks the mouse continuously (throttle, dimmer).
///   Snap       — drag freely; snaps to nearest detent on release.
///   Toggle     — single click flips between min and max (light switch).
/// </summary>
public class LeverInteraction : PlayerPovInteractable
{
    public enum LeverMode { FreeSlide, Snap, Toggle }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("The mesh Transform that rotates (the arm/handle child).")]
    public Transform leverMesh;

    [Header("Rotation")]
    [Tooltip("Local axis the lever rotates around. X = tilt forward/back (typical wall lever).")]
    public Vector3 pivotAxis = Vector3.right;

    [Tooltip("A second local axis perpendicular to pivotAxis that lies in the plane the mouse " +
             "drags across — usually the lever's local Y or Z. Used to build the projection plane.")]
    public Vector3 dragPlaneAxis = Vector3.up;

    [Tooltip("Angle at the back/bottom of travel (degrees).")]
    public float minAngle = -45f;

    [Tooltip("Angle at the front/top of travel (degrees).")]
    public float maxAngle = 45f;

    [Tooltip("Starting angle (degrees). Clamped to [minAngle, maxAngle].")]
    public float startAngle = 0f;

    [Tooltip("Radius from lever pivot used to sample a point on the plane at grab time. " +
             "Should roughly match the handle length so the grab point sits on the handle.")]
    public float grabSampleRadius = 0.2f;

    [Tooltip("Minimum projected distance from pivot to accept a drag sample (world units). " +
             "Prevents jitter if the ray passes very close to the pivot.")]
    public float minGrabRadius = 0.02f;

    [Header("Mode")]
    public LeverMode leverMode = LeverMode.FreeSlide;

    [Tooltip("Number of detent positions (Snap mode). 2 = min/max only, 3 = min/mid/max, etc.")]
    [Min(2)] public int snapPositions = 3;

    [Tooltip("Deg/sec the lever animates to a snap/toggle target. 0 = instant.")]
    public float snapSpeed = 180f;

    [Header("Events")]
    public UnityEvent<float> onLeverChanged;
    public UnityEvent onMinReached;
    public UnityEvent onMaxReached;

    // ── Public read-outs ──────────────────────────────────────────────────────
    public float CurrentAngle { get; private set; }
    public float NormalizedValue => Mathf.InverseLerp(minAngle, maxAngle, CurrentAngle);
    public bool IsHeld { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────
    private float _targetAngle;
    private bool _atMinLast;
    private bool _atMaxLast;

    // Grab-tracking state.
    // We store the grab point in a 2-D local frame (axisA = dragPlaneAxis,
    // axisB = cross(pivotAxis, dragPlaneAxis)) so Atan2 gives a full ±180° angle.
    private float _grabbedA;      // projection onto dragPlaneAxis at grab time
    private float _grabbedB;      // projection onto the perpendicular axis at grab time
    private float _angleAtGrab;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (leverMesh == null)
            leverMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[LeverInteraction] No Collider — add one for raycast detection.", this);

        CurrentAngle = Mathf.Clamp(startAngle, minAngle, maxAngle);
        _targetAngle = CurrentAngle;
        ApplyVisualRotation();
    }

    private void Update()
    {
        AnimateToTarget();
        ApplyVisualRotation();
        FireEdgeEvents();
    }

    // ── PlayerPovInteractable overrides ───────────────────────────────────────
    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);

        if (leverMode == LeverMode.Toggle)
        {
            _targetAngle = Mathf.Approximately(_targetAngle, maxAngle) ? minAngle : maxAngle;
            onLeverChanged?.Invoke(NormalizedValue);
            return;
        }

        // Record the grab point in the same 2-D frame used by OnInteractionDrag.
        Vector3 worldPivotAxis = leverMesh.TransformDirection(pivotAxis).normalized;
        Vector3 worldAxisA = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(worldPivotAxis, worldAxisA).normalized;
        Vector3 offset = point - leverMesh.position;
        _grabbedA = Vector3.Dot(offset, worldAxisA);
        _grabbedB = Vector3.Dot(offset, worldAxisB);

        // If player clicked right on the pivot, fall back to a point on the handle.
        if (new Vector2(_grabbedA, _grabbedB).magnitude < minGrabRadius)
        {
            _grabbedA = grabSampleRadius;
            _grabbedB = 0f;
        }

        _angleAtGrab = CurrentAngle;
        _targetAngle = CurrentAngle;
        IsHeld = true;
    }

    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        IsHeld = false;

        if (leverMode == LeverMode.Snap)
            _targetAngle = NearestSnapAngle(CurrentAngle);
    }

    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!IsHeld || leverMode == LeverMode.Toggle) return;

        // ── Project ray onto the lever's rotation plane ───────────────────────
        // planeNormal = pivotAxis in world space.  Points on this plane move in
        // the same arc as the lever handle, so the intersection gives us a
        // world position we can compare against the grab point.
        Vector3 planeNormal = leverMesh.TransformDirection(pivotAxis).normalized;
        Vector3 planeOrigin = leverMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return; // Ray parallel to plane.

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return; // Plane behind camera.

        Vector3 mouseWorldPoint = ray.origin + ray.direction * t;

        // ── Map the mouse hit into the lever's local 2-D frame ────────────────
        // We use two axes that both lie in the rotation plane:
        //   axisA = dragPlaneAxis  (the direction the handle sweeps — e.g. local Y)
        //   axisB = cross(pivotAxis, dragPlaneAxis)  (perpendicular in the same plane)
        // Together they give a full 2-D coordinate so Atan2 can return ±180°.
        Vector3 worldAxisA = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(planeNormal, worldAxisA).normalized; // planeNormal == pivotAxis world, same frame as OnInteractionBegin

        Vector3 hitOffset = mouseWorldPoint - leverMesh.position;
        float mouseA = Vector3.Dot(hitOffset, worldAxisA);
        float mouseB = Vector3.Dot(hitOffset, worldAxisB);

        if (new Vector2(mouseA, mouseB).magnitude < minGrabRadius) return;

        // ── Atan2 angle of mouse point, then subtract grab-point angle ────────
        // Both angles are measured in the same 2-D frame, so the difference is
        // the rotation needed to bring the grab point to the mouse — total, not
        // incremental, so we SET CurrentAngle rather than adding to it each frame.
        float mouseAngle = Mathf.Atan2(mouseB, mouseA) * Mathf.Rad2Deg;
        float grabAngle = Mathf.Atan2(_grabbedB, _grabbedA) * Mathf.Rad2Deg;
        float delta = Mathf.DeltaAngle(grabAngle, mouseAngle);

        float desiredAngle = _angleAtGrab + delta;
        _targetAngle = Mathf.Clamp(desiredAngle, minAngle, maxAngle);


        // FreeSlide: apply immediately with no animation lag.
        if (leverMode == LeverMode.FreeSlide)
        {
            CurrentAngle = _targetAngle;
            onLeverChanged?.Invoke(NormalizedValue);
        }
        Debug.Log($"Rotate from {CurrentAngle - delta} by {delta} degrees to {CurrentAngle}");
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    private void AnimateToTarget()
    {
        // FreeSlide while held: already set in OnInteractionDrag, nothing to do.
        if (leverMode == LeverMode.FreeSlide && IsHeld) return;

        if (snapSpeed <= 0f)
        {
            CurrentAngle = _targetAngle;
            return;
        }

        float prev = CurrentAngle;
        CurrentAngle = Mathf.MoveTowards(CurrentAngle, _targetAngle, snapSpeed * Time.deltaTime);

        if (!Mathf.Approximately(CurrentAngle, prev))
            onLeverChanged?.Invoke(NormalizedValue);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private float NearestSnapAngle(float angle)
    {
        float best = minAngle;
        float bestDist = Mathf.Abs(angle - minAngle);

        for (int i = 0; i < snapPositions; i++)
        {
            float candidate = Mathf.Lerp(minAngle, maxAngle, (float)i / (snapPositions - 1));
            float dist = Mathf.Abs(angle - candidate);
            if (dist < bestDist) { bestDist = dist; best = candidate; }
        }
        return best;
    }

    private void FireEdgeEvents()
    {
        bool atMin = Mathf.Approximately(CurrentAngle, minAngle);
        bool atMax = Mathf.Approximately(CurrentAngle, maxAngle);

        if (atMin && !_atMinLast) onMinReached?.Invoke();
        if (atMax && !_atMaxLast) onMaxReached?.Invoke();

        _atMinLast = atMin;
        _atMaxLast = atMax;
    }

    private void ApplyVisualRotation()
    {
        leverMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, pivotAxis);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>Drive the lever to a normalised position [0,1] from code.</summary>
    public void SetValue(float normalizedValue)
    {
        float angle = Mathf.Lerp(minAngle, maxAngle, Mathf.Clamp01(normalizedValue));
        _targetAngle = leverMode == LeverMode.Snap ? NearestSnapAngle(angle) : angle;
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (leverMesh == null) return;

        // Pivot axis (rotation axis) — yellow bar through the pivot point
        Gizmos.color = Color.yellow;
        Vector3 worldPivot = leverMesh.TransformDirection(pivotAxis).normalized;
        Gizmos.DrawLine(leverMesh.position - worldPivot * 0.2f,
                        leverMesh.position + worldPivot * 0.2f);

        // Drag plane axis — cyan line showing the direction the grab point travels
        Gizmos.color = Color.cyan;
        Vector3 worldDrag = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Gizmos.DrawLine(leverMesh.position, leverMesh.position + worldDrag * grabSampleRadius);
        Gizmos.DrawSphere(leverMesh.position + worldDrag * grabSampleRadius, 0.015f);

        // Dead-zone sphere
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(leverMesh.position, minGrabRadius);
    }
}