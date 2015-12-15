using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// A class to track the frequency of a sequence of observed values (keys).
    /// </summary>
    /// <typeparam name="TKey">The type of values (keys) to observe.</typeparam>
    public class FrequencyTracker<TKey>
    {
   
        /// <summary>
        /// The maximum number of elements this data structure should store before
        /// recovering space.
        /// </summary>
        public int MaxCapacity { get; }

        /// <summary>
        /// When space exceeds the max capacity, items will have counts removed until enough
        /// items can be removed to hit this target capacity.
        /// </summary>
        public int CapacityToTargetWhenRecoveringSpace { get; }

        /// <summary>
        /// An observation causes the counter for that item to be incremented by this amount, such that it can be
        /// expected to last for this many generations of reductions (decrements) before being removed.
        /// The larger this number is the finer the granularity can be achived when reductions are performed
        /// (increasing accuracy) but the time required for the reduction process grows linearly with the number
        /// of generations.
        /// </summary>
        public uint Generations { get; }

        /// <summary>
        /// The sum of the counts of all observations currently stored in the tracker.
        /// </summary>
        private long _sumOfAmountsOverAllKeys;
        public long SumOfAmountsOverAllKeys {
            get { return _sumOfAmountsOverAllKeys;}
            private set {_sumOfAmountsOverAllKeys = value; } }

        // The count of the number of items is the number of elements we're tracking the count of
        private int _numberOfElements = 0;
        public int Count
        {
            get { return _numberOfElements; }
            private set { _numberOfElements = value; }
        }


        /// <summary>
        /// Where the elements are stored
        /// </summary>
        private readonly ConcurrentDictionary<TKey, uint> _keyCounts;

        /// <summary>
        /// Trigger reduction when this threshold is reached to clear old out observations
        /// </summary>
        protected long TotalCountMaxThreshold { get; }

        /// <summary>
        /// The threshold to reduce to when clearing out old observations.
        /// </summary>
        protected long ReducedAmountToTargetWhenTotalAmountReached { get; }

        // For scaling up before reaching steady-state capacity
        private readonly int _periodBetweenIncrementGrowth;
        private int _capacityAtWhichToIncreaseTheIncrement;

        // The amount to increment on each observation.  This starts at one, and grows linearly
        // such that after a number of observations equal to the configured lifetime of an observation,
        // it will be equal to the number of Generations.
        // After approximateObservationLifetime observations, 1/Generations observations will
        // have received an increment of 1, 2, 3, ... Generations.
        // Thus, when it's time to start reducing observations, 1/Generations values may be cleaned
        // up after a single decrement.
        // Without steadily increasing the increment, there would be a slow loop that might take
        // a very long time on the first reduction  
        private uint _increment;

        // For recovering capacity or reducing observations
        private readonly object _recoveryTaskLock;
        private Task _recoveryTask;
        private Queue<TKey> _cleanupQueue;


        /// <summary>
        /// Construct a new tracker to monitor the frequency of a sequence of observed values (keys).
        /// </summary>
        /// <param name="approximateObservationLifetime">The approximate number of observations that you would like a value included in the frequency estimate.
        /// In practice, more-recent observations will have higher frequency scores than less-frequent observations, but some trace of the score
        /// may last a few generations beyond the specified lifetime.</param>
        /// <param name="maxCapacity">The maximum number of keys to track.  Those with the lowest frequency (or the oldest) will be removed
        /// to make space for newer observations.  If not set, the constructor will allocate capacity equal to
        /// <paramref name="approximateObservationLifetime"/>.</param>
        /// <param name="capacityToTargetWhenRecoveringSpace">When the maxCapacity is reached, this is the goal capacity to reduce
        /// to.  This value should be less than maxCacpacity.  If not specified, it will be set to 95% of max capacity.</param>
        /// <param name="generations">Each key is mapped to an internal count that is added to during each observation and decremented
        /// from when the tracker is recovering space or aging out older observations.  In the stable state of a mostly-full tracker,
        /// an observation will result in a counter being incremented by this parameter so that it survives the same number of recovery
        /// cycles (in which the counter is decremented and the key removed if the counter reaches zero).
        /// Increasing this number improves the accuracy of the frequency tracking estimates, but also causes a linear increase
        /// in the time required when keys are being aged out or removed due to capacity limits.</param>
        public FrequencyTracker(
            int approximateObservationLifetime,
            int? maxCapacity = null,
            double capacityToTargetWhenRecoveringSpace = 0.95f,
            uint generations = 4)
        {
            Generations = generations;
            // After approximateObservationLifetime observations, each of which adds Generations
            // to the TotalCount, the TotalCount will be:
            //    approximateObservationLifetime * Generations
            // At that threshold we should start clearing out old observations
            TotalCountMaxThreshold = ((long)approximateObservationLifetime) * Generations;
            // If the max capacity is not specified, have space for enough observations to keep
            // one for each lifetime.
            MaxCapacity = maxCapacity ?? approximateObservationLifetime;
            // Default reduction thresholds to 95% of the max thresholds
            CapacityToTargetWhenRecoveringSpace = (int) (MaxCapacity*capacityToTargetWhenRecoveringSpace);
            ReducedAmountToTargetWhenTotalAmountReached = ((TotalCountMaxThreshold*95U)/100U);

            // The amount to increment on each observation grows over time
            _increment = 1;
            _periodBetweenIncrementGrowth = approximateObservationLifetime / (int) Generations;
            _capacityAtWhichToIncreaseTheIncrement = _periodBetweenIncrementGrowth;

            // Initialize our main storage data structure
            _keyCounts = new ConcurrentDictionary<TKey, uint>();

            // Use these two objects during clean-up
            _cleanupQueue = new Queue<TKey>();
            _recoveryTaskLock = new object();
        }

        /// <summary>
        /// Get an estimate of the frequency of observations that were of the given key.
        /// </summary>
        /// <param name="key">The key to query.</param>
        /// <returns>The proportion (frequency) with which that observation occurred before this observation,
        /// aged over time.</returns>
        public Proportion Get(TKey key)
        {
            uint count;
            _keyCounts.TryGetValue(key, out count);
            return new Proportion(count, (ulong) SumOfAmountsOverAllKeys);
        }


        private readonly object _capacityLockObj = new object();
        /// <summary>
        /// Add an observation of a given key, returning the estimated frequency of that key before the observation.
        /// </summary>
        /// <param name="key">The key to observe</param>
        /// <returns>The proportion (frequency) with which that observation occurred, aged over time.</returns>
        public Proportion Observe(TKey key)
        {
            // Perform locked conccurrent add to _keyCounts[key]
            uint increment = _increment;
            uint countForThisKeyAfter = _keyCounts.AddOrUpdate(key, (k) => increment, (k, priorValue) => priorValue + increment);
            uint countForThisKeyBefore = countForThisKeyAfter - increment;
            if (countForThisKeyBefore > 0)
                Interlocked.Add(ref _numberOfElements, 1);
            // Perform a locked add to the total for all the key counts
            Interlocked.Add(ref _sumOfAmountsOverAllKeys, _increment);

            // Special logic used during the initial phase of the tracker, during which we increase
            // the increment amount over time.
            if (_capacityAtWhichToIncreaseTheIncrement >= 0 && Count >= _capacityAtWhichToIncreaseTheIncrement)
            {
                lock (_capacityLockObj)
                {
                    if (_capacityAtWhichToIncreaseTheIncrement >= 0 &&
                        _numberOfElements >= _capacityAtWhichToIncreaseTheIncrement)
                    {
                        // We can grow the increment amount so that newer observations have higher
                        // values than older observations, facilitating future clean-up.
                        if (++_increment >= Generations)
                        {
                            _capacityAtWhichToIncreaseTheIncrement = -1;
                        }
                        else
                        {
                            _capacityAtWhichToIncreaseTheIncrement += _periodBetweenIncrementGrowth;
                        }
                    }
                }
            }
            // We need a recovery if the number of keys exceeds our capacity constraint
            // (Though we use this below, we calculate it here because it requires the lock
            //  we're about to let go of.) 
            if (_recoveryTask == null)
            {
                // No recovery tasks are running.  Check to see if one is needed.
                if (_numberOfElements > MaxCapacity)
                {
                    // We're above our space budget.  Start a reduction operation with the goal
                    // of recovering space
                    StartRecoveringSpace();
                }
                else if (SumOfAmountsOverAllKeys > TotalCountMaxThreshold)
                {
                    // We're above our target for the sum of amounts over all keys, which indicates
                    // that we're keeping around old observations we no longer may want.  Start
                    // a reduction operation with the goal of reducing that sum.
                    StartReducingTotal();
                }
            }

            return new Proportion(countForThisKeyBefore, (ulong) SumOfAmountsOverAllKeys);
        }

        /// <summary>
        /// Start a background task to recover space
        /// </summary>
        private void StartRecoveringSpace()
        {
            lock (_recoveryTaskLock)
            {
                if (_recoveryTask != null)
                    return;
                _recoveryTask = Task.Run(() => RecoverSpace( () =>_keyCounts.Count <= CapacityToTargetWhenRecoveringSpace ));
            }
        }

        /// <summary>
        /// Start a background task to reduce the sum of the observation counts.
        /// </summary>
   
        private void StartReducingTotal()
        {
            lock (_recoveryTaskLock)
            {
                if (_recoveryTask != null)
                    return;
                _recoveryTask = Task.Run(() => RecoverSpace(() => SumOfAmountsOverAllKeys <= ReducedAmountToTargetWhenTotalAmountReached));
            }
        }

        private delegate bool RecoveryFinishedCondition();

        /// <summary>
        /// Recover space when the MaxCapacity limit is reached
        /// </summary>
        private void RecoverSpace(RecoveryFinishedCondition finishCondition)
        {
            // We're finished when the capacity has been reduced to CapacityToTargetWhenRecoveringSpace
            bool finished = false;
            while (!finished)
            {
                // If there queue of keys to cleanup is empty, refill it with all of the key's keys
                if (_cleanupQueue.Count == 0)
                {
                    _cleanupQueue = new Queue<TKey>(_keyCounts.ToArray().Select(k => k.Key));
                }
                // Dequeue one key from the clean-up queue (we only run one recovery-task at a time,
                // so we don't need task/thread safety here.
                TKey key = _cleanupQueue.Dequeue();
                // Reduce the count for the current key on the queue and, if the count reaches 0,
                // remove that key
                uint count;
                if (_keyCounts.TryGetValue(key, out count))
                {
                    if (count <= 1)
                    {
                        _keyCounts.TryRemove(key, out count);
                        Interlocked.Add(ref _sumOfAmountsOverAllKeys, -count);
                        Interlocked.Add(ref _numberOfElements, -1);
                    }
                    else
                    {
                        _keyCounts.AddOrUpdate(key, k => 0, (k, priorValue) => priorValue - 1);
                        Interlocked.Add(ref _sumOfAmountsOverAllKeys, -1);
                    }
                }
                // We're finished when the capacity has reached the target capacity
                finished = finishCondition();
            }
            // Now that the task is done, clear the task object so that a new task can be started
            // if necessary.
            lock (_recoveryTaskLock)
            {
                _recoveryTask = null;
            }
        }

    }
}
