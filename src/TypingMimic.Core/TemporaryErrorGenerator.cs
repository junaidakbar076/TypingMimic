using System.Text.RegularExpressions;

namespace TypingMimic.Core;

public sealed class TemporaryErrorGenerator(Random? random = null)
{
    private readonly Random random = random ?? Random.Shared;
    private static readonly IReadOnlyDictionary<char, string> Neighbors = new Dictionary<char, string>
    {
        ['q']="wa", ['w']="qesa", ['e']="wrsd", ['r']="etdf", ['t']="ryfg", ['y']="tugh", ['u']="yihj",
        ['i']="uojk", ['o']="ipkl", ['p']="ol", ['a']="qwsz", ['s']="awedxz", ['d']="serfcx", ['f']="drtgvc",
        ['g']="fhtyvb", ['h']="gyujbn", ['j']="huiknm", ['k']="jiolm", ['l']="kop", ['z']="asx", ['x']="zsdc",
        ['c']="xdfv", ['v']="cfgb", ['b']="vghn", ['n']="bhjm", ['m']="njk"
    };

    public static string GetNeighbors(char c) => Neighbors.GetValueOrDefault(char.ToLowerInvariant(c), "");

    // Conservative allow-list: only simple lower/title-case ASCII prose words, optionally
    // surrounded by sentence punctuation. Entire code lines and fenced blocks are protected.
    public static bool[] EligiblePositions(string text)
    {
        var result = new bool[text.Length];
        bool fenced = false;
        int offset = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            else if (!fenced && !Regex.IsMatch(line, @"[{};=<>`]|^\s{4}|^\t"))
            {
                foreach (Match token in Regex.Matches(line, @"\S+"))
                {
                    var word = Regex.Match(token.Value, "^[\"'(]*([A-Za-z]+)[,!.?\"')]*$");
                    if (!word.Success) continue;
                    var value = word.Groups[1].Value;
                    if (value.Length > 1 && value.Skip(1).Any(char.IsUpper)) continue;
                    for (int i = 0; i < value.Length; i++) result[offset + token.Index + word.Groups[1].Index + i] = true;
                }
            }
            offset += line.Length + 1;
        }
        return result;
    }

    public char? Choose(char intended, double percent)
    {
        var neighbors = GetNeighbors(intended);
        if (neighbors.Length == 0 || random.NextDouble() * 100 >= percent) return null;
        char c = neighbors[random.Next(neighbors.Length)];
        return char.IsUpper(intended) ? char.ToUpperInvariant(c) : c;
    }

    public int CorrectionDelayMs() => random.Next(100, 301);
}
