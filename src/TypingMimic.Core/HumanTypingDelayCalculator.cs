namespace TypingMimic.Core;

public sealed class HumanTypingDelayCalculator(Random? random = null)
{
    private readonly Random random = random ?? Random.Shared;
    public static double BaseDelayMs(double wpm)
    {
        if (!double.IsFinite(wpm) || wpm <= 0) throw new ArgumentOutOfRangeException(nameof(wpm));
        return 60000 / (wpm * 5);
    }

    public double NextDelayMs(TypingSettings settings, string character) =>
        BaseDelayMs(settings.WordsPerMinute) * (1 + (random.NextDouble() * 2 - 1) * settings.VariationPercent / 100)
        + PunctuationPause(settings, character);

    public static int PunctuationPause(TypingSettings s, string c) => c switch
    {
        "," => s.CommaPauseMs,
        "." or "?" or "!" => s.SentencePauseMs,
        "\n" => s.NewLinePauseMs,
        _ => 0
    };

    public static TimeSpan Estimate(string text, TypingSettings settings)
    {
        var count = text.EnumerateRunes().Count();
        double ms = count * BaseDelayMs(settings.WordsPerMinute);
        ms += text.Sum(c => (double)PunctuationPause(settings, c.ToString()));
        if (settings.EnableErrors)
            ms += TemporaryErrorGenerator.EligiblePositions(text).Count(x => x) * settings.ErrorPercent / 100 * 200;
        return TimeSpan.FromMilliseconds(ms);
    }
}
