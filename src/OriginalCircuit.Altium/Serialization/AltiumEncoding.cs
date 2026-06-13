using System.Text;

namespace OriginalCircuit.Altium.Serialization;

/// <summary>
/// Provides encoding support for Altium file formats.
/// </summary>
internal static class AltiumEncoding
{
    // Lazy<T> (thread-safe by default) guarantees the provider is registered and the encoding
    // resolved exactly once, even when multiple reader/writer threads first touch it concurrently.
    private static readonly Lazy<Encoding> _windows1252 = new(() =>
    {
        // Register the code pages encoding provider for Windows-1252 support.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    });

    /// <summary>
    /// Gets the Windows-1252 encoding used by Altium for ASCII strings.
    /// </summary>
    public static Encoding Windows1252 => _windows1252.Value;

    /// <summary>
    /// Ensures encoding providers are registered.
    /// </summary>
    public static void EnsureInitialized() => _ = _windows1252.Value;

    /// <summary>
    /// Decodes a parameter value that carried the <c>%UTF8%</c> key prefix. The surrounding
    /// parameter block is decoded as Windows-1252, so a UTF-8 value arrives as one char per raw
    /// byte ("mojibake"). Re-encoding those chars back to Windows-1252 bytes recovers the original
    /// UTF-8 byte sequence (the mapping is a byte bijection), which is then decoded as UTF-8.
    /// </summary>
    public static string DecodeUtf8ParameterValue(string value)
        => value.Length == 0 ? value : Encoding.UTF8.GetString(Windows1252.GetBytes(value));

    /// <summary>
    /// Inverse of <see cref="DecodeUtf8ParameterValue"/>: maps a Unicode string to the
    /// one-char-per-UTF-8-byte form so that, when the parameter block is subsequently encoded as
    /// Windows-1252, the value is emitted as its UTF-8 byte sequence (matching how Altium stores
    /// <c>%UTF8%</c>-prefixed parameters).
    /// </summary>
    public static string EncodeUtf8ParameterValue(string value)
        => value.Length == 0 ? value : Windows1252.GetString(Encoding.UTF8.GetBytes(value));
}
