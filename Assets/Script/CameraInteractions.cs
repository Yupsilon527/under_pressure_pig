using UnityEngine;
using UnityEngine.InputSystem;

public class CameraInteractions : MonoBehaviour
{

    public float interactDistance = 2.5f;
    public Camera camera;
    bool fireRay = false;
    PlayerPovInteractable interactable;
#if ENABLE_INPUT_SYSTEM
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            fireRay=true;
        }
        else if (context.canceled)
        {
            fireRay = false;
        }
    }
#endif
    void FixedUpdate()
    {
        if (fireRay && interactable == null)
            FindInteractable();
        else if (fireRay && interactable != null)
            Drag();
        else if (!fireRay && interactable != null)
            Release();
    }
    public void FindInteractable()
    {

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);

        foreach (var hit in Physics.RaycastAll(ray, interactDistance))
        {
            if (hit.collider.TryGetComponent(out PlayerPovInteractable i))
            {
                interactable = i;
                i.OnInteractionBegin(hit.point);

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
    private void Drag()
    {
        interactable.OnInteractionDrag(camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f)));
    }
    public void Release()
    {
        interactable?.OnInteractionEnd();
        interactable = null;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(camera.transform.position,
                        camera.transform.position + camera.transform.forward * interactDistance);
    }
}
