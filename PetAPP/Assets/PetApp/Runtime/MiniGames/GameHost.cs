using System;
using System.Collections.Generic;
using System.IO;
using DG.Tweening;
using PetApp.App;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PetApp.MiniGames
{
    public sealed class GameHost : MonoBehaviour
    {
        const int MaxObjects = 200;
        const float MaxSessionSeconds = 180f;
        const float OffscreenY = -8f;
        const float OffscreenX = 6f;

        sealed class HostedObject
        {
            public GameObject go;
            public Vector2 velocity;
            public float radius;
            public Animator animator;
            public bool offscreenSent;
        }

        [SerializeField] Camera gameCamera;
        [SerializeField] Transform stage;
        [SerializeField] GameObject petPrefab;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip rewardClip;
        [SerializeField] AudioSource homeMusic;
        [SerializeField] AudioSource gameMusic;

        readonly Dictionary<int, HostedObject> objects = new Dictionary<int, HostedObject>();
        readonly HashSet<int> touchingPet = new HashSet<int>();
        readonly List<ScriptEvent> events = new List<ScriptEvent>();

        ScriptRuntime runtime;
        AssetBundle bundle;
        GameSession session;
        float startedAt;
        float timerEnd = -1f;
        bool running;
        float homeMusicVolume;

        void Awake() => homeMusicVolume = homeMusic.volume;

        VisualElement Root => AppRoot.Instance.Root;

        void Start()
        {
            Root.Q<Button>("hud-quit").clicked += () => { session?.Fail("quit"); Leave(); };
            Root.Q<Button>("result-continue").clicked += Leave;
        }

        public void Play(GameManifest manifest, string dir)
        {
            Cleanup();
            Root.Q("game-result").AddToClassList("hidden");
            Root.Q<Label>("hud-score").text = "0";
            Root.Q<Label>("hud-timer").text = "";

            try
            {
                bundle = AssetBundle.LoadFromFile(Path.Combine(dir, "assets.bundle"));
                if (bundle == null) throw new InvalidDataException("asset bundle failed to load");
                runtime = new ScriptRuntime(File.ReadAllText(Path.Combine(dir, "game.js")));
            }
            catch (Exception e)
            {
                ShowError($"{manifest.id} v{manifest.version} failed to start: {e.Message}");
                return;
            }

            var pet = Instantiate(petPrefab, stage);
            pet.transform.localPosition = Vector3.zero;
            pet.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            objects[0] = Track(pet);

            startedAt = Time.time;
            session = new GameSession(AppRoot.Instance.Wallet, startedAt);
            session.Ended += ShowResult;
            events.Add(new ScriptEvent { type = "start" });
            running = true;
            Step(0f, UnityEngine.Random.Range(1, int.MaxValue));
        }

        void Update()
        {
            if (!running) return;
            if (timerEnd > 0f) Root.Q<Label>("hud-timer").text = Mathf.CeilToInt(Mathf.Max(0f, timerEnd - Time.time)) + "s";
            if (Time.time - startedAt > MaxSessionSeconds)
            {
                session.Fail("The game ran past the time limit.");
                return;
            }

            CollectInput();
            MoveAndCollide(Time.deltaTime);
            Step(Time.deltaTime, 0);
        }

        void CollectInput()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.isPressed) return;
            var screen = pointer.position.ReadValue();
            if (AppRoot.Instance.IsPointerOverUI(screen)) return;

            var world = gameCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, gameCamera.nearClipPlane + 10f));
            var local = stage.InverseTransformPoint(world);
            if (pointer.press.wasPressedThisFrame) events.Add(new ScriptEvent { type = "tap", x = local.x, y = local.y });
            events.Add(new ScriptEvent { type = "drag", x = local.x, y = local.y });
        }

        void MoveAndCollide(float dt)
        {
            var pet = objects[0];
            var petPos = (Vector2)pet.go.transform.localPosition;
            foreach (var pair in objects)
            {
                if (pair.Key == 0) continue;
                var o = pair.Value;
                var t = o.go.transform;
                t.localPosition += (Vector3)(o.velocity * dt);
                var pos = (Vector2)t.localPosition;

                var touching = Vector2.Distance(pos, petPos) < o.radius + pet.radius;
                if (touching && touchingPet.Add(pair.Key))
                {
                    events.Add(new ScriptEvent { type = "touch", id = pair.Key, other = 0 });
                    events.Add(new ScriptEvent { type = "touch", id = 0, other = pair.Key });
                }
                else if (!touching) touchingPet.Remove(pair.Key);

                if (!o.offscreenSent && (pos.y < OffscreenY || Mathf.Abs(pos.x) > OffscreenX))
                {
                    o.offscreenSent = true;
                    events.Add(new ScriptEvent { type = "offscreen", id = pair.Key });
                }
            }
        }

        void Step(float dt, int seed)
        {
            ScriptCommand[] commands;
            try
            {
                commands = runtime.Step(dt, events, seed);
            }
            catch (ScriptException e)
            {
                events.Clear();
                Debug.LogError($"Mini-game script error: {e.Message}");
                session.Fail("The game hit a problem.");
                return;
            }
            events.Clear();
            foreach (var c in commands)
            {
                if (!running) break;
                Apply(c);
            }
        }

        void Apply(ScriptCommand c)
        {
            switch (c.op)
            {
                case "spawn":
                    if (objects.Count >= MaxObjects) { session.Fail("The game spawned too many objects."); return; }
                    var prefab = bundle.LoadAsset<GameObject>(c.key);
                    if (prefab == null) { session.Fail($"Unknown asset '{c.key}'."); return; }
                    var go = Instantiate(prefab, stage);
                    go.transform.localPosition = new Vector3(c.x, c.y, 0f);
                    objects[c.id] = Track(go);
                    break;
                case "pos":
                    if (objects.TryGetValue(c.id, out var moved)) moved.go.transform.localPosition = new Vector3(c.x, c.y, 0f);
                    break;
                case "vel":
                    if (objects.TryGetValue(c.id, out var pushed)) pushed.velocity = new Vector2(c.x, c.y);
                    break;
                case "destroy":
                    if (c.id != 0 && objects.TryGetValue(c.id, out var dead))
                    {
                        Destroy(dead.go);
                        objects.Remove(c.id);
                        touchingPet.Remove(c.id);
                    }
                    break;
                case "anim":
                    if (string.IsNullOrEmpty(c.s) || !objects.TryGetValue(c.id, out var animated)) break;
                    if (animated.animator != null) animated.animator.SetTrigger(char.ToUpperInvariant(c.s[0]) + c.s.Substring(1));
                    // Models without matching clips still react visibly.
                    var t = animated.go.transform;
                    t.DOComplete();
                    // Rotation, not position: position is driven by the script and a shake would snap it back.
                    if (c.s == "sad") t.DOShakeRotation(0.35f, new Vector3(0f, 0f, 25f), 20);
                    else t.DOPunchScale(Vector3.one * 0.3f, 0.3f, 8);
                    break;
                case "score":
                    var score = Root.Q<Label>("hud-score");
                    score.text = c.n.ToString();
                    score.style.scale = new Scale(Vector3.one * 1.3f);
                    DOTween.To(() => 1.3f, v => score.style.scale = new Scale(Vector3.one * v), 1f, 0.2f);
                    break;
                case "timer":
                    timerEnd = Time.time + c.x;
                    break;
                case "banner":
                    var banner = Root.Q<Label>("hud-banner");
                    banner.text = c.s;
                    banner.RemoveFromClassList("hidden");
                    DOVirtual.DelayedCall(1.2f, () => banner.AddToClassList("hidden"));
                    break;
                case "sound":
                    var clip = bundle.LoadAsset<AudioClip>(c.key);
                    if (clip != null) audioSource.PlayOneShot(clip);
                    break;
                case "camera":
                    break;
                case "music":
                    var track = bundle.LoadAsset<AudioClip>(c.key);
                    if (track == null) break;
                    // The package brings its own loop; Home's pauses rather than stops so it resumes where it was.
                    Fade(homeMusic, 0f, 0.5f).OnComplete(homeMusic.Pause);
                    gameMusic.clip = track;
                    gameMusic.volume = 0f;
                    gameMusic.Play();
                    Fade(gameMusic, homeMusicVolume, 0.8f);
                    break;
                case "shake":
                    var cameraTransform = gameCamera.transform;
                    cameraTransform.DOComplete();
                    cameraTransform.DOShakePosition(0.35f, Mathf.Clamp01(c.x) * 0.6f, 25);
                    break;
                case "log":
                    Debug.Log($"[mini-game] {c.s}");
                    break;
                case "end":
                    session.Complete(c.n, Time.time);
                    break;
            }
        }

        static HostedObject Track(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return new HostedObject
            {
                go = go,
                radius = Mathf.Max(bounds.extents.x, bounds.extents.y) * 0.8f,
                animator = go.GetComponentInChildren<Animator>()
            };
        }

        void ShowResult(SessionResult result)
        {
            running = false;
            AppRoot.Pop(Root.Q("game-result"));
            var failed = result.Error != null;
            Root.Q<Label>("result-title").text = failed ? "Game over" : "Time's up";
            Root.Q<Label>("result-score").text = failed ? result.Error : $"Score: {result.Score}";
            var coins = Root.Q<Label>("result-coins");
            coins.text = "+0";
            if (result.Coins > 0)
            {
                audioSource.PlayOneShot(rewardClip);
                DOTween.To(() => 0, v => coins.text = $"+{v}", result.Coins, 0.8f).SetEase(Ease.OutCubic);
            }
        }

        public void ShowError(string message)
        {
            running = false;
            AppRoot.Pop(Root.Q("game-result"));
            Root.Q<Label>("result-title").text = "Couldn't start";
            Root.Q<Label>("result-score").text = message;
            Root.Q<Label>("result-coins").text = "";
        }

        void Leave()
        {
            Cleanup();
            homeMusic.UnPause();
            Fade(homeMusic, homeMusicVolume, 0.8f);
            AppRoot.Instance.ExitGame();
        }

        static Tween Fade(AudioSource source, float volume, float duration)
        {
            DOTween.Kill(source);
            return DOTween.To(() => source.volume, v => source.volume = v, volume, duration).SetTarget(source);
        }

        void Cleanup()
        {
            // Stopped before the bundle unloads: unloading would cut the clip out from under it anyway.
            if (gameMusic != null)
            {
                gameMusic.Stop();
                gameMusic.clip = null;
            }
            running = false;
            timerEnd = -1f;
            events.Clear();
            touchingPet.Clear();
            foreach (var o in objects.Values) if (o.go != null) Destroy(o.go);
            objects.Clear();
            runtime?.Dispose();
            runtime = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            if (session != null) session.Ended -= ShowResult;
            session = null;
        }

        void OnDestroy() => Cleanup();
    }
}
