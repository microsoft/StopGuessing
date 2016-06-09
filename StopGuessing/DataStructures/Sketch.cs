using System;
using System.Security.Cryptography;
using System.Text;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A sketch is a probabilistic data structure that provides an approximate count
    /// of the number of times an item has been observed or, more generally, the
    /// sum of a set of numbers associated with each item (where the common case
    /// is that each observation of an item is the number 1).
    /// 
    /// One can add observations of items (or numbers associated with items)
    /// and get an estimate of that number.  A sketch always returns a number
    /// that is _at least_ as large as the number observed (up to the maximum
    /// number that the sketch can store).  In other words, it is a lower
    /// bound on the values observed.  It may falsely return a number too large,
    /// but will never return a number too small (unless, again, the value
    /// has exceeded the maximum integer the sketch is designe do store.)
    /// </summary>
    public class Sketch
    {
        /// <summary>
        /// A sketch can be viewed as either having k tables (one for each hash)
        /// or as a two-dimensional array of k columns * n rows.
        /// This member, set by the constructor, contains the number of columns (k),
        /// which can also be understood as the number of tables.
        /// </summary>
        public long NumberOfColumns { get; }

        /// <summary>
        /// A sketch can be viewed as either having k tables (one for each hash)
        /// or as a two-dimensional array of k columns * n rows.
        /// This member, set by the constructor, contains the number of rows (n),
        /// which can also be understood as the number of elements per table.
        /// </summary>
        public long NumberOfRows { get; }

        /// <summary>
        /// The data for the sketch is organized as an array of columns (Tables),
        /// each of which contains elements (one per row) stored using the minimum
        /// number of bits (BitsPerElement) needed to express the maximum-allowed value (MaxValue).
        /// </summary>
        protected ArrayOfUnsignedNumericOfNonstandardSize[] Columns {get; }

        /// <summary>
        /// Track the total value in each column.
        /// </summary>
        protected ulong[] ColumnTotals { get; }
 
        /// <summary>
        /// The number of bits used to store each element in the sketch:
        /// 1 bit (values 0...1) for boolean, 2 bits (values 0...3) for a small counter, and so on.
        /// </summary>
        public int BitsPerElement { get; private set; } 

        /// <summary>
        /// The maximum value that can be stored in an element of the sketch.
        /// This value is derived from BitsPerElement once, in the constructor, when the BitsPerElement is set.
        /// </summary>
        protected ulong MaxValue {get; }

        /// <summary>
        /// True if the NumberOfRows is a power of two.
        /// This value is derived from NumberOfRows once, in the constructor, when the NumberOfRows is set.
        /// </summary>
        public bool IsTheNumberOfRowsAPowerOfTwo { get; }

        /// <summary>
        /// If the NubmerOfRows is a power of two, this contains NumberOfRows-1, which can be used as a mask
        /// that can quickly (without an expensive mod operation) trim hash bytes into an row index of the
        /// correct size.
        /// This value is derived from NumberOfRows once, in the constructor, when the NumberOfRows is set.
        /// </summary>
        public long RowIndexMaskForPowersOfTwo { get; }

        /// <summary>
        /// This is the number of bytes needed to generate an index into a row.  If the NumberOfRows is a power of two,
        /// it contains only as many bits as is needed for the index.  If the NumberOfRows is not a power of two, and a
        /// mod (%) operation will be used to create an index, 10 extra bits will be used to ensure that the bias towards
        /// lower values is less than 0.1%.
        /// This value is derived from NumberOfRows once, in the constructor, when the NumberOfRows is set.
        /// </summary>
        public int HashBytesPerRowIndex { get; }


        /// <summary>
        /// A sketch can be viewed as either having k tables (one for each hash) of n elements
        /// or as a two-dimensional array of k columns * n rows.
        /// This constructor creates a Sketch of size specified by k, n, and the number of bits per element.
        /// </summary>
        /// <param name="numberOfColumns">The number of columns in the sketch, which is equivalent to the number of tables (one table per hash index).</param>
        /// <param name="numberOfRows">The number of rows, which is equivalent to the number of elements per table.</param>
        /// <param name="bitsPerElement">The size of each element, in bits, such that the maximum value that can be stored (MaxValue)
        /// in the sketch elements is 2^(n)-1.</param>
        public Sketch(long numberOfColumns, long numberOfRows, int bitsPerElement)
        {
            // Class members set explicitly by the constuctor's parameters
            NumberOfColumns = numberOfColumns;
            NumberOfRows = numberOfRows;
            BitsPerElement = bitsPerElement;

            // The maximum value that can be stored in an element is 2^(BitsPerElement)-1
            MaxValue = ((((ulong)1) << bitsPerElement) - 1);

            // Each column should have a row of elements (1 element for each row)
            Columns = new ArrayOfUnsignedNumericOfNonstandardSize[numberOfColumns];
            for (long i = 0; i < numberOfColumns; i++)
                Columns[i] = ArrayOfUnsignedNumericOfNonstandardSize.Create(bitsPerElement, numberOfRows);
            ColumnTotals = new ulong[numberOfColumns];


            // Test to see if the NumberOfRows is a power of 2.
            //
            // A number if a power of two if ANDing its value with the value one less than it yields zero.
            // For example, for binary 8 (1000), 8-1=7 (01111) and so (1000 & 0001 = 0000).
            // In all other cases, subtracting one will preserve the leftmost bit and so (x & (x-1)) will be greater than zero.
            IsTheNumberOfRowsAPowerOfTwo = ( (numberOfRows - 1) & numberOfRows ) == 0;

            // Calculate the number of bits that will be required to index within each column (the row index)
            // To do so, count how many times we need to right shift by 1 in order to consume all the bits in the NumberOfRows.
            int hashBitsPerRowIndex = 0;
            for (long shiftedNumberOfRows = numberOfRows; shiftedNumberOfRows > 0; shiftedNumberOfRows = shiftedNumberOfRows >> 1)
                hashBitsPerRowIndex++;

            if (IsTheNumberOfRowsAPowerOfTwo)
            {
                // If the number of rows is a power of two, we overcounted the bits needed by one
                hashBitsPerRowIndex -= 1;
                // The fastest way to create indexes will be to mask using the number of rows - 1,
                // For example, if there are 1024 rows (00010000000000b), the mask is (00001111111111b).
                RowIndexMaskForPowersOfTwo = numberOfRows-1;
            } else {
                // Use an extra 10 bits worth of the hash to reduce the potential bias for lower-indexes to 0.1%.
                hashBitsPerRowIndex += 10;
            }
            // The number of hash bytes we need for each index within a column (the row index) is:
            //    ceiling( HashBitsPerRowIndex / 8 )
            // This is equivalent to floor( (HashBitsPerRowIndex + 7 / 8 ).
            HashBytesPerRowIndex = (hashBitsPerRowIndex + 7) / 8; // Ceiling function of / 8
         }

        /// <summary>
        /// Given a string <paramref name="s"/>, provides an index into each column of the sketch.
        /// Each index identifies a row (element) to which one can write (if adding members/counts)
        /// or read (if testing membership or min counts).
        /// SECURITY NOTE: This indexes can be predicted by an adversary.  If using the sketch in a scenario in which an adversary may
        /// cause harm by targeting certain indexes, include as a prefix of the parameter s a key that is not publicly-known
        /// (and hopefully can be kept secret from any adversaries).  In other words,
        /// ulong[] indexesIntoSketch = getIndexesForString( key + <paramref name="s"/> ).
        /// </summary>
        /// <param name="s">A string to map to indexes pointing into each column (table) of the sketch.
        /// If used in adversarial contexts, create a key string and include
        /// prefix all calls to this method with that key. 
        /// </param>
        /// <returns>An array (size=numberOfColumns) containing indexes into each column of the sketch</returns>
        protected long[] GetIndexesForString(string s) {
            // Allocate an array to hold the return value 
            long[] indexes = new long[NumberOfColumns];

            // Track the number of hashes we've needed to compute to generate indexes, so that we don't repeat the same value
            int hashCount = 0;
            
            // Hash bytes are generated by taking the SHA256 hash of a UTF8 encoded string n + s,
            // where n is the string representation of the number of hashes generated before.
            // So, s="example", the first set of 256 bits will be the SHA256 hash of "0example" (UTF8)
            // and the second will be the hash of "1example".
            byte[] hashBytes = ManagedSHA256.Hash(Encoding.UTF8.GetBytes((hashCount++).ToString() + s));

            // Track the number of bytes we've consumed from the current hash so we know when we need to generate another
            int hashBytesConsumed = 0;

            // Generate an index for each column
            for (long i = 0; i < NumberOfColumns; i++) {
                // Use hash bytes to build a number that we can use to create an index
                long numberBuiltFromHashBytes = 0;
                // Create enough bytes
                for (int j = 0; j < HashBytesPerRowIndex; j++)
                {
                    if (hashBytesConsumed >= hashBytes.Length)
                    {
                        // We need to compute another hash in order to Get another byte to use
                        hashBytes = ManagedSHA256.Hash(Encoding.UTF8.GetBytes((hashCount++).ToString() + s));
                        hashBytesConsumed = 0;
                    }
                    // Build the number by shifting the bytes we have eight bits to the left and then placing
                    // the new byte in the rightmost eight bits
                    numberBuiltFromHashBytes = (numberBuiltFromHashBytes << 8) | hashBytes[hashBytesConsumed++];
                }

                if (IsTheNumberOfRowsAPowerOfTwo)
                {
                    // When the NumberOfRows is a power of two, we can create the index simply by using a mask to
                    // grab the number of bits we need
                    indexes[i] = numberBuiltFromHashBytes & RowIndexMaskForPowersOfTwo;
                }
                else
                {
                    // When the Number of rows is not a power of two, we use the more expenseive mod operation.
                    // (Note that we'll have built a number from a space >= 1024 the the maximum value of the index,
                    //  so as not to bias the index towards lower values by more than 0.1%)
                    indexes[i] = numberBuiltFromHashBytes % NumberOfRows;
                }
            }

            return indexes;            
        }


        /// <summary>
        /// Get the total value stored in a columnn
        /// </summary>
        /// <param name="column">The column</param>
        /// <returns></returns>
        public ulong GetColumnTotal(int column)
        {
            return ColumnTotals[column];
        }


        public class ResultOfGet
        {
            public ulong Min { get;  }
            public ulong Max { get; }
            public ulong MinColumnTotal { get;  }
            public ulong MaxColumnTotal { get; }
            public Proportion Proportion => new Proportion(Min, MinColumnTotal);

            public ResultOfGet(ulong min, ulong max, ulong minColumnTotal, ulong maxColumnTotal)
            {
                Min = min;
                Max = max;
                MinColumnTotal = minColumnTotal;
                MaxColumnTotal = maxColumnTotal;
            }
        }

        public class ResultOfUpdate
        {
            public ulong PriorMin { get; }
            public ulong PriorMax { get; }
            public ulong PriorMinColumnTotal { get; }
            public ulong PriorMaxColumnTotal { get; }
            public ulong NewMin { get; }
            public ulong NewMax { get; }
            public ulong NewMinColumnTotal { get; }
            public ulong NewMaxColumnTotal { get; }

            public Proportion PriorProportion => new Proportion(PriorMin, PriorMinColumnTotal);
            public Proportion NewProportion => new Proportion(NewMin, NewMinColumnTotal);

            public ResultOfUpdate(ulong priorMin, ulong priorMax, ulong priorMinColumnTotal, ulong priorMaxColumnTotal, ulong newMin, ulong newMax, ulong newMinColumnTotal, ulong newMaxColumnTotal)
            {
                PriorMin = priorMin;
                PriorMax = priorMax;
                PriorMinColumnTotal = priorMinColumnTotal;
                PriorMaxColumnTotal = priorMaxColumnTotal;
                NewMin = newMin;
                NewMax = newMax;
                NewMinColumnTotal = newMinColumnTotal;
                NewMaxColumnTotal = newMaxColumnTotal;
            }
        }

        /// <summary>
        /// Get the current state of the sketch for a value's row indexes into each column.
        /// </summary>
        /// <param name="elementIndexes">The row indexes into each column of the sketch</param>
        /// <returns>A class that describes the state of the value in the sketch, with such values as the current min
        /// and the proportion.</returns>
        public ResultOfGet Get(long[] elementIndexes)
        {
            ulong min = ulong.MaxValue;
            ulong max = ulong.MinValue;
            ulong minColumnTotal = ulong.MaxValue;
            ulong maxColumnTotal = ulong.MinValue;
            for (int i = 0; i < elementIndexes.Length; i++)
            {                
                ulong columnValue = Read(i, elementIndexes[i]);

                min = Math.Min(min, columnValue);
                max = Math.Max(max, columnValue);

                ulong columnTotal = GetColumnTotal(i);

                minColumnTotal = Math.Min(minColumnTotal, columnTotal);
                maxColumnTotal = Math.Max(maxColumnTotal, columnTotal);
            }
            return new ResultOfGet(min, max, minColumnTotal, maxColumnTotal);
        }

        /// <summary>
        /// Get the current state of the sketch for a value's row indexes into each column.
        /// </summary>
        /// <param name="s">The string to query the sketch for occurrence/frequency information.</param>
        /// <returns>A class that describes the occurrence/frequency information about the sketch.</returns>
        public ResultOfGet Get(string s)
        {
            return Get(GetIndexesForString(s));
        }


        /// <summary>
        /// Get the minimum of all the values at the indexes identified for the string <paramref name="s"/> with the sketch.
        /// This is a lower bound on the number of times the string <paramref name="s"/> has been witnessed by the sketch
        /// via a call to increment(s) and/or conservativeIncrement(s).  However, counting stops when the maximum value storable
        /// in the sketch is reached.
        /// </summary>
        /// <param name="s">The string to query for the minimum occurrence count of.</param>
        /// <returns>The minimum of all the values at the indexes identified for the string <paramref name="s"/> within the sketch.</returns>
        public ulong GetMin(string s)
        {
            return Get(GetIndexesForString(s)).Min;
        }

        /// <summary>
        /// Test whether a string <paramref name="s"/> has never be witnessed before by seeing if <code>getMin(s) > 0</code>.
        /// This is guaranteed to return true if increment(s) or conservativeIncrement(s) has been called on the sketch.
        /// It is probabilistically likely, but not guaranteed, to return false neither increment(s) or conservativeIncrement(s) have been called.
        /// </summary>
        /// <param name="s">The string to test.</param>
        /// <returns>True if the string has been witnessed before (and sometimes by chance).
        /// False if and only if the string has never been witnessed before.</returns>
        public bool IsNonZero(string s)
        {
            return GetMin(s) > 0;
        }


        /// <summary>
        /// A shortcut to set all the elements of the sketch identified by indexes for the string <paramref name="s"/>
        /// or to Get the minimum of all the values at the indexes identified for the string <paramref name="s"/>
        /// (equivalent to calling <code>getMin(<paramref name="s"/>)</code>.
        /// </summary>
        /// <param name="s">A string to map to indexes pointing into each column (table) of the sketch.</param>
        /// <returns></returns>
        public ulong this[string s]
        {
            get
            {
                return this[GetIndexesForString(s)];
            }
            set
            {
                this[GetIndexesForString(s)] = value;
            }
        }

        /// <summary>
        /// A shortcut to set all the elements of the sketch identified by an array of indexes,
        /// or to Get the minimum of all the values at the indexes identified by the array of indexes.
        /// </summary>
        /// <param name="elementIndexes">An array of indexes into the sketch, one for each column (table).</param>
        /// <returns></returns>
        public ulong this[long[] elementIndexes]
        {
            get
            {
                return Get(elementIndexes).Min;
            }
            set
            {
                //if (((ulong)element_indexes.LongLength) != sketchData.Dimensions[0])
                //    throw new Exception("Incorrect number of indexes into the sketch");
                for (long i = 0; i < elementIndexes.Length; i++)
                    Write(i, elementIndexes[i], value);
            }
        }

        /// <summary>
        /// Count the observation of the string <paramref name="s"/> by adding one
        /// to the value in each table indexed by calling <code>getIndexesForString(<paramref name="s"/>)</code>
        /// If the count is already MaxValue, the operation will have no effect.
        /// </summary>
        /// <param name="s">The string to witness.</param>
        /// <returns>A class describing the state of the value within the sketch.</returns>
        public ResultOfUpdate Increment(string s)
        {
            return Add(GetIndexesForString(s));
        }

        /// <summary>
        /// Count the observation of the string <paramref name="s"/> by adding one
        /// to those values indexed by calling <code>getIndexesForString(<paramref name="s"/>)
        /// for which the value is currently equal to <code>getMin(<paramref name="s"/>)</code>.
        /// This algorithm is more conservative than <code>increment</code> in that it is 
        /// less likely to result in overcounting.
        /// If the count is already MaxValue, the operation will have no effect.
        /// </code>
        /// </summary>
        /// <param name="s">The string to witness.</param>
        /// <returns>A class describing the state of the value within the sketch.</returns>
        public ResultOfUpdate ConservativeIncrement(string s)
        {
            return ConservativeAdd(GetIndexesForString(s));
        }


        /// <summary>
        /// An Add function that is equivalent to calling <code>increment</code> multiple (<paramref name="amountToAdd"/>) times.
        /// If adding would cause the value to exceed MaxValue, MaxValue will be stored instead.
        /// </summary>
        /// <param name="s">The string to witness.</param>
        /// <param name="amountToAdd">The number of times to witness it.
        /// that would result before the Add.</param>
        /// <returns>A class that describes the state of the sketch both before and after the operation.</returns>
        public ResultOfUpdate Add(string s, ulong amountToAdd = 1)
        {
            return Add(GetIndexesForString(s), amountToAdd);
        }

        /// <summary>
        /// An Add function that is equivalent to calling <code>conservativeIncrement</code>
        /// multiple (<paramref name="amountToAdd"/>) times.
        /// If adding would cause the value to exceed MaxValue, MaxValue will be stored instead.
        /// </summary>
        /// <param name="s">The string to witness.</param>
        /// <param name="amountToAdd">The number of times to witness it.
        /// that would result before the Add.</param>
        /// <returns>A class that describes the state of the sketch both before and after the operation.</returns>
        public ResultOfUpdate ConservativeAdd(string s, ulong amountToAdd = 1)
        {
            return ConservativeAdd(GetIndexesForString(s), amountToAdd);
        }


        /// <summary>
        /// WriteAccountToStableStoreAsync <paramref name="value"/> to the underlying sketch data structure (two-dimensional array) at 
        /// column (Table) <paramref name="column"/> and row (element) <paramref name="row"/>. 
        /// </summary>
        /// <param name="column">The column (table) to write to.</param>
        /// <param name="row">The row (element) within the column (table).</param>
        /// <param name="value">The value to write.</param>
        protected virtual void Write(long column, long row, ulong value)
        {
            // If the value is larger than the element can store, store the maximum value allowed in the element
            if (value > MaxValue)
            {
                value = MaxValue;
            }
            ulong oldValue = Columns[column][row];
            if (value != oldValue)
            {
                Columns[column][row] = value;
                ColumnTotals[column] += value - oldValue;
            }
        }

        /// <summary>
        /// Read from the underlying sketch data structure (two-dimensional array) at 
        /// column (Table) <paramref name="column"/> and row (element) <paramref name="row"/>. 
        /// </summary>
        /// <param name="column">The column (table) to read from.</param>
        /// <param name="row">The row (element) within the table (column).</param>
        /// <returns>The value at Sketch[column][row], also viewed element [row] from table [column].</returns>
        protected virtual ulong Read(long column, long row)
        {
            return Columns[column][row];
        }


        /// <summary>
        /// Add to the the value at each [column,index] pair, where the index into 
        /// <paramref name="elementIndexForEachColumn"/>is the column number and the value is the row (element) index.
        /// If adding would cause the value to exceed MaxValue, MaxValue will be stored instead.
        /// </summary>
        /// <param name="elementIndexForEachColumn">An array for which the index is a columnn number and the value
        /// is the row (element) index.</param>
        /// <param name="amountToAdd">The amount to Add to each of the indexed elements.</param>
        /// <returns>A class that describes the state of the sketch both before and after the operation.</returns>
        protected ResultOfUpdate Add(long[] elementIndexForEachColumn, ulong amountToAdd = 1)
        {
            ulong originalMin = ulong.MaxValue;
            ulong originalMax = ulong.MinValue;
            ulong originalMinColumnTotal = ulong.MaxValue;
            ulong originalMaxColumnTotal = ulong.MinValue;
            ulong newMin = ulong.MaxValue;
            ulong newMax = ulong.MinValue;
            ulong newMinColumnTotal = ulong.MaxValue;
            ulong newMaxColumnTotal = ulong.MinValue;

            // Walk through all the [column][row_index] pairs
            for (int column = 0; column < elementIndexForEachColumn.Length; column++)
            {
                // Perform the Add at the [column][row_index]
                long row = elementIndexForEachColumn[column];

                // Record the value before we changed it.
                ulong oldValue = Read(column, row);

                // Track the original min/max value
                originalMin = Math.Min(originalMin, oldValue);
                originalMax = Math.Max(originalMax, oldValue);

                // Track the old column total
                ulong columnTotal = GetColumnTotal(column);

                originalMinColumnTotal = Math.Min(originalMinColumnTotal, columnTotal);
                originalMaxColumnTotal = Math.Max(originalMinColumnTotal, columnTotal);

                ulong newValue = Math.Min(oldValue + amountToAdd, MaxValue);
                // Abort if the new value is no different from the old value
                if (newValue > oldValue)
                {
                    // Store the new value
                    Write(column, row, newValue);

                    columnTotal = GetColumnTotal(column);
                }

                // Track the new values and totals
                newMin = Math.Min(newMin, newValue);
                newMax = Math.Max(newMax, newValue);
                newMinColumnTotal = Math.Min(newMinColumnTotal, columnTotal);
                newMaxColumnTotal = Math.Min(newMaxColumnTotal, columnTotal);
            }

            // Return the minimum observed
            return new ResultOfUpdate(originalMin, originalMax, originalMinColumnTotal, originalMaxColumnTotal,
                newMin, newMax, newMinColumnTotal, newMaxColumnTotal);
        }


        /// <summary>
        /// Use the conservative algorithm to Add to the data structure such that for each [column][row],
        /// the value stored is no less than <code>getMin(<paramref name="elementIndexForEachColumn"/>) + <paramref name="amountToAdd"/></code>
        /// (but never exceeding MaxValue).
        /// </summary>
        /// <param name="elementIndexForEachColumn">An array for which the index is a columnn number and the value
        /// is the row (element) index.</param>
        /// <param name="amountToAdd">The desired increase such that <code>getMin(<paramref name="elementIndexForEachColumn"/>)</code>
        /// after the operation returns is <paramref name="amountToAdd"/> + <code>getMin(<paramref name="elementIndexForEachColumn"/>)</code>
        /// (unless doing so would cause the result to exceed MaxValue).</param>
        /// <returns>A class describing the state of the value within the sketch before and after the Add.</returns>
        protected ResultOfUpdate ConservativeAdd(long[] elementIndexForEachColumn, ulong amountToAdd = 1)
        {
            ulong originalMin = ulong.MaxValue;
            ulong originalMax = ulong.MinValue;
            ulong originalMinColumnTotal = ulong.MaxValue;
            ulong originalMaxColumnTotal = ulong.MinValue;
            ulong newMinColumnTotal = ulong.MaxValue;
            ulong newMaxColumnTotal = ulong.MinValue;

            // Calculate the minimum of the values at each [column][row_index] address before the Add operation
            //ulong min_before_add = ulong.MaxValue;
            ulong[] values = new ulong[elementIndexForEachColumn.Length];
            for (int column = 0; column < elementIndexForEachColumn.Length; column++)
            {
                // WriteAccountToStableStoreAsync the value
                ulong value = Read(column, elementIndexForEachColumn[column]);

                // Track the original min/max value
                originalMin = Math.Min(originalMin, value);
                originalMax = Math.Max(originalMax, value);

                // Track the old column total
                ulong columnTotal = GetColumnTotal(column);

                originalMinColumnTotal = Math.Min(originalMinColumnTotal, columnTotal);
                originalMaxColumnTotal = Math.Max(originalMinColumnTotal, columnTotal);

                // Track the value for this column for quick look-up to determine
                // if it is a min that needs to be conservatively updated
                values[column] = value;
            }

            // There's no addition to be done if the minimum value is MaxValue, as no larger value can be represented
            if (originalMin >= MaxValue)
            {
                return new ResultOfUpdate(originalMin, originalMax, originalMinColumnTotal, originalMaxColumnTotal,
                    originalMin, originalMax, originalMinColumnTotal, originalMaxColumnTotal);
            }

            // Determine the value post-Add value, which is the minimum value observed value over all the [column][row_index] addresses,
            // plus the amount_to_add (but not more than MaxValue).
            ulong newMin = Math.Min(originalMin + amountToAdd, MaxValue);
            ulong newMax = Math.Max(originalMax, newMin);

            // WriteAccountToStableStoreAsync the post-Add value ONLY where the existing value at the [column][row_index] address is less than it.
            for (int column = 0; column < elementIndexForEachColumn.Length; column++)
            {
                if (values[column] < newMin)
                {
                    Write(column, elementIndexForEachColumn[column], newMin);
                }

                ulong columnTotal = GetColumnTotal(column);

                newMinColumnTotal = Math.Min(newMinColumnTotal, columnTotal);
                newMaxColumnTotal = Math.Min(newMaxColumnTotal, columnTotal);
            }

            return new ResultOfUpdate(originalMin, originalMax, originalMinColumnTotal, originalMaxColumnTotal,
                newMin, newMax, newMinColumnTotal, newMaxColumnTotal);
        }

    }
}
