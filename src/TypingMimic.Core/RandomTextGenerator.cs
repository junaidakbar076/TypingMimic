using System.Text;

namespace TypingMimic.Core;

public sealed class RandomTextGenerator(Random? random = null)
{
    private readonly Random random = random ?? Random.Shared;
    public const int DefaultLength = 1000;
    public const int MaximumLength = 100000;
    // A bundled, curated English vocabulary: no downloads or network access.
    public static IReadOnlyList<string> Words { get; } = Array.AsReadOnly("""
        a about above across action active after again air almost along also always
        among another answer any apple area around arrive art ask autumn away
        back balance ball bank beach bear beautiful because become before begin
        behind below best better between bird blue boat book both box branch bread
        break bridge bright bring brook brown build busy butterfly by
        calm camera can candle carry cat center chair change chapter check child
        choose city clear climb clock close cloud coast cold color come common
        corner could country create curious
        dance dark day deep desk develop different discover distance dog door down
        draw dream drink drive during each early earth east easy eat edge eight
        either end enjoy enough enter evening every example explore eye
        face fact fall family far farm fast favorite feel field find fine fire
        first fish five floor flower fly follow food forest form forward four
        free fresh friend from front full future
        game garden gentle get gift give glass go gold good grass great green
        ground group grow guide
        half hand happy harbor hard have head hear heart heavy help here high
        hill history hold home hope horse hot hour house how human
        idea image important in inside into island it its
        join journey joy jump just keep key kind kitchen know lake land large
        last late laugh leaf learn leave left lemon less lesson let letter life
        light like line listen little live local long look love low lunch
        made make many map market may meadow mean meet memory middle might mile
        mind minute mirror moment money moon more morning most mother mountain
        move much music must my name narrow nature near need neighbor never new
        next nice night nine no north note now number
        ocean of off often old on once one only open orange order other our out
        outside over own page paint paper park part path pattern pause people
        person picture place plain plan plant play please point pond possible
        practice present pretty public pull put question quick quiet quite
        rain read ready real reason red remember rest return rich ride right
        river road rock room round rule run
        safe same sand save say school sea search season second see seed seem
        sentence seven shade shape share she sheep shine ship shoe shop short
        should show side simple since sing sister sit six size sky sleep slow
        small smile snow so soft some song soon sound south space speak special
        spring square stand star start stay step still stone story street strong
        study such summer sun sweet swim system
        table take talk tall tea teach team tell ten than thank that the their
        them then there these they thing think third this those thought three
        through time tiny to today together tomorrow too tool top town track
        train travel tree true try turn two under understand until up upon us
        use useful valley value very view village visit voice wait walk wall
        want warm watch water wave way we weather week welcome well went were
        west what when where which while white who whole why wide wild will
        wind window winter wish with without woman wonder wood word work world
        would write year yellow yes yesterday yet you young your
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static readonly int LongestWord = Words.Max(word => word.Length);
    private static readonly string[][] FittingWords = Enumerable.Range(0, LongestWord + 1)
        .Select(length => Words.Where(word => word.Length <= length).ToArray()).ToArray();

    public static void ValidateLength(int length)
    {
        if (length < 1 || length > MaximumLength)
            throw new ArgumentException($"Random character count must be between 1 and {MaximumLength}.");
    }

    public string Generate(int length, bool preserveLineBreaks)
    {
        ValidateLength(length);
        var text = new StringBuilder(length);
        while (text.Length < length)
        {
            // Pick a complete word that fits; never truncate the final word.
            var candidates = FittingWords[Math.Min(length - text.Length, LongestWord)];
            text.Append(candidates[random.Next(candidates.Length)]);
            if (text.Length == length) break;
            int separator = random.Next(12);
            text.Append(separator switch
            {
                0 => ',', 1 => '.', 2 => '?', 3 => '!',
                4 when preserveLineBreaks => '\n',
                _ => ' '
            });
            if (separator < 4 && text.Length < length) text.Append(' ');
        }
        return text.ToString();
    }
}
