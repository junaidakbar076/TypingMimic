using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TypingMimic.Core;

public sealed record PdfExtractionResult(string Text, int PageCount)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public int CharacterCount => Text.EnumerateRunes().Count();
}

// A future OCR extractor can implement the same interface without changing the editor.
public interface IPdfTextExtractor
{
    Task<PdfExtractionResult> ExtractAsync(string path, CancellationToken token);
}

public sealed class PdfTextExtractor : IPdfTextExtractor
{
    public Task<PdfExtractionResult> ExtractAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        using var document = PdfDocument.Open(path);
        var pages = new List<string>();
        foreach (var page in document.GetPages())
        {
            token.ThrowIfCancellationRequested();
            pages.Add(ContentOrderTextExtractor.GetText(page, true).Trim());
        }
        token.ThrowIfCancellationRequested();
        return new PdfExtractionResult(string.Join("\n\n", pages), document.NumberOfPages);
    }, token);
}
