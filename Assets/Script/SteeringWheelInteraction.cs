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
    public enum WheelMode { Drive, Valve, Free }

    public Transform wheelMesh;

    public Vector3 wheelNormal = Vector3.forward;

    public float minGrabRadius = 0.05f;

    public WheelMode wheelMode = WheelMode.Drive;

    public float maxRotation = 450f;

    public float returnSpeed = 90f;

    public float valveTurns = 3f;

    public UnityEvent<float> onValueChanged;

   
    public float CurrentAngle { get; private set; }

    
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

    private Vector2 _grabbedLocalDir;

    private float _angleAtGrab;

    private void Awake()
    {
        if (wheelMesh == null)
            wheelMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[SteeringWheel] No Collider — add one so the raycast can detect it.", this);
    }

    private void Update()
    {
        if (!IsHeld && wheelMode == WheelMode.Drive)
            HandleSpringReturn();

        ApplyVisualRotation();
    }

    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);

        Vector2 rawLocalDir = WorldPointToWheelLocal(point);

        if (rawLocalDir.magnitude < minGrabRadius)
        {
            Debug.Log("[SteeringWheel] Grab point too close to centre — ignored.");
            return;
        }

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

        Vector2 mouseInRestFrame = Rotate2D(mouseLocalDir, -_angleAtGrab);
        float delta = Vector2.SignedAngle(_grabbedLocalDir, mouseInRestFrame);
        float desired = _angleAtGrab - delta;

        float prev = CurrentAngle;
        CurrentAngle = ApplyModeClamp(desired);

        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    private float ApplyModeClamp(float angle)
    {
        switch (wheelMode)
        {
            case WheelMode.Drive: return Mathf.Clamp(angle, -maxRotation, maxRotation);
            case WheelMode.Valve: return Mathf.Clamp(angle, 0f, valveTurns * 360f);
            default: return angle; // Free — no clamp
        }
    }

    private void HandleSpringReturn()
    {
        if (returnSpeed <= 0f || Mathf.Approximately(CurrentAngle, 0f)) return;
        float prev = CurrentAngle;
        CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, returnSpeed * Time.deltaTime);
        if (!Mathf.Approximately(CurrentAngle, prev))
            onValueChanged?.Invoke(NormalizedValue);
    }

    private void ApplyVisualRotation()
    {
        wheelMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, wheelNormal);
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

        Vector3 planeNormal = wheelMesh.TransformDirection(wheelNormal).normalized;
        Vector3 planeOrigin = wheelMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return false; // Parallel — no intersection.

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return false; // Plane is behind the camera.

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    private Vector2 WorldPointToWheelLocal(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - wheelMesh.position;
        return new Vector2(
            Vector3.Dot(offset, wheelMesh.right),
            Vector3.Dot(offset, wheelMesh.up)
        );
    }

    private static Vector2 Rotate2D(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(cos * v.x - sin * v.y,
                           sin * v.x + cos * v.y);
    }

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
