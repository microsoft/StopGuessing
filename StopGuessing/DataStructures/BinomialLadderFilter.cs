using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Clients;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Interfaces;

namespace StopGuessing.DataStructures
{


    /// <summary>
    /// A binomial ladder filter maps elements keys to H indexes in an n bit table of bits, which are initially set at random
    /// with a probability 0.5.  Each index represents a rung on the element's ladder. The indexes set to 1/true are rungs that
    /// are below that element on the ladder and the indexes that are 0/false are rungs above the element, or which the element
    /// has yet to climb.  On avarege, an element that has not been seen before will map to, on average,
    /// H/2 index that are set to 1 (true) and H/2 indexes that are set to 0 (false).  This means that half the rungs
    /// are below the element and half are above it, and so the element is half way up the ladder.
    /// 
    /// If a non-empty subset of the indexes that an element maps to contains 0, observing that element (the Step() method)
    /// will cause a random member of that subset to be changed from 0 to 1, causing the element to move one rung up the ladder.
    /// To ensure that the fraction of bits set to 1 stays constant, the step operation willsimultaneously clear a random bit
    /// selected from subset of indexes in the entire sketch that have value 1.  The more times an element has been observed,
    /// the higher the expected number of indexes that will have been set, the higher it moves up the ladder until
    /// reaching the top (when all of its bits are set).
    /// 
    /// To count the subset of a keys indexes that have been set, one can call the GetRungsBelow method.
    /// Natural variance will cause some never-before-seen keys to have more than k/2 bits set and keys that have been
    /// observed very rarely (or only in the distant past) to have fewer than k/2 bit set.  an element that is observed
    /// only a few times (with ~ k/2 + epsilon bits set) will be among ~ .5^{epsilon} false-positive keys that have
    /// as many bits set by chance.  Thus, until an element has been observed a more substantial number of times it is hard
    /// to differentiate from false positives.  To have confidence greater than one in a million that an element has been observed,
    /// it will take an average of 20 observations.
    /// </summary>
    public class BinomialLadderFilter : FilterArray, IBinomialLadderFilter
    {
        //public readonly FilterArray BitArray;

        public int MaxHeight => base.MaximumBitIndexesPerElement;

        /// <summary>
        /// Construct a binomial sketch, in which a set of k hash functions (k=MaxLadderHeightInRungs) will map any
        /// element to k points with an array of n bits (numberOfRungsInArray).
        /// When one Adds an element to a binomial sketch, a random bit among the subset of k that are currently 0 will be set to 1.
        /// To ensure roughly half the bits remain zero, a random index from the subset of all k bits that are currently 1 will be set to 0.
        /// 
        /// Over time, popular keys will have almost all of their bits set and unpopular keys will be expected to have roughly half their bits set.
        /// </summary>
        /// <param name="numberOfRungsInArray">The total number of bits to maintain in the table.
        /// In theoretical discussions of bloom filters and sketches, this is usually referrted to by the letter n.</param>
        /// <param name="maxLadderHeightInRungs">The number of indexes to map each element to, each of which is assigned a unique pseudorandom
        /// hash function.  This is typically referred to by the letter k.</param>
        public BinomialLadderFilter(int numberOfRungsInArray, int maxLadderHeightInRungs) : base(numberOfRungsInArray, maxLadderHeightInRungs, true)
        {
        }

        public int GetHeight(string key, int? heightOfLadderInRungs = null)
        {
            return GetIndexesAssociatedWithAnElement(key, heightOfLadderInRungs).Count(rung => this[rung]);
        }

        protected int GetIndexOfRandomBitOfDesiredValue(bool desiredValueOfElement)
        {
            int elementIndex;
            // Iterate through random elements until we find one that is set to one (true) and can be cleared
            do
            {
                elementIndex = (int)(StrongRandomNumberGenerator.Get64Bits((ulong)base.Length));
            } while (base[elementIndex] != desiredValueOfElement);

            return elementIndex;
        }

        /// <summary>
        /// When one Adds an element to a binomial sketch, a random bit among the subset of k that are currently 0 (false)
        /// will be set to 1 (true).
        /// To ensure roughly half the bits remain zero at all times, a random index from the subset of all k bits that
        /// are currently 1 (true) will be set to 0 (false).
        /// </summary>
        /// <param name="key">The element to add to the set.</param>
        /// <param name="heightOfLadderInRungs">Set if using a ladder shorter than the default for this sketch</param>
        /// <returns>Of the bits at the indices for the given element, the number of bits that were set to 1 (true)
        /// before the Step operation.  The maximum possible value to be returned, if all bits were already
        /// set to 1 (true) would be MaxLadderHeightInRungs.  If an element has not been seen before, the expected (average)
        /// result is MaxLadderHeightInRungs/2, but will vary with the binomial distribution.</returns>
        public int Step(string key, int? heightOfLadderInRungs = null)
        {
            // Get the set of rungs
            List<int> rungs = GetIndexesAssociatedWithAnElement(key, heightOfLadderInRungs).ToList();
            // Select the subset of rungs that have value zero (that are above the element in the ladder)
            List<int> rungsAbove = rungs.Where(rung => !base[rung]).ToList();

            // Identify an element of the array to set
            int indexOfElementToSet = (rungsAbove.Count > 0) ?
                // If there are rungs with value value zero/false (rungs above the element), pick one at random
                rungsAbove[(int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count))] :
                // otherwise, pick an element with value zero/false from the entire array
                GetIndexOfRandomBitOfDesiredValue(false);

            // Identify an index to clear from the entire array (selected from those elements with value 1/true)
            int indexOfElementToClear = GetIndexOfRandomBitOfDesiredValue(true);

            // Swap the values of element to be set with the element to be cleared
            SwapBits(indexOfElementToSet, indexOfElementToClear);

            // Return the height of the ladder before the step
            return rungs.Count - rungsAbove.Count;
        }

#pragma warning disable 1998
        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken())
            => Step(key, heightOfLadderInRungs);

        public async Task<int> GetHeightAsync(string element, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken()) => GetHeight(element, heightOfLadderInRungs);
#pragma warning restore 1998

    }

}
