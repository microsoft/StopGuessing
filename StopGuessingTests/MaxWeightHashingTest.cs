using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{
    public class MaxWeightHashingTests
    {
        static string[] _nodeNames = new string[] {
                "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };//,
   //             "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty"};

    private MaxWeightHashing<string> CreateMaxWeightHasher()
        {
            MaxWeightHashing<string> hasher = new MaxWeightHashing<string>(
                // For testing purposes only, the private key we will use to initialize
                // the universal hash function for the hash ring will be the key master from Ghostbusters
                "Louis Tully as played by Rick Moranis",
                // The ring will contain the words one to ten, capitalized.
                _nodeNames.Select(word => new KeyValuePair<string, string>(word, word)));
            return hasher;
        }

        [Fact]
        public void MaxWeightHasherEvenDistributionTest()
        {
            Pseudorandom pseudo = new Pseudorandom();
            int trials = 10000000;
            MaxWeightHashing<string> hasher = CreateMaxWeightHasher();

            double expectedFraction = 1d / ((double) _nodeNames.Length);


            Dictionary<string, Int64> counts = new Dictionary<string, Int64>();
            foreach (string number in _nodeNames)
                counts[number] = 0;

            for (int i = 0; i < trials; i++)
            {
                string rand = pseudo.GetString(13);
                string word = hasher.FindMemberResponsible(rand);
                counts[word]++;
            }

            double greatestAdjustedError = 0;
            foreach (string word in _nodeNames)
            {
                double freq = ((double)counts[word]) / (double)trials;
                double error = freq - expectedFraction;
                double adjustedError = error / expectedFraction;
                if (adjustedError > greatestAdjustedError)
                    greatestAdjustedError = adjustedError;
                Assert.True(Math.Abs(adjustedError) < 0.005);
            }

            Console.Out.WriteLine("Highest adjusted error: {0}", greatestAdjustedError);
        }
    }
}
