using System.Text;
using UglyToad.PdfPig;

namespace KurumsalRAG.Infrastructure.Ingestion;

/// <summary>UglyToad.PdfPig ile PDF'ten düz metin çıkarır (sayfa sayfa) + sayfa sayısı.</summary>
public sealed class PdfTextExtractor
{
    /// <param name="maxPages">Aşılırsa hata fırlatılır (upload sertleştirme). 0 = sınırsız.</param>
    public PdfExtractResult ExtractText(Stream pdfStream, int maxPages = 0)
    {
        using var document = PdfDocument.Open(pdfStream);

        if (maxPages > 0 && document.NumberOfPages > maxPages)
            throw new InvalidOperationException(
                $"PDF {document.NumberOfPages} sayfa; izin verilen üst sınır {maxPages} sayfa.");

        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            // Kelimeleri okuma sırasında birleştir; boşlukları normalize et.
            var words = page.GetWords().Select(w => w.Text);
            var pageText = string.Join(' ', words);

            if (!string.IsNullOrWhiteSpace(pageText))
                sb.Append(pageText).Append("\n\n");
        }

        return new PdfExtractResult(sb.ToString().Trim(), document.NumberOfPages);
    }
}

public sealed record PdfExtractResult(string Text, int PageCount);
