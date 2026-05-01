using UnityEngine;

public abstract class PlayerPovInteractable : MonoBehaviour
{
    public virtual void OnInteractionBegin(Vector3 point)
    {
        Debug.Log($"Player interacts with {name}");
    }
    public virtual void OnInteractionDrag(Ray ray)
    {
        Debug.Log($"Player drags {name}");
    }
    public virtual void OnInteractionEnd()
    {
        Debug.Log($"Player no longer interacts with {name}");
    }
    public abstract float GetValueNormalizedFloat(float min, float max);
    public abstract int GetValueNormalizedInt(int min, int max);
}
