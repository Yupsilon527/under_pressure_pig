using UnityEngine;

/// <summary>
/// First-Person Steering Wheel Interaction — Grab-Point Tracking
///
/// Instead of mapping raw mouse delta to rotation, this version:
///   1. On click, records the exact point on the wheel rim that was hit (in wheel-local space).
///   2. Each frame, projects the camera ray onto the wheel's plane to find where the mouse
///      is now pointing on that plane.
///   3. Rotates the wheel so that the originally-grabbed point chases the current mouse point.
///
/// This feels like actually gripping the rim and pulling it — the wheel follows your hand.
///
/// Setup:
///   1. Attach to the steering wheel root GameObject (needs a Collider).
///   2. Assign 'wheelMesh'        — the child Transform that visually spins.
///   3. Assign 'playerCamera'     — your FP camera (or leave null for Camera.main).
///   4. Assign 'cameraLookScript' — your MouseLook component, paused while grabbing.
///   5. 'wheelNormal' should match the axis facing the player
///      (default Vector3.forward / Z — the wheel face points toward the driver).
/// </summary>
public class SteeringWheelInteraction : PlayerPovInteractable
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Child Transform that visually rotates (the rim/spoke mesh).")]
    public Transform wheelMesh;

    [Header("Wheel")]
    [Tooltip("Local axis that points toward the player (the wheel's face normal). " +
             "Default Vector3.forward works for a wheel whose +Z faces the driver.")]
    public Vector3 wheelNormal = Vector3.forward;

    [Tooltip("Maximum rotation in either direction (degrees). 450 = 1.25 full turns.")]
    public float maxRotation = 450f;

    [Tooltip("How fast the wheel returns to centre when released (deg/sec). 0 = no spring.")]
    public float returnSpeed = 90f;

    [Tooltip("Minimum distance from wheel centre a grab point must be (world units). " +
             "Prevents jitter when clicking very near the centre.")]
    public float minGrabRadius = 0.05f;

    // ── Public read-outs ──────────────────────────────────────────────────────
    /// <summary>Accumulated rotation in degrees. Positive = clockwise viewed from driver.</summary>
    public float CurrentAngle { get; private set; }
    public float DesiredAngle { get; private set; }

    /// <summary>Normalised steering in [-1, 1].</summary>
    public float NormalizedAngle => Mathf.Clamp(CurrentAngle / maxRotation, -1f, 1f);

    /// <summary>True while the player is holding the wheel.</summary>
    public bool IsHeld { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────
    private bool _lookingAtWheel;

    // Direction from wheel centre to the grabbed point, in the wheel's local 2-D space,
    // expressed BEFORE any rotation is applied (i.e. at CurrentAngle = 0).
    // We store this once at grab time; every frame we compute the signed angle between
    // this stored direction and where the mouse is now pointing, then add that to _angleAtGrab.
    private Vector2 _grabbedLocalDir;

    // CurrentAngle at the moment the player clicked. The per-frame delta is added on top.
    private float _angleAtGrab;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {

        if (wheelMesh == null)
            wheelMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[SteeringWheel] No Collider found — add one so the raycast can detect it.", this);
    }

    private void Update()
    {
        if (!IsHeld)
            HandleSpringReturn();

        ApplyVisualRotation();
    }

    // ── Hover / grab detection ────────────────────────────────────────────────

    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);
        TryStartGrab(point);
    }
    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        StopGrab();
    }

    // ── Held update ───────────────────────────────────────────────────────────
    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!RaycastWheelPlane(ray, out Vector3 mouseWorldPoint))
            return;

        Vector2 mouseLocalDir = WorldPointToWheelLocal(mouseWorldPoint);
        if (mouseLocalDir.magnitude < minGrabRadius)
            return;

        mouseLocalDir = mouseLocalDir.normalized;
        Vector2 mouseInRestFrame = Rotate2D(mouseLocalDir, -_angleAtGrab);
        float angleDelta = Vector2.SignedAngle(_grabbedLocalDir, mouseInRestFrame);
        CurrentAngle = Mathf.Clamp(CurrentAngle + angleDelta, -maxRotation, maxRotation);

        Debug.Log($"Rotate from {CurrentAngle - angleDelta} by {angleDelta} degrees to {CurrentAngle}");
    }

    // ── Grab start ────────────────────────────────────────────────────────────
    private void TryStartGrab(Vector3 hitWorldPoint)
    {
        // Convert the hit point to the wheel's 2-D plane, then UN-rotate it so that
        // _grabbedLocalDir is relative to angle = 0. This means we don't need to
        // subtract the current rotation each frame — we just compare angles directly.
        Vector2 rawLocalDir = WorldPointToWheelLocal(hitWorldPoint);

        if (rawLocalDir.magnitude < minGrabRadius)
        {
            Debug.Log("[SteeringWheel] Grab point too close to centre — ignored.");
            return;
        }

        // Un-rotate by CurrentAngle so the stored direction is in the "rest" frame.
        _grabbedLocalDir = Rotate2D(rawLocalDir.normalized, -CurrentAngle);
        _angleAtGrab = CurrentAngle;

        IsHeld = true;
    }

    private void StopGrab()
    {
        IsHeld = false;
    }

    // ── Spring return ─────────────────────────────────────────────────────────
    private void HandleSpringReturn()
    {
        if (returnSpeed <= 0f || Mathf.Approximately(CurrentAngle, 0f)) return;
        CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, returnSpeed * Time.deltaTime);
    }

    // ── Visual ────────────────────────────────────────────────────────────────
    private void ApplyVisualRotation()
    {
        wheelMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, wheelNormal);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Intersects a world-space ray with the wheel's face plane.
    /// Returns false if the ray is (nearly) parallel to the plane.
    /// </summary>
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

        if (wheelMesh != null)
        {
            // Show the face normal
            Gizmos.color = Color.yellow;
            Vector3 faceDir = wheelMesh.TransformDirection(wheelNormal).normalized;
            Gizmos.DrawLine(wheelMesh.position, wheelMesh.position + faceDir * 0.35f);
            Gizmos.DrawSphere(wheelMesh.position + faceDir * 0.35f, 0.02f);

            // Show minGrabRadius dead-zone
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(wheelMesh.position, minGrabRadius);
        }
    }
}
