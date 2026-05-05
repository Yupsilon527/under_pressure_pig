using UnityEngine;
using UnityEngine.Events;

public class LeverInteraction : DragInteractable
{
    public enum LeverMode { FreeSlide, Snap, Toggle }

    public float sensitivity = 10;

    public Vector3 dragPlaneAxis = Vector3.up;


    public float grabSampleRadius = 0.2f;


    public LeverMode leverMode = LeverMode.FreeSlide;

    [Min(2)] public int snapPositions = 3;


    public float NormalizedValue => Mathf.InverseLerp(-maxRotation, maxRotation, CurrentAngle);

    private bool _atMinLast;
    private bool _atMaxLast;

    private float _grabbedA;      // projection onto dragPlaneAxis at grab time
    private float _grabbedB;      // projection onto the perpendicular axis at grab time

    private void Awake()
    {
        if (objectMesh == null)
            objectMesh = transform;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning("[LeverInteraction] No Collider — add one for raycast detection.", this);

        CurrentAngle = Mathf.Clamp(startRotation, -maxRotation, maxRotation);
        _targetAngle = CurrentAngle;
        ApplyVisualRotation();
    }

    private void FixedUpdate()
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
            _targetAngle = Mathf.Approximately(_targetAngle, maxRotation) ? -maxRotation : maxRotation;
            onValueChanged?.Invoke(NormalizedValue);
            return;
        }

        Vector3 worldPivotAxis = objectMesh.TransformDirection(pivotAxis).normalized;
        Vector3 worldAxisA = objectMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(worldPivotAxis, worldAxisA).normalized;
        Vector3 offset = point - objectMesh.position;
        _grabbedA = Vector3.Dot(offset, worldAxisA);
        _grabbedB = Vector3.Dot(offset, worldAxisB);

        if (new Vector2(_grabbedA, _grabbedB).magnitude < minGrabRadius)
        {
            _grabbedA = grabSampleRadius;
            _grabbedB = 0f;
        }

        _angleAtGrab = CurrentAngle;
        _targetAngle = CurrentAngle;
    }

    public override void OnInteractionDrag(Ray ray)
    {
        base.OnInteractionDrag(ray);

        if (!IsHeld || leverMode == LeverMode.Toggle) return;

        Vector3 planeNormal = objectMesh.TransformDirection(pivotAxis).normalized;
        Vector3 planeOrigin = objectMesh.position;

        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 1e-5f) return; 

        float t = Vector3.Dot(planeOrigin - ray.origin, planeNormal) / denom;
        if (t < 0f) return; 

        Vector3 mouseWorldPoint = ray.origin + ray.direction * t;

        Vector3 worldAxisA = objectMesh.TransformDirection(dragPlaneAxis).normalized;
        Vector3 worldAxisB = Vector3.Cross(planeNormal, worldAxisA).normalized; 

        Vector3 hitOffset = mouseWorldPoint - objectMesh.position;
        float mouseA = Vector3.Dot(hitOffset, worldAxisA);
        float mouseB = Vector3.Dot(hitOffset, worldAxisB);

        if (new Vector2(mouseA, mouseB).magnitude < minGrabRadius) return;

        float mouseAngle = Mathf.Atan2(mouseB, mouseA) * Mathf.Rad2Deg;
        float grabAngle = Mathf.Atan2(_grabbedB, _grabbedA) * Mathf.Rad2Deg;
        float delta = Mathf.DeltaAngle(grabAngle, mouseAngle) * sensitivity;

        float desiredAngle = _angleAtGrab + delta;
        _targetAngle = Mathf.Clamp(desiredAngle, -maxRotation, maxRotation);


        if (leverMode == LeverMode.FreeSlide)
        {
            CurrentAngle = _targetAngle;
            onValueChanged?.Invoke(NormalizedValue);
        }
        Debug.Log($"Rotate from {CurrentAngle - delta} by {delta} degrees to {CurrentAngle}");
    }

    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        if (leverMode == LeverMode.Snap)
            _targetAngle = NearestSnapAngle(CurrentAngle);
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
            onValueChanged?.Invoke(NormalizedValue);
    }

    private float NearestSnapAngle(float angle)
    {
        float best = -maxRotation;
        float bestDist = Mathf.Abs(angle - -maxRotation);

        for (int i = 0; i < snapPositions; i++)
        {
            float candidate = Mathf.Lerp(-maxRotation, maxRotation, (float)i / (snapPositions - 1));
            float dist = Mathf.Abs(angle - candidate);
            if (dist < bestDist) { bestDist = dist; best = candidate; }
        }
        return best;
    }

    private void FireEdgeEvents()
    {
        bool atMin = Mathf.Approximately(CurrentAngle, -maxRotation);
        bool atMax = Mathf.Approximately(CurrentAngle, maxRotation);

        if (atMin && !_atMinLast) onMinReached?.Invoke();
        if (atMax && !_atMaxLast) onMaxReached?.Invoke();

        _atMinLast = atMin;
        _atMaxLast = atMax;
    }

    public override void ApplyVisualRotation()
    {
        objectMesh.localRotation = Quaternion.AngleAxis(CurrentAngle, pivotAxis);
    }

    public void SetValue(float normalizedValue)
    {
        float angle = Mathf.Lerp(-maxRotation, maxRotation, Mathf.Clamp01(normalizedValue));
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
        if (objectMesh == null) return;

        Gizmos.color = Color.yellow;
        Vector3 worldPivot = objectMesh.TransformDirection(pivotAxis).normalized;
        Gizmos.DrawLine(objectMesh.position - worldPivot * 0.2f,
                        objectMesh.position + worldPivot * 0.2f);

        Gizmos.color = Color.cyan;
        Vector3 worldDrag = objectMesh.TransformDirection(dragPlaneAxis).normalized;
        Gizmos.DrawLine(objectMesh.position, objectMesh.position + worldDrag * grabSampleRadius);
        Gizmos.DrawSphere(objectMesh.position + worldDrag * grabSampleRadius, 0.015f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(objectMesh.position, minGrabRadius);
    }
}
