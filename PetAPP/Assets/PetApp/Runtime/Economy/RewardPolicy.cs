using System;

namespace PetApp.Economy
{
    // Lives in the host, not the game: a mini-game (later, a player-made one) only reports a score.
    public static class RewardPolicy
    {
        public static int CoinsFor(int score, float secondsPlayed) => Math.Max(0, score);
    }
}
