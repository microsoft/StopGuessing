using System;

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
        public DateTime? LastUpdatedUtc { get; private set; }

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

        public static double Decay(double valueLastSetTo, TimeSpan halfLife, DateTime? whenLastSetUtc, DateTime? timeToDecayTo = null)
        {
            if (whenLastSetUtc == null)
                return valueLastSetTo;
            DateTime whenUtcTime = timeToDecayTo ?? DateTime.UtcNow;
            if (whenUtcTime <= whenLastSetUtc.Value)
                return valueLastSetTo;
            TimeSpan timeSinceLastUpdate = whenUtcTime - whenLastSetUtc.Value;
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
            LastUpdatedUtc = whenUtc;
            ValueAtTimeOfLastUpdate = newValue;
        }

        /// <summary>
        /// A double that decays with time
        /// </summary>
        /// <param name="initialValue">The initial value of the double, which defaults to zero.</param>
        /// <param name="initialLastUpdateUtc">The time when the value was set, which defaults to now.</param>
        public DecayingDouble(double initialValue = 0d, DateTime? initialLastUpdateUtc = null)
        {
            ValueAtTimeOfLastUpdate = initialValue;
            LastUpdatedUtc = initialLastUpdateUtc;
        }

        public DecayingDouble Add(TimeSpan halfLife, DecayingDouble amountToAdd)
        {
            if (!LastUpdatedUtc.HasValue)
            {
                return new DecayingDouble(ValueAtTimeOfLastUpdate + amountToAdd.ValueAtTimeOfLastUpdate, amountToAdd.LastUpdatedUtc);
            }
            else if (!amountToAdd.LastUpdatedUtc.HasValue)
            {
                return new DecayingDouble(ValueAtTimeOfLastUpdate + amountToAdd.ValueAtTimeOfLastUpdate, LastUpdatedUtc);
            }
            else if (LastUpdatedUtc.Value > amountToAdd.LastUpdatedUtc.Value)
            {
                return new DecayingDouble(
                        ValueAtTimeOfLastUpdate + amountToAdd.GetValue(halfLife, LastUpdatedUtc.Value),
                        LastUpdatedUtc.Value);
            }
            else
            {
                return new DecayingDouble(amountToAdd.ValueAtTimeOfLastUpdate + GetValue(halfLife, amountToAdd.LastUpdatedUtc.Value), amountToAdd.LastUpdatedUtc.Value);
            }
        }

        public void AddInPlace(TimeSpan halfLife, double amountToAdd, DateTime? whenToAddIt)
        {
            if (!LastUpdatedUtc.HasValue)
            {
                ValueAtTimeOfLastUpdate += amountToAdd;
                LastUpdatedUtc = whenToAddIt;
            }
            else if (!whenToAddIt.HasValue)
            {
                ValueAtTimeOfLastUpdate += amountToAdd;
            }
            else if (LastUpdatedUtc.Value > whenToAddIt.Value)
            {
                ValueAtTimeOfLastUpdate += Decay(amountToAdd, halfLife, whenToAddIt, LastUpdatedUtc);
            }
            else
            {
                ValueAtTimeOfLastUpdate = GetValue(halfLife, whenToAddIt) +
                                          amountToAdd;
                LastUpdatedUtc = whenToAddIt;
            }
        }

        public void SubtractInPlace(TimeSpan halfLife, double amountToSubtract, DateTime? whenToSubtractIt)
        {
            if (!LastUpdatedUtc.HasValue)
            {
                ValueAtTimeOfLastUpdate -= amountToSubtract;
                LastUpdatedUtc = whenToSubtractIt;
            }
            else if (!whenToSubtractIt.HasValue)
            {
                ValueAtTimeOfLastUpdate -= amountToSubtract;
            }
            else if (LastUpdatedUtc.Value > whenToSubtractIt.Value)
            {
                ValueAtTimeOfLastUpdate -= Decay(amountToSubtract, halfLife, whenToSubtractIt, LastUpdatedUtc);
            }
            else
            {
                ValueAtTimeOfLastUpdate = GetValue(halfLife, whenToSubtractIt) -
                                          amountToSubtract;
                LastUpdatedUtc = whenToSubtractIt;
            }
        }


        public void AddInPlace(TimeSpan halfLife, DecayingDouble amountToAdd)
            => AddInPlace(halfLife, amountToAdd.ValueAtTimeOfLastUpdate, amountToAdd.LastUpdatedUtc);

        public void SubtractInPlace(TimeSpan halfLife, DecayingDouble amountToSubtract)
            => SubtractInPlace(halfLife, amountToSubtract.ValueAtTimeOfLastUpdate, amountToSubtract.LastUpdatedUtc);


        public DecayingDouble Subtract(TimeSpan halfLife, DecayingDouble amountToRemove)
        {
            if (!LastUpdatedUtc.HasValue)
            {
                return new DecayingDouble(ValueAtTimeOfLastUpdate - amountToRemove.ValueAtTimeOfLastUpdate, amountToRemove.LastUpdatedUtc);
            }
            else if (!amountToRemove.LastUpdatedUtc.HasValue)
            {
                return new DecayingDouble(ValueAtTimeOfLastUpdate - amountToRemove.ValueAtTimeOfLastUpdate, LastUpdatedUtc);
            } else if (LastUpdatedUtc.Value > amountToRemove.LastUpdatedUtc.Value)
            {
                return
                    new DecayingDouble(
                        ValueAtTimeOfLastUpdate - amountToRemove.GetValue(halfLife, LastUpdatedUtc.Value),
                        LastUpdatedUtc.Value);
            }
            else
            {
                return new DecayingDouble(amountToRemove.ValueAtTimeOfLastUpdate - GetValue(halfLife, amountToRemove.LastUpdatedUtc.Value), amountToRemove.LastUpdatedUtc.Value);
            }
        }

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

