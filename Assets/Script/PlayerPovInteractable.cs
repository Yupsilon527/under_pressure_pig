using UnityEngine;

public class PlayerPovInteractable : MonoBehaviour
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
}
