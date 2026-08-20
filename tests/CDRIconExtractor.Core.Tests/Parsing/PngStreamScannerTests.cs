using System.Buffers.Binary;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class PngStreamScannerTests
{
    [TestMethod]
    public void Find_ReturnsTwoPngsAndTheirDimensions()
    {
        var bytes = File.ReadAllBytes(Fixture("embedded-png-stream.bin"));
        var slices = PngStreamScanner.Find(bytes);

        Assert.AreEqual(2, slices.Count);
        Assert.AreEqual(16, slices[0].Width);
        Assert.AreEqual(16, slices[0].Height);
        Assert.AreEqual(32, slices[1].Width);
        Assert.AreEqual(24, slices[1].Height);
        Assert.AreEqual(64, slices[0].Sha256.Length);
    }

    [TestMethod]
    public void Find_TruncatedPng_IsIgnored()
    {
        byte[] truncated = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
        Assert.AreEqual(0, PngStreamScanner.Find(truncated).Count);
    }

    [TestMethod]
    public void Find_IhdrDimensionLargerThanInt32_IsIgnoredInsteadOfThrowing()
    {
        var bytes = CreateHeaderOnlyPng(uint.MaxValue, 16);

        var slices = PngStreamScanner.Find(bytes);

        Assert.AreEqual(0, slices.Count);
    }

    private static byte[] CreateHeaderOnlyPng(uint width, uint height)
    {
        var bytes = new byte[8 + 12 + 13 + 12];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 6;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33, 4), 0);
        "IEND"u8.CopyTo(bytes.AsSpan(37, 4));
        return bytes;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "CrlIcons", name);
}
