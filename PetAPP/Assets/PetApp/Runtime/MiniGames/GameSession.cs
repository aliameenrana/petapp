using System;
using PetApp.Economy;

namespace PetApp.MiniGames
{
    public sealed class SessionResult
    {
        public readonly int Score;
        public readonly int Coins;
        public readonly string Error;

        public SessionResult(int score, int coins, string error)
        {
            Score = score;
            Coins = coins;
            Error = error;
        }
    }

    public sealed class GameSession
    {
        readonly Wallet wallet;
        readonly float startedAt;

        public SessionResult Result { get; private set; }
        public event Action<SessionResult> Ended;

        public GameSession(Wallet wallet, float startedAt)
        {
            this.wallet = wallet;
            this.startedAt = startedAt;
        }

        public void Complete(int score, float now)
        {
            if (Result != null) return;
            var coins = RewardPolicy.CoinsFor(score, now - startedAt);
            wallet.Add(coins);
            Finish(new SessionResult(score, coins, null));
        }

        public void Fail(string error)
        {
            if (Result != null) return;
            Finish(new SessionResult(0, 0, error));
        }

        void Finish(SessionResult result)
        {
            Result = result;
            Ended?.Invoke(result);
        }
    }
}
