using System;
using System.IO;
using NickScanCentralImagingPortal.Services.ImageProcessing.EagleA25;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.EagleA25
{
    public class EagleA25CargoImageDecoderTests
    {
        [Fact]
        public void Decode_ReadsChunkedCargoImageAndTransposesToLandscape()
        {
            var blob = BuildCargoImage(width: 2, height: 3);

            var decoded = EagleA25CargoImageDecoder.Decode(blob);

            Assert.Equal(3, decoded.Width);
            Assert.Equal(2, decoded.Height);
            Assert.Equal(new ushort[] { 10, 20, 30, 11, 21, 31 }, decoded.HighEnergy);
            Assert.Equal(new ushort[] { 100, 200, 300, 101, 201, 301 }, decoded.LowEnergy);
            Assert.Equal("2", decoded.Metadata["RawWidth"]);
            Assert.Equal("3", decoded.Metadata["RawHeight"]);
        }

        [Fact]
        public void Decode_RejectsNonRapFiles()
        {
            var ex = Assert.Throws<InvalidDataException>(() =>
                EagleA25CargoImageDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.Contains("too small", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] BuildCargoImage(int width, int height)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { 0xA9, 0x52, 0x41, 0x50, 0x0D, 0x0A, 0x1A, 0x0A });

            WriteChunk(ms, "DIMS", writer =>
            {
                writer.Write(4);
                writer.Write(width);
                writer.Write(height);
                writer.Write(3);
                writer.Write(152);
                writer.Write(0);
            });

            WriteChunk(ms, "XRAY", writer =>
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        writer.Write((ushort)(10 + y * 10 + x));
                        writer.Write((ushort)(100 + y * 100 + x));
                    }
                }
            });

            return ms.ToArray();
        }

        private static void WriteChunk(MemoryStream target, string type, Action<BinaryWriter> writeData)
        {
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writeData(writer);
            }

            target.Write(System.Text.Encoding.ASCII.GetBytes(type));
            target.Write(BitConverter.GetBytes((int)data.Length));
            target.Write(data.ToArray());
        }
    }
}
