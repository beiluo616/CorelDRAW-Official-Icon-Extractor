using System.Buffers.Binary;
using CDRIconExtractor.Windows.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Resources;

[TestClass]
public sealed class GenericPeIconScannerTests
{
    [TestMethod]
    public void Scan_GroupIcon_UsesGroupNameAndReferencedRtIconWithoutDuplicateRawIcon()
    {
        var reader = new FakeResourceReader();
        reader.Add(14, new Win32ResourceBlob(14, "10", 1033, CreateGroupIcon(iconId: 5, width: 1, height: 1)));
        reader.Add(3, new Win32ResourceBlob(3, "5", 1033, CreateOnePixelIconDib()));
        var scanner = new GenericPeIconScanner(reader);

        var assets = scanner.Scan("CorelDRW.exe", CancellationToken.None);

        var asset = AssertSingle(assets);
        Assert.AreEqual("RT_GROUP_ICON", asset.ResourceType);
        Assert.AreEqual("10/5", asset.ResourceId);
        Assert.AreEqual(1, asset.Width);
        Assert.AreEqual(1, asset.Height);
    }


    [TestMethod]
    public void Scan_LegacyBitmapStrip_SplitsTenColumnsForBmpRowBmpColMapping()
    {
        var reader = new FakeResourceReader();
        reader.Add(2, new Win32ResourceBlob(2, "321", 1033, CreateIndexed8BitmapStrip(columns: 10, cellSize: 8)));
        var scanner = new GenericPeIconScanner(reader);

        var assets = scanner.Scan("CrlGenericUI.dll", CancellationToken.None);

        Assert.AreEqual(10, assets.Count);
        Assert.AreEqual("RT_BITMAP_STRIP_CELL", assets[7].ResourceType);
        Assert.AreEqual("321:7", assets[7].ResourceId);
        Assert.AreEqual(8, assets[7].Width);
        Assert.AreEqual(8, assets[7].Height);
    }


    [TestMethod]
    public void Scan_CustomNamedResourceType_ExtractsPngAndPreservesResourceName()
    {
        var reader = new FakeResourceReader();
        reader.AddNamedType("COREL_ICON", new Win32ResourceBlob(0, "693", 1033, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")));
        var scanner = new GenericPeIconScanner(reader);

        var assets = scanner.Scan("CrlGenericUI.dll", CancellationToken.None);

        var asset = AssertSingle(assets);
        Assert.AreEqual("693", asset.ResourceId);
        StringAssert.Contains(asset.ResourceType, "COREL_ICON");
        Assert.AreEqual(1, asset.Width);
        Assert.AreEqual(1, asset.Height);
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values)
    {
        Assert.AreEqual(1, values.Count);
        return values[0];
    }

    private static byte[] CreateGroupIcon(ushort iconId, byte width, byte height)
    {
        var bytes = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        bytes[6] = width;
        bytes[7] = height;
        bytes[8] = 0;
        bytes[9] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), 44);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), iconId);
        return bytes;
    }

    private static byte[] CreateOnePixelIconDib()
    {
        var bytes = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 2); // XOR + AND heights
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 0);
        bytes[40] = 0x30;
        bytes[41] = 0x20;
        bytes[42] = 0x10;
        bytes[43] = 0xFF;
        return bytes;
    }


    private static byte[] CreateIndexed8BitmapStrip(int columns, int cellSize)
    {
        var width = columns * cellSize;
        var height = cellSize;
        var rowStride = ((width * 8 + 31) / 32) * 4;
        var paletteBytes = 256 * 4;
        var bytes = new byte[40 + paletteBytes + rowStride * height];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), 256);
        for (var i = 0; i < 256; i++)
        {
            var offset = 40 + i * 4;
            bytes[offset] = (byte)i;
            bytes[offset + 1] = (byte)i;
            bytes[offset + 2] = (byte)i;
        }
        var pixels = 40 + paletteBytes;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bytes[pixels + y * rowStride + x] = (byte)((x / cellSize) * 20 + 10);
        return bytes;
    }

    private sealed class FakeResourceReader : IWin32ResourceReader, IWin32ResourceCatalog
    {
        private readonly Dictionary<ushort, List<Win32ResourceBlob>> _resources = [];
        private readonly Dictionary<string, List<Win32ResourceBlob>> _namedResources = new(StringComparer.OrdinalIgnoreCase);

        public void Add(ushort typeId, Win32ResourceBlob blob)
        {
            if (!_resources.TryGetValue(typeId, out var list))
            {
                list = [];
                _resources[typeId] = list;
            }
            list.Add(blob);
        }

        public void AddNamedType(string typeName, Win32ResourceBlob blob)
        {
            if (!_namedResources.TryGetValue(typeName, out var list))
            {
                list = [];
                _namedResources[typeName] = list;
            }
            list.Add(blob);
        }

        public IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, ushort typeId) =>
            _resources.TryGetValue(typeId, out var list) ? list : [];

        public IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, string typeName) =>
            _namedResources.TryGetValue(typeName, out var list) ? list : [];

        public IReadOnlyList<Win32ResourceTypeSummary> InspectResourceTypes(string modulePath) =>
            _namedResources.Select(pair => new Win32ResourceTypeSummary(
                pair.Key,
                null,
                pair.Value.Count,
                pair.Value.Sum(x => (long)x.Bytes.Length),
                pair.Value.Select(x => x.Name).Take(8).ToArray())).ToArray();
    }
}
