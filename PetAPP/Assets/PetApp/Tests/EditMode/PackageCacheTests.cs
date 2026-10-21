using System;
using System.IO;
using NUnit.Framework;
using PetApp.MiniGames;

namespace PetApp.Tests
{
    public class PackageCacheTests
    {
        string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "petapp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(dir, true);

        static GameManifest Manifest(int version, string hash) =>
            new GameManifest { id = "catch", version = version, sha256 = hash };

        [Test]
        public void Decides_between_cache_download_and_unavailable()
        {
            Assert.AreEqual(CacheDecision.UseCached, PackageCache.Decide(Manifest(3, "a"), Manifest(3, "a")));
            Assert.AreEqual(CacheDecision.Download, PackageCache.Decide(Manifest(4, "b"), Manifest(3, "a")));
            Assert.AreEqual(CacheDecision.Download, PackageCache.Decide(Manifest(3, "b"), Manifest(3, "a")), "republished under the same version");
            Assert.AreEqual(CacheDecision.Download, PackageCache.Decide(Manifest(3, "a"), null));
            Assert.AreEqual(CacheDecision.UseCached, PackageCache.Decide(null, Manifest(3, "a")), "offline with a cached copy");
            Assert.AreEqual(CacheDecision.Unavailable, PackageCache.Decide(null, null));
        }

        [Test]
        public void Corrupted_package_is_not_reported_as_cached()
        {
            var cache = new PackageCache(dir);
            var bytes = new byte[] { 1, 2, 3 };
            cache.Store(Manifest(1, PackageCache.Sha256(bytes)), bytes);
            Assert.AreEqual(1, cache.ReadValidCached("catch").version);

            File.WriteAllBytes(cache.PackagePath("catch"), new byte[] { 9 });

            Assert.IsNull(cache.ReadValidCached("catch"));
        }

        [Test]
        public void Store_rejects_bytes_that_do_not_match_the_manifest()
        {
            var cache = new PackageCache(dir);

            Assert.Throws<InvalidDataException>(() => cache.Store(Manifest(1, "deadbeef"), new byte[] { 1, 2, 3 }));
            Assert.IsNull(cache.ReadValidCached("catch"));
        }

        [Test]
        public void Deleted_game_needs_downloading_again_and_can_be_restored()
        {
            var cache = new PackageCache(dir);
            var bytes = new byte[] { 1, 2, 3 };
            var manifest = Manifest(2, PackageCache.Sha256(bytes));
            cache.Store(manifest, bytes);

            cache.Delete("catch");

            Assert.IsNull(cache.ReadValidCached("catch"));
            Assert.AreEqual(CacheDecision.Download, PackageCache.Decide(manifest, cache.ReadValidCached("catch")));
            cache.Store(manifest, bytes);
            Assert.AreEqual(2, cache.ReadValidCached("catch").version);
        }
    }
}
