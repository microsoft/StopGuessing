namespace StopGuessing.DataStructures
{
    /// <summary>
    /// This class stores arrays of unsigned values of arbitrary bit-length (up to 64 bits),
    /// accessed as if it were an array of ulongs.  For example, it can be used to create an
    /// array of values from 0..7 that consumes only 3 bits per element (plus some contant padding)
    /// or an array of values from 0..1023 that requires only 10 bits per element (plus padding).
    /// This implementation stores the values in a byte array that is publically accessible, allowing
    /// the data to be written to disk using standard interfaces for writing byte arrays.
    /// </summary>
    public class ArrayOfUnsignedNumericOfNonstandardSize
    {

        /// <summary>
        /// The array is stored in any underlying byte array, which is made public so that the values can
        /// be read/written to other forms of storage (e.g., disk)
        /// </summary>
        public byte[] AsByteArray { get; protected set; }

        /// <summary>
        /// The number of elements in the array
        /// </summary>
        public long LongLength { get; protected set; }
        public int Length => (int)LongLength;

        /// <summary>
        /// The number of bits used to store each unsigned value.
        /// The range of values that can be stored at each element are 0 ... ((2^BitsPerValue)-1).
        /// </summary>
        public int BitsPerElement { get; protected set; }

        /// <summary>
        /// The maximum value that can be stored in an element represented using <code>BitsPerElement</code> bits
        /// </summary>
        protected ulong MaxValue;

        /// <summary>
        /// Access the array of values as ulongs, using a ulong index, to read and write like any other array.
        /// If a value is greater than can be stored, the rightmost BitsPerValue bits will be stored.
        /// </summary>
        /// <param name="index">The index into the array</param>
        /// <returns></returns>
        public ulong this[long index]
        {
            get { return ReadValue(index); }
            set { WriteValue(index, value); }
        }

        /// <summary>
        ///    This static factory method is used to create instances of ArrayOfUnsignedNumericOfNonstandardSize.
        ///    We use this factory, rather than a standard constructor, so that we have the option to substitute in a more 
        ///    performant class if the BitsPerValue is 1 (a simple bit array), 8 (a byte array), 16, 32, or 64.
        /// </summary>
        /// <param name="length">The number of elements in the array.</param>
        /// <param name="bitsPerValue">The number of bits used to store each element.</param>
        /// <returns>An array of Length elements, each of which stores an unsigned numeric values using BitsPerValue bits of the array.</returns>
        public static ArrayOfUnsignedNumericOfNonstandardSize Create( int bitsPerValue, long length)
        {
            return new ArrayOfUnsignedNumericOfNonstandardSize(bitsPerValue, length);
        }



        protected ArrayOfUnsignedNumericOfNonstandardSize(int bitsPerElement, long length)
        {
            LongLength = length;
            BitsPerElement = bitsPerElement;

            // The maximum value that can be stored is is (2^(BitsPerElement))-1
            MaxValue = ((((ulong)1) << bitsPerElement) - 1);

            // Caclculate the total number of bits needed by the array (the length in bits)
            ulong lengthInBits = ((ulong)length * (ulong)bitsPerElement);
            // The number of bytes to allocate to provide LengthInBits bits is 
            // ceiling(LengthInBits / 8)
            ulong lengthInBytes = ( lengthInBits + 7) / 8;

            // Allocate the byte array that will hold all of the elments
            AsByteArray = new byte[lengthInBytes];
        }

        /// <summary>
        /// Read the value of the array at index <paramref name="index"/>.
        /// Implmenets the Get{} functioanlity of <code>this[ulong index].</code>
        /// </summary>
        /// <param name="index">An index into the array</param>
        /// <returns>The value at <paramref name="index"/></returns>
        protected ulong ReadValue(long index)
        {
            // Allocate the value to return.
            // We'll construct this value from the left (high order bits) to the right.
            // We need to give it an initial value of zero because each operation that constructs
            // the value will shift any existing bits to the left before using a binary OR
            // to attach the new bits to the right side of the value.
            ulong returnValue = 0;

            // As we construct the value from the underlying byte array,
            // we label the bits within each byte such that bit 0 is the leftmost (most significant) bit
            // and 7 is the rightmost (least significant) bit.

            // Identify the leftmost bit of the element within the array...
            long leftmostBit = index * BitsPerElement;
            // the byte at which this bit is located...
            long byteIndexOfLeftmostBit = leftmostBit / 8;
            // and the bit within that byte
            int bitIndexOfLeftmostBit = (int)(leftmostBit % 8);

            // Identify the rightmost bit of the element within the array...
            long rightmostBit = leftmostBit + BitsPerElement - 1;
            // the byte at which this bit is located...
            long byteIndexOfRightmostBit = rightmostBit / 8;
            // and the bit within that byte
            int bitIndexOfRightmostBit = (int)(rightmostBit % 8);

            // Track the number of bits of the element that have yet to be read out of the byte array
            int bitsLeftAfterReadingFromThisByte = BitsPerElement;

            // Step through the array byte by byte obtaining the bits needed to construct the value to be read
            for (long byteIndex = byteIndexOfLeftmostBit; byteIndex <= byteIndexOfRightmostBit; byteIndex++)
            {
                // Determine which bits to read from this byte.

                // If this is the first byte we're reading from the underlying array, we should only
                // read bits starting with bit_index_of_the_leftmost_bit. If we started reading in earlier
                // bytes, we start reading with bit 0.
                int leftmostBitWithinByte = (byteIndex == byteIndexOfLeftmostBit) ? bitIndexOfLeftmostBit : 0;
                
                // If this is the last byte we're reading from the underlying array, we should stop
                // reading bits when we reach bit_index_of_the_rightmost_bit. If we have more bytes to read after
                // this one, we read through (and including) bit 7.
                int rightmostBitWithinByte = (byteIndex == byteIndexOfRightmostBit) ? bitIndexOfRightmostBit : 7;

                // Count the numbrer of bits that will be read from this byte
                int numberOfBitsToReadFromThisByte = rightmostBitWithinByte + 1 - leftmostBitWithinByte;
                
                // Determine the number of bits that we'll have left to read after we've processed the bits from this byte
                bitsLeftAfterReadingFromThisByte -= numberOfBitsToReadFromThisByte;
                
                // Read out the byte from the underlying byte array
                byte bitsFromArrayByte = AsByteArray[byteIndex];
                
                // Remove any bits that are not part of the value to be read
                if (numberOfBitsToReadFromThisByte < 8)
                {
                    if (leftmostBitWithinByte > 0)
                    {
                        // Some of the bits that we read are not part of the value, as they come before the leftmost bit.
                        // Erase (set to zero) these bits by performing a binary AND with a mask for which all the bits
                        // before the start bit are 0 and all the bits after (and including) the start bit are 1.
                        byte mask = (byte)(0xFF >> leftmostBitWithinByte); 
                        bitsFromArrayByte &= mask;
                    }
                    if (rightmostBitWithinByte < 7)
                    {
                        // Some of the bits that we read are not part of the value, as they come after the rightmost bit.
                        // Shift the byte from the array to remove any bits that come after the end bit.
                        int shift = 7 - rightmostBitWithinByte;
                        bitsFromArrayByte >>= shift;
                    }
                }
                // Append the bits read from the byte array, shifting any bits that are already
                // part of the return value to the right.
                returnValue |= ((ulong)bitsFromArrayByte) << bitsLeftAfterReadingFromThisByte;
            }
            return returnValue;
        }



        /// <summary>
        /// WriteAccountToStableStoreAsync <paramref name="value"/> into the array at index <paramref name="index"/>.
        /// Implemenets the set{} functioanlity of <code>this[ulong index].</code>
        /// If passed <paramref name="value"/> greater than MaxValue (2^BitsPerElement-1),
        /// MaxValue will be written instead. 
        /// </summary>
        /// <param name="index">The index into the array.</param>
        /// <param name="value">The value to be written at that index.</param>
        protected void WriteValue(long index, ulong value)
        {
            // The maximum unsigned value that can be stored in BitsPerElement bits is
            // 2^(BitsPerElement)-1 (MaxValue).  If the ulong value passed
            // is greater than this, we should store MaxValue instead.
            if (value > MaxValue)
                value = MaxValue;

            // As we store the value from into the underlying byte array,
            // we label the bits within each byte such that bit 0 is the leftmost (most significant) bit
            // and 7 is the rightmost (least significant) bit.

            // Identify the leftmost bit of the element within the array...
            long leftmostBit = index * BitsPerElement;
            // the byte at which this bit is located...
            long byteIndexOfLeftmostBit = leftmostBit / 8;
            // and the bit within that byte
            int bitIndexOfLeftmostBit = (int)(leftmostBit % 8);

            // Identify the rightmost bit of the element within the array...
            long rightmostBit = leftmostBit + BitsPerElement - 1;
            //ulong ending_index = starting_index + ((ulong)BitsPerElement) - 1;
            // the byte at which this bit is located...
            long byteIndexOfRightmostBit = rightmostBit / 8;
            // and the bit within that byte
            int bitIndexOfRightmostBit = (int)(rightmostBit % 8);

            // Track the number of bits that remain to be written to the byte array
            int numberOfBitsLeftAfterThisWrite = BitsPerElement;

            // Walk through each of the bytes of the array that we'll need to write into
            // in order to store the value
            for (long byteIndex = byteIndexOfLeftmostBit; byteIndex <= byteIndexOfRightmostBit; byteIndex++)
            {
                // Determine which bits to read from this byte.

                // If this is the first byte we're reading from the underlying array, we should only
                // read bits starting with bit_index_of_the_leftmost_bit. If we started reading in earlier
                // bytes, we start reading with bit 0.
                int leftmostBitWithinByte = byteIndex == byteIndexOfLeftmostBit ? bitIndexOfLeftmostBit : 0;

                // If this is the last byte we're reading from the underlying array, we should stop
                // reading bits when we reach bit_index_of_the_rightmost_bit. If we have more bytes to read after
                // this one, we read through (and including) bit 7.
                int rightmostBitWithinByte = byteIndex == byteIndexOfRightmostBit ? bitIndexOfRightmostBit : 7;

                // Count the numbrer of bits that will be read from this byte
                int numberOfBitsToWriteToThisByte = rightmostBitWithinByte + 1 - leftmostBitWithinByte;

                // Remove the bits we're about to write from the number left to be written
                numberOfBitsLeftAfterThisWrite -= numberOfBitsToWriteToThisByte;

                // We will write the part of the value that comes to the left (is more significant)
                // than the bits that will remain after this byte.  In other words, if there are two bits
                // to write after this byte is written, we'll want to write the bits to the left of that
                // and will thus shift the value to be written to the right by two bits.
                byte valueToWrite = (byte)(value >> numberOfBitsLeftAfterThisWrite);

                // WriteAccountToStableStoreAsync out the byte
                if (numberOfBitsToWriteToThisByte == 8)
                {
                    // If writing 8 bits, writing to memory is as simple as storing the 8 bits
                    // in the byte
                    AsByteArray[byteIndex] = valueToWrite;
                }
                else
                {
                    // Create a mask with number_of_bits_to_write_to_this_byte ones on the right.
                    // For example, if there are 4 bits to write, the mask should be 00001111b.
                    // This can be created by calculating 2^(number_of_bits_to_write_to_this_byte) - 1.
                    byte makeToRemoveBitsNotBeingWritten = 
                        (byte)((1 << numberOfBitsToWriteToThisByte) - 1);
                    // Remove bits that are not part of the value to write
                    valueToWrite &= makeToRemoveBitsNotBeingWritten;

                    // Create a mask to zero out the location of the bits to be written from the existing
                    // value of the byte that is currently stored at this location.
                    byte maskOfLocationOfNewValue = 0xFF;
                    if (leftmostBitWithinByte > 0)
                    {
                        // Remove bits to the left of the leftmost bit from the mask
                        maskOfLocationOfNewValue &= (byte)(0xFF >> leftmostBitWithinByte);
                    }
                    if (rightmostBitWithinByte < 7)
                    {
                        // Remove bits to the right of the rightmost bit from the mask.
                        int shift = 7 - rightmostBitWithinByte;
                        maskOfLocationOfNewValue &= (byte)(0xFF << shift);

                        // Also shift the value we plan to write to put it in the correct
                        // position within the byte.
                        valueToWrite <<= shift;
                    }

                    // Read the old value and zero out the bits to which we will write
                    byte unmodifiedBitsFromOldValue = (byte)(AsByteArray[byteIndex] & (~maskOfLocationOfNewValue));

                    // Use binary OR to incorporate the bits we will store
                    byte newValue = (byte)(unmodifiedBitsFromOldValue | valueToWrite);

                    // WriteAccountToStableStoreAsync out the new value
                    AsByteArray[byteIndex] = newValue;
                }
            }
        }
    }


}
