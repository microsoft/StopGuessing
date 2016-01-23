using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.AspNet.Razor.Chunks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// This class will track the n most recent values observed unique itmes.  When you Add a new item to the set,
    /// and the set is already at capacity, the least-recently added item is removed.  If you re-Add
    /// an item already in the set, it will become the last to be removed (as if it were just added
    /// for the first time).
    /// </summary>
    /// <typeparam name="T"></typeparam>    
    [DataContract]
    [JsonConverter(typeof(SmallCapacityConstrainedSetConverter))]
    public class SmallCapacityConstrainedSet<T> : IEquatable<SmallCapacityConstrainedSet<T>>
    {
        /// <summary>
        /// The maximum number of elements the set can hold.
        /// </summary>
        public int Capacity { get; protected set; }

        protected LinkedList<T> AsList;
        protected ReaderWriterLockSlim RwLock;

        /// <summary>
        /// Get the members of the set ordered from the most-recently added to the least-recently added.
        /// </summary>
        public IEnumerable<T> LeastRecentFirst => MostRecentFirst.Reverse();

        public bool Contains(T item)
        {
            RwLock.EnterReadLock();
            try
            {
                return AsList.Contains(item);
            }
            finally
            {
                RwLock.ExitReadLock();
            }            
        } 

        /// <summary>
        /// Get the members of the set ordered from the most-recently added to the least-recently added.
        /// </summary>
        public IEnumerable<T> MostRecentFirst
        {
            get
            {
                {
                    RwLock.EnterReadLock();
                    try
                    {
                        return AsList.ToArray();
                    }
                    finally
                    {
                        RwLock.ExitReadLock();
                    }
                }
            }
        }

        /// <summary>
        /// Create a set with a given capacity
        /// </summary>
        /// <param name="capacity">The maximum number of items that the set can hold.
        /// When capacity is exceeded, the oldest member of the set is removed.</param>
        public SmallCapacityConstrainedSet(int capacity)
        {
            AsList = new LinkedList<T>();
            Capacity = capacity;
            RwLock = new ReaderWriterLockSlim();
        }
       

        /// <summary>
        /// Add an item to the set.  If the set is already full, the oldest item in the set
        /// (the one that was added least recently) will be removed to make space for the new item.
        /// If the item is in the set, adding it will renew the recency with which it was added.
        /// </summary>
        /// <param name="item">The item to Add.</param>
        /// <returns>Returns true if the item was already a member of the set; false otherwise.</returns>
        public bool Add(T item)
        {
            RwLock.EnterWriteLock();
            try
            {
                bool itemIsAlreadyPresent = AsList.Contains(item);
                if (itemIsAlreadyPresent)
                {
                    if (!item.Equals(AsList.First))
                    {
                        // Make the item, which is already a member,
                        // the most recent
                        AsList.Remove(item);
                        AsList.AddFirst(item);
                    }
                }
                else
                {
                    if (AsList.Count >= Capacity)
                    {
                        // We need to remove the oldest item to make space for the new item
                        AsList.RemoveLast();
                    }
                    // Add the new item
                    AsList.AddFirst(item);

                }
                return itemIsAlreadyPresent;
            }
            finally
            {
                RwLock.ExitWriteLock();
            }
        }

        public bool Remove(T item)
        {
            RwLock.EnterWriteLock();
            try
            {
                return AsList.Remove(item);
            }
            finally
            {
                RwLock.ExitWriteLock();
            }
        }

        public void UnionWith(IEnumerable<T> newMembers)
        {
            foreach (T newMember in newMembers)
                Add(newMember);
        }
        
        public bool Equals(SmallCapacityConstrainedSet<T> other)
        {
            return other.Capacity == Capacity &&
                   other.LeastRecentFirst.SequenceEqual(LeastRecentFirst);
        }
    }




    public class SmallCapacityConstrainedSetConverter : JsonConverter
    {
        private const string CapacityName = "Capacity";
        private const string MembersName = "Members";


        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load JObject from stream
            JObject jObject = JObject.Load(reader);

            // Determine the specified capacity of the sequence
            int capacity = jObject[CapacityName].Value<int>();

            // Create an instance
            dynamic target = Activator.CreateInstance(objectType, capacity);

            // Create a list of values to initialize with
            Type typeArrayOfT = typeof (List<>).MakeGenericType(objectType.GenericTypeArguments);
            dynamic values = jObject[MembersName].ToObject(typeArrayOfT);
            target.UnionWith(values);

            return target;
        }


        public override void WriteJson(JsonWriter writer, Object value, JsonSerializer serializer)
        {
            dynamic set = value;
            // Start the object
            writer.WriteStartObject();
            // WriteAccountToStableStoreAsync the capacity
            writer.WritePropertyName(CapacityName);
            serializer.Serialize(writer, set.Capacity);
            // WriteAccountToStableStoreAsync the members
            writer.WritePropertyName(MembersName);
            serializer.Serialize(writer, set.LeastRecentFirst);
            // Close the object 
            writer.WriteEndObject();
        }

        public override bool CanConvert(Type objectType)
        {
            bool canConvert = StaticUtilities.IsAssignableToGenericType(objectType, typeof (SmallCapacityConstrainedSet<>));
            return canConvert;
        }
    }

}