namespace Uno.Api.Progression;

/// <summary>Initial progression tuning. Only naturally completed matches qualify.</summary>
public static class ProgressionPolicy
{
    public const int ParticipationXp = 30;
    public const int WinnerBonusXp = 50;
    public const int MinimumCardsPlayed = 7;

    public static int Award(bool won, bool naturalFinish, int cardsPlayed) =>
        naturalFinish && cardsPlayed >= MinimumCardsPlayed
            ? ParticipationXp + (won ? WinnerBonusXp : 0) : 0;

    // Level 1 starts at zero; each subsequent level costs 100 more XP.
    public static long Threshold(int level) =>
        level < 1 ? throw new ArgumentOutOfRangeException(nameof(level))
            : checked(50L * level * (level - 1L));

    public static int Level(long xp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(xp);
        int low = 1, high = 1_000_000_000;
        while (low < high)
        {
            int mid = low + (high - low + 1) / 2;
            // Divide before multiplying to avoid overflow at extreme XP values.
            if ((long)mid * (mid - 1L) <= xp / 50) low = mid;
            else high = mid - 1;
        }
        return low;
    }

    public static string Season(DateTimeOffset completedAt) =>
        completedAt.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
