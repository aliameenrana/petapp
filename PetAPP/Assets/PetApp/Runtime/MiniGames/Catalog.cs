using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PetApp.MiniGames
{
    [Serializable]
    public class PlatformPackage
    {
        public string platform;
        public string url;
        public string sha256;
        public int size;
    }

    [Serializable]
    public class CatalogGame
    {
        public string id;
        public string title;
        public int version;
        public int sdk;
        public PlatformPackage[] packages;
    }

    [Serializable]
    public class FeedItem
    {
        public string title;
        public string video;
        public string gameId;
    }

    [Serializable]
    public class Catalog
    {
        public CatalogGame[] games = Array.Empty<CatalogGame>();
        public FeedItem[] feed = Array.Empty<FeedItem>();

        public static string Platform => Application.platform == RuntimePlatform.IPhonePlayer ? "ios" : "osx";

        public CatalogGame Find(string id) => Array.Find(games, g => g.id == id);

        public static GameManifest ManifestFor(CatalogGame game, string catalogUrl)
        {
            var package = Array.Find(game.packages ?? Array.Empty<PlatformPackage>(), p => p.platform == Platform);
            if (package == null) return null;
            return new GameManifest
            {
                id = game.id,
                version = game.version,
                sha256 = package.sha256,
                url = new Uri(new Uri(catalogUrl), package.url).ToString(),
                sdk = game.sdk
            };
        }
    }

    public static class CatalogClient
    {
        static string CachePath => Path.Combine(Application.persistentDataPath, "catalog.json");

        // Calls back with (catalog, online). Falls back to the last catalog seen so cached games stay playable offline.
        public static IEnumerator Load(string url, Action<Catalog, bool> done)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = 8;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var json = request.downloadHandler.text;
                Catalog catalog = null;
                try
                {
                    catalog = JsonUtility.FromJson<Catalog>(json);
                    File.WriteAllText(CachePath, json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Catalog unreadable: {e.Message}");
                }
                if (catalog != null)
                {
                    done(catalog, true);
                    yield break;
                }
            }
            else
            {
                Debug.LogWarning($"Catalog fetch failed ({url}): {request.error}");
            }

            Catalog cached = null;
            if (File.Exists(CachePath))
            {
                try { cached = JsonUtility.FromJson<Catalog>(File.ReadAllText(CachePath)); }
                catch (Exception e) { Debug.LogWarning($"Cached catalog unreadable: {e.Message}"); }
            }
            done(cached ?? new Catalog(), false);
        }
    }
}
