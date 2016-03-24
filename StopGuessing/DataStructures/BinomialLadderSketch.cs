using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Clients;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    public interface IBinomialLadderSketch
    {
        Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken());

        Task<int> GetHeightAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken());
    }


    /// <summary>
    /// A binomial ladder maps keys to k indexes in an n bit table of bits, which are initially set at random
    /// with a probability 0.5.  Each index represents a rung on a ladder. The indexes set to 1/true are rungs that
    /// are below that key on the ladder and the indexes that are 0/false are rungs above the key, or which the key
    /// has yet to climb.  On avarege, a key that has not been seen before will map to, on average,
    /// k/2 index that are set to 1 (true) and k/2 indexes that are set to 0 (false).  This means that half the rungs
    /// are below the key and half are above it, and so the key is half way up the ladder.
    /// 
    /// If a non-empty subset of the indexes that a key maps to contains 0, observing that key (the Step() method)
    /// will cause a random member of that subset to be changed from 0 to 1, causing the key to move one rung up the ladder.
    /// To ensure that the fraction of bits set to 1 stays constant, the step operation willsimultaneously clear a random bit
    /// selected from subset of indexes in the entire sketch that have value 1.  The more times a key has been observed,
    /// the higher the expected number of indexes that will have been set, the higher it moves up the ladder until
    /// reaching the top (when all of its bits are set).
    /// 
    /// To count the subset of a keys indexes that have been set, one can call the GetRungsBelow method.
    /// Natural variance will cause some never-before-seen keys to have more than k/2 bits set and keys that have been
    /// observed very rarely (or only in the distant past) to have fewer than k/2 bit set.  A key that is observed
    /// only a few times (with ~ k/2 + epsilon bits set) will be among ~ .5^{epsilon} false-positive keys that have
    /// as many bits set by chance.  Thus, until a key has been observed a more substantial number of times it is hard
    /// to differentiate from false positives.  To have confidence greater than one in a million that a key has been observed,
    /// it will take an average of 20 observations.
    /// </summary>
    public class BinomialLadderSketch : IBinomialLadderSketch
    {
        public readonly SketchArray Elements;

        /// <summary>
        /// Construct a binomial sketch, in which a set of k hash functions (k=MaxLadderHeightInRungs) will map any
        /// key to k points with an array of n bits (numberOfRungsInSketch).
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 will be set to 1.
        /// To ensure roughly half the bits remain zero, a random index from the subset of all k bits that are currently 1 will be set to 0.
        /// 
        /// Over time, popular keys will have almost all of their bits set and unpopular keys will be expected to have roughly half their bits set.
        /// </summary>
        /// <param name="numberOfRungsInSketch">The total number of bits to maintain in the table.
        /// In theoretical discussions of bloom filters and sketches, this is usually referrted to by the letter n.</param>
        /// <param name="maxLadderHeightInRungs">The number of indexes to map each key to, each of which is assigned a unique pseudorandom
        /// hash function.  This is typically referred to by the letter k.</param>
        public BinomialLadderSketch(int numberOfRungsInSketch, int maxLadderHeightInRungs)
        {
            Elements = new SketchArray(numberOfRungsInSketch, maxLadderHeightInRungs, true);
        }

        public int GetHeight(string key, int? heightOfLadderInRungs = null)
        {
            return Elements.GetElementIndexesForKey(key, heightOfLadderInRungs).Count(rung => Elements[rung]);
        }

        protected int GetRandomIndexWithElementOfDesiredValue(bool desiredValueOfElement)
        {
            int elementIndex;
            // Iterate through random elements until we find one that is set to one (true) and can be cleared
            do
            {
                elementIndex = (int)(StrongRandomNumberGenerator.Get64Bits((ulong)Elements.Length));
            } while (Elements[elementIndex] != desiredValueOfElement);

            return elementIndex;
        }

        /// <summary>
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 (false)
        /// will be set to 1 (true).
        /// To ensure roughly half the bits remain zero at all times, a random index from the subset of all k bits that
        /// are currently 1 (true) will be set to 0 (false).
        /// </summary>
        /// <param name="key">The key to add to the set.</param>
        /// <param name="heightOfLadderInRungs">Set if using a ladder shorter than the default for this sketch</param>
        /// <returns>Of the bits at the indices for the given key, the number of bits that were set to 1 (true)
        /// before the Step operation.  The maximum possible value to be returned, if all bits were already
        /// set to 1 (true) would be MaxLadderHeightInRungs.  If a key has not been seen before, the expected (average)
        /// result is MaxLadderHeightInRungs/2, but will vary with the binomial distribution.</returns>
        public int Step(string key, int? heightOfLadderInRungs = null)
        {
            // Get the set of rungs
            List<int> rungs = Elements.GetElementIndexesForKey(key, heightOfLadderInRungs).ToList();
            // Select the subset of rungs that have value zero (that are above the key in the ladder)
            List<int> rungsAbove = rungs.Where(rung => !Elements[rung]).ToList();

            // Identify an element of the array to set
            int indexOfElementToSet = (rungsAbove.Count > 0) ?
                // If there are rungs with value value zero/false (rungs above the key), pick one at random
                rungsAbove[(int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count))] :
                // otherwise, pick an element with value zero/false from the entire array
                GetRandomIndexWithElementOfDesiredValue(false);

            // Identify an index to clear from the entire array (selected from those elements with value 1/true)
            int indexOfElementToClear = GetRandomIndexWithElementOfDesiredValue(true);

            // Swap the values of element to be set with the element to be cleared
            Elements.SwapElements(indexOfElementToSet, indexOfElementToClear);

            // Return the height of the ladder before the step
            return rungs.Count - rungsAbove.Count;
        }

#pragma warning disable 1998
        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken())
            => Step(key, heightOfLadderInRungs);

        public async Task<int> GetHeightAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken()) => GetHeight(key, heightOfLadderInRungs);
#pragma warning restore 1998

        public void ClearRandomElement()
        {
            Elements.ClearElement((int) StrongRandomNumberGenerator.Get32Bits((uint) Elements.Length));
        }

        public void SetRandomElement()
        {
            Elements.SetElement((int)StrongRandomNumberGenerator.Get32Bits((uint)Elements.Length));
        }



        public static int HeightRequiredForToAchieveConfidenceOfPriorObservations(int heightOfLadderInRungs, double confidenceLevelCommonlyCalledPValue)
        {
            BinomialDistribution binomialDistribution = BinomialDistribution.ForCoinFlips(heightOfLadderInRungs);
            int minRequired = heightOfLadderInRungs + 1;
            double p = 0;
            while (minRequired > 0)
            {
                p += binomialDistribution[minRequired - 1];
                if (p > confidenceLevelCommonlyCalledPValue || minRequired == 0)
                    break;
                minRequired--;
            }
            return minRequired;
        }

    }

}
