using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SIIDecryptSharp
{
    public class SII_Header
    {
        public UInt32 Signature;
        public UInt32 DataSize;

        public SII_Header() {
            Signature = 999;
            DataSize = 0;
        }
    }
    public class SII_Data
    {
        public SII_Header Header;
        public byte[] Data;

        public SII_Data()
        {
            Header = new SII_Header();
            Data = new byte[0];
        }
    }
    public class Decryptor
    {
        public static byte[] SII_Key = new byte[]
        {
            0x2a, 0x5f, 0xcb, 0x17, 0x91, 0xd2, 0x2f, 0xb6, 0x02, 0x45, 0xb3, 0xd8, 0x36, 0x9e, 0xd0, 0xb2,
            0xc2, 0x73, 0x71, 0x56, 0x3f, 0xbf, 0x1f, 0x3c, 0x9e, 0xdf, 0x6b, 0x11, 0x82, 0x5a, 0x5d, 0x0a
        };

        public static byte[] Decrypt(string filePath, bool decode=true)
        {
            var bytes = File.ReadAllBytes(filePath);

            int streamPos = 0;

            if(!StreamUtils.TryReadUInt32(ref bytes, ref streamPos, out UInt32 fileType))
            {
                throw new Exception("Invalid file");
            }

            if(fileType == (UInt32)SignatureType.Encrypted)
            {
                var data = Decrypt(ref bytes, streamPos);
                bytes = data.Data;
                byte[] destination = new byte[data.Header.DataSize];
                uint dataSize = (uint)data.Header.DataSize;
                Zlib.uncompress(destination, ref dataSize, data.Data, (uint)data.Data.Length);
                data.Data = destination;
                bytes = destination;
                data = new SII_Data();
            }
            if (decode)
            {
                streamPos = 0;
                if (!StreamUtils.TryReadUInt32(ref bytes, ref streamPos, out UInt32 dataType))
                {
                    throw new Exception("Invalid data");
                }
                switch (dataType)
                {
                    case ((uint)SignatureType.PlainText):
                        return DecodePlaintext(ref bytes, streamPos).Data;
                    case ((uint)SignatureType.Binary):
                        return DecodeBinary(ref bytes, streamPos).Data;
                    case ((uint)SignatureType._3nK):
                        throw new NotImplementedException("_3nK decoding is not implmented yet.");
                }
            }
            return new byte[0];
        }

        private static SII_Data Decrypt(ref byte[] encrypted, int offset)
        {
            StringBuilder stringBuilder = new StringBuilder();

            List<byte> decrypted = new List<byte>();

            SII_Header header = new SII_Header();

            byte[] HMAC = new byte[0];
            byte[] IV = new byte[0];

            var streamPos = 0;

            if (encrypted.Length - streamPos >= sizeof(UInt32))
            {
                header.Signature = BitConverter.ToUInt32(encrypted, streamPos);
                streamPos += sizeof(UInt32);
            }
            if (encrypted.Length - streamPos >= 32)
            {
                HMAC = encrypted.Skip(streamPos).Take(32).ToArray();
                streamPos += 32;
            }
            if (encrypted.Length - streamPos >= 16)
            {
                IV = encrypted.Skip(streamPos).Take(16).ToArray();
                streamPos += 16;
            }
            if (encrypted.Length - streamPos >= sizeof(UInt32))
            {
                header.DataSize = BitConverter.ToUInt32(encrypted, streamPos);
                streamPos += sizeof(UInt32);
            }


            var finalEncrypted = encrypted.Skip(streamPos).ToArray();

            using (Aes aes = Aes.Create())
            {
                aes.Key = SII_Key;
                aes.IV = IV;

                // Decrypt
                ICryptoTransform decipher = aes.CreateDecryptor(aes.Key, aes.IV);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, decipher, CryptoStreamMode.Write))
                    {
                        cs.Write(finalEncrypted, 0, finalEncrypted.Length);
                        cs.FlushFinalBlock();
                        decrypted.AddRange(ms.ToArray());
                    }
                }
            }

            return new SII_Data() { Data = decrypted.ToArray(), Header = header };
        }

        private static SII_Data DecodePlaintext(ref byte[] data, int offset)
        {
            var result = new SII_Data();

            result.Header = new SII_Header();
            result.Header.DataSize = (uint)data.Length;
            result.Header.Signature = (uint)SignatureType.PlainText;
            result.Data = data;
            return result;
        }

        private static SII_Data DecodeBinary(ref byte[] data, int offset)
        {
            var result = new SII_Data();
            result.Header = new SII_Header();
            result.Header.DataSize = (uint)data.Length;
            result.Header.Signature = (uint)SignatureType.Binary;
            result.Data = BSII_Decoder.Decode(ref data);
            return result;
        }
    }
}