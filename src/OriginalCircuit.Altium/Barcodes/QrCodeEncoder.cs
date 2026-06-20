using System;
using System.Collections.Generic;
using System.Text;

namespace OriginalCircuit.Altium.Barcodes;

/// <summary>QR Code error-correction level (recoverable data fraction): L≈7%, M≈15%, Q≈25%, H≈30%.</summary>
public enum QrErrorCorrection
{
    /// <summary>Low (~7% recovery).</summary>
    Low = 0,
    /// <summary>Medium (~15% recovery).</summary>
    Medium = 1,
    /// <summary>Quartile (~25% recovery).</summary>
    Quartile = 2,
    /// <summary>High (~30% recovery).</summary>
    High = 3
}

/// <summary>
/// An encoded QR Code symbol: a square grid of light/dark modules. <see cref="this[int,int]"/> is indexed
/// <c>[row, col]</c> with <c>row 0</c> at the top and <c>true</c> meaning a dark module. The grid excludes the
/// surrounding quiet zone.
/// </summary>
public sealed class QrCodeSymbol
{
    private readonly bool[,] _modules;

    internal QrCodeSymbol(bool[,] modules, int version, QrErrorCorrection ecc, int mask)
    {
        _modules = modules;
        Size = modules.GetLength(0);
        Version = version;
        ErrorCorrection = ecc;
        Mask = mask;
    }

    /// <summary>Module count per side (21 for version 1, +4 per version).</summary>
    public int Size { get; }

    /// <summary>QR version (1–40).</summary>
    public int Version { get; }

    /// <summary>The error-correction level used.</summary>
    public QrErrorCorrection ErrorCorrection { get; }

    /// <summary>The data-mask pattern (0–7) selected.</summary>
    public int Mask { get; }

    /// <summary>Module rows (== <see cref="Size"/>).</summary>
    public int Rows => Size;

    /// <summary>Module columns (== <see cref="Size"/>).</summary>
    public int Columns => Size;

    /// <summary>True if the module at <paramref name="row"/> (0 = top) / <paramref name="col"/> (0 = left) is dark.</summary>
    public bool this[int row, int col] => _modules[row, col];

    /// <summary>Returns a copy of the module grid as <c>[row, col]</c> with <c>true</c> = dark.</summary>
    public bool[,] ToArray() => (bool[,])_modules.Clone();
}

/// <summary>
/// Encoder for ISO/IEC 18004 QR Code symbols (model 2). Selects the smallest version that fits the data at the
/// requested error-correction level, encodes using numeric / alphanumeric / byte mode, adds Reed-Solomon error
/// correction, lays out the function and data modules, and applies the lowest-penalty data mask.
/// </summary>
/// <remarks>
/// This is the symbology Altium uses for a PCB <c>Text</c> primitive whose
/// <see cref="OriginalCircuit.Altium.Models.Pcb.PcbText.BarCodeType"/> is
/// <see cref="OriginalCircuit.Altium.Models.Pcb.PcbBarCodeKind.QrCode"/>. The module pattern is not stored in
/// the file, so the renderer encodes it on the fly. Kanji and ECI modes are not implemented (byte mode covers
/// the remaining cases). Output is verified byte-identical to the <c>segno</c> reference QR library.
/// </remarks>
public static class QrCodeEncoder
{
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    // Per version (1..40), four error levels (L,M,Q,H), five ints: ecPerBlock, g1Blocks, g1Data, g2Blocks, g2Data.
    private static readonly int[,] Ecc =
    {
        { 7,1,19,0,0, 10,1,16,0,0, 13,1,13,0,0, 17,1,9,0,0 },
        { 10,1,34,0,0, 16,1,28,0,0, 22,1,22,0,0, 28,1,16,0,0 },
        { 15,1,55,0,0, 26,1,44,0,0, 18,2,17,0,0, 22,2,13,0,0 },
        { 20,1,80,0,0, 18,2,32,0,0, 26,2,24,0,0, 16,4,9,0,0 },
        { 26,1,108,0,0, 24,2,43,0,0, 18,2,15,2,16, 22,2,11,2,12 },
        { 18,2,68,0,0, 16,4,27,0,0, 24,4,19,0,0, 28,4,15,0,0 },
        { 20,2,78,0,0, 18,4,31,0,0, 18,2,14,4,15, 26,4,13,1,14 },
        { 24,2,97,0,0, 22,2,38,2,39, 22,4,18,2,19, 26,4,14,2,15 },
        { 30,2,116,0,0, 22,3,36,2,37, 20,4,16,4,17, 24,4,12,4,13 },
        { 18,2,68,2,69, 26,4,43,1,44, 24,6,19,2,20, 28,6,15,2,16 },
        { 20,4,81,0,0, 30,1,50,4,51, 28,4,22,4,23, 24,3,12,8,13 },
        { 24,2,92,2,93, 22,6,36,2,37, 26,4,20,6,21, 28,7,14,4,15 },
        { 26,4,107,0,0, 22,8,37,1,38, 24,8,20,4,21, 22,12,11,4,12 },
        { 30,3,115,1,116, 24,4,40,5,41, 20,11,16,5,17, 24,11,12,5,13 },
        { 22,5,87,1,88, 24,5,41,5,42, 30,5,24,7,25, 24,11,12,7,13 },
        { 24,5,98,1,99, 28,7,45,3,46, 24,15,19,2,20, 30,3,15,13,16 },
        { 28,1,107,5,108, 28,10,46,1,47, 28,1,22,15,23, 28,2,14,17,15 },
        { 30,5,120,1,121, 26,9,43,4,44, 28,17,22,1,23, 28,2,14,19,15 },
        { 28,3,113,4,114, 26,3,44,11,45, 26,17,21,4,22, 26,9,13,16,14 },
        { 28,3,107,5,108, 26,3,41,13,42, 30,15,24,5,25, 28,15,15,10,16 },
        { 28,4,116,4,117, 26,17,42,0,0, 28,17,22,6,23, 30,19,16,6,17 },
        { 28,2,111,7,112, 28,17,46,0,0, 30,7,24,16,25, 24,34,13,0,0 },
        { 30,4,121,5,122, 28,4,47,14,48, 30,11,24,14,25, 30,16,15,14,16 },
        { 30,6,117,4,118, 28,6,45,14,46, 30,11,24,16,25, 30,30,16,2,17 },
        { 26,8,106,4,107, 28,8,47,13,48, 30,7,24,22,25, 30,22,15,13,16 },
        { 28,10,114,2,115, 28,19,46,4,47, 28,28,22,6,23, 30,33,16,4,17 },
        { 30,8,122,4,123, 28,22,45,3,46, 30,8,23,26,24, 30,12,15,28,16 },
        { 30,3,117,10,118, 28,3,45,23,46, 30,4,24,31,25, 30,11,15,31,16 },
        { 30,7,116,7,117, 28,21,45,7,46, 30,1,23,37,24, 30,19,15,26,16 },
        { 30,5,115,10,116, 28,19,47,10,48, 30,15,24,25,25, 30,23,15,25,16 },
        { 30,13,115,3,116, 28,2,46,29,47, 30,42,24,1,25, 30,23,15,28,16 },
        { 30,17,115,0,0, 28,10,46,23,47, 30,10,24,35,25, 30,19,15,35,16 },
        { 30,17,115,1,116, 28,14,46,21,47, 30,29,24,19,25, 30,11,15,46,16 },
        { 30,13,115,6,116, 28,14,46,23,47, 30,44,24,7,25, 30,59,16,1,17 },
        { 30,12,121,7,122, 28,12,47,26,48, 30,39,24,14,25, 30,22,15,41,16 },
        { 30,6,121,14,122, 28,6,47,34,48, 30,46,24,10,25, 30,2,15,64,16 },
        { 30,17,122,4,123, 28,29,46,14,47, 30,49,24,10,25, 30,24,15,46,16 },
        { 30,4,122,18,123, 28,13,46,32,47, 30,48,24,14,25, 30,42,15,32,16 },
        { 30,20,117,4,118, 28,40,47,7,48, 30,43,24,22,25, 30,10,15,67,16 },
        { 30,19,118,6,119, 28,18,47,31,48, 30,34,24,34,25, 30,20,15,61,16 },
    };

    // Alignment-pattern centre coordinates per version (version 1 has none).
    private static readonly int[][] AlignmentPositions =
    {
        new int[]{}, new[]{6,18}, new[]{6,22}, new[]{6,26}, new[]{6,30}, new[]{6,34},
        new[]{6,22,38}, new[]{6,24,42}, new[]{6,26,46}, new[]{6,28,50}, new[]{6,30,54},
        new[]{6,32,58}, new[]{6,34,62}, new[]{6,26,46,66}, new[]{6,26,48,70}, new[]{6,26,50,74},
        new[]{6,30,54,78}, new[]{6,30,56,82}, new[]{6,30,58,86}, new[]{6,34,62,90},
        new[]{6,28,50,72,94}, new[]{6,26,50,74,98}, new[]{6,30,54,78,102}, new[]{6,28,54,80,106},
        new[]{6,32,58,84,110}, new[]{6,30,58,86,114}, new[]{6,34,62,90,118}, new[]{6,26,50,74,98,122},
        new[]{6,30,54,78,102,126}, new[]{6,26,52,78,104,130}, new[]{6,30,56,82,108,134},
        new[]{6,34,60,86,112,138}, new[]{6,30,58,86,114,142}, new[]{6,34,62,90,118,146},
        new[]{6,30,54,78,102,126,150}, new[]{6,24,50,76,102,128,154}, new[]{6,28,54,80,106,132,158},
        new[]{6,32,58,84,110,136,162}, new[]{6,26,54,82,110,138,166}, new[]{6,30,58,86,114,142,170},
    };

    /// <summary>
    /// Encodes <paramref name="text"/> into a QR Code symbol at the given error-correction level.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    /// <exception cref="NotSupportedException">The data is too large for the largest (version 40) symbol.</exception>
    public static QrCodeSymbol Encode(string text, QrErrorCorrection ecc = QrErrorCorrection.Medium)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new ArgumentException("QR Code text must be non-empty.", nameof(text));

        var mode = SelectMode(text);
        var bytes = mode == Mode.Byte ? Encoding.UTF8.GetBytes(text) : null;
        int dataLen = mode == Mode.Byte ? bytes!.Length : text.Length;

        int version = SelectVersion(mode, text, dataLen, ecc)
            ?? throw new NotSupportedException(
                "QR payload is too large for a version-40 symbol at the requested error-correction level.");

        var bits = BuildBitStream(text, bytes, mode, version, ecc);
        var codewords = AddErrorCorrection(bits, version, ecc);
        return BuildMatrix(codewords, version, ecc, null);
    }

    // Test hook: encode with a specific data mask (0-7) rather than the lowest-penalty choice.
    internal static QrCodeSymbol EncodeForcedMask(string text, QrErrorCorrection ecc, int mask)
    {
        var mode = SelectMode(text);
        var bytes = mode == Mode.Byte ? Encoding.UTF8.GetBytes(text) : null;
        int dataLen = mode == Mode.Byte ? bytes!.Length : text.Length;
        int version = SelectVersion(mode, text, dataLen, ecc)
            ?? throw new NotSupportedException("QR payload is too large for a version-40 symbol.");
        var codewords = AddErrorCorrection(BuildBitStream(text, bytes, mode, version, ecc), version, ecc);
        return BuildMatrix(codewords, version, ecc, mask);
    }

    /// <summary>Tries to encode <paramref name="text"/>; returns false for null, empty, or over-capacity input.</summary>
    public static bool TryEncode(string? text, QrErrorCorrection ecc, out QrCodeSymbol? symbol)
    {
        symbol = null;
        if (string.IsNullOrEmpty(text)) return false;
        try { symbol = Encode(text, ecc); return true; }
        catch (Exception e) when (e is NotSupportedException or ArgumentException) { return false; }
    }

    private enum Mode { Numeric = 1, Alphanumeric = 2, Byte = 4 }

    private static Mode SelectMode(string text)
    {
        bool numeric = true, alpha = true;
        foreach (var c in text)
        {
            if (c is < '0' or > '9') numeric = false;
            if (Alphanumeric.IndexOf(c) < 0) alpha = false;
        }
        return numeric ? Mode.Numeric : alpha ? Mode.Alphanumeric : Mode.Byte;
    }

    private static int CharCountBits(Mode mode, int version)
    {
        int group = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        return mode switch
        {
            Mode.Numeric => new[] { 10, 12, 14 }[group],
            Mode.Alphanumeric => new[] { 9, 11, 13 }[group],
            _ => new[] { 8, 16, 16 }[group],
        };
    }

    private static int DataCodewords(int version, QrErrorCorrection ecc)
    {
        int b = (int)ecc * 5;
        return Ecc[version - 1, b + 1] * Ecc[version - 1, b + 2] + Ecc[version - 1, b + 3] * Ecc[version - 1, b + 4];
    }

    private static int DataBitCount(Mode mode, string text, int dataLen)
    {
        return mode switch
        {
            Mode.Numeric => 10 * (dataLen / 3) + (dataLen % 3 == 1 ? 4 : dataLen % 3 == 2 ? 7 : 0),
            Mode.Alphanumeric => 11 * (dataLen / 2) + (dataLen % 2 == 1 ? 6 : 0),
            _ => 8 * dataLen,
        };
    }

    private static int? SelectVersion(Mode mode, string text, int dataLen, QrErrorCorrection ecc)
    {
        for (int v = 1; v <= 40; v++)
        {
            int needed = 4 + CharCountBits(mode, v) + DataBitCount(mode, text, dataLen);
            if (needed <= DataCodewords(v, ecc) * 8) return v;
        }
        return null;
    }

    // ── Bit stream (mode + count + data + terminator + padding) ──────────────────────────────────────────
    private sealed class BitWriter
    {
        public readonly List<bool> Bits = new();
        public void Add(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) Bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static byte[] BuildBitStream(string text, byte[]? bytes, Mode mode, int version, QrErrorCorrection ecc)
    {
        int dataLen = mode == Mode.Byte ? bytes!.Length : text.Length;
        var w = new BitWriter();
        w.Add((int)mode, 4);
        w.Add(dataLen, CharCountBits(mode, version));

        switch (mode)
        {
            case Mode.Numeric:
                for (int i = 0; i < text.Length; i += 3)
                {
                    int n = Math.Min(3, text.Length - i);
                    int val = int.Parse(text.Substring(i, n));
                    w.Add(val, n == 3 ? 10 : n == 2 ? 7 : 4);
                }
                break;
            case Mode.Alphanumeric:
                for (int i = 0; i < text.Length; i += 2)
                {
                    if (i + 1 < text.Length)
                        w.Add(Alphanumeric.IndexOf(text[i]) * 45 + Alphanumeric.IndexOf(text[i + 1]), 11);
                    else
                        w.Add(Alphanumeric.IndexOf(text[i]), 6);
                }
                break;
            default:
                foreach (var b in bytes!) w.Add(b, 8);
                break;
        }

        int capacityBits = DataCodewords(version, ecc) * 8;
        // Terminator: up to four 0 bits, not exceeding capacity.
        int terminator = Math.Min(4, capacityBits - w.Bits.Count);
        for (int i = 0; i < terminator; i++) w.Bits.Add(false);
        // Pad to a byte boundary.
        while (w.Bits.Count % 8 != 0) w.Bits.Add(false);

        var data = new List<byte>();
        for (int i = 0; i < w.Bits.Count; i += 8)
        {
            int b = 0;
            for (int j = 0; j < 8; j++) b = (b << 1) | (w.Bits[i + j] ? 1 : 0);
            data.Add((byte)b);
        }
        // Pad bytes: 0xEC, 0x11 alternating, to the data capacity.
        bool ec = true;
        while (data.Count < DataCodewords(version, ecc)) { data.Add(ec ? (byte)0xEC : (byte)0x11); ec = !ec; }
        return data.ToArray();
    }

    // ── Reed-Solomon over GF(256), primitive poly 0x11D, generator alpha = 2 ─────────────────────────────
    private static readonly int[] Log = new int[256];
    private static readonly int[] ALog = new int[256];

    static QrCodeEncoder()
    {
        int v = 1;
        for (int i = 0; i < 255; i++) { ALog[i] = v; Log[v] = i; v <<= 1; if ((v & 0x100) != 0) v ^= 0x11D; }
    }

    private static int Mul(int a, int b) => (a == 0 || b == 0) ? 0 : ALog[(Log[a] + Log[b]) % 255];

    private static int[] GeneratorPoly(int n)
    {
        var g = new[] { 1 };
        for (int i = 0; i < n; i++)
        {
            var next = new int[g.Length + 1];
            for (int j = 0; j < g.Length; j++)
            {
                next[j] ^= g[j];
                next[j + 1] ^= Mul(g[j], ALog[i]);
            }
            g = next;
        }
        return g;
    }

    private static byte[] RsRemainder(IReadOnlyList<byte> data, int n)
    {
        var gen = GeneratorPoly(n);
        var ecc = new byte[n];
        foreach (var d in data)
        {
            int factor = d ^ ecc[0];
            for (int j = 0; j < n - 1; j++) ecc[j] = (byte)(ecc[j + 1] ^ Mul(gen[j + 1], factor));
            ecc[n - 1] = (byte)Mul(gen[n], factor);
        }
        return ecc;
    }

    // Splits the data into the version/level block layout, computes each block's ECC, then interleaves both.
    private static byte[] AddErrorCorrection(byte[] data, int version, QrErrorCorrection ecc)
    {
        int b = (int)ecc * 5;
        int ecPerBlock = Ecc[version - 1, b];
        int g1c = Ecc[version - 1, b + 1], g1d = Ecc[version - 1, b + 2];
        int g2c = Ecc[version - 1, b + 3], g2d = Ecc[version - 1, b + 4];

        var dataBlocks = new List<byte[]>();
        var ecBlocks = new List<byte[]>();
        int pos = 0;
        void AddBlocks(int count, int len)
        {
            for (int i = 0; i < count; i++)
            {
                var block = new byte[len];
                Array.Copy(data, pos, block, 0, len);
                pos += len;
                dataBlocks.Add(block);
                ecBlocks.Add(RsRemainder(block, ecPerBlock));
            }
        }
        AddBlocks(g1c, g1d);
        AddBlocks(g2c, g2d);

        var stream = new List<byte>();
        int maxData = Math.Max(g1d, g2d);
        for (int i = 0; i < maxData; i++)
            foreach (var block in dataBlocks)
                if (i < block.Length) stream.Add(block[i]);
        for (int i = 0; i < ecPerBlock; i++)
            foreach (var block in ecBlocks)
                stream.Add(block[i]);
        return stream.ToArray();
    }

    // ── Matrix layout ───────────────────────────────────────────────────────────────────────────────────
    private static QrCodeSymbol BuildMatrix(byte[] codewords, int version, QrErrorCorrection ecc, int? forcedMask)
    {
        int size = version * 4 + 17;
        var module = new bool[size, size];
        var function = new bool[size, size];

        void Set(int r, int c, bool dark) { module[r, c] = dark; function[r, c] = true; }

        // Finder patterns + separators at the three corners.
        void Finder(int row, int col)
        {
            for (int r = -1; r <= 7; r++)
                for (int c = -1; c <= 7; c++)
                {
                    int rr = row + r, cc = col + c;
                    if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
                    bool dark = (r >= 0 && r <= 6 && (c == 0 || c == 6)) ||
                                (c >= 0 && c <= 6 && (r == 0 || r == 6)) ||
                                (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                    Set(rr, cc, dark);
                }
        }
        Finder(0, 0);
        Finder(0, size - 7);
        Finder(size - 7, 0);

        // Timing patterns.
        for (int i = 8; i < size - 8; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }

        // Alignment patterns (skip any overlapping a finder).
        var pos = AlignmentPositions[version - 1];
        foreach (var ar in pos)
            foreach (var ac in pos)
            {
                if ((ar <= 7 && ac <= 7) || (ar <= 7 && ac >= size - 8) || (ar >= size - 8 && ac <= 7)) continue;
                for (int r = -2; r <= 2; r++)
                    for (int c = -2; c <= 2; c++)
                        Set(ar + r, ac + c, Math.Max(Math.Abs(r), Math.Abs(c)) != 1);
            }

        // Dark module + reserved format/version areas.
        Set(size - 8, 8, true);
        ReserveFormat(function, size);
        if (version >= 7) ReserveVersion(function, size);

        // Place data bits in the upward/downward zig-zag (two columns at a time, right to left), skipping the
        // vertical timing column; remainder modules past the codeword stream stay light. (ISO/IEC 18004 §8.7.3.)
        int bit = 0;
        int total = codewords.Length * 8;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5; // skip the vertical timing column
            for (int vert = 0; vert < size; vert++)
            {
                bool upward = ((right + 1) & 2) == 0;
                for (int j = 0; j < 2; j++)
                {
                    int col = right - j;
                    int row = upward ? size - 1 - vert : vert;
                    if (function[row, col]) continue;
                    module[row, col] = bit < total && ((codewords[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                    bit++;
                }
            }
        }

        // Choose the lowest-penalty mask (or the caller-forced one).
        int bestMask = 0, bestPenalty = int.MaxValue;
        bool[,]? best = null;
        for (int m = 0; m < 8; m++)
        {
            if (forcedMask is int fm && fm != m) continue;
            var trial = (bool[,])module.Clone();
            ApplyMask(trial, function, m);
            PlaceFormat(trial, size, ecc, m);
            if (version >= 7) PlaceVersion(trial, size, version);
            int p = Penalty(trial, size);
            if (p < bestPenalty) { bestPenalty = p; bestMask = m; best = trial; }
        }

        return new QrCodeSymbol(best!, version, ecc, bestMask);
    }

    private static void ApplyMask(bool[,] m, bool[,] function, int mask)
    {
        int size = m.GetLength(0);
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
            {
                if (function[r, c]) continue;
                bool invert = mask switch
                {
                    0 => (r + c) % 2 == 0,
                    1 => r % 2 == 0,
                    2 => c % 3 == 0,
                    3 => (r + c) % 3 == 0,
                    4 => (r / 2 + c / 3) % 2 == 0,
                    5 => (r * c) % 2 + (r * c) % 3 == 0,
                    6 => ((r * c) % 2 + (r * c) % 3) % 2 == 0,
                    _ => ((r + c) % 2 + (r * c) % 3) % 2 == 0,
                };
                if (invert) m[r, c] = !m[r, c];
            }
    }

    // ── Format and version information (BCH-coded) ───────────────────────────────────────────────────────
    // QR error-correction level indicator bits (distinct from the L/M/Q/H enum order): M=00, L=01, H=10, Q=11.
    private static int FormatEccBits(QrErrorCorrection ecc) => ecc switch
    {
        QrErrorCorrection.Low => 1,
        QrErrorCorrection.Medium => 0,
        QrErrorCorrection.Quartile => 3,
        _ => 2,
    };

    private static void ReserveFormat(bool[,] function, int size)
    {
        for (int i = 0; i <= 8; i++) { if (i != 6) function[8, i] = true; if (i != 6) function[i, 8] = true; }
        for (int i = 0; i < 8; i++) function[8, size - 1 - i] = true;
        for (int i = 0; i < 7; i++) function[size - 1 - i, 8] = true;
    }

    private static void PlaceFormat(bool[,] m, int size, QrErrorCorrection ecc, int mask)
    {
        int data = (FormatEccBits(ecc) << 3) | mask;
        int rem = data << 10;
        for (int i = 14; i >= 10; i--)
            if (((rem >> i) & 1) != 0) rem ^= 0x537 << (i - 10);
        int bits = ((data << 10) | rem) ^ 0x5412;

        // The 15 format bits are placed most-significant-bit first along each copy's L-strip.
        for (int i = 0; i <= 5; i++) m[8, i] = Bit(bits, 14 - i);
        m[8, 7] = Bit(bits, 8);
        m[8, 8] = Bit(bits, 7);
        m[7, 8] = Bit(bits, 6);
        for (int i = 9; i < 15; i++) m[14 - i, 8] = Bit(bits, 14 - i);

        for (int i = 0; i < 7; i++) m[size - 1 - i, 8] = Bit(bits, 14 - i);
        for (int i = 7; i < 15; i++) m[8, size - 15 + i] = Bit(bits, 14 - i);
        m[size - 8, 8] = true; // dark module
    }

    private static void ReserveVersion(bool[,] function, int size)
    {
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 3; j++)
            {
                function[i, size - 11 + j] = true;
                function[size - 11 + j, i] = true;
            }
    }

    private static void PlaceVersion(bool[,] m, int size, int version)
    {
        int rem = version << 12;
        for (int i = 17; i >= 12; i--)
            if (((rem >> i) & 1) != 0) rem ^= 0x1F25 << (i - 12);
        int bits = (version << 12) | rem;
        for (int i = 0; i < 18; i++)
        {
            bool b = Bit(bits, i);
            int r = i / 3, c = i % 3;
            m[r, size - 11 + c] = b;
            m[size - 11 + c, r] = b;
        }
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    // ── Mask penalty (ISO/IEC 18004 §8.8.2) ─────────────────────────────────────────────────────────────
    private static int Penalty(bool[,] m, int size)
    {
        int penalty = 0;

        // Rule 1: runs of five or more same-coloured modules in a row/column.
        for (int line = 0; line < size; line++)
        {
            penalty += RunPenalty(m, size, line, true);
            penalty += RunPenalty(m, size, line, false);
        }

        // Rule 2: 2x2 blocks of one colour.
        for (int r = 0; r < size - 1; r++)
            for (int c = 0; c < size - 1; c++)
                if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1])
                    penalty += 3;

        // Rule 3: finder-like 1:1:3:1:1 patterns (with a 4-module light run) in rows and columns.
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
            {
                if (c <= size - 7 && FinderLike(m, r, c, true)) penalty += 40;
                if (r <= size - 7 && FinderLike(m, r, c, false)) penalty += 40;
            }

        // Rule 4: deviation of the dark-module proportion from 50%.
        int dark = 0;
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                if (m[r, c]) dark++;
        int percent = dark * 100 / (size * size);
        penalty += 10 * (Math.Abs(percent - 50) / 5);

        return penalty;
    }

    private static int RunPenalty(bool[,] m, int size, int line, bool row)
    {
        int penalty = 0, run = 1;
        bool prev = row ? m[line, 0] : m[0, line];
        for (int i = 1; i < size; i++)
        {
            bool cur = row ? m[line, i] : m[i, line];
            if (cur == prev) { run++; }
            else { if (run >= 5) penalty += 3 + (run - 5); run = 1; prev = cur; }
        }
        if (run >= 5) penalty += 3 + (run - 5);
        return penalty;
    }

    private static readonly bool[] Pattern = { true, false, true, true, true, false, true };

    private static bool FinderLike(bool[,] m, int r, int c, bool row)
    {
        // 1:1:3:1:1 dark pattern preceded or followed by four light modules.
        for (int i = 0; i < 7; i++)
        {
            bool v = row ? m[r, c + i] : m[r + i, c];
            if (v != Pattern[i]) return false;
        }
        bool lightBefore = true, lightAfter = true;
        for (int i = 1; i <= 4; i++)
        {
            int before = row ? c - i : r - i;
            int after = row ? c + 6 + i : r + 6 + i;
            int max = m.GetLength(0);
            if (before < 0 || (row ? m[r, before] : m[before, c])) lightBefore = false;
            if (after >= max || (row ? m[r, after] : m[after, c])) lightAfter = false;
        }
        return lightBefore || lightAfter;
    }
}
