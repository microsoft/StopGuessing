using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// A generic collection that tracks the most recent items in a sequence, and allows
    /// these items to be addressed by their position in history from the current (0th) item, to
    /// the previous (item -1 or 1), on the the nth item (addressed as item -(n-1) or +(n-1).
    /// 
    /// Items can be added to the sequence either through the Add() method, by
    /// setting the value of the 0th element, or by setting the Current element's value.
    /// 
    /// When an item is added to the sequence, the former current item (item 0) becomes
    /// item 1, the former item 1 becomes item 2, and so on.  If the sequence is at capacity,
    /// the oldest item in the sequence falls off.
    /// 
    /// This collection is thread-safe.  You can modify it from multiple threads knowing that
    /// its internal locks will prevent thread-safety issues.
    /// 
    /// Internally, this collection is a circular array.
    /// </summary>
    /// <typeparam name="T">The type of the item to keep a sequence of.</typeparam>
    [DataContract]
    [JsonConverter(typeof(SequenceConverter))]
    public class Sequence<T> : ICollection<T>, IEquatable<Sequence<T>>// , IComparable<T> //, IEnumerable<T> , IList<T>
    {
        [IgnoreDataMember]
        protected int CurrentItemIndex;
        [IgnoreDataMember]
        public int Count { get; protected set; }
        [IgnoreDataMember]
        protected internal long ItemsAlreadyObserved { get; set; }
        [IgnoreDataMember]
        protected T[] SequenceArray;

        public IEnumerable<T> OldestToMostRecent => this;
        public IEnumerable<T> MostRecentToOldest => this.Reverse();

        /// <summary>
        /// The number of elements in the collection, which is the number of items in the sequence
        /// that the data structure remembers.
        /// </summary>
        public int Capacity
        {
            get { return SequenceArray.Length; }
            set
            {
                if (SequenceArray != null )
                    throw new Exception("Once the capacity of a seqeunce has been set it cannot be changed.");
                SequenceArray = new T[value];
            }
        }

        /// <summary>
        /// The number of elements that have been added to the collection, the oldest of which may
        /// have been dropped if they exceed the capacity of the sequence.
        /// </summary>
        public long CountOfAllObservedItems => ItemsAlreadyObserved;

        /// <summary>
        /// The number of items in the collection that can be accessed.  If the number of items that has
        /// been observed by (added to) the sequence, the number of items observed will be the result.
        /// If more items have been observed than can be stored (the history exceeds the capacity of the
        /// sequence's memory), the result will be the capacity.
        /// </summary>
        //public int Count => (Count < Capacity) ? (int)Count : Capacity;


        /// <summary>
        /// This constructor is used during JSON deserialization.
        /// </summary>
        /// <param name="capacity"></param>
        /// <param name="itemsAlreadyObserved"></param>
        public Sequence(int capacity, long itemsAlreadyObserved)
        {
            // For deserialization only.
            SequenceArray = new T[capacity];
            CurrentItemIndex = 0;
            Count = 0;
            ItemsAlreadyObserved = itemsAlreadyObserved;
        }


        /// <summary>
        /// The constructor of a sequence of given capacity.
        /// </summary>
        /// <param name="capacity">The number of items in the sequence that shoudl be tracked before they are dropped from the historical memory</param>
        public Sequence(int capacity) : this(capacity, 0)
        {
        }

        /// <summary>
        /// Access an item in the sequence by how recently it was observed.
        /// </summary>
        /// <param name="index">The index is a 0-based array back in time.  The most recent item is index 0.
        /// The next most recent item is -1 (one back in history) or 1, then -2 or 2.
        /// For the general case, the ith most recent item is either at index i or -i.
        /// int.MinValue is not allowed.
        /// </param>
        /// <returns></returns>
        public T this[int index]
        {
            get
            {
                lock (SequenceArray)
                {
                    return SequenceArray[SequenceIndexToArrayIndex(index)];
                }
            }
            set
            {
                if (index != 0)
                    throw new IndexOutOfRangeException("You can only set the current value of a sequence");
                Add(value);
            }
        }

        /// <summary>
        /// Add in item to the sequence.  This item will become the most recent item in the sequence (item 0),
        /// the item that was the most recent item will become item 1, and so on.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="value">The item to Add to the sequence.</param>
        public virtual void Add(T value)
        {
            lock (SequenceArray)
            {
                if (++CurrentItemIndex >= SequenceArray.Length)
                    CurrentItemIndex = 0;
                SequenceArray[CurrentItemIndex] = value;
                if (Count < Capacity)
                {
                    Count++;
                }
                ItemsAlreadyObserved++;
            }
        }

        /// <summary>
        /// Add in item to the sequence.  This item will become the most recent item in the sequence (item 0),
        /// the item that was the most recent item will become item 1, and so on.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="values">The set of items to add to the sequence.</param>
        public virtual void AddRange(IEnumerable<T> values)
        {
            lock (SequenceArray)
            {
                foreach (T value in values)
                {
                    if (++CurrentItemIndex >= SequenceArray.Length)
                        CurrentItemIndex = 0;
                    SequenceArray[CurrentItemIndex] = value;
                    if (Count < Capacity)
                    {
                        Count++;
                    }
                    ItemsAlreadyObserved++;
                }
            }
        }

        /// <summary>
        /// Test to see whether an item is in the sequence.  This is a linear search and so may be costly for large sequences.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="value">The item to search for</param>
        /// <returns></returns>
        public virtual bool Contains(T value)
        {
            lock (SequenceArray)
            {
                return SequenceArray.Contains(value);
            }
        }

        /// <summary>
        /// Empty the sequence of all contents
        /// This method is thread-safe.
        /// </summary>
        public void Clear()
        {
            lock (SequenceArray)
            {
                CurrentItemIndex = 0;
                Count = 0;
                ItemsAlreadyObserved = 0;
            }
        }

        /// <summary>
        /// Copy the items in a part of the sequence to fill a buffer.  Copies from oldest item (the furthest index back) to the most recent item.
        /// </summary>
        /// <param name="subsequenceBuffer">An array into which to copy the subsequence.  The length of the array determines how many items may be copied.
        /// The length of the array may not be longer than the available history.</param>
        /// <param name="index">The item in the sequence to start copying, using 0-based indexing.  Positives and negative index values are equivalent (though int.Minvalue is not allowed).
        /// For one item, you would set this to 0.  For ten items, you would set this to 9 or -9 and use a subsequence buffer of length 10.</param>
        public void CopyTo(T[] subsequenceBuffer, int index)
        {
            lock (SequenceArray)
            {

                int numItemsToCopy = subsequenceBuffer.Length - index;

                if (numItemsToCopy > Capacity)
                    throw new IndexOutOfRangeException(
                        "Attempt to Get a subsequence using items beyond the sequence's capacity.");

                int howFarBackToStart = numItemsToCopy;
                while (index < subsequenceBuffer.Length)
                    subsequenceBuffer[index++] = this[--howFarBackToStart];
            }
        }

        /// <summary>
        /// Copy a portion of the subsequence into an array.  Copies from oldest item (the largest index) to the most recent item (the index of smallest magnitude.)
        /// </summary>
        /// <param name="howFarBackToStart">The item in the sequence to start copying, using 0-based indexing.  Positives and negative index values are equivalent (though int.Minvalue is not allowed).
        /// For one item, you would set this to 0.  For ten items, you would set this to 9 or -9 and set howManyItemsToTake to 10.</param>
        /// <param name="howManyItemsToTake">The number of items in the subsequence to copy.</param>
        /// <returns></returns>
        public T[] GetSubsequence(int howFarBackToStart, int howManyItemsToTake)
        {
            T[] subsequenceBuffer = new T[howManyItemsToTake];
            lock (SequenceArray)
            {
                if (howFarBackToStart < 0)
                    howFarBackToStart = -howFarBackToStart;
                if (howFarBackToStart >= Count)
                    throw new IndexOutOfRangeException("Attempt to Get a subsequence using items older than are tracked, or the number of observed items.");
                if (howFarBackToStart > Capacity)
                    throw new IndexOutOfRangeException("Attempt to request a subsequence further back in time than the sequence has capacity to remember.");

                for (int i = 0; i < subsequenceBuffer.Length; i++)
                    subsequenceBuffer[i] = this[howFarBackToStart - i];
            }
            return subsequenceBuffer;
        }

        public T[] GetSubsequence(int howFarBackToStart)
        {
            if (howFarBackToStart < 0)
                howFarBackToStart = -howFarBackToStart;
            return GetSubsequence(howFarBackToStart, howFarBackToStart + 1);
        }

        public T[] GetSubsequence()
        {
            return GetSubsequence(Count - 1, Count);
        }



        /// <summary>
        /// An internal method for mapping an sequence index into our circular array
        /// </summary>
        /// <param name="sequenceIndex"></param>
        /// <returns></returns>
        protected int SequenceIndexToArrayIndex(int sequenceIndex)
        {
            // Allow previous value to be indexed via [-i] or [i] -- either one,
            // internally, we'll use positive values
            if (sequenceIndex == int.MinValue)
                throw new IndexOutOfRangeException("Cannot index int.MinValue.");
            if (sequenceIndex < 0)
                sequenceIndex = -sequenceIndex;
            // Ensure that length doesn't exceed the sequence length
            //Fix the bug, change ">=" to >
            if (sequenceIndex > Count)
                throw new IndexOutOfRangeException("Index is out of range of valid sequence values.");

            // Index off of the current item's index to Get the right position in the sliding window...
            int arrayIndex = CurrentItemIndex - sequenceIndex;
            // ...wrapping around to the start of the array, if necessary
            if (arrayIndex < 0)
                arrayIndex += SequenceArray.Length;

            return arrayIndex;
        }

        /// <summary>
        /// An read-only accessor for getting the most-recent item in the sequence
        /// or adding to the sequence
        /// </summary>
        public T Current
        {
            get
            {
                lock (SequenceArray)
                {
                    return SequenceArray[CurrentItemIndex];
                }
            }
            set { Add(value); }
        }



        //
        // For ICollection support
        //

        public bool Remove(T item)
        {
            lock (SequenceArray)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (EqualityComparer<T>.Default.Equals(item, this[i]))
           
                    {
                        RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
        }

        public virtual void RemoveAt(int index)
        {
            lock (SequenceArray)
            {
                int currentIndex = SequenceIndexToArrayIndex(index);
                for (int i = index; i < Count; i++)
                {
                    int nextIndex = currentIndex - 1;
                    if (nextIndex < 0)
                        nextIndex += SequenceArray.Length;
                    SequenceArray[currentIndex] = SequenceArray[nextIndex];
                }
                Count--;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new SequenceEnumerator(this);
        }

        public bool IsReadOnly => false;

        public bool Equals(Sequence<T> other)
        {
            return other.Capacity == Capacity &&
                   other.Count == Count &&
                   other.GetSubsequence().SequenceEqual(GetSubsequence());
        }

        public override bool Equals(object other)
        {
            if (!(other is Sequence<T>)) return false;
            return Equals((Sequence<T>) other);
        }

        public override int GetHashCode()
        {
            return GetSubsequence().GetHashCode();
        }

        //public int CompareTo(T other)
        //{
        //    throw new NotImplementedException();
        //}

        public class SequenceEnumerator : IEnumerator<T>
        {
            readonly Sequence<T> _sequence;
            private int _count;

            public SequenceEnumerator(Sequence<T> sequence)
            {
                _sequence = sequence;
                Reset();
            }

            public T Current => _sequence[_count];

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                return (--_count >= 0);
            }

            public void Reset()
            {
                _count = _sequence.Count;
            }

            public void Dispose()
            { }
        }
    }


    /// <summary>
    /// Since Sequence is designed for efficiency of computation, and not of storage,
    /// this class converts sequences into a compact format for JSON storage when JSON.Net is used.
    /// 
    /// It stores the capacity in a 'Capacity' field.
    /// It stores the values of the sequence, in order from first-added to last-added (current), in
    /// the 'Values' field.
    /// The 'AdditionalItemsObserved field is added if and only if the number of items observed
    /// exceeds the capacity so that the ItemsAlreadyObserved field can be preserved.  It's invariant is:
    ///    ItemsAlreadyObserved = Count + AdditionalItemsObserved
    /// where AdditionalItemsObserved is 0 if not present.
    /// 
    /// To overcome challenges with using generic JsonConverters, this class uses the 'dynamic'
    /// keyword.
    /// </summary>
    public class SequenceConverter : JsonConverter
    {
        private const string CapacityName = "Capacity";
        private const string AdditionalItemsObservedName = "AdditionalItemsObserved";
        private const string ValuesName = "Values";


        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer
            )
        {
            // Load JObject from stream
            JObject jObject = JObject.Load(reader);

            // Determine the specified capacity of the sequence
            int capacity = jObject[CapacityName].Value<int>();

            long additionalItemsObserved = 0;
            JToken additionalItemsObservedToken;
            if (jObject.TryGetValue(AdditionalItemsObservedName, out additionalItemsObservedToken))
            {
                additionalItemsObserved = additionalItemsObservedToken.ToObject<long>();
            }

            // Create target object based on JObject
            //objectType.GenericTypeArguments[0]
            //dynamic target = typeof (Sequence<>).MakeGenericType(objectType.GenericTypeArguments).GetConstructor(new Type[]
            //{
            //    typeof(int), typeof(long)
            //})?.Invoke(new object[] {capacity, additionalItemsObserved});

            dynamic target = Activator.CreateInstance(objectType, capacity, additionalItemsObserved);

            Type typeArrayOfT = typeof(List<>).MakeGenericType(objectType.GenericTypeArguments);
            //List<dynamic> values = (List<dynamic>) jObject[ValuesName].ToObject(typeArrayOfT);
            dynamic values = jObject[ValuesName].ToObject(typeArrayOfT);
            target.AddRange(values);

            return target;
        }


        public override void WriteJson(
            JsonWriter writer,
            Object value,
            JsonSerializer serializer
            )
        {
            dynamic sequence = value;
            writer.WriteStartObject();
            writer.WritePropertyName(CapacityName);
            serializer.Serialize(writer, sequence.Capacity);
            long additionalItemsObserved = sequence.ItemsAlreadyObserved - sequence.Count;
            if (additionalItemsObserved > 0)
            {
                writer.WritePropertyName(AdditionalItemsObservedName);
                serializer.Serialize(writer, additionalItemsObserved);
            }
            writer.WritePropertyName(ValuesName);
            serializer.Serialize(writer, sequence.GetSubsequence(sequence.Count - 1, sequence.Count));
            writer.WriteEndObject();
        }


        public override bool CanConvert(Type objectType)
        {
            bool canConvert = StaticUtilities.IsAssignableToGenericType(objectType, typeof (Sequence<>));
            return canConvert;
        }
    }
    
}
