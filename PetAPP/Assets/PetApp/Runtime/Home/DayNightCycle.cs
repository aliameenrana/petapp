using PetApp.App;
using UnityEngine;
using UnityEngine.Rendering;

namespace PetApp.Home
{
    // t: 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.
    public sealed class DayNightCycle : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField] Camera cam;
        [SerializeField] ParticleSystem fireflies;
        [SerializeField] Volume postFX;
        [SerializeField] float dayLengthSeconds = 900f;
        [Tooltip("Sun rotation (pitch, yaw) when the app opens; the clock starts at the morning time that gives this pitch.")]
        [SerializeField] Vector2 startSun = new Vector2(50f, -30f);

        public float Night { get; private set; }

        static readonly Gradient SunColor = Make(
            (0.00f, new Color(0.35f, 0.45f, 0.9f)), (0.24f, new Color(1f, 0.45f, 0.25f)), (0.32f, new Color(1f, 0.85f, 0.65f)),
            (0.50f, new Color(1f, 0.97f, 0.9f)), (0.70f, new Color(1f, 0.8f, 0.55f)), (0.77f, new Color(1f, 0.4f, 0.2f)), (1.00f, new Color(0.35f, 0.45f, 0.9f)));
        static readonly Gradient Sky = Make(
            (0.00f, new Color(0.04f, 0.05f, 0.14f)), (0.23f, new Color(0.35f, 0.25f, 0.45f)), (0.28f, new Color(1f, 0.65f, 0.5f)),
            (0.40f, new Color(0.55f, 0.8f, 1f)), (0.65f, new Color(0.6f, 0.78f, 1f)), (0.76f, new Color(1f, 0.55f, 0.35f)), (0.82f, new Color(0.2f, 0.15f, 0.35f)), (1.00f, new Color(0.04f, 0.05f, 0.14f)));
        static readonly Gradient Ambient = Make(
            (0.00f, new Color(0.12f, 0.14f, 0.28f)), (0.27f, new Color(0.5f, 0.4f, 0.45f)), (0.45f, new Color(0.65f, 0.68f, 0.72f)),
            (0.72f, new Color(0.62f, 0.55f, 0.5f)), (0.80f, new Color(0.25f, 0.2f, 0.35f)), (1.00f, new Color(0.12f, 0.14f, 0.28f)));

        const float MinPitch = -10f;
        const float MaxPitch = 60f;

        float t;
        float yawOffset;

        void Awake()
        {
            // Inverts the pitch curve in Update, taking the morning root so the day brightens toward noon first.
            var height = Mathf.InverseLerp(MinPitch, MaxPitch, startSun.x);
            t = 0.25f + Mathf.Asin(height) / (Mathf.PI * 2f);
            yawOffset = startSun.y - t * 360f;
        }

        void Update()
        {
            // Mini-games share this scene's sun; hold a fixed front-lit noon so a game never plays at night or backlit.
            var inGame = AppRoot.Instance != null && AppRoot.Instance.InGame;
            RenderSettings.fog = !inGame;
            if (postFX != null) postFX.enabled = !inGame;
            if (inGame)
            {
                sun.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
                sun.color = new Color(1f, 0.97f, 0.9f);
                sun.intensity = 1.2f;
                RenderSettings.ambientLight = new Color(0.7f, 0.72f, 0.75f);
                return;
            }

            t = (t + Time.deltaTime / dayLengthSeconds) % 1f;
            var height = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
            Night = Mathf.Clamp01(-height * 3f + 0.3f);

            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(MinPitch, MaxPitch, Mathf.Clamp01(height)), t * 360f + yawOffset, 0f);
            sun.color = SunColor.Evaluate(t);
            sun.intensity = Mathf.Lerp(0.15f, 1.3f, Mathf.Clamp01(height * 2f + 0.2f));
            sun.shadowStrength = Mathf.Lerp(0f, 0.85f, Mathf.Clamp01(height * 4f));

            var sky = Sky.Evaluate(t);
            cam.backgroundColor = sky;
            RenderSettings.fogColor = sky;
            RenderSettings.ambientLight = Ambient.Evaluate(t);

            if (fireflies == null) return;
            var emission = fireflies.emission;
            emission.rateOverTime = Night * 12f;
        }

        static Gradient Make(params (float time, Color color)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (var i = 0; i < keys.Length; i++) colors[i] = new GradientColorKey(keys[i].color, keys[i].time);
            var g = new Gradient();
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
