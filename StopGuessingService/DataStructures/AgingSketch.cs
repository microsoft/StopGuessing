namespace StopGuessing.DataStructures
{

    public class AgingSketch : Sketch
    {
        readonly ulong[] _numberElementsZero;
        readonly long[] _agingIndex;
        readonly System.Threading.Thread[] _columnAgingThreads;
        readonly ulong _rowZerosMin;
        readonly ulong _rowZerosMax;

        const float DefaultFractionRowZerosLowWaterMark = .45f;
        const float DefaultFractionRowZerosHighWaterMark = .55f;

        public AgingSketch(long numberOfColumns, long numberOfRows, int bitsPerElement,
            float fractionRowZerosLowWaterMark = DefaultFractionRowZerosLowWaterMark, 
            float fractionRowZerosHighWaterMark = DefaultFractionRowZerosHighWaterMark)
            : base(numberOfColumns, numberOfRows, bitsPerElement)
        {
            _columnAgingThreads = new System.Threading.Thread[numberOfColumns];
            _agingIndex = new long[numberOfColumns];
            _numberElementsZero = new ulong[numberOfColumns];
            _rowZerosMin = (ulong)(numberOfRows * fractionRowZerosLowWaterMark);
            _rowZerosMax = (ulong)(numberOfRows * fractionRowZerosHighWaterMark);
            for (long i = 0; i < numberOfColumns; i++)
            {
                _numberElementsZero[i] = (ulong) numberOfRows;
            }
        }

        protected override void Write(long column, long row, ulong value)
        {
            ulong newValue = value > MaxValue ? MaxValue : value;
            lock (Columns[column])
            {
                ulong oldValue = Read(column, row);
                base.Write(column, row, value);
                if (newValue == 0 && oldValue > 0)
                {
                    // Non-zero replaced with zero =>
                    // One more zero element
                    _numberElementsZero[column]++;
                }
                else if (newValue != 0 && oldValue == 0)
                {
                    // Zero value replaced by a non-zero => 
                    // One fewer zero element
                    ulong numZeroElements = --_numberElementsZero[column];
                    if (numZeroElements < _rowZerosMin && (_columnAgingThreads[column] == null))
                    {
                        if (_columnAgingThreads[column] == null)
                        {
                            System.Threading.Thread thread = _columnAgingThreads[column] = 
                                new System.Threading.Thread(() => AgeColumn(column));
                            thread.Start();
                        }
                    }
                }
            }
            //return new_value;
        }

        protected void AgeColumn(long column)
        {
            long index = _agingIndex[column];
            while (_numberElementsZero[column] < _rowZerosMax)
            {
                ulong value;
                // step to the index with the next non-zero value
                while ((value = Read(column, index)) == 0)
                {
                    if (++index >= NumberOfRows)
                        index = 0;
                }
                // Decrement that value
                Write(column, index, --value);
            }
            _agingIndex[column] = index;

            // Mark that the columnn is done
            _columnAgingThreads[column] = null;
        }

    }

   public  class AgingMembershipSketch : AgingSketch
    {
        public AgingMembershipSketch(long numberOfColumns, long numberOfRows, int bitsPerElement = 2, float fractionRowZerosMin = .45f, float fractionRowZerosMax = .55f)
            : base(numberOfColumns, numberOfRows, bitsPerElement, fractionRowZerosMin, fractionRowZerosMax)
        {
        }

        /// <summary>
        /// Test if a string (<paramref name="s"/>) is a member of the set and Add it if it is not.
        /// </summary>
        /// <param name="s">The string to Add.</param>
        /// <returns>True if <paramref name="s"/> was already a member of the set.</returns>
        public bool AddMember(string s)
        {
            return Add(s, 2).PriorMin > 0;
        }

        /// <summary>
        /// Test if a string (<paramref name="s"/>) is a member of the set.
        /// </summary>
        /// <param name="s">Thee string to search for</param>
        /// <returns>True if <paramref name="s"/> isa member of the set.</returns>
        public bool IsMember(string s)
        {
            return IsNonZero(s);
        }

    } 

}
