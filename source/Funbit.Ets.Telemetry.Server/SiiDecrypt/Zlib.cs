using System.IO;
using System.IO.Compression;

namespace SIIDecryptSharp
{
    // Replaces the upstream zlib.dll P/Invoke wrapper with a managed implementation
    // so we don't have to ship a native zlib binary. Only decompression is needed.
    public class Zlib
    {
        public static int uncompress(byte[] destBuffer, ref uint destLen, byte[] sourceBuffer, uint sourceLen)
        {
            // Skip the 2-byte zlib stream header; DeflateStream handles the raw deflate data.
            using (var input = new MemoryStream(sourceBuffer, 2, (int)sourceLen - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int total = 0;
                int read;
                while (total < destBuffer.Length &&
                       (read = deflate.Read(destBuffer, total, destBuffer.Length - total)) > 0)
                {
                    total += read;
                }
                destLen = (uint)total;
                return 0;
            }
        }
    }
}
