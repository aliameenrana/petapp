using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using PetApp.App;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PetApp.MiniGames
{
    public sealed class MiniGamesController : MonoBehaviour
    {
        public static MiniGamesController Instance { get; private set; }

        [SerializeField] VisualTreeAsset cardTemplate;
        [SerializeField] GameHost host;

        public Catalog Catalog { get; private set; } = new Catalog();
        public bool Online { get; private set; }
        public event Action<Catalog> CatalogLoaded;

        const float PollSeconds = 5f;

        PackageCache cache;
        string catalogUrl;
        UnityWebRequest activeDownload;
        bool cancelled;
        bool refreshing;
        bool updateCheckPending;
        string lastCatalogSignature;
        readonly HashSet<string> promptedUpdates = new HashSet<string>();
        readonly Dictionary<string, VisualElement> cards = new Dictionary<string, VisualElement>();

        VisualElement Root => AppRoot.Instance.Root;

        void Awake()
        {
            Instance = this;
            cache = new PackageCache(Path.Combine(Application.persistentDataPath, "games"));
            catalogUrl = AppConfig.Load().catalogUrl;
        }

        void Start()
        {
            Root.Q<Button>("game-loading-cancel").clicked += CancelDownload;
            Root.Q<Button>("update-download").clicked += () => AnswerUpdate(true);
            Root.Q<Button>("update-later").clicked += () => AnswerUpdate(false);
            AppRoot.Instance.TabChanged += tab => { if (tab == "games") Refresh(); };
            Refresh();
            StartCoroutine(Poll());
        }

        // Lets a version published from the studio reach a running app within seconds; a real backend would push instead.
        IEnumerator Poll()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (true)
            {
                yield return wait;
                if (!AppRoot.Instance.InGame && !launching) Refresh();
            }
        }

        void AnswerUpdate(bool download)
        {
            Root.Q("game-update").AddToClassList("hidden");
            updateChoice?.TrySetResult(download);
        }

        TaskCompletionSource<bool> updateChoice;

        Task<bool> ConfirmUpdate(CatalogGame game, GameManifest cached)
        {
            updateChoice = new TaskCompletionSource<bool>();
            Root.Q<Label>("update-text").text = $"{game.title} v{cached.version} → v{game.version}";
            AppRoot.Pop(Root.Q("game-update"));
            return updateChoice.Task;
        }

        public void Refresh()
        {
            if (refreshing) return;
            refreshing = true;
            StartCoroutine(CatalogClient.Load(catalogUrl, OnCatalog));
        }

        void OnCatalog(Catalog catalog, bool online)
        {
            refreshing = false;
            // Rebuilding on every poll would restart the feed's videos, so only react to a real change.
            var signature = JsonUtility.ToJson(catalog) + online;
            var changed = signature != lastCatalogSignature;
            if (changed)
            {
                lastCatalogSignature = signature;
                Catalog = catalog;
                Online = online;
                BuildCards();
                CatalogLoaded?.Invoke(catalog);
            }
            if (changed || updateCheckPending) OfferUpdates();
        }

        // One prompt at a time, once per version; an update found while busy is retried on the next poll.
        void OfferUpdates()
        {
            updateCheckPending = false;
            if (!Online) return;
            foreach (var game in Catalog.games)
            {
                var cached = cache.ReadValidCached(game.id);
                if (cached == null || PackageCache.Decide(Catalog.ManifestFor(game, catalogUrl), cached) != CacheDecision.Download) continue;
                var key = $"{game.id}@{game.version}";
                if (promptedUpdates.Contains(key)) continue;
                if (AppRoot.Instance.InGame || launching)
                {
                    updateCheckPending = true;
                    return;
                }
                promptedUpdates.Add(key);
                Launch(game.id);
                return;
            }
        }

        void BuildCards()
        {
            var list = Root.Q<ScrollView>("games-list");
            list.Clear();
            cards.Clear();
            foreach (var game in Catalog.games)
            {
                var card = cardTemplate.Instantiate();
                card.Q<Label>("card-title").text = game.title;
                var id = game.id;
                card.Q<Button>("card-action").clicked += () => Launch(id);
                card.Q<Button>("card-delete").clicked += () => Delete(id);
                cards[id] = card;
                UpdateCard(game);
                list.Add(card);
            }
            if (Catalog.games.Length == 0)
                list.Add(new Label(Online ? "No games yet." : "Offline. Connect to download games.") { name = "empty" });
        }

        void UpdateCard(CatalogGame game)
        {
            if (!cards.TryGetValue(game.id, out var card)) return;
            var cached = cache.ReadValidCached(game.id);
            var remote = Online ? Catalog.ManifestFor(game, catalogUrl) : null;
            var decision = PackageCache.Decide(remote, cached);
            var status = card.Q<Label>("card-status");
            var action = card.Q<Button>("card-action");
            action.SetEnabled(decision != CacheDecision.Unavailable);
            card.Q<Button>("card-delete").EnableInClassList("hidden", cached == null);

            switch (decision)
            {
                case CacheDecision.UseCached:
                    status.text = Online ? $"Ready · v{cached.version}" : $"Offline · v{cached.version}";
                    action.text = "Play";
                    break;
                case CacheDecision.Download:
                    var size = Array.Find(game.packages ?? Array.Empty<PlatformPackage>(), p => p.platform == Catalog.Platform)?.size ?? 0;
                    status.text = cached == null ? $"Not installed · {size / 1024f / 1024f:0.0} MB" : $"Update available · v{cached.version} → v{game.version}";
                    action.text = cached == null ? "Get" : "Update";
                    break;
                default:
                    status.text = "Unavailable offline";
                    action.text = "Get";
                    break;
            }
        }

        bool launching;

        public void Launch(string gameId)
        {
            if (AppRoot.Instance.InGame || launching) return;
            var game = Catalog.Find(gameId);
            if (game == null)
            {
                Debug.LogWarning($"Game {gameId} is not in the catalog");
                return;
            }
            launching = true;
            StartCoroutine(LaunchRoutine(game));
        }

        IEnumerator LaunchRoutine(CatalogGame game)
        {
            var cached = cache.ReadValidCached(game.id);
            var remote = Online ? Catalog.ManifestFor(game, catalogUrl) : null;
            var decision = PackageCache.Decide(remote, cached);

            // Confirm before re-downloading an already-installed game; a fresh install was an explicit tap already.
            if (decision == CacheDecision.Download && cached != null)
            {
                var confirmTask = ConfirmUpdate(game, cached);
                while (!confirmTask.IsCompleted) yield return null;
                if (!confirmTask.Result) { launching = false; yield break; }
            }

            var app = AppRoot.Instance;
            app.EnterGame();
            var overlay = Root.Q("game-loading");
            var text = Root.Q<Label>("game-loading-text");
            var progress = Root.Q<ProgressBar>("game-progress");
            AppRoot.Pop(overlay);
            progress.value = 0;

            if (decision == CacheDecision.Unavailable)
            {
                Fail("This game hasn't been downloaded yet and you're offline.");
                launching = false;
                yield break;
            }

            var manifest = cached;
            if (decision == CacheDecision.Download)
            {
                text.text = cached == null ? $"Downloading {game.title}…" : $"Updating {game.title}…";
                cancelled = false;
                activeDownload = UnityWebRequest.Get(remote.url);
                var op = activeDownload.SendWebRequest();
                while (!op.isDone)
                {
                    progress.value = activeDownload.downloadProgress;
                    yield return null;
                }
                var request = activeDownload;
                activeDownload = null;
                if (cancelled)
                {
                    request.Dispose();
                    launching = false;
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    var error = request.error;
                    request.Dispose();
                    Fail($"Download failed: {error}");
                    launching = false;
                    yield break;
                }
                try
                {
                    cache.Store(remote, request.downloadHandler.data);
                    manifest = remote;
                }
                catch (InvalidDataException e)
                {
                    Fail(e.Message);
                    launching = false;
                    yield break;
                }
                finally
                {
                    request.Dispose();
                }
            }

            string dir;
            try
            {
                dir = Extract(manifest);
            }
            catch (Exception e)
            {
                Fail($"Package is broken: {e.Message}");
                launching = false;
                yield break;
            }

            overlay.AddToClassList("hidden");
            UpdateCard(game);
            launching = false;
            host.Play(manifest, dir);
        }

        static string ExtractRoot(string id) => Path.Combine(Application.temporaryCachePath, "games", id);

        public void Delete(string gameId)
        {
            if (launching || AppRoot.Instance.InGame) return;
            cache.Delete(gameId);
            var extracted = ExtractRoot(gameId);
            if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
            var game = Catalog.Find(gameId);
            if (game != null) UpdateCard(game);
        }

        string Extract(GameManifest manifest)
        {
            var dir = Path.Combine(ExtractRoot(manifest.id), manifest.version + "-" + manifest.sha256.Substring(0, 8));
            if (Directory.Exists(dir)) return dir;
            var staging = dir + ".partial";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            var stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

            using (var zip = new ZipArchive(File.OpenRead(cache.PackagePath(manifest.id)), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    // Player-made packages are untrusted: an entry like "../../x" must not escape the folder.
                    if (!target.StartsWith(stagingRoot, StringComparison.Ordinal))
                        throw new InvalidDataException($"entry escapes package: {entry.FullName}");
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using var input = entry.Open();
                    using var output = File.Create(target);
                    input.CopyTo(output);
                }
            }
            Directory.Move(staging, dir);
            return dir;
        }

        void CancelDownload()
        {
            cancelled = true;
            activeDownload?.Abort();
            Root.Q("game-loading").AddToClassList("hidden");
            AppRoot.Instance.ExitGame();
        }

        void Fail(string message)
        {
            Root.Q("game-loading").AddToClassList("hidden");
            host.ShowError(message);
        }
    }
}
