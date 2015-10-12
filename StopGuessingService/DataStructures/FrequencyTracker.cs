using System.Collections.Generic;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public class FrequencyTracker<T>
    {
        // The maximum number of elements this data structure should store before
        // recovering space.
        public int MaxCapacity { get; }

        // The capacity to target when recovering space
        public int CapacityToTargetWhenRecoveringSpace { get; }

        // The number of generations to separate values into for the purpose
        // of making recovery perform reasonably fast
        public uint Generations { get; }

        // Where the elements are stored
        private readonly SortedDictionary<T, uint> _elementCounts;

        // The sum of all the element's counts
        public ulong TotalAmount { get; private set; }

        // The count of the number of items is the number of elements we're tracking the count of
        public int Count => _elementCounts.Count;

        // For scaling up before reaching steady-state capacity
        private int _periodBetweenIncrementGrowth;
        private int _capacityAtWhichToIncreaseTheIncrement;
        private uint _increment;


        // For recovering capacity
        private readonly object _recoveryTaskLock;
        private Task _recoveryTask;
        private Queue<T> _cleanupQueue;


        public FrequencyTracker(int maxCapacity, int capacityToTargetWhenRecoveringSpace = -1,
            uint generations = 4)
        {
            CapacityToTargetWhenRecoveringSpace = capacityToTargetWhenRecoveringSpace;
            Generations = generations;
            if (CapacityToTargetWhenRecoveringSpace < 0)
            {
                // Target reduction of 5% of capacity to 95% of max capacity.
                CapacityToTargetWhenRecoveringSpace = (int) ((maxCapacity*95L)/100L);
            }
            MaxCapacity = maxCapacity;
            CapacityToTargetWhenRecoveringSpace = capacityToTargetWhenRecoveringSpace;
            _increment = 1;
            _periodBetweenIncrementGrowth = MaxCapacity/(int)Generations;
            _capacityAtWhichToIncreaseTheIncrement = _periodBetweenIncrementGrowth;
            _elementCounts = new SortedDictionary<T, uint>();
            _cleanupQueue = new Queue<T>();
            _recoveryTaskLock = new object();
        }

        public Proportion Get(T element)
        {
            uint count;
            lock (_elementCounts)
            {
                _elementCounts.TryGetValue(element, out count);
            }
            return new Proportion(count, TotalAmount);
        }

        public Proportion Add(T element)
        {
            uint count;
            bool recoveryNeeded;
            lock (_elementCounts)
            {
                _elementCounts.TryGetValue(element, out count);
                TotalAmount += _increment;
                count += _increment;
                _elementCounts[element] = count;
                if (Count == _capacityAtWhichToIncreaseTheIncrement)
                {
                    if (++_increment >= Generations)
                    {
                        _capacityAtWhichToIncreaseTheIncrement = -1;
                    }
                    else
                    {
                        _periodBetweenIncrementGrowth += _periodBetweenIncrementGrowth;
                    }
                }
                recoveryNeeded = _elementCounts.Count > MaxCapacity;
            }
            if (recoveryNeeded && _recoveryTask == null)
                StartRecoveringSpace();
            return new Proportion(count, TotalAmount);
        }

        private void StartRecoveringSpace()
        {
            lock (_recoveryTaskLock)
            {
                if (_recoveryTask != null)
                    return;
                _recoveryTask = Task.Run(() => RecoverSpace());
            }
        }

        private void RecoverSpace()
        {
            bool finished = false;
            while (!finished)
            {
                if (_cleanupQueue.Count == 0)
                {
                    lock (_elementCounts)
                    {
                        _cleanupQueue = new Queue<T>(_elementCounts.Keys);
                    }
                }
                T key = _cleanupQueue.Dequeue();
                lock (_elementCounts)
                {
                    uint count;
                    if (_elementCounts.TryGetValue(key, out count))
                    {
                        if (count <= 1)
                        {
                            _elementCounts.Remove(key);
                            TotalAmount -= count;
                        }
                        else
                        {
                            _elementCounts[key] = count - 1;
                            TotalAmount -= 1;
                        }
                    }
                    finished = _elementCounts.Count <= CapacityToTargetWhenRecoveringSpace;
                }
            }
            lock (_recoveryTaskLock)
            {
                _recoveryTask = null;
            }
        }

    }
}
