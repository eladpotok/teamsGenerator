using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Identity;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TeamsGeneratorWebAPI
{
    public static class Utils
    {
        public static bool VerifyHashedPassword(string hashedPassword, string password)
        {
            byte[] buffer4;
            if (hashedPassword == null)
            {
                return false;
            }
            if (password == null)
            {
                throw new ArgumentNullException("password");
            }
            byte[] src = Convert.FromBase64String(hashedPassword);
            if ((src.Length != 0x31) || (src[0] != 0))
            {
                return false;
            }
            byte[] dst = new byte[0x10];
            Buffer.BlockCopy(src, 1, dst, 0, 0x10);
            byte[] buffer3 = new byte[0x20];
            Buffer.BlockCopy(src, 0x11, buffer3, 0, 0x20);
            using (Rfc2898DeriveBytes bytes = new Rfc2898DeriveBytes(password, dst, 0x3e8))
            {
                buffer4 = bytes.GetBytes(0x20);
            }
            var onlyFirst = buffer3.Except(buffer4);
            var onlySecond = buffer4.Except(buffer3);
            return !onlyFirst.Any() && !onlySecond.Any();
        }

        public static string HashPassword(string password)
        {
            byte[] salt;
            byte[] buffer2;
            if (password == null)
            {
                throw new ArgumentNullException("password");
            }
            using (Rfc2898DeriveBytes bytes = new Rfc2898DeriveBytes(password, 0x10, 0x3e8))
            {
                salt = bytes.Salt;
                buffer2 = bytes.GetBytes(0x20);
            }
            byte[] dst = new byte[0x31];
            Buffer.BlockCopy(salt, 0, dst, 1, 0x10);
            Buffer.BlockCopy(buffer2, 0, dst, 0x11, 0x20);
            return Convert.ToBase64String(dst);
        }

        public static byte[] ObjectToByteArray(Object obj)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        public static string CompressBase64(string base64Input)
        {
            // Decode original Base64 to raw bytes
            var originalBytes = Convert.FromBase64String(base64Input);

            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                gzip.Write(originalBytes, 0, originalBytes.Length);
            }

            // Re-encode compressed bytes to Base64
            return Convert.ToBase64String(output.ToArray());
        }

        public static string DecompressBase64(string compressedBase64)
        {
            var compressedBytes = Convert.FromBase64String(compressedBase64);

            using var input = new MemoryStream(compressedBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);

            // Return original Base64 string
            return Convert.ToBase64String(output.ToArray());
        }

        internal static List<string> GetStringByBlocks(string imageBase64)
        {
            string base64 = imageBase64;
            int maxChunkSize = 32000; // safe for Azure Tables (UTF-16 + overhead)
            var chunks = SplitString(base64, maxChunkSize);

            return chunks;
        }

        public static List<string> SplitString(string str, int chunkSize)
        {
            var chunks = new List<string>();

            for (int i = 0; i < str.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, str.Length - i);
                chunks.Add(str.Substring(i, length));
            }

            return chunks;
        }
    }
}


