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
            using (SHA256Managed hash = new SHA256Managed())
            {
                return hash.ComputeHash(buffer);
            }

        }

    }
}
