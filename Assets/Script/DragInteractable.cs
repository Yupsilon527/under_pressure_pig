using UnityEngine;

public class DragInteractable : PlayerPovInteractable
{


    public float startRotation = 0f;
    public float maxRotation = 45f;

    public float snapSpeed = 90f;
    public float CurrentAngle { get; protected set; }

    public override void OnInteractionBegin(Vector3 point)
    {
        IsHeld = true;
        base.OnInteractionBegin(point);
    }
    public override void OnInteractionEnd()
    {
        base.OnInteractionEnd();
        IsHeld = false;

    }
    public override float GetValueNormalizedFloat(float min, float max)
    {
        throw new System.NotImplementedException();
    }

    public override int GetValueNormalizedInt(int min, int max)
    {
        throw new System.NotImplementedException();
    }
}
