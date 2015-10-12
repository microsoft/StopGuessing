using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A binomial sketch maps keys to k indexes in an n bit table of bits, which are initially set at random
    /// with a probability 0.5.  On avarege, a key that has not been seen before will map to, on average,
    /// k/2 indexes that are set to 1 (true) and k/2 indexes that are set to 0 (false).
    /// 
    /// If a non-empty subset of the indexes that a key maps to contains 0, observing that key (the Observe() method)
    /// will cause a random member of that subset to be changed from 0 to 1.  To ensure that the fraction of bits set to 1
    /// stays constant, it will simultaneously clear a random bit selected from subset of indexes in the entire sketch
    /// that have value 1.  The more times a key has been observed, the higher the expected number of indexes that will
    /// have been set.
    /// 
    /// To count the subset of a keys indexes that have been set, one can call the GetNumberOfIndexesSet method.
    /// Natural variance will cause some never-before-seen keys to have more than k/2 bits set and keys that have been
    /// observed very rarely (or only in the distant past) to have fewer than k/2 bit set.  A key that is observed
    /// only a few times FIXME  (with ~ k/2 + epsilon bits set) will be no more 
    /// 
    /// </summary>
    public class BinomialSketch
    {
        /// <summary>
        /// The number of hash functions that will index into the sketch
        /// </summary>
        public int NumberOfIndexes { get; }

        /// <summary>
        /// The size of the sketch in bits
        /// </summary>
        public int SizeInBits { get; }

        // The bits of the sketch
        private readonly BitArray _sketch;

        // The hash functions used to index into the sketch
        private readonly UniversalHashFunction[] _universalHashFunctions;

        /// <summary>
        /// Construct a binomial sketch, in which a set of k hash functions (k=NumberOfIndexes) will map any
        /// key to k points with an array of n bits (sizeInBits).
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 will be set to 1.
        /// To ensure roughly half the bits remain zero, a random index from the subset of all k bits that are currently 1 will be set to 0.
        /// 
        /// Over time, popular keys will have almost all of their bits set and unpopular keys will be expected to have roughly half their bits set.
        /// </summary>
        /// <param name="sizeInBits">The total number of bits to maintain in the table.  In theoretical discussions of bloom filters and sketches
        /// in general, this is usually referrted to by the letter n.</param>
        /// <param name="numberOfIndexes">The number of indexes to map each key to, each of which is assigned a unique pseudorandom
        /// hash function.</param>
        /// <param name="keyToPreventAlgorithmicComplexityAttacks">A pseudorandom seed that allows the same sketch to be created
        /// twice, but (if kept secret) prevents an attacker from knowing the distribution of hashes and thus counters
        /// algorithmic complexity attacks.</param>
        public BinomialSketch(int sizeInBits, int numberOfIndexes, string keyToPreventAlgorithmicComplexityAttacks)
        {
            NumberOfIndexes = numberOfIndexes;
            string keyToPreventAlgorithmicComplexityAttacks1 = keyToPreventAlgorithmicComplexityAttacks ?? "";
            SizeInBits = sizeInBits;
            // Align on next byte boundary
            if ((SizeInBits & 7) != 0)
                SizeInBits = (sizeInBits + 8) ^ 0x7;
            int capacityInBytes = SizeInBits / 8;

            _universalHashFunctions = new UniversalHashFunction[numberOfIndexes];
            for (int i = 0; i < _universalHashFunctions.Length; i++)
            {
                _universalHashFunctions[i] =
                    new UniversalHashFunction(i.ToString() + keyToPreventAlgorithmicComplexityAttacks1, 64);
            }
            // Initialize the sketch setting ~half the bits randomly to zero by using the
            // cryptographic random number generator.
            byte[] initialSketchValues = new byte[capacityInBytes];
            RandomNumberGenerator.Create().GetBytes(initialSketchValues);
            _sketch = new BitArray(initialSketchValues);
        }

        /// <summary>
        /// Map a key to its corresponding indexes in the sketch.
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <returns></returns>        /// 
        private IEnumerable<int> GetIndexesForKey(byte[] key)
        {
            byte[] sha256HashOfKey = SHA256.Create().ComputeHash(key);

            return _universalHashFunctions.Select(f => (int) f.Hash(sha256HashOfKey) % SizeInBits);
        }

        private IEnumerable<int> GetIndexesForKey(string key)
        {
            return GetIndexesForKey(Encoding.UTF8.GetBytes(key));
        }

        /// <summary>
        /// Get the subset of indexes for a given key that map to bits that are not set (a.k.a. 0 or false).
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private IEnumerable<int> GetUnsetIndexesForKey(string key)
        {
            return GetIndexesForKey(key).Where(index => !_sketch[index]);
        }

        /// <summary>
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 (false)
        /// will be set to 1 (true).
        /// To ensure roughly half the bits remain zero at all times, a random index from the subset of all k bits that
        /// are currently 1 (true) will be set to 0 (false).
        /// </summary>
        /// <param name="key">The key to add to the set.</param>
        /// <param name="rng">Optionally passing a RandomNumberGenerator should improve performance as it will
        /// save the Observe operation from having to create one. (RandomNumberGenerators are not thread safe, and
        /// should not be used between Tasks/Threads.)</param>
        /// <returns>Of the bits at the indices for the given key, the number of bits that were set to 1 (true)
        /// before the Observe operation.  The maximum possible value to be returned, if all bits were already
        /// set to 1 (true) would be NumberOfIndexes.  If a key has not been seen before, the expected (average)
        /// result is NumberOfIndexes/2, but will vary with the binomial distribution.</returns>
        public int Observe(string key, RandomNumberGenerator rng = null)
        {
            rng = rng ?? RandomNumberGenerator.Create();

            List<int> indexesUnset = GetUnsetIndexesForKey(key).ToList();
            if (indexesUnset.Count > 0)
            {
                byte[] randBytes = new byte[8];
                rng.GetBytes(randBytes);
                int indexToSet = indexesUnset[ (int)_universalHashFunctions[_universalHashFunctions.Length - 1].Hash(randBytes) %
                                               indexesUnset.Count];
                int indexToClear = 0;
                foreach (var hashFunction in _universalHashFunctions)
                {
                    indexToClear = (int)hashFunction.Hash(randBytes) % SizeInBits;
                    if (_sketch[indexToClear])
                        break;
                }
                _sketch[indexToClear] = false;
                _sketch[indexToSet] = true;
            }
            return NumberOfIndexes - indexesUnset.Count;
        }

        /// <summary>
        /// For a given key that maps to NumberOfIndexes in the sketch, return the number of those indexes
        /// that are set to 1 (true).
        /// </summary>
        /// <param name="key">The key to query the sketch for.</param>
        /// <returns>Of the bits at the indices for the given key, the number of bits that are set to 1 (true).
        /// The maximum possible value to be returned, if all bits are set to 1 (true),
        /// would be NumberOfIndexes.  If a key has not been seen before, the expected (average) result is
        /// NumberOfIndexes/2, but will vary with the binomial distribution.</returns>
        public int GetNumberOfIndexesSet(string key)
        {
            return NumberOfIndexes - GetUnsetIndexesForKey(key).Count();
        }
    }
}
