using DocGlance.Services.Translation;

namespace DocGlance.Tests.Services.Translation;

public sealed class TranslationTextChunkerTests
{
    [Fact]
    public void Split_AndReassemble_PreserveParagraphsAndPunctuation()
    {
        string text = new string('a', 1_170) + ".\r\n   " + new string('b', 80);

        IReadOnlyList<TranslationTextChunk> chunks = TranslationTextChunker.Split(text);

        chunks.Should().HaveCount(2);
        chunks[0].SeparatorAfter.Should().Be("\r\n   ");
        TranslationTextChunker.Reassemble(chunks, chunks.Select(chunk => chunk.Text).ToArray())
            .Should().Be(text);
    }

    [Fact]
    public void Split_DoesNotSeparateSurrogatePairs()
    {
        string text = new string('a', TranslationTextChunker.MaxChunkCharacters - 1) + "😀" + "tail";

        IReadOnlyList<TranslationTextChunk> chunks = TranslationTextChunker.Split(text);

        string.Join(string.Empty, chunks.Select(chunk => chunk.Text)).Should().Be(text);
        chunks.All(chunk =>
            !char.IsHighSurrogate(chunk.Text[chunk.Text.Length - 1]) &&
            !char.IsLowSurrogate(chunk.Text[0])).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void Split_RejectsEmptyText(string text)
    {
        Action action = () => TranslationTextChunker.Split(text);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Split_RejectsSelectionsAboveTheLimit()
    {
        string text = new('x', TranslationTextChunker.MaxSelectionCharacters + 1);

        Action action = () => TranslationTextChunker.Split(text);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
