using System.IO;
using System.Linq;
using PetApp.App;
using PetApp.Feed;
using PetApp.Home;
using PetApp.MiniGames;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PetApp.Editor
{
    public static partial class SceneSetup
    {
        const string PetName = "Dog";
        const string Art = "Assets/PetApp/Art";
        const string Generated = "Assets/PetApp/Generated";
        const string UI = "Assets/PetApp/UI";
        const string ScenePath = "Assets/Scenes/App.unity";
        const string PanelSettingsPath = Generated + "/PanelSettings.asset";
        static readonly Vector3 GameStageOrigin = new Vector3(1000f, 0f, 0f);

        [MenuItem("Tools/Set scene up")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/PetApp/Resources/PetApp");

            var petPrefab = BuildPetPrefab();
            var panelSettings = BuildPanelSettings();
            WriteAppConfig();

            BuildScene(petPrefab, panelSettings);
            ConfigurePlayer();

            Debug.Log("Scene set up. Press Play.");
        }

        static void WriteAppConfig()
        {
            const string path = "Assets/PetApp/Resources/PetApp/AppConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<AppConfig>(path) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AppConfig>(), path);
        }

        static GameObject BuildPetPrefab()
        {
            var fbx = $"{Art}/Pet2/CartoonPets.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                var n = c.name.ToLowerInvariant();
                c.loopTime = n.Contains("idle") || n.Contains("walk") || n.Contains("run") || n.Contains("dance");
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Pick(params string[] keys) =>
                keys.Select(k => all.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(k))).FirstOrDefault(c => c != null);
            var idle = Pick("idle") ?? all.FirstOrDefault();
            var jump = Pick("jump") ?? idle;
            var happy = Pick("yes", "dance", "wave", "jump") ?? idle;
            var sad = Pick("no", "hitrec", "hit", "death") ?? idle;
            Debug.Log($"Pet clips: {string.Join(", ", all.Select(c => c.name))} → idle={idle?.name}, jump={jump?.name}, happy={happy?.name}, sad={sad?.name}");

            var controllerPath = $"{Generated}/Pet.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idle;
            sm.defaultState = idleState;
            foreach (var (trigger, clip) in new[] { ("Jump", jump), ("Happy", happy), ("Sad", sad) })
            {
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                var state = sm.AddState(trigger);
                state.motion = clip;
                var enter = sm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
                enter.duration = 0.05f;
                enter.canTransitionToSelf = true;
                var back = state.AddTransition(idleState);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
            }

            var material = SaveMaterial($"{Generated}/Pet.mat", "Universal Render Pipeline/Lit", m =>
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/Pet2/T_CartoonAnimal_{PetName}.png")));

            var root = new GameObject("Pet");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            // The FBX holds both animals plus a stray display cylinder; hide all but our animal so they don't skew its bounds.
            var other = PetName == "Dog" ? "Cat" : "Dog";
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (t != body.transform && (t.name.Contains(other) || t.name.StartsWith("pCylinder"))) t.gameObject.SetActive(false);
            foreach (var r in body.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            // Not ??: Unity's missing-component placeholder is non-null to C#, so it never falls through.
            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var bounds = Bounds(body);
            body.transform.localScale = Vector3.one * (1.5f / Mathf.Max(0.01f, bounds.size.y));
            bounds = Bounds(body);
            body.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = Bounds(body);
            root.AddComponent<PetIdle>();
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center - root.transform.position;
            box.size = bounds.size;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Generated}/Pet.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static PanelSettings BuildPanelSettings()
        {
            var themePath = $"{Generated}/RuntimeTheme.tss";
            File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(themePath);

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1170, 2532);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        }

        static void BuildScene(GameObject petPrefab, PanelSettings panelSettings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            // The cameras clear to a colour, so the default procedural sky is never seen; it only feeds reflections,
            // and its HDR sun disc reflected off smooth surfaces overflows to INF/NaN that bloom smears across the view.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.62f, 0.6f);

            var homeCam = MakeCamera("HomeCamera", new Vector3(0f, 2.5f, -5.5f), new Color(1f, 0.9f, 0.82f));
            homeCam.fieldOfView = 62f;
            homeCam.transform.rotation = Quaternion.Euler(7.253f, 0f, 0f);

            BuildEnvironment(light, homeCam);

            var pet = (GameObject)PrefabUtility.InstantiatePrefab(petPrefab);
            pet.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            // Home only: the mini-game shares the prefab and its collision sizes assume 1.5 units.
            pet.transform.localScale = Vector3.one * 1.4f;
            var petAudio = pet.AddComponent<AudioSource>();
            petAudio.playOnAwake = false;
            var burst = BuildBurst(pet.transform);
            var juice = pet.AddComponent<PetJuice>();
            Set(juice, "body", pet.transform.Find("Body"));
            Set(juice, "animator", pet.GetComponentInChildren<Animator>());
            Set(juice, "burst", burst);
            Set(juice, "audioSource", petAudio);
            Set(juice, "jumpClip", Clip("sfx_jump"));
            Set(juice, "landClip", Clip("sfx_land"));
            Set(juice, "comboClip", Clip("sfx_combo"));
            Set(juice, "cam", homeCam);
            Set(homeCam.gameObject.AddComponent<OrbitCamera>(), "target", pet.GetComponent<Collider>());

            var gameCam = MakeCamera("GameCamera", GameStageOrigin + new Vector3(0f, 0f, -10f), new Color(0.62f, 0.85f, 1f));
            gameCam.orthographic = true;
            gameCam.orthographicSize = 7f;
            var stage = new GameObject("GameStage").transform;
            stage.position = GameStageOrigin;

            var blankCam = MakeCamera("BlankCamera", new Vector3(0f, -100f, 0f), new Color(0.98f, 0.96f, 0.93f));
            blankCam.cullingMask = 0;

            // The UIDocument property setters don't reliably serialize in edit mode; write the fields directly.
            var ui = new GameObject("UI").AddComponent<UIDocument>();
            Set(ui, "m_PanelSettings", AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            Set(ui, "sourceAsset", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UI}/App.uxml"));
            if (new SerializedObject(ui).FindProperty("m_PanelSettings").objectReferenceValue == null)
                throw new System.Exception($"UIDocument lost its PanelSettings reference ({PanelSettingsPath})");
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var app = new GameObject("App");
            app.AddComponent<AudioListener>();
            var music = new GameObject("Music").AddComponent<AudioSource>();
            music.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Art}/Audio/music.ogg");
            music.loop = true;
            music.playOnAwake = true;
            music.volume = 0.35f;
            var appAudio = app.AddComponent<AudioSource>();
            appAudio.playOnAwake = false;

            var root = app.AddComponent<AppRoot>();
            Set(root, "document", ui);
            Set(root, "homeCamera", homeCam);
            Set(root, "gameCamera", gameCam);
            Set(root, "blankCamera", blankCam);

            var host = app.AddComponent<GameHost>();
            Set(host, "gameCamera", gameCam);
            Set(host, "stage", stage);
            Set(host, "petPrefab", petPrefab);
            Set(host, "audioSource", appAudio);
            Set(host, "rewardClip", Clip("sfx_reward"));
            var gameMusic = new GameObject("GameMusic").AddComponent<AudioSource>();
            gameMusic.loop = true;
            gameMusic.playOnAwake = false;
            Set(host, "homeMusic", music);
            Set(host, "gameMusic", gameMusic);

            var games = app.AddComponent<MiniGamesController>();
            Set(games, "cardTemplate", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UI}/GameCard.uxml"));
            Set(games, "host", host);

            var feed = app.AddComponent<FeedController>();
            Set(feed, "pageTemplate", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UI}/FeedPage.uxml"));

            var home = app.AddComponent<HomeController>();
            Set(home, "cam", homeCam);
            Set(home, "pet", juice);
            Set(home, "petCollider", pet.GetComponent<Collider>());

            ApplyLayout(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static ParticleSystem BuildBurst(Transform parent)
        {
            var material = SaveMaterial($"{Generated}/Burst.mat", "Universal Render Pipeline/Particles/Unlit", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/Particles/star_06.png"));
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            });

            var go = new GameObject("Burst");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.6f), new Color(1f, 0.85f, 0.3f));
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "PetApp";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.petapp.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // The dev catalog is served over plain HTTP from this Mac; a production CDN would be HTTPS.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
        }

        static Camera MakeCamera(string name, Vector3 position, Color background)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.transform.position = position;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            return cam;
        }

        static GameObject Primitive(PrimitiveType type, string name, Vector3 scale, Material material)
        {
            var root = new GameObject(name);
            var mesh = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = scale;
            mesh.GetComponent<Renderer>().sharedMaterial = material;
            return root;
        }

        static void SavePrefab(string path, GameObject go)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static Material SaveMaterial(string path, string shader, System.Action<Material> configure)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Art}/Audio/{name}.ogg");

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
