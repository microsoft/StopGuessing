using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A FilterArray is a bit array that contains additional methods to support probabilistic data
    /// structures that map elements (keys) to a set of deterministic, pseudorandom indexes in the bit array.
    /// These methods support data structures such as bloom filters and binomial ladder filters that
    /// store information about an element within the bits at the indexes that are pseudorandomly associated
    /// with the element.
    /// </summary>
    public class FilterArray
    {
        /// <summary>
        /// The bit array used by a filter
        /// </summary>
        protected readonly BitArray BitArray;

        /// <summary>
        /// The hash functions used to index into the bit array to map elements to a set of bits
        /// </summary>
        protected readonly UniversalHashFunction[] HashFunctionsMappingElementsToBitsInTheArray;

        /// <summary>
        /// The number of bits in the bit array
        /// </summary>
        public int Length => BitArray.Length;

        /// <summary>
        /// The number of hash functions that will index elements to indexes in the bit array
        /// </summary>
        public int MaximumBitIndexesPerElement => HashFunctionsMappingElementsToBitsInTheArray.Length;

        /// <summary>
        /// Construct a filter array.
        /// </summary>
        /// <param name="numberOfBitsInArray">The size of the array in bits.</param>
        /// <param name="maximumBitIndexesPerElement">The maximum (and default) number of indexes (bits) in the array to associate with elements.</param>
        /// <param name="initilizeBitsOfArrayAtRandom">If set to true, the bits of the filter array will be set to 0 or 1 at random (indpendently, each with probability 0.5).</param>
        /// <param name="saltForHashFunctions">A salt used to generate the hash functions.
        /// Any two filter arrays generated with the same salt will use the same hash functions.
        /// The salt should be kept secret from attackerse who might try to manipulate the selection of elements,
        /// such as to intentionally cause bit collisions with the array.</param>
        public FilterArray(int numberOfBitsInArray, int maximumBitIndexesPerElement, bool initilizeBitsOfArrayAtRandom,
            string saltForHashFunctions = "")
        {
            // Align on byte boundary to guarantee no less than numberOfBitsInArray
            int capacityInBytes = (numberOfBitsInArray + 7) / 8;

            // Create hash functions to map elements to indexes in the bit array.
            HashFunctionsMappingElementsToBitsInTheArray = new UniversalHashFunction[maximumBitIndexesPerElement];
            for (int i = 0; i < HashFunctionsMappingElementsToBitsInTheArray.Length; i++)
            {
                HashFunctionsMappingElementsToBitsInTheArray[i] =
                    new UniversalHashFunction(i + ":" + saltForHashFunctions, 64);
            }

            if (initilizeBitsOfArrayAtRandom)
            {
                // Initialize the bit array setting ~half the bits randomly to zero by using the
                // cryptographic random number generator.
                byte[] initialBitValues = new byte[capacityInBytes];
                StrongRandomNumberGenerator.GetBytes(initialBitValues);
                BitArray = new BitArray(initialBitValues);
            }
            else
            {
                // Start with all bits of the array set to zero.
                BitArray = new BitArray(capacityInBytes * 8);
            }
        }

        /// <summary>
        /// Map a element to its corresponding bits in the array.
        /// </summary>
        /// <param name="element">The element to be mapped to indexes</param>
        /// <param name="numberOfIndexesRequested">The number of indexes to associate with the element.  If not set,
        /// uses the MaximumBitIndexesPerElement specified in the constructor.</param>
        /// <returns>The array indexes associated with the element.</returns>
        public IEnumerable<int> GetIndexesAssociatedWithAnElement(byte[] element, int? numberOfIndexesRequested = null)
        {
            // Build a set of indexes into the bit array associated with the element
            HashSet<int> indexesIntoBitArray = new HashSet<int>();

            // Determine how many indexes to generate for this element.
            // The lesser of the maximum number supported and the number requested (if provided)
            int numberOfBitIndexesToCreate = Math.Min(HashFunctionsMappingElementsToBitsInTheArray.Length, numberOfIndexesRequested ?? int.MaxValue);

            // Use one hash function to generate each index
            for (int i = 0; i < numberOfBitIndexesToCreate; i++)
            {
                UniversalHashFunction hashFunction = HashFunctionsMappingElementsToBitsInTheArray[i];
                byte[] valueToHash = ManagedSHA256.Hash(element);
                do
                {
                    // Use the hash function to index into the array of bits
                    int indexIntoBitArray = (int)(hashFunction.Hash(valueToHash) % (uint)Length);
                    if (indexesIntoBitArray.Add(indexIntoBitArray))
                    {
                        // Common case: this index points to a bit that is not yet associated with the element
                        // This index can thus represent the i_th bit for this element.
                        break;
                    }
                    // This hash function generated an index to a bit that is already associated with this element.
                    // We'll need to rehash to create an index for the i_th bit that was not already assigned.
                    valueToHash = ManagedSHA256.Hash(valueToHash);
                } while (true);
            }

            return indexesIntoBitArray;
        }

        /// <summary>
        /// Get the set of indexes associated with an element.
        /// </summary>
        /// <param name="element">The value that should be associated with a set of indexes.</param>
        /// <param name="numberOfIndexesRequested">The optional number of indexes to associate with the element.  If not provided,
        /// the number of indexes returned will be equal to the default (MaximumBitIndexesPerElement) set in the constructor.</param>
        /// <returns></returns>
        public IEnumerable<int> GetIndexesAssociatedWithAnElement(string element, int? numberOfIndexesRequested = null)
        {
            return GetIndexesAssociatedWithAnElement(Encoding.UTF8.GetBytes(element), numberOfIndexesRequested);
        }

        /// <summary>
        /// Set the bit an in index to one (true).
        /// </summary>
        /// <param name="indexOfBitToSet">The index of the bit to set.</param>
        /// <returns>True if the operation caused the value of the bit to change, false otherwise.</returns>
        public bool SetBitToOne(int indexOfBitToSet) => AssignBit(indexOfBitToSet, true);
        /// <summary>
        /// Clear the bit an in index to zero (false).
        /// </summary>
        /// <param name="indexOfBitToClear">The index of the bit to clear.</param>
        /// <returns>True if the operation caused the value of the bit to change, false otherwise.</returns>
        public bool ClearBitToZero(int indexOfBitToClear) => AssignBit(indexOfBitToClear, false);

        /// <summary>
        /// Read or write bits of the filter array directly.
        /// </summary>
        /// <param name="index">The index of the bit to read/write.</param>
        /// <returns>The value of the bit.</returns>
        public bool this[int index]
        {
            get { return BitArray[index]; }
            set { BitArray[index] = value; }
        }

        /// <summary>
        /// Assign the bit at a given index to a desired value, and return true if and only if
        /// the bit was already set to that value.
        /// </summary>
        /// <param name="indexOfTheBitToAssign">The index of the bit to set.</param>
        /// <param name="desiredValue">The value to set the bit to.</param>
        /// <returns></returns>
        public bool AssignBit(int indexOfTheBitToAssign, bool desiredValue)
        {
            bool result = BitArray[indexOfTheBitToAssign] != desiredValue;
            if (result)
            {
                BitArray[indexOfTheBitToAssign] = desiredValue;
            }
            return result;
        }
        
        /// <summary>
        /// Clear a randomly-selected bit of the filter array to zero.
        /// </summary>
        public void ClearRandomBitToZero()
        {
            ClearBitToZero((int)StrongRandomNumberGenerator.Get32Bits((uint)BitArray.Length));
        }

        /// <summary>
        /// Set a randomly-selected bit of the filter array to one.
        /// </summary>
        public void SetRandomBitToOne()
        {
            SetBitToOne((int)StrongRandomNumberGenerator.Get32Bits((uint)BitArray.Length));
        }

        /// <summary>
        /// Assign the value of either zero or one to a randomly-selected bit of the filter array.
        /// </summary>
        /// <param name="value">The value to be assigned.  Passing 0 causes a value 0 (false) to be stored
        /// and passing any other value causes a one (true) to be stored in the randomly-selected bit.</param>
        public void AssignRandomBit(int value)
        {
            AssignBit((int)StrongRandomNumberGenerator.Get32Bits((uint)BitArray.Length), value != 0);
        }

        /// <summary>
        /// Swap the values of the bits at two indexes into the filter array.
        /// </summary>
        /// <param name="indexA">Index to the first of the two bits to swap.</param>
        /// <param name="indexB">Index to the second of the two bits to swap.</param>
        public void SwapBits(int indexA, int indexB)
        {
            bool tmp = BitArray[indexA];
            BitArray[indexA] = BitArray[indexB];
            BitArray[indexB] = tmp;
        }
        
    }
}
