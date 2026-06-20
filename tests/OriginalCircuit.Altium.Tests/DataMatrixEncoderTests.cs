using System.Text;
using OriginalCircuit.Altium.Barcodes;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for the ISO/IEC 16022 ECC200 Data Matrix encoder. The reference symbols were produced by the
/// independent <c>ppf-datamatrix</c> Python library and cross-checked against a Reed-Solomon reference
/// (codewords for "123456" are [142,164,186] data + [114,25,5,88,102] ECC), so a byte-for-byte match here
/// validates the whole pipeline: ASCII encodation, symbol sizing, Reed-Solomon, placement and finder/timing.
/// </summary>
public sealed class DataMatrixEncoderTests
{
    // "123456" -> three digit-pair codewords [142,164,186] -> smallest square (10x10).
    private static readonly string[] Ref123456 =
    {
        "#.#.#.#.#.",
        "##..#.##.#",
        "##.....#..",
        "##...###.#",
        "##....#...",
        "#.....####",
        "###.##....",
        "####.##..#",
        "#..###.#..",
        "##########",
    };

    // The Coherent Digitiser's barcode payload -> 12 data codewords -> 16x16.
    private static readonly string[] RefCoherent =
    {
        "#.#.#.#.#.#.#.#.",
        "#.#..#......####",
        "#....##.#.#.....",
        "##....#.....#..#",
        "##.#.#.##...#.#.",
        "##..#.#..###...#",
        "#.....#...##.##.",
        "#.....##.#.....#",
        "#####...###..##.",
        "#.#.#..#..#..#.#",
        "#.#..#####..#...",
        "####..#.###.#.##",
        "#.###.#.#.#####.",
        "#...#.##..####.#",
        "###.#..#.#.##.#.",
        "################",
    };

    // "1234567890" -> five digit-pair codewords -> 12x12.
    private static readonly string[] Ref1234567890 =
    {
        "#.#.#.#.#.#.",
        "##..#..#.#.#",
        "##..##......",
        "##...###.###",
        "##..#...##..",
        "#.#......#.#",
        "##.#.###....",
        "###.##.##..#",
        "#####..#..#.",
        "##.###.###.#",
        "######.#..#.",
        "############",
    };

    private static string Render(DataMatrixSymbol s)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < s.Rows; r++)
        {
            for (int c = 0; c < s.Columns; c++) sb.Append(s[r, c] ? '#' : '.');
            if (r < s.Rows - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void AssertMatches(string[] expected, DataMatrixSymbol actual)
    {
        Assert.Equal(expected.Length, actual.Rows);
        Assert.Equal(expected[0].Length, actual.Columns);
        Assert.Equal(string.Join('\n', expected), Render(actual));
    }

    [Fact]
    public void Encode_123456_MatchesReference()
        => AssertMatches(Ref123456, DataMatrixEncoder.Encode("123456"));

    [Fact]
    public void Encode_CoherentPayload_Matches16x16Reference()
        => AssertMatches(RefCoherent, DataMatrixEncoder.Encode("8CHDIG01-V1R0"));

    [Fact]
    public void Encode_1234567890_MatchesReference()
        => AssertMatches(Ref1234567890, DataMatrixEncoder.Encode("1234567890"));

    [Theory]
    [InlineData("1", 10)]          // 1 codeword -> 10x10 (cap 3)
    [InlineData("123456", 10)]     // 3 codewords -> 10x10
    [InlineData("ABCD", 12)]       // 4 codewords -> 12x12 (cap 5)
    [InlineData("8CHDIG01-V1R0", 16)] // 12 codewords -> 16x16 (cap 12)
    [InlineData("ABCDEFGHIJKLM", 18)] // 13 codewords -> 18x18 (cap 18)
    public void Encode_SelectsSmallestFittingSquare(string text, int expectedSize)
    {
        var s = DataMatrixEncoder.Encode(text);
        Assert.Equal(expectedSize, s.Rows);
        Assert.Equal(s.Rows, s.Columns); // square
    }

    [Theory]
    [InlineData(62, 32)]   // 32x32: 2x2 data regions, 1 RS block
    [InlineData(204, 52)]  // 52x52: 2x2 regions, 2 interleaved RS blocks
    [InlineData(368, 72)]  // 72x72: 4x4 regions, 4 interleaved RS blocks
    public void Encode_LargePayload_SelectsMultiRegionSquare(int length, int size)
    {
        // Highly-mixed printable ASCII (resists C40), so the symbol size is driven purely by codeword count;
        // exercises the multi-data-region placement and the interleaved Reed-Solomon block path.
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append((char)(33 + i * 7 % 94));
        var s = DataMatrixEncoder.Encode(sb.ToString());

        Assert.Equal(size, s.Rows);
        Assert.Equal(size, s.Columns);
        Assert.True(s[size - 1, 0] && s[size - 1, size - 1], "solid bottom finder leg present");
    }

    [Fact]
    public void Encode_HasCanonicalFinderPattern()
    {
        var s = DataMatrixEncoder.Encode("HELLO WORLD");
        int n = s.Rows;
        for (int r = 0; r < n; r++)
        {
            Assert.True(s[r, 0], "left column must be solid (finder L)");
            Assert.Equal((r & 1) == 1, s[r, n - 1]); // right timing: dark on odd row
        }
        for (int c = 0; c < n; c++)
        {
            Assert.True(s[n - 1, c], "bottom row must be solid (finder L)");
            Assert.Equal((c & 1) == 0, s[0, c]); // top timing: dark on even column
        }
    }

    [Fact]
    public void Encode_DigitPairing_ShrinksSymbol()
    {
        // Ten digits pair into five codewords (12x12); ten letters are ten codewords (16x16).
        Assert.Equal(12, DataMatrixEncoder.Encode("1234567890").Rows);
        Assert.Equal(16, DataMatrixEncoder.Encode("ABCDEFGHIJ").Rows);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        var a = DataMatrixEncoder.Encode("SERIAL-42");
        var b = DataMatrixEncoder.Encode("SERIAL-42");
        Assert.Equal(Render(a), Render(b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryEncode_NullOrEmpty_ReturnsFalse(string? text)
    {
        Assert.False(DataMatrixEncoder.TryEncode(text, out var s));
        Assert.Null(s);
    }

    [Fact]
    public void TryEncode_ValidText_ReturnsSymbol()
    {
        Assert.True(DataMatrixEncoder.TryEncode("8CHDIG01-V1R0", out var s));
        Assert.NotNull(s);
        Assert.Equal(16, s!.Rows);
    }

    [Fact]
    public void Encode_Empty_Throws() => Assert.Throws<ArgumentException>(() => DataMatrixEncoder.Encode(""));
}
