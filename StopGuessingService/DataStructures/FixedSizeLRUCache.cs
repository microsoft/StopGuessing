using System.Collections.Generic;

namespace StopGuessing.DataStructures
{
    public class FixedSizeLruCache<TKey, TValue> : DictionaryThatTracksAccessRecency<TKey, TValue>
    {
        public int Capacity { get; }

        public FixedSizeLruCache(int capacity)
        {
            Capacity = capacity;
        }


        public override void Add(TKey key, TValue value)
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
                // Remove the oldest node
                if (KeyToLinkedListNode.Count == Capacity)
                {
                    LinkedListNode<KeyValuePair<TKey, TValue>> oldestNode = KeysOrderedFromMostToLeastRecentlyUsed.Last;
                    KeysOrderedFromMostToLeastRecentlyUsed.Remove(oldestNode);
                    KeyToLinkedListNode.Remove(oldestNode.Value.Key);
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

    }
}
