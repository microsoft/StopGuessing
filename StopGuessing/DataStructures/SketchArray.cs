using System.Collections;
using System.Collections.Generic;
using System.Text;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{

    public class SketchArray
    {
        /// <summary>
        /// The number of hash functions that will index keys to elements of the array
        /// </summary>
        public int MaximumElementsPerKey { get; }

        /// <summary>
        /// The size of the sketch in bits
        /// </summary>
        public int Length => ElementArray.Length;

        /// <summary>
        /// The array storing all of the elements of the sketch
        /// </summary>
        protected readonly BitArray ElementArray;

        /// <summary>
        /// The hash functions used to index into the sketch to map keys to elements
        /// </summary>
        protected readonly UniversalHashFunction[] HashFunctionsMappingKeysToElements;

        public SketchArray(int numberOfElementsInSketch, int maximumElementsPerKey, bool initilizeElementsAtRandom)
        {
            MaximumElementsPerKey = maximumElementsPerKey;

            // Align on byte boundary to guarantee no less than numberOfElementsInSketch
            int capacityInBytes = (numberOfElementsInSketch + 7) / 8;

            // Create hash functions for map keys to elements
            HashFunctionsMappingKeysToElements = new UniversalHashFunction[maximumElementsPerKey];
            for (int i = 0; i < HashFunctionsMappingKeysToElements.Length; i++)
            {
                HashFunctionsMappingKeysToElements[i] =
                    new UniversalHashFunction(64);
            }

            if (initilizeElementsAtRandom)
            {
                // Initialize the sketch setting ~half the bits randomly to zero by using the
                // cryptographic random number generator.
                byte[] initialSketchValues = new byte[capacityInBytes];
                StrongRandomNumberGenerator.GetBytes(initialSketchValues);
                ElementArray = new BitArray(initialSketchValues);
            }
            else
            {
                // Start with an empty sketch of zero elements
                ElementArray = new BitArray(capacityInBytes * 8);
            }
        }

        /// <summary>
        /// Map a key to its corresponding rungs (indexes) on the ladder (array).
        /// </summary>
        /// <param name="key">The key to be mapped to indexes</param>
        /// <param name="numberOfElementsRequested">The number of rungs to get keys for.  If not set,
        /// uses the MaximumElementsPerKey specified in the constructor.</param>
        /// <returns>The rungs (array indexes) associated with the key</returns>
        public IEnumerable<int> GetElementIndexesForKey(byte[] key, int? numberOfElementsRequested = null)
        {

            byte[] sha256HashOfKey = ManagedSHA256.Hash(key);

            HashSet<int> elementIndexes = new HashSet<int>();
            int numberOfElementIndexes = MaximumElementsPerKey;
            if (numberOfElementsRequested.HasValue && numberOfElementsRequested.Value < HashFunctionsMappingKeysToElements.Length)
            {
                numberOfElementIndexes = numberOfElementsRequested.Value;
            }

            for (int i = 0; i < numberOfElementIndexes; i++)
            {
                UniversalHashFunction hashFunction = HashFunctionsMappingKeysToElements[i];
                do
                {
                    // Use the hash function to index into an element of the array
                    byte[] valueToHash = sha256HashOfKey;
                    int elementIndex = (int)(hashFunction.Hash(valueToHash) % (uint)Length);
                    if (elementIndexes.Add(elementIndex))
                    {
                        // Common case: this element index has not already been selected by one of the hash functions
                        // used in a previous iteration of this loop.  This index can thus represent the i_th element
                        // for this key.
                        break;
                    }
                    // This hash function generated an index that already represents another element for this key.
                    // We'll need to rehash to create an index for the i_th element that was not already assigned.
                    valueToHash = ManagedSHA256.Hash(valueToHash);
                } while (true);
            }

            return elementIndexes;
        }

        public IEnumerable<int> GetElementIndexesForKey(string key, int? numberOfElementsRequested = null)
        {
            return GetElementIndexesForKey(Encoding.UTF8.GetBytes(key), numberOfElementsRequested);
        }

        public bool SetElement(int indexOfElementToSet) => Assign(indexOfElementToSet, true);
        public bool ClearElement(int indexOfElementToClear) => Assign(indexOfElementToClear, false);


        public bool this[int index]
        {
            get { return ElementArray[index]; }
            set { ElementArray[index] = value; }
        }

        /// <summary>
        /// Assign the element at a given index to a desired value, and return true if and only if
        /// the element was already set to that value.
        /// </summary>
        /// <param name="index">The index of the element to set.</param>
        /// <param name="desiredValue">The value to set the element to.</param>
        /// <returns></returns>
        public bool Assign(int index, bool desiredValue)
        {
            bool result = ElementArray[index] != desiredValue;
            if (result)
            {
                ElementArray[index] = desiredValue;
            }
            return result;
        }

        public void SwapElements(int indexA, int indexB)
        {
            bool tmp = ElementArray[indexA];
            ElementArray[indexA] = ElementArray[indexB];
            ElementArray[indexB] = tmp;
        }


    }
}
