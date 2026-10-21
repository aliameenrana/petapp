using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PetApp.App;
using PetApp.MiniGames;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace PetApp.Feed
{
    public sealed class FeedController : MonoBehaviour
    {
        const int PoolSize = 3;
        const float SwipeThreshold = 0.18f;
        const float DragStartPixels = 12f;
        const float StallTimeout = 15f;

        sealed class Page
        {
            public FeedItem item;
            public VisualElement root;
            public VisualElement video;
            public VisualElement state;
            public Label stateText;
            public Button retry;
            public float lastProgressAt = -1f;
            public int attempt;
            public UnityWebRequest download;
        }

        [SerializeField] VisualTreeAsset pageTemplate;

        readonly List<Page> pages = new List<Page>();
        readonly VideoPlayer[] pool = new VideoPlayer[PoolSize];
        readonly int[] owner = { -1, -1, -1 };
        readonly RenderTexture[] textures = new RenderTexture[PoolSize];

        VisualElement pager;
        VisualElement column;
        int index;
        float height;
        int pointerId = -1;
        Vector2 pointerStart;
        bool dragging;
        string cacheDir;

        void Start()
        {
            cacheDir = Path.Combine(Application.temporaryCachePath, "feed");
            Directory.CreateDirectory(cacheDir);
            var root = AppRoot.Instance.Root;
            pager = root.Q("feed-pager");
            column = root.Q("feed-column");
            pager.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            pager.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            pager.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            pager.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);

            for (var i = 0; i < PoolSize; i++)
            {
                var player = new GameObject($"FeedVideo{i}").AddComponent<VideoPlayer>();
                player.transform.SetParent(transform, false);
                player.playOnAwake = false;
                player.isLooping = true;
                player.skipOnDrop = true;
                player.source = VideoSource.Url;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.audioOutputMode = VideoAudioOutputMode.Direct;
                player.prepareCompleted += OnPrepared;
                player.errorReceived += (vp, message) => OnError(vp, message);
                pool[i] = player;
            }

            AppRoot.Instance.TabChanged += _ => UpdatePlayback();
            MiniGamesController.Instance.CatalogLoaded += Build;
            Build(MiniGamesController.Instance.Catalog);
        }

        void Build(Catalog catalog)
        {
            if (pages.Count == catalog.feed.Length) return;
            column.Clear();
            pages.Clear();
            for (var i = 0; i < PoolSize; i++) owner[i] = -1;

            foreach (var item in catalog.feed)
            {
                var root = pageTemplate.Instantiate().Q(className: "feed-page");
                var page = new Page
                {
                    item = item,
                    root = root,
                    video = root.Q("video"),
                    state = root.Q("state"),
                    stateText = root.Q<Label>("state-text"),
                    retry = root.Q<Button>("retry")
                };
                root.Q<Label>("title").text = item.title;
                var gameId = item.gameId;
                root.Q<Button>("play").clicked += () =>
                {
                    foreach (var p in pool) p.Pause();
                    MiniGamesController.Instance.Launch(gameId);
                };
                var pageIndex = pages.Count;
                page.retry.clicked += () => Prepare(pageIndex, true);
                pages.Add(page);
                column.Add(root);
            }
            index = Mathf.Clamp(index, 0, Mathf.Max(0, pages.Count - 1));
            Layout();
            AssignPlayers();
        }

        void Layout()
        {
            height = pager.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0f) return;
            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].root.style.top = i * height;
                pages[i].root.style.height = height;
            }
            SetOffset(0f);
        }

        void SetOffset(float drag) => column.style.translate = new Translate(0, -index * height + drag);

        // Page i always uses slot i % 3, so the previous, current and next pages never share a player.
        void AssignPlayers()
        {
            for (var i = index - 1; i <= index + 1; i++)
                if (i >= 0 && i < pages.Count) Prepare(i, false);
            UpdatePlayback();
        }

        void Prepare(int pageIndex, bool force)
        {
            var slot = pageIndex % PoolSize;
            var page = pages[pageIndex];
            if (owner[slot] == pageIndex && !force) return;

            if (owner[slot] >= 0 && owner[slot] < pages.Count) Cancel(pages[owner[slot]]);
            Cancel(page);
            owner[slot] = pageIndex;
            pool[slot].Stop();
            page.video.style.backgroundImage = StyleKeyword.None;
            ShowState(page, "Loading…", false);
            page.lastProgressAt = Time.time;
            StartCoroutine(Load(page, pageIndex, slot, page.attempt));
        }

        // Downloads the whole clip, then plays the local file. Apple's player can't stream an MP4 whose index (moov)
        // sits at the end of the file and reports "no video tracks"; the feed's clips are encoded that way.
        // Downloading the neighbours also makes the preload real: their bytes are on disk before the swipe.
        IEnumerator Load(Page page, int pageIndex, int slot, int attempt)
        {
            var path = Path.Combine(cacheDir, PackageCache.Sha256(Encoding.UTF8.GetBytes(page.item.video)).Substring(0, 24) + ".mp4");
            if (!File.Exists(path))
            {
                var partial = $"{path}.{attempt}.part";
                string error = null;
                using (var request = new UnityWebRequest(page.item.video, UnityWebRequest.kHttpVerbGET,
                           new DownloadHandlerFile(partial) { removeFileOnAbort = true }, null))
                {
                    page.download = request;
                    var op = request.SendWebRequest();
                    var progress = 0f;
                    while (!op.isDone)
                    {
                        if (request.downloadProgress > progress)
                        {
                            progress = request.downloadProgress;
                            page.lastProgressAt = Time.time;
                            if (page.attempt == attempt) page.stateText.text = $"Loading… {progress * 100f:0}%";
                        }
                        yield return null;
                    }
                    if (page.download == request) page.download = null;
                    if (request.result != UnityWebRequest.Result.Success) error = request.error ?? "download failed";
                }

                if (error != null || page.attempt != attempt)
                {
                    if (File.Exists(partial)) File.Delete(partial);
                    if (page.attempt != attempt) yield break;
                    Debug.LogWarning($"Feed video failed ({page.item.video}): {error}");
                    page.lastProgressAt = -1f;
                    ShowState(page, "Couldn't load this video.", true);
                    yield break;
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
            }

            if (page.attempt != attempt || owner[slot] != pageIndex) yield break;
            var player = pool[slot];
            player.url = path;
            player.Prepare();
        }

        static void Cancel(Page page)
        {
            page.attempt++;
            page.lastProgressAt = -1f;
            page.download?.Abort();
            page.download = null;
        }

        void OnPrepared(VideoPlayer player)
        {
            var slot = System.Array.IndexOf(pool, player);
            if (slot < 0 || owner[slot] < 0) return;
            var page = pages[owner[slot]];
            page.lastProgressAt = -1f;

            var w = (int)player.width;
            var h = (int)player.height;
            var rt = textures[slot];
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) rt.Release();
                rt = textures[slot] = new RenderTexture(w, h, 0);
            }
            player.targetTexture = rt;
            page.video.style.backgroundImage = Background.FromRenderTexture(rt);

            // Fill the screen when the shapes are close; letterbox otherwise so nothing important is cropped.
            var screenAspect = pager.resolvedStyle.width / Mathf.Max(1f, pager.resolvedStyle.height);
            var close = Mathf.Abs(Mathf.Log((w / (float)h) / screenAspect)) < 0.25f;
            page.video.style.backgroundSize = new BackgroundSize(close ? BackgroundSizeType.Cover : BackgroundSizeType.Contain);

            page.state.AddToClassList("hidden");
            player.Play();
            UpdatePlayback();
        }

        void OnError(VideoPlayer player, string message)
        {
            var slot = System.Array.IndexOf(pool, player);
            if (slot < 0 || owner[slot] < 0) return;
            Debug.LogWarning($"Feed video failed ({player.url}): {message}");
            var page = pages[owner[slot]];
            page.lastProgressAt = -1f;
            ShowState(page, "Couldn't load this video.", true);
        }

        void ShowState(Page page, string text, bool canRetry)
        {
            page.state.RemoveFromClassList("hidden");
            page.stateText.text = text;
            page.retry.EnableInClassList("hidden", !canRetry);
        }

        void Update()
        {
            foreach (var slotOwner in owner)
            {
                if (slotOwner < 0 || slotOwner >= pages.Count) continue;
                var page = pages[slotOwner];
                if (page.lastProgressAt > 0f && Time.time - page.lastProgressAt > StallTimeout)
                {
                    Cancel(page);
                    pool[slotOwner % PoolSize].Stop();
                    ShowState(page, "This video is taking too long.", true);
                }
            }
        }

        void UpdatePlayback()
        {
            var visible = AppRoot.Instance.CurrentTab == "feed" && !AppRoot.Instance.InGame;
            for (var slot = 0; slot < PoolSize; slot++)
            {
                var player = pool[slot];
                if (!player.isPrepared) continue;
                if (visible && owner[slot] == index) player.Play();
                else player.Pause();
            }
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            pointerId = evt.pointerId;
            pointerStart = evt.position;
            dragging = false;
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointerId) return;
            if (evt.pressedButtons == 0)
            {
                pointerId = -1;
                dragging = false;
                return;
            }
            var dy = evt.position.y - pointerStart.y;
            if (!dragging && Mathf.Abs(dy) > DragStartPixels)
            {
                dragging = true;
                pager.CapturePointer(pointerId);
                column.RemoveFromClassList("feed-column--animating");
            }
            if (dragging) SetOffset(dy);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointerId) return;
            pointerId = -1;
            if (!dragging) return;
            dragging = false;
            pager.ReleasePointer(evt.pointerId);

            var fraction = (evt.position.y - pointerStart.y) / Mathf.Max(1f, height);
            if (fraction < -SwipeThreshold) index = Mathf.Min(index + 1, pages.Count - 1);
            else if (fraction > SwipeThreshold) index = Mathf.Max(index - 1, 0);
            column.AddToClassList("feed-column--animating");
            SetOffset(0f);
            AssignPlayers();
        }

        void OnDestroy()
        {
            foreach (var rt in textures) if (rt != null) rt.Release();
        }
    }
}
