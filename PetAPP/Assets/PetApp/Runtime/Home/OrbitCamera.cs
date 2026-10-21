using PetApp.App;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UIElements;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace PetApp.Home
{
    // Always looks at the pet's centre. Touch: one finger orbits, two fingers pinch-zoom and twist.
    // Mouse: drag orbits, wheel zooms.
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Collider target;
        [SerializeField] Vector2 pitchLimits = new Vector2(2f, 60f);
        [SerializeField] Vector2 distanceLimits = new Vector2(2.5f, 12f);
        [SerializeField] float orbitDegreesPerScreen = 220f;
        [SerializeField] float smoothing = 12f;

        Vector3 pivot;
        float yaw, pitch, distance;
        float targetYaw, targetPitch, targetDistance;
        float startYaw, startPitch, startDistance;
        bool blocked;

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Start()
        {
            // Read once: the pet hops when tapped and the camera shouldn't hop with it.
            pivot = target.bounds.center;
            var offset = transform.position - pivot;
            distance = offset.magnitude;
            var look = Quaternion.LookRotation(-offset).eulerAngles;
            yaw = look.y;
            pitch = Mathf.DeltaAngle(0f, look.x);
            targetYaw = yaw;
            targetPitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
            targetDistance = Mathf.Clamp(distance, distanceLimits.x, distanceLimits.y);
            startYaw = targetYaw;
            startPitch = targetPitch;
            startDistance = targetDistance;
            AppRoot.Instance.Root.Q<Button>("home-focus").clicked += Recenter;
            Apply();
        }

        void LateUpdate()
        {
            var app = AppRoot.Instance;
            if (!app.InGame && app.CurrentTab == "home")
            {
                if (Touch.activeTouches.Count > 0) ReadTouches();
                else ReadMouse();
            }

            var t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            yaw = Mathf.Lerp(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);
            Apply();
        }

        void ReadTouches()
        {
            var touches = Touch.activeTouches;
            foreach (var touch in touches)
                if (touch.began && touches.Count == 1) blocked = AppRoot.Instance.IsPointerOverUI(touch.screenPosition);
            if (blocked) return;

            if (touches.Count == 1)
            {
                Orbit(touches[0].delta);
                return;
            }

            var a = touches[0];
            var b = touches[1];
            var previousA = a.screenPosition - a.delta;
            var previousB = b.screenPosition - b.delta;
            var spread = Vector2.Distance(a.screenPosition, b.screenPosition);
            var previousSpread = Vector2.Distance(previousA, previousB);
            if (spread > 1f && previousSpread > 1f) Zoom(previousSpread / spread);
            targetYaw -= Vector2.SignedAngle(previousB - previousA, b.screenPosition - a.screenPosition);
        }

        void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
                blocked = AppRoot.Instance.IsPointerOverUI(position);

            var scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && !AppRoot.Instance.IsPointerOverUI(position)) Zoom(Mathf.Pow(0.88f, scroll));
            if (blocked) return;

            if (mouse.leftButton.isPressed) Orbit(mouse.delta.ReadValue());
        }

        // Glides back through the smoothing; DeltaAngle stops it unwinding every turn the player made.
        public void Recenter()
        {
            targetYaw = yaw + Mathf.DeltaAngle(yaw, startYaw);
            targetPitch = startPitch;
            targetDistance = startDistance;
        }

        void Orbit(Vector2 delta)
        {
            var scale = orbitDegreesPerScreen / Screen.height;
            targetYaw += delta.x * scale;
            targetPitch = Mathf.Clamp(targetPitch - delta.y * scale, pitchLimits.x, pitchLimits.y);
        }

        void Zoom(float factor) =>
            targetDistance = Mathf.Clamp(targetDistance * factor, distanceLimits.x, distanceLimits.y);

        void Apply()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }
    }
}
