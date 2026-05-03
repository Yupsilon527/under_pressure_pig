using UnityEngine;
using UnityEngine.Events;

public class LeverInteraction : PlayerPovInteractable
{
    public enum LeverMode { FreeSlide, Snap, Toggle }

    public Transform leverMesh;

    public Vector3 pivotAxis = Vector3.right;

    public Vector3 dragPlaneAxis = Vector3.up;

    public float minAngle = -45f;

    public float maxAngle = 45f;

    public float startAngle = 0f;

    public float grabSampleRadius = 0.2f;

    public float minGrabRadius = 0.02f;

    public LeverMode leverMode = LeverMode.FreeSlide;

    [Min(2)] public int snapPositions = 3;

    public float snapSpeed = 180f;

    public UnityEvent<float> onLeverChanged;
    public UnityEvent onMinReached;
    public UnityEvent onMaxReached;

    public float CurrentAngle { get; private set; }
    public float NormalizedValue => Mathf.InverseLerp(minAngle, maxAngle, CurrentAngle);
    public bool IsHeld { get; private set; }

    private float _targetAngle;
    private bool _atMinLast;
    private bool _atMaxLast;

    private float _grabbedA;      // projection onto dragPlaneAxis at grab time
    private float _grabbedB;      // projection onto the perpendicular axis at grab time
    private float _angleAtGrab;

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

    public override void OnInteractionBegin(Vector3 point)
    {
        base.OnInteractionBegin(point);

        if (leverMode == LeverMode.Toggle)
        {
            _targetAngle = Mathf.Approximately(_targetAngle, maxAngle) ? minAngle : maxAngle;
            onLeverChanged?.Invoke(NormalizedValue);
            return;
        }

        Vector3 worldPivotAxis = leverMesh.TransformDirection(pivotAxis).normalized;
        Vector3 worldAxisA = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(worldPivotAxis, worldAxisA).normalized;
        Vector3 offset = point - leverMesh.position;
        _grabbedA = Vector3.Dot(offset, worldAxisA);
        _grabbedB = Vector3.Dot(offset, worldAxisB);

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

        Vector3 planeNormal = leverMesh.TransformDirection(pivotAxis).normalized;
        Vector3 planeOrigin = leverMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return; 

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return; 

        Vector3 mouseWorldPoint = ray.origin + ray.direction * t;

        Vector3 worldAxisA = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(planeNormal, worldAxisA).normalized; 

        Vector3 hitOffset = mouseWorldPoint - leverMesh.position;
        float mouseA = Vector3.Dot(hitOffset, worldAxisA);
        float mouseB = Vector3.Dot(hitOffset, worldAxisB);

        if (new Vector2(mouseA, mouseB).magnitude < minGrabRadius) return;

        float mouseAngle = Mathf.Atan2(mouseB, mouseA) * Mathf.Rad2Deg;
        float grabAngle = Mathf.Atan2(_grabbedB, _grabbedA) * Mathf.Rad2Deg;
        float delta = Mathf.DeltaAngle(grabAngle, mouseAngle);

        float desiredAngle = _angleAtGrab + delta;
        _targetAngle = Mathf.Clamp(desiredAngle, minAngle, maxAngle);


        if (leverMode == LeverMode.FreeSlide)
        {
            CurrentAngle = _targetAngle;
            onLeverChanged?.Invoke(NormalizedValue);
        }
        Debug.Log($"Rotate from {CurrentAngle - delta} by {delta} degrees to {CurrentAngle}");
    }

    private void AnimateToTarget()
    {
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

    public void SetValue(float normalizedValue)
    {
        float angle = Mathf.Lerp(minAngle, maxAngle, Mathf.Clamp01(normalizedValue));
        _targetAngle = leverMode == LeverMode.Snap ? NearestSnapAngle(angle) : angle;
    }

    public override float GetValueNormalizedFloat(float min, float max)
    {
        return Mathf.Lerp(min, max, NormalizedValue);
    }

    public override int GetValueNormalizedInt(int min, int max)
    {
        float f = GetValueNormalizedFloat(min, max);
        return Mathf.Clamp(Mathf.RoundToInt(f), min, max);
    }

    private void OnDrawGizmosSelected()
    {
        if (leverMesh == null) return;

        Gizmos.color = Color.yellow;
        Vector3 worldPivot = leverMesh.TransformDirection(pivotAxis).normalized;
        Gizmos.DrawLine(leverMesh.position - worldPivot * 0.2f,
                        leverMesh.position + worldPivot * 0.2f);

        Gizmos.color = Color.cyan;
        Vector3 worldDrag = leverMesh.TransformDirection(dragPlaneAxis).normalized;
        Gizmos.DrawLine(leverMesh.position, leverMesh.position + worldDrag * grabSampleRadius);
        Gizmos.DrawSphere(leverMesh.position + worldDrag * grabSampleRadius, 0.015f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(leverMesh.position, minGrabRadius);
    }
}
