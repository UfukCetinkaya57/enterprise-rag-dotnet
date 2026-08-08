using KurumsalRAG.Infrastructure.Persistence;

namespace KurumsalRAG.Tests;

public sealed class QuestionNormalizerTests
{
    [Theory]
    [InlineData("Kaç gün izin var?", "kaç gün izin var")]
    [InlineData("  KAÇ   GÜN   ", "kaç gün")]
    [InlineData("Uzaktan çalışma!!!", "uzaktan çalışma")]
    public void Normalize_lowercases_trims_and_strips_punctuation(string input, string expected)
        => Assert.Equal(expected, QuestionNormalizer.Normalize(input));

    [Fact]
    public void Equivalent_questions_produce_same_hash()
    {
        var a = QuestionNormalizer.Hash("Yıllık izin kaç gün?");
        var b = QuestionNormalizer.Hash("  yıllık   izin   kaç   gün  ");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_questions_produce_different_hash()
        => Assert.NotEqual(QuestionNormalizer.Hash("izin kaç gün"), QuestionNormalizer.Hash("uzaktan kaç gün"));
}
