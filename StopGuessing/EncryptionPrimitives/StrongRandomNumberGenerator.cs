using System;
using System.Security.Cryptography;

namespace StopGuessing.EncryptionPrimitives
{
    /// <summary>
    /// A utility interface to .NET's cryptographically-strong random number generator
    /// (the interface we which .NET provided)
    /// </summary>
    public static class StrongRandomNumberGenerator
    {
        // Pre-allocate a thread-safe random number generator
        private static RNGCryptoServiceProvider LocalRandomNumberGenerator = new RNGCryptoServiceProvider();
        private static int FIXME_memoryLeakHack = 0;
        public static void GetBytes(byte[] bytes)
        {
            if (FIXME_memoryLeakHack++ > 10000)
            {
                // To try to fix a memory leak, freeing and regerating the RNG service
                // every 10,000 calls
                FIXME_memoryLeakHack = 0;
                RNGCryptoServiceProvider toDispose = LocalRandomNumberGenerator;
                LocalRandomNumberGenerator = new RNGCryptoServiceProvider();
                toDispose.Dispose();
            }
            LocalRandomNumberGenerator.GetBytes(bytes);
        }

        public static ulong Get64Bits(ulong? mod = null)
        {
            byte[] randBytes = new byte[8];
            GetBytes(randBytes);

            ulong result = BitConverter.ToUInt64(randBytes, 0);
            if (mod.HasValue)
                result = result % mod.Value;
            return result;
        }

        public static ulong Get64Bits(long mod)
        {
            return Get64Bits((ulong)mod);
        }

        public static uint Get32Bits(uint? mod = null)
        {
            // We'll need the randomness to determine which bit to set and which to clear 
            byte[] randBytes = new byte[4];
            GetBytes(randBytes);

            uint result = BitConverter.ToUInt32(randBytes, 0);
            if (mod.HasValue)
                result = result % mod.Value;
            return result;
        }

        public static uint Get32Bits(int mod)
        {
            return Get32Bits((uint)mod);
        }

        public static double GetFraction()
        {
            return (double)Get64Bits() / (double)ulong.MaxValue;
        }
    }
}
