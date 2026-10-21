using UnityEngine;

namespace PetApp.MiniGameSdk
{
    // Packages ship no code, but their prefabs may use components from this SDK, which every host app includes (like Roblox's built-in instances).
    // Animates a child so it never fights the position the script sets on the root.
    public sealed class HostMotion : MonoBehaviour
    {
        [SerializeField] Vector3 spinDegreesPerSecond;
        [SerializeField] float bobHeight;
        [SerializeField] float bobSpeed = 3f;
        [SerializeField] float driftSpeed;
        [SerializeField] float driftWrap = 12f;

        Vector3 origin;
        float phase;

        void Awake()
        {
            origin = transform.localPosition;
            phase = Random.value * 10f;
        }

        void Update()
        {
            if (spinDegreesPerSecond != Vector3.zero) transform.Rotate(spinDegreesPerSecond * Time.deltaTime, Space.Self);
            var position = origin;
            if (bobHeight != 0f) position.y += Mathf.Sin((Time.time + phase) * bobSpeed) * bobHeight;
            if (driftSpeed != 0f) position.x = Mathf.Repeat(origin.x + driftSpeed * Time.time + driftWrap, driftWrap * 2f) - driftWrap;
            transform.localPosition = position;
        }
    }
}
