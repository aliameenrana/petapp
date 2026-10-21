using System;
using System.IO;
using UnityEngine;

namespace PetApp.Economy
{
    public interface IWalletStore
    {
        int Load();
        void Save(int coins);
    }

    public sealed class FileWalletStore : IWalletStore
    {
        [Serializable]
        class Data
        {
            public int coins;
        }

        readonly string path;

        public FileWalletStore(string path)
        {
            this.path = path;
        }

        public int Load()
        {
            if (!File.Exists(path)) return 0;
            try
            {
                return Math.Max(0, JsonUtility.FromJson<Data>(File.ReadAllText(path)).coins);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Wallet file unreadable, starting at 0: {e.Message}");
                return 0;
            }
        }

        public void Save(int coins)
        {
            // Write-then-swap so a crash mid-save never leaves a half-written wallet.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(new Data { coins = coins }));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    public sealed class Wallet
    {
        readonly IWalletStore store;

        public int Coins { get; private set; }
        public event Action<int> Changed;

        public Wallet(IWalletStore store)
        {
            this.store = store;
            Coins = store.Load();
        }

        public void Add(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return;
            Coins = (int)Math.Min((long)Coins + amount, int.MaxValue);
            store.Save(Coins);
            Changed?.Invoke(Coins);
        }
    }
}
