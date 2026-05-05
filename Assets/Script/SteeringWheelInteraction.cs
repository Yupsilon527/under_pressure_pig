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
                default: return CurrentAngle; // Free — caller decides meaning
            }
        }
    }

    private Vector2 _grabbedLocalDir;


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

    public bool Asd(Vector3 point, bool begin)
    {
        Vector2 localDir = WorldPointToWheelLocal(point);

        if (localDir.magnitude < minGrabRadius)
        {
            Debug.Log("[SteeringWheel] Grab point too close to centre — ignored.");
            if (begin)
            {
                _grabbedLocalDir = Rotate2D(localDir.normalized, -CurrentAngle);
                _angleAtGrab = CurrentAngle;
            }
            else
            {
                localDir = localDir.normalized;
                Vector2 mouseInRestFrame = Rotate2D(localDir, -_angleAtGrab);
                float delta = Vector2.SignedAngle(_grabbedLocalDir, mouseInRestFrame);
                float desired = _angleAtGrab - delta;
                SetAngle(ApplyModeClamp(desired));
            }
            return true;
        }
        return false;
    }

    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);
        Asd(point, true);
    }

    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!IsHeld) return;

        if (!RaycastWheelPlane(ray, out Vector3 mouseWorldPoint))
            return;

        float prev = CurrentAngle;
        if (Asd(mouseWorldPoint, false))
        {
            if (!Mathf.Approximately(CurrentAngle, prev))
                onValueChanged?.Invoke(NormalizedValue);
        }
    }
    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        _angleAtGrab = 0;
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

        Vector3 planeNormal = objectMesh.TransformDirection(pivotAxis).normalized;
        Vector3 planeOrigin = objectMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return false; // Parallel — no intersection.

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return false; // Plane is behind the camera.

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    private Vector2 WorldPointToWheelLocal(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - objectMesh.position;
        return new Vector2(
            Vector3.Dot(offset, objectMesh.right),
            Vector3.Dot(offset, objectMesh.up)
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
        if (objectMesh == null) return;

        Gizmos.color = Color.yellow;
        Vector3 faceDir = objectMesh.TransformDirection(pivotAxis).normalized;
        Gizmos.DrawLine(objectMesh.position, objectMesh.position + faceDir * 0.35f);
        Gizmos.DrawSphere(objectMesh.position + faceDir * 0.35f, 0.02f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(objectMesh.position, minGrabRadius);
    }
}
