using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StopGuessing.EncryptionPrimitives;
using System.Threading;
using System.Threading.Tasks;

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
        public int HeightOfLaddersInRungs { get; }

        /// <summary>
        /// The size of the sketch in bits
        /// </summary>
        public int TotalNumberOfRungElements => _rungElements.Length;

        /// <summary>
        /// The bits of the array that can be used as rungs of a ladder
        /// </summary>
        private readonly BitArray _rungElements;

        /// <summary>
        /// The hash functions used to index into the sketch to map keys to rungs
        /// (There is one hash function per rung)
        /// </summary>
        private readonly UniversalHashFunction[] _hashFunctionsMappingKeysToRungs;


        /// <summary>
        /// Construct a binomial sketch, in which a set of k hash functions (k=HeightOfLaddersInRungs) will map any
        /// key to k points with an array of n bits (numberOfRungsInSketch).
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 will be set to 1.
        /// To ensure roughly half the bits remain zero, a random index from the subset of all k bits that are currently 1 will be set to 0.
        /// 
        /// Over time, popular keys will have almost all of their bits set and unpopular keys will be expected to have roughly half their bits set.
        /// </summary>
        /// <param name="numberOfRungsInSketch">The total number of bits to maintain in the table.
        /// In theoretical discussions of bloom filters and sketches, this is usually referrted to by the letter n.</param>
        /// <param name="heightOfLaddersInRungs">The number of indexes to map each key to, each of which is assigned a unique pseudorandom
        /// hash function.  This is typically referred to by the letter k.</param>
        /// <param name="keyToPreventAlgorithmicComplexityAttacks">A pseudorandom seed that allows the same sketch to be created
        /// twice, but (if kept secret) prevents an attacker from knowing the distribution of hashes and thus counters
        /// algorithmic complexity attacks.</param>
        public BinomialLadderSketch(int numberOfRungsInSketch, int heightOfLaddersInRungs, string keyToPreventAlgorithmicComplexityAttacks)
        {
            HeightOfLaddersInRungs = heightOfLaddersInRungs;
            string keyToPreventAlgorithmicComplexityAttacks1 = keyToPreventAlgorithmicComplexityAttacks ?? StrongRandomNumberGenerator.Get64Bits().ToString();
            int actualSizeInBits = numberOfRungsInSketch;

            // Align on next byte boundary
            if ((actualSizeInBits & 7) != 0)
                actualSizeInBits = (numberOfRungsInSketch + 8) ^ 0x7;
            int capacityInBytes = actualSizeInBits / 8;

            _hashFunctionsMappingKeysToRungs = new UniversalHashFunction[heightOfLaddersInRungs];
            for (int i = 0; i < _hashFunctionsMappingKeysToRungs.Length; i++)
            {
                _hashFunctionsMappingKeysToRungs[i] =
                    new UniversalHashFunction(i.ToString() + keyToPreventAlgorithmicComplexityAttacks1, 64);
            }

            // Initialize the sketch setting ~half the bits randomly to zero by using the
            // cryptographic random number generator.
            byte[] initialSketchValues = new byte[capacityInBytes];
            StrongRandomNumberGenerator.GetBytes(initialSketchValues);
            _rungElements = new BitArray(initialSketchValues);
        }

        /// <summary>
        /// Map a key to its corresponding rungs (indexes) on the ladder (array).
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <param name="numberOfRungs">The number of rungs to get keys for.  If not set,
        /// uses the HeightOfLaddersInRungs specified in the constructor.</param>
        /// <returns>The rungs (array indexes) associated with the key</returns>
        protected IEnumerable<int> GetRungs(byte[] key, int? numberOfRungs = null)
        {

            byte[] sha256HashOfKey = ManagedSHA256.Hash(key);

            if (numberOfRungs == null || numberOfRungs.Value >= _hashFunctionsMappingKeysToRungs.Length)
                return _hashFunctionsMappingKeysToRungs.Select(f => (int) (f.Hash(sha256HashOfKey)%(uint) TotalNumberOfRungElements));
            else
                return _hashFunctionsMappingKeysToRungs.Take(numberOfRungs.Value).Select(f => (int)(f.Hash(sha256HashOfKey) % (uint)TotalNumberOfRungElements));
        }

        protected IEnumerable<int> GetRungs(string key, int? numberOfRungs = null)
        {
            return GetRungs(Encoding.UTF8.GetBytes(key), numberOfRungs);
        }

        /// <summary>
        /// Get the subset of indexes for a given key that map to bits that are not set (a.k.a. 0 or false).
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <param name="numberOfRungs">The number of rungs to get keys for.  If not set,
        /// uses the HeightOfLaddersInRungs specified in the constructor.</param>
        /// <returns>The rungs (array indexes) associated with the key that are above the position
        /// of the key on the ladder (the elements at these indexes are set to zero/false).</returns>
        public IEnumerable<int> GetRungsAbove(string key, int? numberOfRungs = null)
        {
            return GetRungs(key, numberOfRungs).Where(index => !_rungElements[index]);
        }


        
        protected void Step(int indexOfRungToClimb, int indexOfNonRungElementToClear)
        {
            // ReSharper disable once RedundantBoolCompare
            if (_rungElements[indexOfRungToClimb] == true || _rungElements[indexOfNonRungElementToClear] == false)
                return;
            _rungElements[indexOfRungToClimb] = true;
            _rungElements[indexOfNonRungElementToClear] = false;
        }

        public void Step(int indexOfZeroElementToSetToOne)
        {            
            int randomIndexToAnElementSetToOneThatWeCanClearToZero;
            do
            {
                // Iterate through random elements until we find one that is set to one (true) and can be cleared
                randomIndexToAnElementSetToOneThatWeCanClearToZero = (int) (StrongRandomNumberGenerator.Get64Bits((ulong) _rungElements.Length));
            } while (_rungElements[randomIndexToAnElementSetToOneThatWeCanClearToZero] == false);

            Step(indexOfZeroElementToSetToOne, randomIndexToAnElementSetToOneThatWeCanClearToZero);
        }


        /// <summary>
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 (false)
        /// will be set to 1 (true).
        /// To ensure roughly half the bits remain zero at all times, a random index from the subset of all k bits that
        /// are currently 1 (true) will be set to 0 (false).
        /// </summary>
        /// <param name="key">The key to add to the set.</param>       
        /// <returns>Of the bits at the indices for the given key, the number of bits that were set to 1 (true)
        /// before the Step operation.  The maximum possible value to be returned, if all bits were already
        /// set to 1 (true) would be HeightOfLaddersInRungs.  If a key has not been seen before, the expected (average)
        /// result is HeightOfLaddersInRungs/2, but will vary with the binomial distribution.</returns>
        public int Step(string key)
        {
            // Get a list of indexes that are not yet set
            List<int> rungsAbove = GetRungsAbove(key).ToList();

            // We can only update state to record the observation if there is an unset (0) index that we can set (to 1).
            if (rungsAbove.Count > 0)
            {
                //FIXME REMOVEME NumberOfObservations++;
                
                // First, pick an index to a zero element at random.
                int indexToSet = rungsAbove[ (int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count)) ];

                // Next, set the zero element associted with the key and clear a random one element from the entire array
                Step(indexToSet);
            }

            // The number of bits set to 1/true is the number that were not 0/false.
            return HeightOfLaddersInRungs - rungsAbove.Count;
        }

    }
}
