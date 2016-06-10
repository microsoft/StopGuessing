using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// A Dictionary for which Keys and Values are returned in order from
    /// those most-recently used to those least-recently used, and that provides properties
    /// for the most-recently and least-recently used key/value pairs.
    /// Entries are considered accessed if they are added or fetched via Add(), Get(),
    /// or the index operator ([]).  Touching an entry by walking throught the .Values
    /// collection or via the LeastRecentlyAccessed property does not impact the 
    /// access ordering.
    /// 
    /// This data structure uses locking for thread safety.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class DictionaryThatTracksAccessRecency<TKey, TValue> : IDictionary<TKey, TValue>
    {
        protected Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> KeyToLinkedListNode;
        protected LinkedList<KeyValuePair<TKey, TValue>> KeysOrderedFromMostToLeastRecentlyUsed;

        public DictionaryThatTracksAccessRecency()
        {
            KeyToLinkedListNode = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
            KeysOrderedFromMostToLeastRecentlyUsed = new LinkedList<KeyValuePair<TKey, TValue>>();
        }

        /// <summary>
        /// The key/value pair that was most-recently accessed via an index operater
        /// ( [key] ), Get(), or Add().  If you remove this item from the collection,
        /// this property will point to the next most-recently accessed item. 
        /// </summary>
        public KeyValuePair<TKey, TValue> MostRecentlyAccessed => KeysOrderedFromMostToLeastRecentlyUsed.First.Value;

        /// <summary>
        /// The key/value pair that was least-recently accessed via an index operater
        /// ( [key] ), Get(), or Add().  Accessing this property does NOT cause the
        /// item to be treated as if it was just accessed on future calls to these
        /// properties.  Rather, a call to D[ D.LeastRecentlyAccessed.Key ] would
        /// cause this item to be the most-recently accessed.  If you remove this item
        /// from the collection, this property will point to the next most-recently
        /// accessed item. 
        /// </summary>
        public KeyValuePair<TKey, TValue> LeastRecentlyAccessed => KeysOrderedFromMostToLeastRecentlyUsed.Last.Value;

        /// <summary>
        /// Get a list of the <paramref name="numberToGet"/> most-recently-accessed items,
        /// sorted from most-recently-used (first) to least-recently used.
        /// </summary>
        /// <param name="numberToGet">The number of most-recently-accessed items to Get</param>
        /// <returns>A list of the <paramref name="numberToGet"/> most-recently-accessed items,
        /// sorted from most-recently-used (first) to least-recently used.</returns>
        public IList<KeyValuePair<TKey, TValue>> GetMostRecentlyAccessed(int numberToGet)
        {
            List<KeyValuePair<TKey, TValue>> mostRecentlyAccessedItems = new List<KeyValuePair<TKey, TValue>>(numberToGet);
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                LinkedListNode<KeyValuePair<TKey, TValue>> node = KeysOrderedFromMostToLeastRecentlyUsed.First;
                for (int i = 0; i < numberToGet && node != null; i++)
                {
                    mostRecentlyAccessedItems.Add(node.Value);
                    node = node.Next;
                }
            }
            return mostRecentlyAccessedItems;
        }

        /// <summary>
        /// Get a list of the <paramref name="numberToGet"/> least-recently-accessed items,
        /// sorted from least-recently-used (first) to most-recently used.
        /// </summary>
        /// <param name="numberToGet">The number of most-recently-accessed items to Get</param>
        /// <returns>A list of the <paramref name="numberToGet"/> least-recently-accessed items,
        /// sorted from least-recently-used (first) to most-recently used.</returns>
        public List<KeyValuePair<TKey, TValue>> GetLeastRecentlyAccessed(int numberToGet)
        {
            List<KeyValuePair<TKey, TValue>> leastRecentlyAccessedItems = new List<KeyValuePair<TKey, TValue>>(numberToGet);
            lock(KeysOrderedFromMostToLeastRecentlyUsed)
            {
                LinkedListNode<KeyValuePair<TKey, TValue>> node = KeysOrderedFromMostToLeastRecentlyUsed.Last;
                for (int i = 0; i < numberToGet && node != null; i++)
                {
                    leastRecentlyAccessedItems.Add(node.Value);
                    node = node.Previous;
                }
            }
            return leastRecentlyAccessedItems;
        }

        /// <summary>
        /// Remove and return the  <paramref name="numberToGet"/> least-recently-accessed items,
        /// sorted from least-recently-used (first) to most-recently used.
        /// </summary>
        /// <param name="numberToGet">The number of most-recently-accessed items to Get</param>
        /// <returns>A list of the <paramref name="numberToGet"/> least-recently-accessed items,
        /// sorted from least-recently-used (first) to most-recently used.</returns>
        public IList<KeyValuePair<TKey, TValue>> RemoveAndGetLeastRecentlyAccessed(int numberToGet)
        {
            List<KeyValuePair<TKey, TValue>> leastRecentlyAccessedItems = new List<KeyValuePair<TKey, TValue>>(numberToGet);
            lock(KeysOrderedFromMostToLeastRecentlyUsed)
            {
                numberToGet = Math.Min(numberToGet, KeysOrderedFromMostToLeastRecentlyUsed.Count);
                for (int i = 0; i < numberToGet; i++)
                {
                    // Add the least-recently-accessed item to the result list
                    leastRecentlyAccessedItems.Add(KeysOrderedFromMostToLeastRecentlyUsed.Last.Value);
                    // Remove the item from the dictionary and linked list.
                    KeyToLinkedListNode.Remove(KeysOrderedFromMostToLeastRecentlyUsed.Last.Value.Key);
                    KeysOrderedFromMostToLeastRecentlyUsed.RemoveLast();
                }
            }
            return leastRecentlyAccessedItems;
        }


        public int Count => KeyToLinkedListNode.Count;


        /// <summary>
        /// The same as Dictonary.Keys, except that the ordering of the result is from the most-recently
        /// accessed item to the least-recently accessed item.
        /// </summary>
        public ICollection<TKey> Keys
        {
            get
            {
                lock(KeysOrderedFromMostToLeastRecentlyUsed)
                {
                    return KeysOrderedFromMostToLeastRecentlyUsed.Select(keyValue => keyValue.Key).ToList();
                }
            }
        }

        /// <summary>
        /// The same as Dictonary.Values, except that the ordering of the result is from the most-recently
        /// accessed item to the least-recently accessed item.
        /// </summary>
        public ICollection<TValue> Values
        {
            get
            {
                lock (KeysOrderedFromMostToLeastRecentlyUsed)
                {
                    return KeysOrderedFromMostToLeastRecentlyUsed.Select(keyValue => keyValue.Value).ToList();
                }
            }
        }
        
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public TValue this[TKey key]
        {
            get
            {
                return Get(key);
            }
            set
            {
                Add(key, value);
            }
        }


        public virtual void Add(TKey key, TValue value)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                // Remove any nodes with the same key
                LinkedListNode<KeyValuePair<TKey, TValue>> nodeWithSameKey;
                if (KeyToLinkedListNode.TryGetValue(key, out nodeWithSameKey))
                {
                    KeysOrderedFromMostToLeastRecentlyUsed.Remove(nodeWithSameKey);
                    KeyToLinkedListNode.Remove(key);
                }
                // Add
                // Creeate the node
                LinkedListNode<KeyValuePair<TKey, TValue>> newNode =
                    new LinkedListNode<KeyValuePair<TKey, TValue>>(
                        new KeyValuePair<TKey, TValue>(key, value));
                // Put it at the front of the recency-order linked list
                KeysOrderedFromMostToLeastRecentlyUsed.AddFirst(newNode);
                // Add it to the dictionary for fast lookup.
                KeyToLinkedListNode[key] = newNode;

            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        protected virtual TValue GetWithinLock(TKey key)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node = KeyToLinkedListNode[key];
            KeysOrderedFromMostToLeastRecentlyUsed.Remove(node);
            KeysOrderedFromMostToLeastRecentlyUsed.AddFirst(node);
            return node.Value.Value;
        }

        protected virtual TValue Get(TKey key)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return GetWithinLock(key);
            }
        }


        public void Clear()
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                KeyToLinkedListNode.Clear();
                KeysOrderedFromMostToLeastRecentlyUsed.Clear();
            }
        }


        public bool ContainsKey(TKey key)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return KeyToLinkedListNode.ContainsKey(key);
            }
        }

        public bool Contains(TValue value)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return KeyToLinkedListNode.Values.Any(node => node.Value.Value.Equals(value));
            }
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ContainsKey(item.Key);
        }

        internal bool RemoveWithinLock(TKey key)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node;
            if (KeyToLinkedListNode.TryGetValue(key, out node))
            {
                KeysOrderedFromMostToLeastRecentlyUsed.Remove(node);
                return KeyToLinkedListNode.Remove(key);
            }
            else
            {
                return false;
            }
        }

        public bool Remove(TKey key)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return RemoveWithinLock(key);
            }
        }


        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item.Key);
        }

        internal bool TryGetValueWithinLock(TKey key, out TValue value)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> kvp;
            if (KeyToLinkedListNode.TryGetValue(key, out kvp))
            {
                value = kvp.Value.Value;
                return true;
            }
            else
            {
                value = default(TValue);
                return false;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return TryGetValueWithinLock(key, out value);
            }
        }


        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                KeysOrderedFromMostToLeastRecentlyUsed.CopyTo(array, arrayIndex);
            }
        }
        

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return KeysOrderedFromMostToLeastRecentlyUsed.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (KeysOrderedFromMostToLeastRecentlyUsed)
            {
                return KeysOrderedFromMostToLeastRecentlyUsed.ToList().GetEnumerator();
            }
        }
        

           


    }
}
