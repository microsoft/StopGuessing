using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using StopGuessing.EncryptionPrimitives;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A class that represents a double that decays over time using a half life, to borrow
    /// a concept from radioactive decay.  A value that is assigned a value 1d and a half life of
    /// one day will have value 0.5d after one day, .25d after two days, and so on.
    /// More generally, if you assign the value x, after e half lives it's value will be x/(2^e).
    ///
    /// Since a double that decays with time will have a constantly-changing real value, it should
    /// never be used as the key into a Dictionary or kept in a HashSet.  It should not be compared
    /// for exact equality.
    /// </summary>
    public struct DecayingDouble
    {
        /// <summary>
        /// The last time the value was updated (UTC).
        /// </summary>
        public DateTime LastUpdatedUtc { get; private set; }

        /// <summary>
        /// The value at the time of last update.  The current value must be
        /// adjusted for any decay that has occurred since the time of the last
        /// update.
        /// </summary>
        public double ValueAtTimeOfLastUpdate { get; private set; }


        /// <summary>
        /// Get the value of the double at a given instant in time.  If using the current system time,
        /// the preferred way to get the value is not to call this method but to simply access the Value field.
        /// </summary>
        /// <param name="halfLife">The half life to use when determining the current value.</param>
        /// <param name="whenUtc">The instant of time for which the value should be calcualted, factoring in
        /// the decay rate.  The default time is the current UTC time.</param>
        /// <returns>The value as of the specified time</returns>
        public double GetValue(TimeSpan halfLife, DateTime? whenUtc = null)
        {
            return Decay(ValueAtTimeOfLastUpdate, halfLife, LastUpdatedUtc, whenUtc);
        }

        public static double Decay(double valueLastSetTo, TimeSpan halfLife, DateTime whenLastSetUtc, DateTime? timeToDecayTo = null)
        {
            DateTime whenUtcTime = timeToDecayTo ?? DateTime.UtcNow;
            if (whenUtcTime <= whenLastSetUtc)
                return valueLastSetTo;
            TimeSpan timeSinceLastUpdate = whenUtcTime - whenLastSetUtc;
            double halfLivesSinceLastUpdate = timeSinceLastUpdate.TotalMilliseconds / halfLife.TotalMilliseconds;
            return valueLastSetTo / Math.Pow(2, halfLivesSinceLastUpdate);
        }

        /// <summary>
        /// Set the value at a point in time.  If setting using the current system time,
        /// the preferred approach is to simply set the Value field.
        /// </summary>
        /// <param name="newValue">The new value to set.</param>
        /// <param name="whenUtc">When the set happened so as to correctly calculate future decay rates.
        /// Should be adjusted to UTC time.
        /// The default time is the current system time adjusted to UTC.</param>
        public void SetValue(double newValue, DateTime? whenUtc = null)
        {
            LastUpdatedUtc = whenUtc ?? DateTime.UtcNow;
            ValueAtTimeOfLastUpdate = newValue;
        }

        /// <summary>
        /// A double that decays with time
        /// </summary>
        /// <param name="initialValue">The initial value of the double, which defaults to zero.</param>
        /// <param name="initialLastUpdateUtc">The time when the value was set, which defaults to now.</param>
        public DecayingDouble(double initialValue, DateTime? initialLastUpdateUtc)
        {
            ValueAtTimeOfLastUpdate = initialValue;
            LastUpdatedUtc = initialLastUpdateUtc ?? DateTime.UtcNow;
        }

        //public static double Add(double lastValue, DateTime whenWasValueLastSetUtc, double amountToAdd, TimeSpan halfLife,
        //    DateTime? timeOfAddOperationUtc = null)
        //{
        //    return Decay(lastValue, halfLife, whenWasValueLastSetUtc, timeOfAddOperationUtc) + amountToAdd;
        //}

        /// <summary>
        /// In-place addition
        /// </summary>
        /// <param name="amountToAdd">The amount to add to the existing value.</param>
        /// <param name="halfLife">The half life to use when determining the existing value.</param>
        /// <param name="timeOfAddOperationUtc">When to add it, in UTC time (if NULL, sets it to the current clock time)</param>
        public void Add(double amountToAdd, TimeSpan halfLife, DateTime? timeOfAddOperationUtc = null)
        {
            SetValue(GetValue(halfLife, timeOfAddOperationUtc) + amountToAdd, timeOfAddOperationUtc);
        }
    }
}

