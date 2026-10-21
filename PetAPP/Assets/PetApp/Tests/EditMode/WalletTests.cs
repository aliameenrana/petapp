using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PetApp.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace PetApp.Tests
{
    public class WalletTests
    {
        string dir;
        string WalletPath => Path.Combine(dir, "wallet.json");

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "petapp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(dir, true);

        [Test]
        public void Coins_survive_restart()
        {
            new Wallet(new FileWalletStore(WalletPath)).Add(15);
            new Wallet(new FileWalletStore(WalletPath)).Add(5);

            Assert.AreEqual(20, new Wallet(new FileWalletStore(WalletPath)).Coins);
        }

        [Test]
        public void Corrupt_save_starts_at_zero_and_is_overwritten()
        {
            File.WriteAllText(WalletPath, "{not json");
            LogAssert.Expect(LogType.Warning, new Regex("Wallet file unreadable"));

            var wallet = new Wallet(new FileWalletStore(WalletPath));
            Assert.AreEqual(0, wallet.Coins);

            wallet.Add(3);
            Assert.AreEqual(3, new Wallet(new FileWalletStore(WalletPath)).Coins);
        }

        [Test]
        public void Changed_reports_the_new_balance()
        {
            var wallet = new Wallet(new FileWalletStore(WalletPath));
            wallet.Add(4);
            int? reported = null;
            wallet.Changed += c => reported = c;

            wallet.Add(6);

            Assert.AreEqual(10, reported);
        }
    }
}
