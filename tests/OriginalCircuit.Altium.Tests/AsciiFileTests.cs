using System.Text;
using OriginalCircuit.Altium.Serialization.Readers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that documents Altium saved in its ASCII (text) format are reported as such by every
/// binary reader, rather than as a generic corrupt file.
/// </summary>
public sealed class AsciiFileTests
{
    private const string AsciiSchematic =
        "|HEADER=Protel for Windows - Schematic Capture Ascii File Version 5.0|WEIGHT=1\r\n|RECORD=31|FONTIDCOUNT=1\r\n";

    [Theory]
    [InlineData("")]
    [InlineData("﻿")]
    [InlineData("\r\n  ")]
    public void ReadingAnAsciiDocument_ThrowsAsciiFileException(string lead)
    {
        var bytes = Encoding.UTF8.GetBytes(lead + AsciiSchematic);

        Assert.Throws<AltiumAsciiFileException>(() => new SchDocReader().Read(new MemoryStream(bytes)));
        Assert.Throws<AltiumAsciiFileException>(() => new SchLibReader().Read(new MemoryStream(bytes)));
        Assert.Throws<AltiumAsciiFileException>(() => new PcbDocReader().Read(new MemoryStream(bytes)));
        Assert.Throws<AltiumAsciiFileException>(() => new PcbLibReader().Read(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task OpeningAnAsciiDocumentFromDisk_NamesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "Ascii_" + Guid.NewGuid().ToString("N") + ".SchDoc");
        await File.WriteAllTextAsync(path, AsciiSchematic);
        try
        {
            var ex = await Assert.ThrowsAsync<AltiumAsciiFileException>(async () => await AltiumLibrary.OpenSchDocAsync(path));
            Assert.Equal(path, ex.FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OtherBytes_AreStillReportedAsCorrupt()
    {
        var ex = Assert.Throws<AltiumCorruptFileException>(() => new SchDocReader().Read(new MemoryStream(new byte[1024])));
        Assert.IsNotType<AltiumAsciiFileException>(ex);
    }
}
