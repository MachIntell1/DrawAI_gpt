using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MachIntellDrawAI.Infrastructure
{
    internal static class StableHash
    {
        public static string Bytes(byte[] value, string prefix = "")
        {
            using (var sha = SHA256.Create())
                return prefix + Hex(sha.ComputeHash(value));
        }

        public static string Text(string value, string prefix = "") => Bytes(Encoding.UTF8.GetBytes(value), prefix);

        public static string File(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
