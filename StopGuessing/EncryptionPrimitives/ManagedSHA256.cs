using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace StopGuessing.EncryptionPrimitives
{
    public static class ManagedSHA256
    {
        public static byte[] Hash(byte[] buffer)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return hash.ComputeHash(buffer);
            }

        }

    }
}
