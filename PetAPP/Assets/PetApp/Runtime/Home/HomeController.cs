using PetApp.App;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PetApp.Home
{
    public sealed class HomeController : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] PetJuice pet;
        [SerializeField] Collider petCollider;

        Vector2 pressPosition;
        bool tracking;

        void Update()
        {
            var app = AppRoot.Instance;
            if (app.InGame || app.CurrentTab != "home") { tracking = false; return; }
            var pointer = Pointer.current;
            if (pointer == null) return;

            var screen = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                tracking = !app.IsPointerOverUI(screen);
                pressPosition = screen;
            }
            if (!tracking || !pointer.press.wasReleasedThisFrame) return;
            tracking = false;
            // A drag is a camera gesture, not a tap.
            if ((screen - pressPosition).magnitude > Screen.height * 0.02f) return;
            if (petCollider.Raycast(cam.ScreenPointToRay(screen), out _, 100f)) pet.Tap();
        }
    }
}
