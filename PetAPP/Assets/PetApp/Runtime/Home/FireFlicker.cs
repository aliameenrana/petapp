using UnityEngine;

namespace PetApp.Home
{
    public sealed class FireFlicker : MonoBehaviour
    {
        [SerializeField] Light fireLight;
        [SerializeField] DayNightCycle cycle;
        [SerializeField] float dayIntensity = 1.2f;
        [SerializeField] float nightIntensity = 4f;

        float seed;

        void Awake() => seed = Random.value * 100f;

        void Update()
        {
            var noise = Mathf.PerlinNoise(seed, Time.time * 6f) * 0.6f + Mathf.PerlinNoise(seed + 7f, Time.time * 17f) * 0.4f;
            var baseIntensity = Mathf.Lerp(dayIntensity, nightIntensity, cycle.Night);
            fireLight.intensity = baseIntensity * (0.75f + noise * 0.5f);
        }
    }
}
