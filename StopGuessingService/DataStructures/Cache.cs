using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// An interface for classes that have an async method to call to start 
    /// freeing the resources they are using.  This can be handy when a class
    /// needs to store data to a disk or over a network before it releases
    /// itself from memory.
    /// 
    /// If implementing IAsyncDisposable and IDisposable, users should call _either_
    /// Dispose or DisposeAsync, not both.  This allows implementers to make the
    /// Dispose method simply call await DisposeAsync();
    /// </summary>
    public interface IAsyncDisposable
    {
        Task DisposeAsync();
    }


    //public interface IHasUniqueIdentityKeyString
    //{
    //    string UniqueIdentityKeyString { Get; }
    //}


    /// <summary>
    /// Implements a cache with a LRU (least-recently used)-like replacement policy.
    /// (Since the cache knows when objects were accessed from the cache -- when it wrote
    /// an item or retrieved it -- it treats these as uses.  It does not actually
    /// know when an object is used after the access happens.)
    /// 
    /// This class implements IDicionary via inheritance.
    /// </summary>
    /// <typeparam name="TKey">The type of key used to access items in the cache.</typeparam>
    /// <typeparam name="TValue">The value type of values stored in the cache.</typeparam>
    public class Cache<TKey, TValue> : DictionaryThatTracksAccessRecency<TKey, TValue>
    {
        public virtual TValue ConstructDefaultValueForMissingCacheEntry(TKey key)
        {
            return default(TValue);
        }


        /// <summary>
        /// Requests the removal of items from the cache to free up space, removing the
        /// least-recently accessed items first.
        /// </summary>
        /// <param name="numberOfItemsToRemove">The number of items to remove.</param>
        public void RecoverSpace(int numberOfItemsToRemove)
        {
            // Remove the least-recently-accessed values from the cache
            KeyValuePair<TKey, TValue>[] entriesToRemove = RemoveAndGetLeastRecentlyAccessed(numberOfItemsToRemove).ToArray();

            ConcurrentBag<Task> asyncDisposalTasks = new ConcurrentBag<Task>();

            // Call the disposal method on each class.
            Parallel.For(0, entriesToRemove.Length, (counter, loop) =>
            {
                KeyValuePair<TKey, TValue> entryToRemove = entriesToRemove[counter];
                TValue valueToRemove = entryToRemove.Value;
                
                // ReSharper disable once CanBeReplacedWithTryCastAndCheckForNull
                if (valueToRemove is IAsyncDisposable)
                {
                    asyncDisposalTasks.Add(((IAsyncDisposable)valueToRemove).DisposeAsync());
                }
                else if (valueToRemove is IDisposable)
                {
                    ((IDisposable)valueToRemove).Dispose();
                }

                // Remove the last reference to the value so that it can be garbage collected immediately.
                entriesToRemove[counter] = default(KeyValuePair<TKey, TValue>);
            });

            Task.WaitAll(asyncDisposalTasks.ToArray());
        }

        /// <summary>
        /// Requests the removal of items from the cache to free up space, removing the
        /// least-recently accessed items first.
        /// </summary>
        /// <param name="fractionOfItemsToRemove">The fraction of items to remove.</param>
        public void RecoverSpace(double fractionOfItemsToRemove)
        {
            // Calculate the number ot items to remove as a fraction of the total number of items in the cache
            int numberOfItemsToRemove = (int)(Count * fractionOfItemsToRemove);
            // Remove that many
            RecoverSpace(numberOfItemsToRemove);
        }
    }


}