using System;
using System.Collections.Generic;

namespace OriginalCircuit.Altium.Barcodes;

/// <summary>
/// An encoded ECC200 Data Matrix symbol: a square (or rectangular) grid of light/dark modules.
/// <see cref="this[int,int]"/> is indexed <c>[row, col]</c> with <c>row 0</c> at the TOP of the symbol and
/// <c>true</c> meaning a dark module. The grid does <em>not</em> include the surrounding quiet zone — that
/// is the caller's responsibility when rendering.
/// </summary>
public sealed class DataMatrixSymbol
{
    private readonly bool[,] _modules;

    internal DataMatrixSymbol(bool[,] modules)
    {
        _modules = modules;
        Rows = modules.GetLength(0);
        Columns = modules.GetLength(1);
    }

    /// <summary>Number of module rows (symbol height in modules), excluding the quiet zone.</summary>
    public int Rows { get; }

    /// <summary>Number of module columns (symbol width in modules), excluding the quiet zone.</summary>
    public int Columns { get; }

    /// <summary>True if the module at <paramref name="row"/> (0 = top) / <paramref name="col"/> (0 = left) is dark.</summary>
    public bool this[int row, int col] => _modules[row, col];

    /// <summary>Returns a copy of the module grid as <c>[row, col]</c> with <c>true</c> = dark.</summary>
    public bool[,] ToArray() => (bool[,])_modules.Clone();
}

/// <summary>
/// Encoder for ISO/IEC 16022 ECC200 Data Matrix symbols. Converts a text string into a grid of dark/light
/// modules using ASCII encodation, automatic (smallest-fitting) square symbol sizing, Reed-Solomon error
/// correction over GF(256), and the standard Annex F module placement.
/// </summary>
/// <remarks>
/// This is the symbology Altium uses for a PCB <c>Text</c> primitive whose
/// <see cref="OriginalCircuit.Altium.Models.Pcb.PcbText.BarCodeType"/> is
/// <see cref="OriginalCircuit.Altium.Models.Pcb.PcbBarCodeKind.DataMatrix"/>. Altium stores only the source
/// text (the module pattern is not persisted), so the renderer encodes the symbol on the fly.
///
/// Only ASCII encodation is implemented (digit pairs, single ASCII bytes, and an upper-shift escape for
/// bytes 128-255). This always produces a valid, scannable symbol; it may occasionally pick a symbol one
/// size larger than an encoder that also uses the C40/Text/Base256 schemes, but for typical part numbers,
/// serial numbers and URLs the result is identical. Only square symbols are emitted (Altium's default).
/// </remarks>
public static class DataMatrixEncoder
{
    // SymRows, SymCols, DataRegRows, DataRegCols, RegionsH, RegionsV, DataCW, ErrCW (square ECC200 sizes,
    // smallest first). Single Reed-Solomon block for every square size through 48x48; larger square sizes
    // interleave, encoded via BlocksFor(). Verified against ISO/IEC 16022, libdmtx and zxing.
    private static readonly int[][] Sizes =
    {
        //  rows cols dRows dCols rH rV  data  ecc
        new[] {  10,  10,   8,   8, 1, 1,    3,    5 },
        new[] {  12,  12,  10,  10, 1, 1,    5,    7 },
        new[] {  14,  14,  12,  12, 1, 1,    8,   10 },
        new[] {  16,  16,  14,  14, 1, 1,   12,   12 },
        new[] {  18,  18,  16,  16, 1, 1,   18,   14 },
        new[] {  20,  20,  18,  18, 1, 1,   22,   18 },
        new[] {  22,  22,  20,  20, 1, 1,   30,   20 },
        new[] {  24,  24,  22,  22, 1, 1,   36,   24 },
        new[] {  26,  26,  24,  24, 1, 1,   44,   28 },
        new[] {  32,  32,  14,  14, 2, 2,   62,   36 },
        new[] {  36,  36,  16,  16, 2, 2,   86,   42 },
        new[] {  40,  40,  18,  18, 2, 2,  114,   48 },
        new[] {  44,  44,  20,  20, 2, 2,  144,   56 },
        new[] {  48,  48,  22,  22, 2, 2,  174,   68 },
        new[] {  52,  52,  24,  24, 2, 2,  204,   84 },
        new[] {  64,  64,  14,  14, 4, 4,  280,  112 },
        new[] {  72,  72,  16,  16, 4, 4,  368,  144 },
        new[] {  80,  80,  18,  18, 4, 4,  456,  192 },
        new[] {  88,  88,  20,  20, 4, 4,  576,  224 },
        new[] {  96,  96,  22,  22, 4, 4,  696,  272 },
        new[] { 104, 104,  24,  24, 4, 4,  816,  336 },
        new[] { 120, 120,  18,  18, 6, 6, 1050,  408 },
        new[] { 132, 132,  20,  20, 6, 6, 1304,  496 },
        new[] { 144, 144,  22,  22, 6, 6, 1558,  620 },
    };

    /// <summary>
    /// Encodes <paramref name="text"/> into an ECC200 Data Matrix symbol.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    /// <exception cref="NotSupportedException">The data is too large for the largest (144x144) square symbol.</exception>
    public static DataMatrixSymbol Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new ArgumentException("Data Matrix text must be non-empty.", nameof(text));

        var data = EncodeAscii(text);

        var size = SelectSize(data.Count)
            ?? throw new NotSupportedException(
                $"Data Matrix payload of {data.Count} codewords exceeds the 1558-codeword capacity of the largest square symbol.");

        int dataCap = size[6], eccCount = size[7];
        Pad(data, dataCap);
        var codewords = AddErrorCorrection(data, eccCount, BlocksFor(size));

        return new DataMatrixSymbol(Place(codewords, size));
    }

    /// <summary>
    /// Tries to encode <paramref name="text"/>; returns false (with <paramref name="symbol"/> null) for null,
    /// empty, or over-capacity input instead of throwing.
    /// </summary>
    public static bool TryEncode(string? text, out DataMatrixSymbol? symbol)
    {
        symbol = null;
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            symbol = Encode(text);
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    // ── ASCII encodation (ISO/IEC 16022 §5.2.3) ─────────────────────────────────────────────────────────
    private static List<byte> EncodeAscii(string text)
    {
        var data = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // Digit pair: two consecutive decimal digits -> one codeword (greedy, checked first).
            if (i + 1 < text.Length && IsDigit(c) && IsDigit(text[i + 1]))
            {
                data.Add((byte)(130 + (c - '0') * 10 + (text[i + 1] - '0')));
                i++;
                continue;
            }
            if (c <= 127)
            {
                data.Add((byte)(c + 1)); // single ASCII byte
            }
            else if (c <= 255)
            {
                data.Add(235);                  // upper-shift escape
                data.Add((byte)(c - 127));      // (c - 128) + 1
            }
            else
            {
                throw new ArgumentException(
                    $"Data Matrix ASCII encodation cannot encode the character U+{(int)c:X4}; only bytes 0-255 are supported.");
            }
        }
        return data;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    // ── Symbol sizing + padding ─────────────────────────────────────────────────────────────────────────
    private static int[]? SelectSize(int dataCount)
    {
        foreach (var s in Sizes)
            if (s[6] >= dataCount) return s;
        return null;
    }

    // Pad to the symbol's data capacity: first pad is the literal 129, the rest use the 253-state randomiser
    // keyed on the pad's 1-based position within the data region.
    private static void Pad(List<byte> data, int dataCap)
    {
        if (data.Count >= dataCap) return;
        data.Add(129);
        while (data.Count < dataCap)
        {
            int position = data.Count + 1;                  // 1-based position of this pad codeword
            int pseudo = ((149 * position) % 253) + 1;
            int value = 129 + pseudo;
            if (value > 254) value -= 254;
            data.Add((byte)value);
        }
    }

    private static int BlocksFor(int[] size)
    {
        // The interleave-block count is fixed per symbol by ISO/IEC 16022. Single block through 48x48.
        return (size[0], size[1]) switch
        {
            (52, 52) => 2,
            (64, 64) => 2,
            (72, 72) => 4,
            (80, 80) => 4,
            (88, 88) => 4,
            (96, 96) => 4,
            (104, 104) => 6,
            (120, 120) => 6,
            (132, 132) => 8,
            (144, 144) => 10,
            _ => 1,
        };
    }

    // ── Reed-Solomon over GF(256), primitive poly 0x12D, generator alpha = 2 ─────────────────────────────
    private static readonly int[] Log = new int[256];
    private static readonly int[] ALog = new int[256];

    static DataMatrixEncoder()
    {
        int v = 1;
        for (int i = 0; i < 255; i++)
        {
            ALog[i] = v;
            Log[v] = i;
            v <<= 1;
            if ((v & 0x100) != 0) v ^= 0x12D;
        }
        ALog[255] = ALog[0]; // unused guard
    }

    private static int Mul(int a, int b) => (a == 0 || b == 0) ? 0 : ALog[(Log[a] + Log[b]) % 255];

    // Generator polynomial coefficients g[0..n], leading (x^n) coefficient g[0] = 1, for roots alpha^1..alpha^n.
    private static int[] GeneratorPoly(int n)
    {
        var g = new int[] { 1 };
        for (int i = 1; i <= n; i++)
        {
            var next = new int[g.Length + 1];
            for (int j = 0; j < g.Length; j++)
            {
                next[j] ^= g[j];                       // g(x) * x
                next[j + 1] ^= Mul(g[j], ALog[i]);     // g(x) * alpha^i
            }
            g = next;
        }
        return g;
    }

    // Reed-Solomon remainder of data(x)*x^n mod g(x); returns n ECC codewords, highest-degree first.
    private static byte[] RsRemainder(IReadOnlyList<byte> data, int n)
    {
        var gen = GeneratorPoly(n);
        var ecc = new byte[n];
        foreach (var d in data)
        {
            int factor = d ^ ecc[0];
            for (int j = 0; j < n - 1; j++)
                ecc[j] = (byte)(ecc[j + 1] ^ Mul(gen[j + 1], factor));
            ecc[n - 1] = (byte)Mul(gen[n], factor);
        }
        return ecc;
    }

    // Splits data into `blocks` interleaved Reed-Solomon blocks (round-robin by stride), computes each
    // block's ECC, then returns the full codeword stream: data in original order, ECC words interleaved.
    private static byte[] AddErrorCorrection(IReadOnlyList<byte> data, int eccTotal, int blocks)
    {
        int eccPerBlock = eccTotal / blocks;
        var blockEcc = new byte[blocks][];
        for (int b = 0; b < blocks; b++)
        {
            var blockData = new List<byte>();
            for (int i = b; i < data.Count; i += blocks) blockData.Add(data[i]);
            blockEcc[b] = RsRemainder(blockData, eccPerBlock);
        }

        var stream = new byte[data.Count + eccTotal];
        for (int i = 0; i < data.Count; i++) stream[i] = data[i];
        int pos = data.Count;
        for (int j = 0; j < eccPerBlock; j++)
            for (int b = 0; b < blocks; b++)
                stream[pos++] = blockEcc[b][j];
        return stream;
    }

    // ── Module placement (ISO/IEC 16022 Annex F) + finder/timing assembly ──────────────────────────────
    private static bool[,] Place(byte[] codewords, int[] size)
    {
        int symRows = size[0], symCols = size[1];
        int dRows = size[2], dCols = size[3];
        int regH = size[4], regV = size[5];

        int nrow = regV * dRows; // mapping-matrix (data-region interiors concatenated)
        int ncol = regH * dCols;
        var bits = new sbyte[nrow * ncol];
        Array.Fill(bits, (sbyte)-1);

        void Module(int row, int col, int chr, int bit)
        {
            if (row < 0) { row += nrow; col += 4 - ((nrow + 4) % 8); }
            if (col < 0) { col += ncol; row += 4 - ((ncol + 4) % 8); }
            // chr is 1-based; bit 1 = MSB (0x80) ... bit 8 = LSB (0x01).
            bits[row * ncol + col] = (sbyte)((codewords[chr - 1] >> (8 - bit)) & 1);
        }

        void Utah(int row, int col, int chr)
        {
            Module(row - 2, col - 2, chr, 1);
            Module(row - 2, col - 1, chr, 2);
            Module(row - 1, col - 2, chr, 3);
            Module(row - 1, col - 1, chr, 4);
            Module(row - 1, col, chr, 5);
            Module(row, col - 2, chr, 6);
            Module(row, col - 1, chr, 7);
            Module(row, col, chr, 8);
        }

        void Corner1(int chr)
        {
            Module(nrow - 1, 0, chr, 1); Module(nrow - 1, 1, chr, 2); Module(nrow - 1, 2, chr, 3);
            Module(0, ncol - 2, chr, 4); Module(0, ncol - 1, chr, 5); Module(1, ncol - 1, chr, 6);
            Module(2, ncol - 1, chr, 7); Module(3, ncol - 1, chr, 8);
        }

        void Corner2(int chr)
        {
            Module(nrow - 3, 0, chr, 1); Module(nrow - 2, 0, chr, 2); Module(nrow - 1, 0, chr, 3);
            Module(0, ncol - 4, chr, 4); Module(0, ncol - 3, chr, 5); Module(0, ncol - 2, chr, 6);
            Module(0, ncol - 1, chr, 7); Module(1, ncol - 1, chr, 8);
        }

        void Corner3(int chr)
        {
            Module(nrow - 3, 0, chr, 1); Module(nrow - 2, 0, chr, 2); Module(nrow - 1, 0, chr, 3);
            Module(0, ncol - 2, chr, 4); Module(0, ncol - 1, chr, 5); Module(1, ncol - 1, chr, 6);
            Module(2, ncol - 1, chr, 7); Module(3, ncol - 1, chr, 8);
        }

        void Corner4(int chr)
        {
            Module(nrow - 1, 0, chr, 1); Module(nrow - 1, ncol - 1, chr, 2); Module(0, ncol - 3, chr, 3);
            Module(0, ncol - 2, chr, 4); Module(0, ncol - 1, chr, 5); Module(1, ncol - 3, chr, 6);
            Module(1, ncol - 2, chr, 7); Module(1, ncol - 1, chr, 8);
        }

        int chr = 1, r = 4, c = 0;
        do
        {
            if (r == nrow && c == 0) Corner1(chr++);
            if (r == nrow - 2 && c == 0 && ncol % 4 != 0) Corner2(chr++);
            if (r == nrow - 2 && c == 0 && ncol % 8 == 4) Corner3(chr++);
            if (r == nrow + 4 && c == 2 && ncol % 8 == 0) Corner4(chr++);

            do // sweep up-right
            {
                if (r < nrow && c >= 0 && bits[r * ncol + c] < 0) Utah(r, c, chr++);
                r -= 2; c += 2;
            } while (r >= 0 && c < ncol);
            r += 1; c += 3;

            do // sweep down-left
            {
                if (r >= 0 && c < ncol && bits[r * ncol + c] < 0) Utah(r, c, chr++);
                r += 2; c -= 2;
            } while (r < nrow && c >= 0);
            r += 3; c += 1;
        } while (r < nrow || c < ncol);

        // The unvisited bottom-right corner of certain symbols carries a fixed pattern.
        if (bits[nrow * ncol - 1] < 0)
        {
            bits[nrow * ncol - 1] = 1;
            bits[nrow * ncol - ncol - 2] = 1;
        }

        // Assemble the final symbol: tile regV x regH data regions, each wrapped in a finder/timing border.
        var symbol = new bool[symRows, symCols];
        for (int mr = 0; mr < nrow; mr++)
        {
            int regionRow = mr / dRows, localRow = mr % dRows;
            int rowBase = regionRow * (dRows + 2);
            for (int mc = 0; mc < ncol; mc++)
            {
                int regionCol = mc / dCols, localCol = mc % dCols;
                int colBase = regionCol * (dCols + 2);
                symbol[rowBase + 1 + localRow, colBase + 1 + localCol] = bits[mr * ncol + mc] == 1;
            }
        }

        // Finder pattern / timing track around each data region.
        for (int regionRow = 0; regionRow < regV; regionRow++)
        for (int regionCol = 0; regionCol < regH; regionCol++)
        {
            int r0 = regionRow * (dRows + 2);
            int c0 = regionCol * (dCols + 2);
            int rBot = r0 + dRows + 1;
            int cRight = c0 + dCols + 1;
            for (int rr = r0; rr <= rBot; rr++)
            {
                symbol[rr, c0] = true;                            // solid left leg
                symbol[rr, cRight] = ((rr - r0) & 1) == 1;        // right timing: dark on odd local row
            }
            for (int cc = c0; cc <= cRight; cc++)
            {
                symbol[rBot, cc] = true;                          // solid bottom leg
                symbol[r0, cc] = ((cc - c0) & 1) == 0;            // top timing: dark on even local col
            }
        }

        return symbol;
    }
}
