using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.EncryptionPrimitives;

namespace Simulator
{
    /// <summary>
    /// This class takes a set of value/weight pairs (v_i/w_i) and produces a selector
    /// that can be sampled to return a value from the set such that v_i is returned with
    /// probability w_i / SUM(w_i, over all i).
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    public class WeightedSelector<T>
    {
        /// <summary>
        /// the values
        /// </summary>
        private readonly List<T> _items = new List<T>();

        // The cumulative value of all the weights for all values including the one at index i
        private readonly List<double> _cumulativeWeight = new List<double>();

        // Add an item to the selector
        public void AddItem(T item, double weight)
        {
            _items.Add(item);
            _cumulativeWeight.Add(weight + (_cumulativeWeight.Count > 0 ? _cumulativeWeight[_cumulativeWeight.Count-1] : 0));
        }

        /// <summary>
        /// Get the list of items in the selector
        /// </summary>
        /// <param name="count">A limit on the number of items to get</param>
        /// <returns>The first count items in the selector</returns>
        public List<T> GetItems(int count = Int32.MaxValue)
        {
            count = Math.Min(count, _items.Count);
            return _items.Take(count).ToList();
        }

        /// <summary>
        /// Create a new selector that only contains the first <paramref name="count"/> items of this selector.
        /// </summary>
        /// <param name="count">The number of values to keep</param>
        /// <returns>The new selector that has only the first <paramref name="count"/> items.</returns>
        public WeightedSelector<T> TrimToInitialItems(int count)
        {
            WeightedSelector<T> trimmed = new WeightedSelector<T>();
            count = Math.Min(count, _items.Count);
            trimmed._items.AddRange(_items.Take(count));
            trimmed._cumulativeWeight.AddRange(_cumulativeWeight.Take(count));
            return trimmed;
        }

        /// <summary>
        /// Get an item from the selector at random using the weights to match the desired distribution.
        /// </summary>
        /// <returns>A value in the selector identified at weighted random.</returns>
        public T GetItemByWeightedRandom()
        {
            if (_cumulativeWeight.Count == 0)
                return default(T);
            double randomValueLessThanWeight =
                StrongRandomNumberGenerator.GetFraction() *_cumulativeWeight[_cumulativeWeight.Count - 1];

            // Binary search to find the correct index
            int minIndex = 0;
            int maxIndex = _cumulativeWeight.Count - 1;
            while (maxIndex > minIndex)
            {
                int midPointIndex = (maxIndex + minIndex) / 2;
                double midPointValue = _cumulativeWeight[midPointIndex];
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
