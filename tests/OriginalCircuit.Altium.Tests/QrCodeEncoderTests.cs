using System.Text;
using OriginalCircuit.Altium.Barcodes;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for the ISO/IEC 18004 QR Code encoder. Reference symbols come from the independent <c>segno</c>
/// library (its module placement, Reed-Solomon and format-info are standard; only its automatic mask choice
/// deviates, so references for a specific mask are taken with that mask forced). The encoder's own automatic
/// mask selection uses the standard penalty score — which, for the Coherent Digitiser's payload, selects mask
/// 2, exactly matching Altium's rendered symbol.
/// </summary>
public sealed class QrCodeEncoderTests
{
    // "8CHDIG01-V1R0" at ECC M -> Version 1, the standard penalty selects mask 2 (Altium's symbol).
    private static readonly string[] CoherentRef =
    {
        "#######..#..#.#######",
        "#.....#...#.#.#.....#",
        "#.###.#.####..#.###.#",
        "#.###.#.#.#.#.#.###.#",
        "#.###.#.#..##.#.###.#",
        "#.....#.#.#.#.#.....#",
        "#######.#.#.#.#######",
        "........##.##........",
        "#.#####.....#.#####..",
        "#.##...###..#.##.##..",
        "....#.######.#.#...#.",
        ".#.#...##......#..###",
        "..#..###.###.....###.",
        "........##.##....###.",
        "#######...#.#.....#.#",
        "#.....#.#####...##..#",
        "#.###.#.#.#.#...#.##.",
        "#.###.#.##..#..#.#...",
        "#.###.#.##.#...#.....",
        "#.....#..#.....#....#",
        "#######.#.##...#..#..",
    };

    // "12345" numeric at ECC M, Version 1, mask 4 (forced).
    private static readonly string[] Numeric12345Mask4 =
    {
        "#######.#..##.#######",
        "#.....#..#....#.....#",
        "#.###.#...#.#.#.###.#",
        "#.###.#.#.#...#.###.#",
        "#.###.#.###.#.#.###.#",
        "#.....#.#.##..#.....#",
        "#######.#.#.#.#######",
        "........#####........",
        "#...#.####.#.#####..#",
        "#####...#..##..#.##..",
        ".#..#####..#..###...#",
        "#.#..#.##.#..##.#####",
        ".###.###....###.....#",
        "........##..###...###",
        "#######.#...##...#.#.",
        "#.....#....##..#.#.#.",
        "#.###.#.#..#..###.###",
        "#.###.#...###..#.#.##",
        "#.###.#...##..#####..",
        "#.....#...#..##.#.##.",
        "#######.##..###...###",
    };

    private static string Render(QrCodeSymbol s)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < s.Size; r++)
        {
            for (int c = 0; c < s.Size; c++) sb.Append(s[r, c] ? '#' : '.');
            if (r < s.Size - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public void Encode_CoherentPayload_MatchesAltiumSymbol()
    {
        var s = QrCodeEncoder.Encode("8CHDIG01-V1R0", QrErrorCorrection.Medium);
        Assert.Equal(1, s.Version);
        Assert.Equal(21, s.Size);
        Assert.Equal(2, s.Mask); // standard penalty selects mask 2 — matches Altium
        Assert.Equal(string.Join('\n', CoherentRef), Render(s));
    }

    [Fact]
    public void EncodeForcedMask_Numeric_MatchesReference()
    {
        var s = QrCodeEncoder.EncodeForcedMask("12345", QrErrorCorrection.Medium, 4);
        Assert.Equal(string.Join('\n', Numeric12345Mask4), Render(s));
    }

    [Theory]
    [InlineData("8CHDIG01-V1R0", QrErrorCorrection.Medium, 1)] // alphanumeric, 13 chars
    [InlineData("123456", QrErrorCorrection.Medium, 1)]        // numeric
    [InlineData("https://example.com/foo", QrErrorCorrection.Medium, 2)] // byte mode
    [InlineData("8CHDIG01-V1R0", QrErrorCorrection.High, 2)]   // higher ECC bumps the version
    public void Encode_SelectsExpectedVersion(string text, QrErrorCorrection ecc, int version)
    {
        var s = QrCodeEncoder.Encode(text, ecc);
        Assert.Equal(version, s.Version);
        Assert.Equal(version * 4 + 17, s.Size);
    }

    [Fact]
    public void Encode_PlacesThreeFinderPatterns()
    {
        var s = QrCodeEncoder.Encode("HELLO WORLD", QrErrorCorrection.Medium);
        int n = s.Size;
        foreach (var (r0, c0) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
        {
            // Finder: dark 7x7 border ring with a dark 3x3 centre.
            Assert.True(s[r0, c0] && s[r0, c0 + 6] && s[r0 + 6, c0] && s[r0 + 6, c0 + 6], "finder corners dark");
            Assert.True(s[r0 + 3, c0 + 3], "finder centre dark");
            Assert.False(s[r0 + 1, c0 + 1], "finder inner ring light");
        }
    }

    [Fact]
    public void Encode_ByteMode_IsScannableSize()
    {
        // Byte mode (lowercase/symbols) selects byte encodation; just assert a sane, deterministic symbol.
        var a = QrCodeEncoder.Encode("https://originalcircuit.com", QrErrorCorrection.Medium);
        var b = QrCodeEncoder.Encode("https://originalcircuit.com", QrErrorCorrection.Medium);
        Assert.Equal(Render(a), Render(b));
        Assert.True(a.Size >= 21);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryEncode_NullOrEmpty_ReturnsFalse(string? text)
    {
        Assert.False(QrCodeEncoder.TryEncode(text, QrErrorCorrection.Medium, out var s));
        Assert.Null(s);
    }

    [Fact]
    public void TryEncode_Valid_ReturnsSymbol()
    {
        Assert.True(QrCodeEncoder.TryEncode("8CHDIG01-V1R0", QrErrorCorrection.Medium, out var s));
        Assert.NotNull(s);
        Assert.Equal(21, s!.Size);
    }
}
