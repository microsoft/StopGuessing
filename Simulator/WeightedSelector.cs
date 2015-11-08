using System.Collections.Generic;
using StopGuessing.EncryptionPrimitives;

namespace Simulator
{
    public class WeightedSelector<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly List<ulong> _cumulativeWeight = new List<ulong>();

        public void AddItem(T item, ulong weight)
        {
            _items.Add(item);
            _cumulativeWeight.Add(weight + (_cumulativeWeight.Count > 0 ? _cumulativeWeight[_cumulativeWeight.Count-1] : 0));
        }

        public T GetItemByWeightedRandom()
        {
            if (_cumulativeWeight.Count == 0)
                return default(T);
            ulong randomValueLessThanWeight =
                StrongRandomNumberGenerator.Get64Bits(_cumulativeWeight[_cumulativeWeight.Count - 1]);

            // Binary search to find the correct index
            int minIndex = 0;
            int maxIndex = _cumulativeWeight.Count - 1;
            while (maxIndex > minIndex)
            {
                int midPointIndex = (maxIndex + minIndex) / 2;
                ulong midPointValue = _cumulativeWeight[midPointIndex];
                if (midPointValue < randomValueLessThanWeight)
                {
                    // The midpoint has a value that is smaller than point to find, and so 
                    // the index to return must be at least one greater than it.
                    minIndex = midPointIndex + 1;
                }
                else
                {
                    // The midpoint value is at least as large as the point to find, and so
                    // the index to return may not be greater than it.
                    maxIndex = midPointIndex;
                }
            }

            return _items[minIndex];
        }
    }
}
