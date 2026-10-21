using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace PetApp.MiniGames
{
    [Serializable]
    public class GameManifest
    {
        public string id;
        public int version;
        public string sha256;
        public string url;
        public int sdk;
    }

    public enum CacheDecision
    {
        UseCached,
        Download,
        Unavailable
    }

    public sealed class PackageCache
    {
        readonly string root;

        public PackageCache(string root)
        {
            this.root = root;
        }

        public string PackagePath(string id) => Path.Combine(root, id, "package.zip");
        string ManifestPath(string id) => Path.Combine(root, id, "manifest.json");

        // remote is null when the catalog could not be fetched (offline).
        public static CacheDecision Decide(GameManifest remote, GameManifest validCached)
        {
            if (remote == null) return validCached != null ? CacheDecision.UseCached : CacheDecision.Unavailable;
            var upToDate = validCached != null && validCached.version == remote.version && validCached.sha256 == remote.sha256;
            return upToDate ? CacheDecision.UseCached : CacheDecision.Download;
        }

        public GameManifest ReadValidCached(string id)
        {
            var manifestPath = ManifestPath(id);
            var packagePath = PackagePath(id);
            if (!File.Exists(manifestPath) || !File.Exists(packagePath)) return null;
            try
            {
                var manifest = JsonUtility.FromJson<GameManifest>(File.ReadAllText(manifestPath));
                return manifest != null && manifest.sha256 == Sha256(File.ReadAllBytes(packagePath)) ? manifest : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Cached package for {id} unreadable, will re-download: {e.Message}");
                return null;
            }
        }

        public void Delete(string id)
        {
            var dir = Path.Combine(root, id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        public void Store(GameManifest manifest, byte[] package)
        {
            if (Sha256(package) != manifest.sha256)
                throw new InvalidDataException($"Package for {manifest.id} v{manifest.version} does not match its manifest hash");
            Directory.CreateDirectory(Path.Combine(root, manifest.id));
            File.WriteAllBytes(PackagePath(manifest.id), package);
            File.WriteAllText(ManifestPath(manifest.id), JsonUtility.ToJson(manifest));
        }

        public static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
