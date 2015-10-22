using System.Collections.Generic;

namespace StopGuessing.DataStructures
{
    public class SequenceWithFastLookup<T> : Sequence<T>
    {
        protected HashSet<T> FastLookupSet;

        public SequenceWithFastLookup(int capacity) : base(capacity)
        {
            FastLookupSet = new HashSet<T>();
        }

        public override void RemoveAt(int index)
        {
            lock (SequenceArray)
            {
                FastLookupSet.Remove(this[index]);
                base.RemoveAt(index);
            }
        }

        public override void Add(T value)
        {
            lock (SequenceArray)
            {
                FastLookupSet.Add(value);
                base.Add(value);
            }
        }

        public override bool Contains(T value)
        {
            lock (SequenceArray)
            {
                return FastLookupSet.Contains(value);
            }
        }
    }
}
