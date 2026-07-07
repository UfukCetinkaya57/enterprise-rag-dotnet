using System.Text;
using UglyToad.PdfPig;

namespace KurumsalRAG.Infrastructure.Ingestion;

/// <summary>UglyToad.PdfPig ile PDF'ten düz metin çıkarır (sayfa sayfa).</summary>
public sealed class PdfTextExtractor
{
    public string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);

        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            // Kelimeleri okuma sırasında birleştir; boşlukları normalize et.
            var words = page.GetWords().Select(w => w.Text);
            var pageText = string.Join(' ', words);

            if (!string.IsNullOrWhiteSpace(pageText))
                sb.Append(pageText).Append("\n\n");
        }

        return sb.ToString().Trim();
    }
}
