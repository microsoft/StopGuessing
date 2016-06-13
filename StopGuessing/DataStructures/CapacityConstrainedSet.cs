using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
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
    [JsonConverter(typeof(CapacityConstrainedSetConverter))]
    public class CapacityConstrainedSet<T> : HashSet<T>, IEquatable<CapacityConstrainedSet<T>>
    {
        /// <summary>
        /// The maximum number of elements the set can hold.
        /// </summary>
        public int Capacity { get; protected set; }

        /// <summary>
        /// This internal representation of the set maintains ordering from most- to least-recently added.
        /// </summary>
        private readonly LinkedList<T> _recency;

        /// <summary>
        /// Get the members of the set ordered from the most-recently added to the least-recently added.
        /// </summary>
        public List<T> InOrderAdded
        {
            get
            {
                lock (_recency)
                {
                    return _recency.Reverse().ToList();
                }
            }
        }

        /// <summary>
        /// Create a set with a given capacity
        /// </summary>
        /// <param name="capacity">The maximum number of items that the set can hold.
        /// When capacity is exceeded, the oldest member of the set is removed.</param>
        public CapacityConstrainedSet(int capacity)
        {
            Capacity = capacity;
            //_membership = new HashSet<T>();
            _recency = new LinkedList<T>();
        }
       

        /// <summary>
        /// Add an item to the set.  If the set is already full, the oldest item in the set
        /// (the one that was added least recently) will be removed to make space for the new item.
        /// If the item is in the set, adding it will renew the recency with which it was added.
        /// </summary>
        /// <param name="item">The item to Add.</param>
        /// <returns>Returns true if the item was already a member of the set; false otherwise.</returns>
        public new bool Add(T item)
        {
            lock (_recency)
            {
                bool itemIsAlreadyPresent = Contains(item);
                if (itemIsAlreadyPresent)
                {
                    if (!item.Equals(_recency.First()))
                    {
                        // Make the item, which is already a member,
                        // the most recent
                        _recency.Remove(item);
                        _recency.AddFirst(item);
                    }
                }
                else
                {
                    if (Count >= Capacity)
                    {
                        // We need to remove the oldest item to make space for the new item
                        base.Remove(_recency.Last());
                        _recency.RemoveLast();
                    }
                    // Add the new item
                    base.Add(item);
                    _recency.AddFirst(item);

                }
                return itemIsAlreadyPresent;
            }
        }

        public new bool Remove(T item)
        {
            lock (_recency)
            {
                _recency.Remove(item);
                return base.Remove(item);
            }
        }

        public new void UnionWith(IEnumerable<T> newMembers)
        {
            foreach (T newMember in newMembers)
                Add(newMember);
        }
        
        public bool Equals(CapacityConstrainedSet<T> other)
        {
            return other.Capacity == Capacity &&
                   other.InOrderAdded.SequenceEqual(InOrderAdded);
        }
    }




    public class CapacityConstrainedSetConverter : JsonConverter
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
            serializer.Serialize(writer, set.InOrderAdded);
            // Close the object 
            writer.WriteEndObject();
        }

        public override bool CanConvert(Type objectType)
        {
            bool canConvert = objectType.GetGenericTypeDefinition() == typeof (CapacityConstrainedSet<>);
            //bool canConvert = StaticUtilities.IsAssignableToGenericType(objectType, typeof (CapacityConstrainedSet<>));
            return canConvert;
        }
    }

}