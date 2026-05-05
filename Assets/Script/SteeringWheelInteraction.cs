using UnityEngine;

public class SteeringWheelInteraction : DragInteractable
{
    public enum WheelMode { Drive, Valve, Free }

    public WheelMode wheelMode = WheelMode.Drive;
    public float valveTurns = 3f;

    public float NormalizedValue
    {
        get
        {
            switch (wheelMode)
            {
                case WheelMode.Drive: return Mathf.Clamp(CurrentAngle / maxRotation, -1f, 1f);
                case WheelMode.Valve: return Mathf.Clamp01(CurrentAngle / (valveTurns * 360f));
                default: return CurrentAngle;
            }
        }
    }

    // Stable world-space axes captured at grab time.
    // We CANNOT use objectMesh.right/up — they rotate with CurrentAngle every frame
    // via ApplyVisualRotation, corrupting the 2D projection and causing hyperspin.
    private Vector3 _planeRight;
    private Vector3 _planeUp;

    // Last frame's mouse direction in the stable plane frame.
    // We diff consecutive frames so rotation accumulates freely beyond ±180°.
    private Vector2 _lastMouseDir;

    private void Awake()
    {
        if (objectMesh == null)
            objectMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[SteeringWheel] No Collider — add one so the raycast can detect it.", this);
    }

    private void FixedUpdate()
    {
        if (!IsHeld && wheelMode == WheelMode.Drive)
            HandleSpringReturn();

        ApplyVisualRotation();
    }

    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);

        // Build stable plane axes from the PARENT transform so they don't spin with objectMesh.
        // If there's no parent, fall back to world space.
        Transform reference = objectMesh.parent != null ? objectMesh.parent : objectMesh;
        Vector3 normal = reference.TransformDirection(pivotAxis).normalized;

        _planeRight = Vector3.Cross(Vector3.up, normal).normalized;
        if (_planeRight.sqrMagnitude < 0.01f)           // degenerate when normal ≈ world up
            _planeRight = Vector3.Cross(Vector3.right, normal).normalized;
        _planeUp = Vector3.Cross(normal, _planeRight).normalized;

        // Record the initial mouse direction in the stable frame.
        Vector2 rawLocalDir = WorldPointToWheelLocal(point);
        if (rawLocalDir.magnitude < minGrabRadius)
        {
            Debug.Log("[SteeringWheel] Grab point too close to centre — ignored.");
            return;
        }

        _lastMouseDir = rawLocalDir.normalized;
    }

    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!IsHeld) return;
        if (!RaycastWheelPlane(ray, out Vector3 mouseWorldPoint)) return;

        Vector2 mouseLocalDir = WorldPointToWheelLocal(mouseWorldPoint);
        if (mouseLocalDir.magnitude < minGrabRadius) return;

        mouseLocalDir = mouseLocalDir.normalized;

        // Incremental signed angle from last frame to this frame.
        // SignedAngle returns [-180, 180] which is fine for per-frame deltas —
        // the mouse can't realistically move >180° in one frame.
        // We ACCUMULATE into CurrentAngle so there's no wrap limit.
        float delta = Vector2.SignedAngle(_lastMouseDir, mouseLocalDir);
        _lastMouseDir = mouseLocalDir;   // advance for next frame

        float prev = CurrentAngle;
        CurrentAngle = ApplyModeClamp(CurrentAngle + delta);

        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        // Nothing extra needed — _lastMouseDir is re-initialised on the next grab.
    }

    private float ApplyModeClamp(float angle)
    {
        switch (wheelMode)
        {
            case WheelMode.Drive: return Mathf.Clamp(angle, -maxRotation, maxRotation);
            case WheelMode.Valve: return Mathf.Clamp(angle, 0f, valveTurns * 360f);
            default: return angle;
        }
    }

    private void HandleSpringReturn()
    {
        if (snapSpeed <= 0f || Mathf.Approximately(CurrentAngle, 0f)) return;
        float prev = CurrentAngle;
        CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, snapSpeed * Time.deltaTime);
        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    public override void ApplyVisualRotation()
    {
        objectMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, pivotAxis);
    }

    public void SetNormalizedValue(float t)
    {
        t = Mathf.Clamp01(t);
        switch (wheelMode)
        {
            case WheelMode.Drive: CurrentAngle = Mathf.Lerp(-maxRotation, maxRotation, t); break;
            case WheelMode.Valve: CurrentAngle = t * valveTurns * 360f; break;
        }
        ApplyVisualRotation();
    }

    public void SetAngle(float degrees)
    {
        CurrentAngle = ApplyModeClamp(degrees);
        ApplyVisualRotation();
    }

    public override float GetValueNormalizedFloat(float min, float max)
    {
        switch (wheelMode)
        {
            case WheelMode.Drive: return Mathf.Lerp(min, max, NormalizedValue * 0.5f + 0.5f);
            case WheelMode.Valve: return Mathf.Lerp(min, max, NormalizedValue);
            default: return Mathf.Lerp(min, max, CurrentAngle);
        }
    }

    public override int GetValueNormalizedInt(int min, int max)
    {
        float f = GetValueNormalizedFloat(min, max);
        return Mathf.Clamp(Mathf.RoundToInt(f), min, max);
    }

    private bool RaycastWheelPlane(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        Vector3 planeNormal = Vector3.Cross(_planeRight, _planeUp).normalized;
        Vector3 planeOrigin = objectMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return false;

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return false;

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    private Vector2 WorldPointToWheelLocal(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - objectMesh.position;
        return new Vector2(
            Vector3.Dot(offset, _planeRight),
            Vector3.Dot(offset, _planeUp)
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (objectMesh == null) return;

        Gizmos.color = Color.yellow;
        Vector3 faceDir = objectMesh.TransformDirection(pivotAxis).normalized;
        Gizmos.DrawLine(objectMesh.position, objectMesh.position + faceDir * 0.35f);
        Gizmos.DrawSphere(objectMesh.position + faceDir * 0.35f, 0.02f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(objectMesh.position, minGrabRadius);
    }
}