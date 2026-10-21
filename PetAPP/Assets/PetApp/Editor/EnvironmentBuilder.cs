using System.IO;
using System.Linq;
using PetApp.Home;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PetApp.Editor
{
    public static partial class SceneSetup
    {
        const string Nature = Art + "/Nature";
        static readonly Vector3 CampfirePosition = new Vector3(2.2f, 0f, 1.6f);

        static readonly string[] Trees =
        {
            "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5",
            "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5", "TwistedTree_1", "TwistedTree_2", "TwistedTree_3"
        };
        static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        static readonly string[] Pebbles =
        {
            "Pebble_Round_1", "Pebble_Round_2", "Pebble_Round_3", "Pebble_Round_4", "Pebble_Round_5",
            "Pebble_Square_1", "Pebble_Square_2", "Pebble_Square_3", "Pebble_Square_4", "Pebble_Square_5", "Pebble_Square_6"
        };
        static readonly string[] GroundCover = { "Grass_Common_Short", "Grass_Wispy_Short", "Clover_1", "Clover_2" };
        static readonly string[] SmallPlants =
            { "Grass_Common_Tall", "Grass_Wispy_Tall", "Flower_3_Single", "Flower_4_Single", "Mushroom_Common", "Mushroom_Laetiporus" };
        static readonly string[] BigPlants =
            { "Bush_Common", "Bush_Common_Flowers", "Flower_3_Group", "Flower_4_Group", "Fern_1", "Plant_1", "Plant_7" };

        // FBX material name -> texture. The imported materials are opaque, so leaves render as white cards without these.
        static readonly (string name, string texture, bool cutout)[] NatureMaterialSpecs =
        {
            ("Bark_DeadTree", "Bark_DeadTree", false), ("Bark_NormalTree", "Bark_NormalTree", false),
            ("Bark_TwistedTree", "Bark_TwistedTree", false), ("Mushrooms", "Mushrooms", false),
            ("PathRocks", "PathRocks_Diffuse", false), ("Rocks", "Rocks_Diffuse", false),
            ("Flowers", "Flowers", true), ("Grass", "Grass", true), ("Leaves", "Leaves", true),
            // The plain leaf textures are white tint masks; the _C versions carry the colour.
            ("Leaves_NormalTree", "Leaves_NormalTree_C", true), ("Leaves_Pine", "Leaf_Pine_C", true),
            ("Leaves_TwistedTree", "Leaves_TwistedTree_C", true), ("Material", "Leaves_TwistedTree_C", true)
        };
        static System.Collections.Generic.Dictionary<string, Material> natureMaterials;

        static DayNightCycle BuildEnvironment(Light sun, Camera homeCam)
        {
            var root = new GameObject("Environment").transform;
            var random = new System.Random(7);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            string Pick(string[] list) => list[random.Next(list.Length)];

            var grass = Primitive(PrimitiveType.Cylinder, "Ground", new Vector3(60f, 0.1f, 60f),
                SaveMaterial($"{Generated}/Ground.mat", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.33f, 0.55f, 0.24f));
                    m.SetFloat("_Smoothness", 0.1f);
                }));
            grass.transform.SetParent(root);
            grass.transform.position = new Vector3(0f, -0.1f, 0f);
            var dirt = Primitive(PrimitiveType.Cylinder, "Clearing", new Vector3(4.5f, 0.02f, 4.5f),
                SaveMaterial($"{Generated}/Dirt.mat", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.45f, 0.34f, 0.22f));
                    m.SetFloat("_Smoothness", 0f);
                }));
            dirt.transform.SetParent(root);
            dirt.transform.position = CampfirePosition + new Vector3(-1f, -0.01f, -0.6f);
            // A huge flat caster shadows itself in stripes (acne) when the sun is low; the ground only needs to receive.
            foreach (var r in new[] { grass, dirt }) r.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            natureMaterials = BuildNatureMaterials();
            var layout = LoadLayout();
            if (layout != null) PlaceProps(layout, root);

            bool NearCampfire(Vector3 p, float margin) => (p - CampfirePosition).magnitude < 1.2f + margin;
            // Anything taller than grass stays out of the camera's view of the pet.
            bool InView(Vector3 p, float margin) => p.z < -1.5f && Mathf.Abs(p.x) < 3.5f + margin + p.z * -0.4f;
            bool Tall(Vector3 p, float margin) => NearCampfire(p, margin) || InView(p, margin);
            bool Low(Vector3 p, float margin) => p.magnitude < 1.3f || NearCampfire(p, 0f);

            void Scatter(string[] models, int count, float minRadius, float maxRadius, float minScale, float maxScale,
                float margin, System.Func<Vector3, float, bool> blocked)
            {
                for (var placed = 0; placed < count;)
                {
                    var angle = Range(0f, Mathf.PI * 2f);
                    var p = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Range(minRadius, maxRadius);
                    if (blocked(p, margin)) continue;
                    var model = Pick(models);
                    var scale = Range(minScale, maxScale);
                    var yaw = Range(0f, 360f);
                    // With a saved layout the props come from it, but the random sequence still runs so the campfire builds identically.
                    if (layout == null) Place(model, p, scale, yaw, root);
                    placed++;
                }
            }

            Scatter(Trees, 30, 8f, 24f, 0.9f, 1.4f, 2f, Tall);
            Scatter(BigPlants, 40, 6.5f, 14f, 0.8f, 1.3f, 0.5f, Tall);
            Scatter(Rocks, 8, 5f, 12f, 0.6f, 1.2f, 0.8f, Tall);
            Scatter(SmallPlants, 30, 3.5f, 10f, 0.7f, 1.1f, 0f, Tall);
            Scatter(Pebbles, 16, 1.6f, 5f, 0.6f, 1.2f, 0f, Low);
            Scatter(GroundCover, 260, 1.3f, 16f, 0.8f, 1.4f, 0f, Low);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 40f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            sun.shadows = LightShadows.Soft;

            var campfire = BuildCampfire(root, random);
            var fireflies = BuildFireflies(root);

            var cycle = root.gameObject.AddComponent<DayNightCycle>();
            Set(cycle, "sun", sun);
            Set(cycle, "cam", homeCam);
            Set(cycle, "fireflies", fireflies);
            Set(campfire.GetComponent<FireFlicker>(), "cycle", cycle);

            Set(cycle, "postFX", BuildPostProcessing(root));
            var cameraData = homeCam.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.stopNaN = true;
            return cycle;
        }

        static GameObject Place(string model, Vector3 position, float scale, float rotationY, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Nature}/{model}.fbx");
            if (asset == null)
            {
                Debug.LogWarning($"Nature model missing: {model}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
            go.transform.localScale *= scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials
                    .Select(m => m != null && natureMaterials.TryGetValue(m.name, out var replacement) ? replacement : m)
                    .ToArray();
            return go;
        }

        static System.Collections.Generic.Dictionary<string, Material> BuildNatureMaterials()
        {
            var result = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var (name, texture, cutout) in NatureMaterialSpecs)
            {
                var basePath = $"{Nature}/Textures/{texture}.png";
                var normalPath = $"{Nature}/Textures/{texture}_Normal.png";
                ConfigureTexture(basePath, cutout, false);
                var hasNormal = File.Exists(normalPath);
                if (hasNormal) ConfigureTexture(normalPath, false, true);

                result[name] = SaveMaterial($"{Generated}/Nature_{name}.mat", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Smoothness", 0.05f);
                    m.enableInstancing = true;
                    if (hasNormal)
                    {
                        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                        m.EnableKeyword("_NORMALMAP");
                    }
                    m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetFloat("_Cull", cutout ? 0f : 2f);
                    if (cutout) m.EnableKeyword("_ALPHATEST_ON");
                    else m.DisableKeyword("_ALPHATEST_ON");
                    m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                    m.renderQueue = (int)(cutout ? RenderQueue.AlphaTest : RenderQueue.Geometry);
                });
            }
            return result;
        }

        static void ConfigureTexture(string path, bool cutout, bool normal)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            // Bleeds leaf colour into the clear pixels so mipmaps don't fringe white, and keeps leaves from thinning out at a distance.
            importer.alphaIsTransparency = cutout;
            importer.mipMapsPreserveCoverage = cutout;
            importer.alphaTestReferenceValue = 0.5f;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        static GameObject BuildCampfire(Transform parent, System.Random random)
        {
            var fire = new GameObject("Campfire");
            fire.transform.SetParent(parent);
            fire.transform.position = CampfirePosition;

            for (var i = 0; i < 9; i++)
            {
                var a = i / 9f * Mathf.PI * 2f;
                Place($"Pebble_Round_{1 + random.Next(5)}", CampfirePosition + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.6f, 1.6f, i * 40f, fire.transform);
            }

            var bark = SaveMaterial($"{Generated}/Logs.mat", "Universal Render Pipeline/Lit", m =>
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Nature}/Textures/Bark_NormalTree.png")));
            for (var i = 0; i < 5; i++)
            {
                var log = Primitive(PrimitiveType.Cylinder, "Log", new Vector3(0.12f, 0.35f, 0.12f), bark);
                log.transform.SetParent(fire.transform, false);
                log.transform.localRotation = Quaternion.Euler(0f, i * 72f, 0f) * Quaternion.Euler(55f, 0f, 0f);
                log.transform.localPosition = log.transform.localRotation * new Vector3(0f, 0.25f, 0f) + new Vector3(0f, 0.05f, 0f);
            }

            var flame = Particles("Flame", fire.transform, new Vector3(0f, 0.15f, 0f), "circle_05", additive: true);
            Configure(flame, lifetime: (0.5f, 0.9f), speed: (0.6f, 1.3f), size: (0.35f, 0.7f), rate: 35f, worldSpace: false, gravity: -0.1f,
                gradient: Fade(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.45f, 0.1f), new Color(0.8f, 0.1f, 0.05f)), shrink: true, coneRadius: 0.18f, coneAngle: 8f);

            var embers = Particles("Embers", fire.transform, new Vector3(0f, 0.3f, 0f), "star_06", additive: true);
            Configure(embers, lifetime: (1.8f, 3.5f), speed: (0.8f, 1.8f), size: (0.03f, 0.07f), rate: 9f, worldSpace: true, gravity: -0.05f,
                gradient: Fade(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.5f, 0.15f), new Color(1f, 0.3f, 0.1f)), shrink: true, coneRadius: 0.2f, coneAngle: 20f);
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;

            var smoke = Particles("Smoke", fire.transform, new Vector3(0f, 0.7f, 0f), "circle_05", additive: false);
            Configure(smoke, lifetime: (3f, 4.5f), speed: (0.3f, 0.6f), size: (0.5f, 0.9f), rate: 4f, worldSpace: true, gravity: -0.02f,
                gradient: Fade(new Color(0.5f, 0.5f, 0.5f, 0.25f), new Color(0.6f, 0.6f, 0.6f, 0.15f), new Color(0.7f, 0.7f, 0.7f, 0f)), shrink: false, coneRadius: 0.15f, coneAngle: 12f);
            var grow = smoke.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2f));

            var light = new GameObject("FireLight").AddComponent<Light>();
            light.transform.SetParent(fire.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 7f;
            var flicker = fire.AddComponent<FireFlicker>();
            Set(flicker, "fireLight", light);

            var audio = fire.AddComponent<AudioSource>();
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Art}/Audio/fire.wav");
            audio.loop = true;
            audio.playOnAwake = true;
            audio.spatialBlend = 1f;
            audio.minDistance = 2f;
            audio.maxDistance = 15f;
            audio.volume = 0.7f;
            return fire;
        }

        static ParticleSystem BuildFireflies(Transform parent)
        {
            var ps = Particles("Fireflies", parent, new Vector3(0f, 1.2f, 3f), "star_06", additive: true);
            Configure(ps, lifetime: (4f, 7f), speed: (0.05f, 0.25f), size: (0.05f, 0.1f), rate: 0f, worldSpace: true, gravity: 0f,
                gradient: Pulse(new Color(0.85f, 1f, 0.45f)), shrink: false, coneRadius: 0f, coneAngle: 0f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14f, 2.5f, 10f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            var main = ps.main;
            main.maxParticles = 80;
            return ps;
        }

        static ParticleSystem Particles(string name, Transform parent, Vector3 localPosition, string texture, bool additive)
        {
            var material = SaveMaterial($"{Generated}/{name}.mat", "Universal Render Pipeline/Particles/Unlit", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/Particles/{texture}.png"));
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            });
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        static void Configure(ParticleSystem ps, (float, float) lifetime, (float, float) speed, (float, float) size, float rate, bool worldSpace,
            float gravity, Gradient gradient, bool shrink, float coneRadius, float coneAngle)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.Item1, lifetime.Item2);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Item1, speed.Item2);
            main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = Mathf.Max(0.01f, coneRadius);
            shape.angle = coneAngle;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
            if (shrink)
            {
                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            }
        }

        static Gradient Fade(Color start, Color middle, Color end)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(middle, 0.4f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(start.a, 0.1f), new GradientAlphaKey(middle.a, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Gradient Pulse(Color color)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.2f, 0.45f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Volume BuildPostProcessing(Transform parent)
        {
            var path = $"{Generated}/PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(18f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            AssetDatabase.SaveAssets();

            var volume = new GameObject("PostFX").AddComponent<Volume>();
            volume.transform.SetParent(parent);
            // Local to the Home area: the mini-game stage sits at x=1000 and should never get Home's bloom, Scene view included.
            volume.isGlobal = false;
            var bounds = volume.gameObject.AddComponent<BoxCollider>();
            bounds.isTrigger = true;
            bounds.center = new Vector3(0f, 10f, 0f);
            bounds.size = new Vector3(160f, 60f, 160f);
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            return volume;
        }
    }
}
