using System;
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
    /// only a few times (with ~ k/2 + epsilon bits set) will be among ~ .5^{epsilon} false-positive keys that have
    /// as many bits set by chance.  Thus, until a key has been observed a more substantial number of times it is hard
    /// to differentiate from false positives.  To have confidence greater than one in a million that a key has been observed,
    /// it will take an average of 20 observations.
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

        public ulong NumberOfObservations { get; protected set; }

        public int NumberOfObservationsAccountingForAging => (int)
            Math.Min(NumberOfObservations, _maxNumberOfObservationsAccountingForAging);

        private readonly ulong _maxNumberOfObservationsAccountingForAging;

        private readonly double[] _cumulativeProbabilitySetByChance;

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
            _maxNumberOfObservationsAccountingForAging = (ulong) SizeInBits/(ulong) (NumberOfIndexes*2);
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
            StrongRandomNumberGenerator.GetBytes(initialSketchValues);
            _sketch = new BitArray(initialSketchValues);

            // binomialProbability[i] = (n choose k) * (p)^k * (1-p)^(n-k)
            // since p=.5, this is (n choose k) 0.5^(n)
            double[] binomialProbability = new double[numberOfIndexes + 1];
            double probabilityOfAnyGivenValue = Math.Pow(0.5d, numberOfIndexes);
            double nChooseK = 1d;
            for (int k = 0; k <= numberOfIndexes/2; k++)
            {
                binomialProbability[k] = binomialProbability[numberOfIndexes-k] =
                    nChooseK * probabilityOfAnyGivenValue;
                nChooseK *= (numberOfIndexes - k)/(1d + k);
            }

            _cumulativeProbabilitySetByChance = new double[numberOfIndexes + 1];
            _cumulativeProbabilitySetByChance[numberOfIndexes] = binomialProbability[numberOfIndexes];
            for (int k = numberOfIndexes; k > 0; k--)
                _cumulativeProbabilitySetByChance[k-1] = 
                    _cumulativeProbabilitySetByChance[k] + binomialProbability[k-1];
        }

        /// <summary>
        /// Map a key to its corresponding indexes in the sketch.
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <returns></returns>        /// 
        private IEnumerable<int> GetIndexesForKey(byte[] key)
        {
            byte[] sha256HashOfKey = ManagedSHA256.Hash(key);

            return _universalHashFunctions.Select(f => (int) (f.Hash(sha256HashOfKey) % (uint)SizeInBits));
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
        public IEnumerable<int> GetIndexesOfZeroElements(string key)
        {
            return GetIndexesForKey(key).Where(index => !_sketch[index]);
        }




        public void SetAZeroElementToOneAndClearAOneElementToZero(
            int indexOfAZeroElementToSetToOne, int indexOfAOneElementToClearToZero)
        {
            // ReSharper disable once RedundantBoolCompare
            if (_sketch[indexOfAZeroElementToSetToOne] == true || _sketch[indexOfAOneElementToClearToZero] == false)
                return;
            _sketch[indexOfAZeroElementToSetToOne] = true;
            _sketch[indexOfAOneElementToClearToZero] = false;
        }

        public void SetAZeroElementToOneAndClearARandomOneElementToZero(int indexOfZeroElementToSetToOne)
        {            
            int randomIndexToAnElementSetToOneThatWeCanClearToZero;
            do
            {
                // Iterate through random elements until we find one that is set to one (true) and can be cleared
                randomIndexToAnElementSetToOneThatWeCanClearToZero = (int) (StrongRandomNumberGenerator.Get64Bits((ulong) _sketch.Length));
            } while (_sketch[randomIndexToAnElementSetToOneThatWeCanClearToZero] == false);

            SetAZeroElementToOneAndClearAOneElementToZero(indexOfZeroElementToSetToOne, 
                randomIndexToAnElementSetToOneThatWeCanClearToZero);
        }


        /// <summary>
        /// When one Adds a key to a binomial sketch, a random bit among the subset of k that are currently 0 (false)
        /// will be set to 1 (true).
        /// To ensure roughly half the bits remain zero at all times, a random index from the subset of all k bits that
        /// are currently 1 (true) will be set to 0 (false).
        /// </summary>
        /// <param name="key">The key to add to the set.</param>       
        /// <returns>Of the bits at the indices for the given key, the number of bits that were set to 1 (true)
        /// before the Observe operation.  The maximum possible value to be returned, if all bits were already
        /// set to 1 (true) would be NumberOfIndexes.  If a key has not been seen before, the expected (average)
        /// result is NumberOfIndexes/2, but will vary with the binomial distribution.</returns>
        public int Observe(string key)
        {
            // Get a list of indexes that are not yet set
            List<int> zeroElementIndexes = GetIndexesOfZeroElements(key).ToList();

            // We can only update state to record the observation if there is an unset (0) index that we can set (to 1).
            if (zeroElementIndexes.Count > 0)
            {
                NumberOfObservations++;
                
                // First, pick an index to a zero element at random.
                int indexToSet = zeroElementIndexes[ (int) (StrongRandomNumberGenerator.Get32Bits((uint) zeroElementIndexes.Count)) ];

                // Next, set the zero element associted with the key and clear a random one element from the entire array
                SetAZeroElementToOneAndClearARandomOneElementToZero(indexToSet);
            }

            // The number of bits set to 1/true is the number that were not 0/false.
            return NumberOfIndexes - zeroElementIndexes.Count;
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
            return NumberOfIndexes - GetIndexesOfZeroElements(key).Count();
        }

        /// <summary>
        /// Determine the probability that a given number of indexes might be set simply by random chance.
        /// If testing to determine whether we've observed a key before, this is a test of the null
        /// hypothesesis that we have not observed the key.  (In other words, when we have not observed
        /// a key, the only reason for indexes to be set would be chance.)  The inverse of this probability
        /// is the expected number of tests one would expect to get one false positive in.
        /// </summary>
        /// <param name="numberOfIndexesSet">The number of indexes set for a given key, as returned by
        /// GetNumberoFindexesSet() or Observe().</param>
        /// <returns>The probability p</returns>
        public double TestNullHypothesisThatAllIndexesWereSetByChance(int numberOfIndexesSet)
        {
            return _cumulativeProbabilitySetByChance[NumberOfIndexes];
        }

        public double TestNullHypothesisThatAllIndexesWereSetByChance(string key)
        {
            return TestNullHypothesisThatAllIndexesWereSetByChance(GetNumberOfIndexesSet(key));
        }

        /// <summary>
        /// Estimates the number of observations of a key (the number of times Observe(key) has been called) at a given level
        /// of statistical confidence (p value).
        /// In other words, how many observations can we assume occurred and reject the null hypothesis that fewer observations
        /// occurred and the nubmer of bits set was this high due to chance.
        /// </summary>
        /// <param name="numberOfIndexesSet">The number of indexes set in the sketch as returned by a call to Observe() or Add().
        /// </param>
        /// <param name="confidenceLevelCommonlyCalledPValue">The p value, or confidence level, at which we want to be sure
        /// the claimed number of observations occurred.</param>
        /// <returns></returns>
        public int CountObservationsForGivenConfidence(int numberOfIndexesSet, double confidenceLevelCommonlyCalledPValue)
        {
            int observations = 0;
            while (_cumulativeProbabilitySetByChance[numberOfIndexesSet] < confidenceLevelCommonlyCalledPValue)
            {
                numberOfIndexesSet--;
                observations++;
            }
            return observations;            
        }

        /// <param name="key">The key to estimate the number of observations of.</param>
        /// <param name="confidenceLevelCommonlyCalledPValue">The p value, or confidence level, at which we want to be sure
        /// the claimed number of observations occurred.</param>
        public int CountObservationsForGivenConfidence(string key, double confidenceLevelCommonlyCalledPValue)
        {
            return CountObservationsForGivenConfidence(GetNumberOfIndexesSet(key), confidenceLevelCommonlyCalledPValue);
        }

    }
}
