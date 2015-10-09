using System;
using System.Linq;
using System.Collections.Generic;
using StopGuessing.DataStructures;
using Xunit;

namespace xUnit_Tests
{
    
    public class ConsistentHashRingTests
    {
        static string[] _oneToTenAsWords = new string[] {
                "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"};

        public ConsistentHashRing<string> CreateConsistentHashRingForTenValues()
        {
            ConsistentHashRing<string> ring = new ConsistentHashRing<string>(
                // For testing purposes only, the private key we will use to initialize
                // the universal hash function for the hash ring will be the key master from Ghostbusters
                "Louis Tully as played by Rick Moranis",
                // The ring will contain the words one to ten, capitalized.
                _oneToTenAsWords.Select( word => new KeyValuePair<string,string>(word, word) ),
                1024);
            return ring;
        }

        [Fact]
        public void HashRingEvenDistributionTest()
        {
            Pseudorandom pseudo = new Pseudorandom();
            int trials = 10000000;
            ConsistentHashRing<string> ring = CreateConsistentHashRingForTenValues();
            Dictionary<string,double> ringCoverage = ring.FractionalCoverage;

            double expectedFraction = 1d / 10d;

            foreach (string word in _oneToTenAsWords)
            {
                double share = ringCoverage[word];
                double bias = (expectedFraction - share) / expectedFraction;
                double absBias = Math.Abs(bias);
                Assert.True(bias < .15d);
                Assert.True(bias < .15d);
            }


            Dictionary<string, Int64> counts = new Dictionary<string, Int64>();
            Dictionary<string, double> frequencies = new Dictionary<string, double>();
            foreach (string number in _oneToTenAsWords)
                counts[number] = 0;

            for (int i = 0; i < trials; i++)
            {
                string rand = pseudo.GetString(13);
                string word = ring.FindMemberResponsible(rand);
                counts[word]++;
            }
            foreach (string word in _oneToTenAsWords)
            {
                double freq = frequencies[word] = ((double)counts[word]) / (double)trials;
                double error = freq - ringCoverage[word];
                Assert.True(Math.Abs(error) < 0.01);
            }

        }
    }
}
