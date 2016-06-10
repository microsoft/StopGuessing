using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A cache that will automatically load missing values using a delegate function passed
    /// into the constructor.
    /// 
    /// Accessing an item that is not yet cached via the indexor (c[key]) will cause the cache to
    /// load the missing value into the cache by calling the delegate function.  (TryGet
    /// will return false, and not fetch, unless the item is present.)
    /// 
    /// GetAync allows the caller to try to fetch a value from the cache, but if if
    /// the value has to be loaded from disk, the network, or a parallel universe, the
    /// caller can still Get work done while waiting.
    /// 
    /// (Self-loading caches should not be confused with a self-loathing caches.  The latter
    /// are much less helpful when values are missing.  Their one advantage is that, when
    /// things go awry, such caches are more likely to blame themselves than to blame those
    /// who instantiated or called them them.  They are often heard muttering "I should
    /// have known when I evicted that entry that you would need it again so shortly.")
    /// </summary>
    /// <typeparam name="TKey">The type of the key used to identify items in the cache.</typeparam>
    /// <typeparam name="TValue">The type of the values stored within the cache.</typeparam>
    public class SelfLoadingCache<TKey, TValue> : Cache<TKey, TValue>
    {
        /// <summary>
        /// A function for loaded values into the cache.  Give it a cache key, and it should
        /// automatically load in a cache value.
        /// </summary>
        /// <param name="key">They key of the value to be loaded into the cache.</param>
        /// <returns>The value loaded into the cache.</returns>
        public delegate Task<TValue> FunctionToConstructAValueFromItsKeyAsync(TKey key, CancellationToken cancellationToken);
        public delegate TValue FunctionToConstructAValueFromItsKey(TKey key);

        /// <summary>
        /// The class keeps a copy of the function to call when values are missing
        /// and needed to be loaded.  When a value is missing and there isn't a task
        /// already loading it, this class will call the delegate to load it.
        /// </summary>
        protected FunctionToConstructAValueFromItsKeyAsync ConstructAValueFromItsKeyAsync;
        protected FunctionToConstructAValueFromItsKey ConstructAValueFromItsKey;

        /// <summary>
        /// When a caller attempts to load a value from the cache, and its not present,
        /// we create a task to load that value and store it here.  This way if two or more
        /// callers request the same missing value, they can all wait for the same task
        /// to complete rather than replicating the work.
        /// </summary>
        protected readonly Dictionary<TKey, Task<TValue>> ValuesUnderConstruction;


        /// <summary>
        /// To create a self-loading cache, one must pass it a function that will load in values
        /// missing from the cache.
        /// </summary>
        /// <param name="constructAValueFromItsKeyAsync">A function that takes a key and return the appropriate value to load into the cache.</param>
        public SelfLoadingCache(FunctionToConstructAValueFromItsKeyAsync constructAValueFromItsKeyAsync = null)
        {
            ConstructAValueFromItsKeyAsync = constructAValueFromItsKeyAsync;
            ConstructAValueFromItsKey = null;
            ValuesUnderConstruction = new Dictionary<TKey, Task<TValue>>();
        }
        public SelfLoadingCache(FunctionToConstructAValueFromItsKey constructAValueFromItsKey = null)
        {
            ConstructAValueFromItsKeyAsync = null;
            ConstructAValueFromItsKey = constructAValueFromItsKey;
            ValuesUnderConstruction = new Dictionary<TKey, Task<TValue>>();
        }

        /// <summary>
        /// Fetch a key from the cache.  Since fetching values from a self-loading cache may require
        /// waiting for data to load from disk, a network, or a parallel universe, this async method
        /// allows the caller to do other work.
        /// </summary>
        /// <param name="key">The key to fetch from the cache.</param>
        /// <param name="cancellationToken">For cancelling aync operations.</param>
        /// <returns></returns>
        public virtual async Task<TValue> GetAsync(TKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            TValue resultValue;
            Task<TValue> taskToConstructValueAsync;
            bool justConstructedTheCacheEntry = false;
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                if (TryGetValueWithinLock(key, out resultValue))
                {
                    // The result is in the cache and can be returned immediately.
                    return resultValue;
                }
                else
                {
                    // The result needs to be constructed and placed into the cache before it can be returned.

                    if (ValuesUnderConstruction.TryGetValue(key, out taskToConstructValueAsync))
                    {
                        // Good news for the lazy!
                        // Some other task/thread has already requested this cache entry and started constructing its value.
                        // Rather than replicate the same work and create two out-of-sync copies of the value, I (this task)
                        // can instead wait for the task that is already doing the work.
                    }
                    else if (ConstructAValueFromItsKeyAsync != null)
                    {
                        // Create a task to start constructing the value from the key, which may require
                        // us to go to disk or the network to load data.  
                        justConstructedTheCacheEntry = true;
                        taskToConstructValueAsync = ConstructAValueFromItsKeyAsync(key, cancellationToken);
                    }

                    else if (ConstructAValueFromItsKey != null)
                    {
                        // Create a task to start constructing the value from the key, which may require
                        // us to go to disk or the network to load data.  
                        justConstructedTheCacheEntry = true;
                        return this[key] = ConstructAValueFromItsKey(key);
                    }
                    else {
                        // Alas, we've been given no way to load the value.  All we can do is return a default value.
                        return default(TValue);
                    }
                }
            }
            resultValue = await taskToConstructValueAsync;
            if (justConstructedTheCacheEntry)
            {
                // Since I did the work to construct this cache entry, I get to put it into the cache.
                this[key] = resultValue;
            }
            return resultValue;
        }


        /// <summary>
        /// Override the ancestor's Get() method used to Get values from the indexer (called on: val = cache[key];)
        /// syncronously.
        /// </summary>
        /// <param name="key">The key of the value to load.</param>
        /// <returns>The value in the cache (or loaded into the cache) for the provided key.</returns>
        protected override TValue Get(TKey key)
        {
            return GetAsync(key).Result;       
        }


    }
}
