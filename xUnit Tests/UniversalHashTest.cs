using System;
using System.Linq;
using Xunit;
using StopGuessing.EncryptionPrimitives;


namespace xUnit_Tests
{
    public class UniversalHashTest
    {
        [Fact]
        public void UniversalHashTestBias()
        {
            Pseudorandom pseudo = new Pseudorandom();
            UniversalHashFunction f = new UniversalHashFunction("Louis Tully as played by Rick Moranis!");
            ulong trials = 100000000;

            ulong[] bitCounts = new ulong[64];

            for (ulong trial = 0; trial < trials; trial++)
            {
                string randomString = pseudo.GetString(8);
                UInt64 supposedlyUnbiasedBits = f.Hash(randomString, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
                for (int bit=0; bit < bitCounts.Length; bit++)
                {
                    if ((supposedlyUnbiasedBits & (0x8000000000000000ul >> bit)) != 0ul)
                        bitCounts[bit]++;
                }
            }

            double[] biases = bitCounts.Select(count => ( (0.5d - (((double)count) / ((double)trials)))) / 0.5d ).ToArray();

            /// The first 32 bits should be unbiased
            for (int bit = 0; bit < 32; bit++)
            {
                double bias = biases[bit];
                double biasAbs = Math.Abs(bias);
                Assert.True(biasAbs < 0.0005d);
            }

        }
    }
}
