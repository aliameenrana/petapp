using NUnit.Framework;
using PetApp.Economy;
using PetApp.MiniGames;

namespace PetApp.Tests
{
    public class RewardTests
    {
        class MemoryStore : IWalletStore
        {
            public int Saved;
            public int Load() => Saved;
            public void Save(int coins) => Saved = coins;
        }

        [TestCase(22, 30f, 22)]
        [TestCase(80, 30f, 80)]
        [TestCase(10000, 3f, 10000)]
        [TestCase(0, 30f, 0)]
        [TestCase(-5, 30f, 0)]
        public void Coins_equal_score_with_no_cap(int score, float seconds, int expected)
        {
            Assert.AreEqual(expected, RewardPolicy.CoinsFor(score, seconds));
        }

        [Test]
        public void Only_the_first_result_of_a_session_pays()
        {
            var wallet = new Wallet(new MemoryStore());
            var session = new GameSession(wallet, startedAt: 0f);

            session.Complete(10, now: 30f);
            session.Complete(40, now: 31f);

            Assert.AreEqual(10, wallet.Coins);
            Assert.AreEqual(10, session.Result.Score);
        }

        [Test]
        public void Failed_session_pays_nothing_even_if_completed_later()
        {
            var wallet = new Wallet(new MemoryStore());
            var session = new GameSession(wallet, startedAt: 0f);

            session.Fail("boom");
            session.Complete(10, now: 30f);

            Assert.AreEqual(0, wallet.Coins);
            Assert.AreEqual("boom", session.Result.Error);
        }
    }
}
