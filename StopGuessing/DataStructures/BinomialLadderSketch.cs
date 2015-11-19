using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
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
    public class BinomialLadderSketch
    {

        /// <summary>
        /// The number of hash functions that will index keys to rungs
        /// </summary>
        public int MaxLadderHeightInRungs { get; }

        /// <summary>
        /// The size of the sketch in bits
        /// </summary>
        public int TotalNumberOfRungElements => RungElements.Length;

        /// <summary>
        /// The bits of the array that can be used as rungs of a ladder
        /// </summary>
        protected readonly BitArray RungElements;

        /// <summary>
        /// The hash functions used to index into the sketch to map keys to rungs
        /// (There is one hash function per rung)
        /// </summary>
        protected readonly UniversalHashFunction[] HashFunctionsMappingKeysToRungs;


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
            MaxLadderHeightInRungs = maxLadderHeightInRungs;

            // Align on byte boundary to guarantee no less than numberOfRungsInSketch
            int capacityInBytes = (numberOfRungsInSketch + 7) / 8;

            // Create hash functions for each rung
            HashFunctionsMappingKeysToRungs = new UniversalHashFunction[maxLadderHeightInRungs];
            for (int i = 0; i < HashFunctionsMappingKeysToRungs.Length; i++)
            {
                HashFunctionsMappingKeysToRungs[i] =
                    new UniversalHashFunction(64);
            }

            // Initialize the sketch setting ~half the bits randomly to zero by using the
            // cryptographic random number generator.
            byte[] initialSketchValues = new byte[capacityInBytes];
            StrongRandomNumberGenerator.GetBytes(initialSketchValues);
            RungElements = new BitArray(initialSketchValues);
        }

        /// <summary>
        /// Map a key to its corresponding rungs (indexes) on the ladder (array).
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <param name="heightOfLadderInRungs">The number of rungs to get keys for.  If not set,
        /// uses the MaxLadderHeightInRungs specified in the constructor.</param>
        /// <returns>The rungs (array indexes) associated with the key</returns>
        protected IEnumerable<int> GetRungs(byte[] key, int? heightOfLadderInRungs = null)
        {

            byte[] sha256HashOfKey = ManagedSHA256.Hash(key);

            if (heightOfLadderInRungs == null || heightOfLadderInRungs.Value >= HashFunctionsMappingKeysToRungs.Length)
                return HashFunctionsMappingKeysToRungs.Select(f => (int) (f.Hash(sha256HashOfKey)%(uint) TotalNumberOfRungElements));
            else
                return HashFunctionsMappingKeysToRungs.Take(heightOfLadderInRungs.Value).Select(f => (int)(f.Hash(sha256HashOfKey) % (uint)TotalNumberOfRungElements));
        }

        protected IEnumerable<int> GetRungs(string key, int? heightOfLadderInRungs = null)
        {
            return GetRungs(Encoding.UTF8.GetBytes(key), heightOfLadderInRungs);
        }

        /// <summary>
        /// Get the subset of indexes for a given key that map to bits that are not set (a.k.a. 0 or false).
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <param name="heightOfLadderInRungs">The number of rungs to get keys for.  If not set,
        /// uses the MaxLadderHeightInRungs specified in the constructor.</param>
        /// <returns>The rungs (array indexes) associated with the key that are above the position
        /// of the key on the ladder (the elements at these indexes are set to zero/false).</returns>
        public IEnumerable<int> GetRungsAbove(string key, int? heightOfLadderInRungs = null)
        {
            return GetRungs(key, heightOfLadderInRungs).Where(index => !RungElements[index]);
        }

        
        protected void Step(int rungElementToClimb, int indexOfNonRungElementToClear)
        {
            // ReSharper disable once RedundantBoolCompare
            if (RungElements[rungElementToClimb] == true || RungElements[indexOfNonRungElementToClear] == false)
                return;
            RungElements[rungElementToClimb] = true;
            RungElements[indexOfNonRungElementToClear] = false;
        }

        public void Step(int rungElementToClimb)
        {            
            int randomClimbedRungElementToMarkAsUnclimbed;
            do
            {
                // Iterate through random elements until we find one that is set to one (true) and can be cleared
                randomClimbedRungElementToMarkAsUnclimbed = (int) (StrongRandomNumberGenerator.Get64Bits((ulong) RungElements.Length));
            } while (RungElements[randomClimbedRungElementToMarkAsUnclimbed] == false);

            Step(rungElementToClimb, randomClimbedRungElementToMarkAsUnclimbed);
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
            // Get a list of indexes that are not yet set
            List<int> rungsAbove = GetRungsAbove(key, heightOfLadderInRungs).ToList();

            // We can only update state to record the observation if there is an unset (0) index that we can set (to 1).
            if (rungsAbove.Count > 0)
            {
                // First, pick an index to a zero element at random.
                int indexToSet = rungsAbove[ (int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count)) ];

                // Next, set the zero element associted with the key and clear a random one element from the entire array
                Step(indexToSet);
            }

            // The number of bits set to 1/true is the number that were not 0/false.
            return MaxLadderHeightInRungs - rungsAbove.Count;
        }


        public BinomialLadder GetLadder(string key, int? heightOfLadderInRungs = null)
        {
            int heightOfLadderInRungsOrDefault = heightOfLadderInRungs ?? MaxLadderHeightInRungs;
            return new BinomialLadder(this, GetRungsAbove(key, heightOfLadderInRungsOrDefault), heightOfLadderInRungsOrDefault);
        }


        public class BinomialLadder : BinomialLadderForKey<int>
        {
            protected BinomialLadderSketch Sketch;
            public BinomialLadder(BinomialLadderSketch sketch, IEnumerable<int> rungsNotYetClimbed, int heightOfLadderInRungs)
                : base(rungsNotYetClimbed, heightOfLadderInRungs)
            {
                Sketch = sketch;
            }

            protected override async Task StepAsync(int rungToClimb, CancellationToken cancellationToken = new CancellationToken())
            {
                await Task.Run(() =>
                {
                    Sketch.Step(rungToClimb);
                }, cancellationToken);

            }
        }

    }
}
