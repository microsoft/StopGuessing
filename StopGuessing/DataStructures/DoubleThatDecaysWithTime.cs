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
    ///// <summary>
    ///// A class that represents a double that decays over time using a half life, to borrow
    ///// a concept from radioactive decay.  A value that is assigned a value 1d and a half life of
    ///// one day will have value 0.5d after one day, .25d after two days, and so on.
    ///// More generally, if you assign the value x, after e half lives it's value will be x/(2^e).
    /////
    ///// Since a double that decays with time will have a constantly-changing real value, it should
    ///// never be used as the key into a Dictionary or kept in a HashSet.  It should not be compared
    ///// for exact equality.
    ///// </summary>
    //public struct DoubleThatDecaysWithTime
    //{
    //    /// <summary>
    //    /// The time period over which a score will decay to half it's current value.
    //    /// </summary>
    //    public TimeSpan HalfLife { get; private set; }

    //    /// <summary>
    //    /// The last time the value was updated (UTC).
    //    /// </summary>
    //    public DateTime LastUpdatedUtc { get; private set; }

    //    /// <summary>
    //    /// The value at the time of last update.  The current value must be
    //    /// adjusted for any decay that has occurred since the time of the last
    //    /// update.
    //    /// </summary>
    //    public double ValueAtTimeOfLastUpdate { get; private set; }

    //    /// <summary>
    //    /// This a the preferred way to get and set the current value, so long as you are using the
    //    /// current (UTC) system time as the measure of time with which to calculate the decay of the value.
    //    /// </summary>
    //    [JsonIgnore]
    //    public double Value
    //    {
    //        get { return GetValue(); }
    //        set { SetValue(value); }
    //    }

    //    /// <summary>
    //    /// Get the value of the double at a given instant in time.  If using the current system time,
    //    /// the preferred way to get the value is not to call this method but to simply access the Value field.
    //    /// </summary>
    //    /// <param name="whenUtc">The instant of time for which the value should be calcualted, factoring in
    //    /// the decay rate.  The default time is the current UTC time.</param>
    //    /// <returns>The value as of the specified time</returns>
    //    public double GetValue(DateTime? whenUtc = null)
    //    {
    //        DateTime whenUtcTime = whenUtc ?? DateTime.UtcNow;
    //        if (whenUtcTime <= this.LastUpdatedUtc)
    //            return ValueAtTimeOfLastUpdate;
    //        TimeSpan timeSinceLastUpdate = whenUtcTime - LastUpdatedUtc;
    //        double halfLivesSinceLastUpdate = timeSinceLastUpdate.TotalMilliseconds/HalfLife.TotalMilliseconds;
    //        return ValueAtTimeOfLastUpdate/Math.Pow(2, halfLivesSinceLastUpdate);
    //    }

    //    /// <summary>
    //    /// Set the value at a point in time.  If setting using the current system time,
    //    /// the preferred approach is to simply set the Value field.
    //    /// </summary>
    //    /// <param name="newValue">The new value to set.</param>
    //    /// <param name="whenUtc">When the set happened so as to correctly calculate future decay rates.
    //    /// Should be adjusted to UTC time.
    //    /// The default time is the current system time adjusted to UTC.</param>
    //    public void SetValue(double newValue, DateTime? whenUtc = null)
    //    {
    //        LastUpdatedUtc = whenUtc ?? DateTime.UtcNow;
    //        ValueAtTimeOfLastUpdate = newValue;
    //    }

    //    /// <summary>
    //    /// A double that decays with time
    //    /// </summary>
    //    /// <param name="halfLife">The half life determines the decay rate.  The value represented by this class
    //    /// will be reduced by a factor of two during the half life.</param>
    //    /// <param name="initialValue">The initial value of the double, which defaults to zero.</param>
    //    /// <param name="initialLastUpdateUtc">The time when the value was set, which defaults to now.</param>
    //    public DoubleThatDecaysWithTime(TimeSpan halfLife, double initialValue, DateTime? initialLastUpdateUtc)
    //    {
    //        HalfLife = halfLife;
    //        ValueAtTimeOfLastUpdate = initialValue;
    //        LastUpdatedUtc = initialLastUpdateUtc ?? DateTime.UtcNow;
    //    }

    //    /// <summary>
    //    /// In-place addition
    //    /// </summary>
    //    /// <param name="amountToAdd">The amount to add</param>
    //    /// <param name="timeOfEventUtc">When to add it, in UTC time (if NULL, sets it to the current clock time)</param>
    //    public void Add(double amountToAdd, DateTime? timeOfEventUtc = null)
    //    {
    //        SetValue(GetValue(timeOfEventUtc) + amountToAdd, timeOfEventUtc);
    //    }

    //    /// <summary>
    //    /// Create an implicit conversion to double for comparisons and such
    //    /// </summary>
    //    /// <param name="doubleThatDecaysWithTime"></param>
    //    static public implicit operator double (DoubleThatDecaysWithTime doubleThatDecaysWithTime)
    //    {
    //        return doubleThatDecaysWithTime.Value;
    //    }

    //    /// <summary>
    //    /// In place subtraction
    //    /// </summary>
    //    /// <param name="amountToSubtract">The amount to subtract</param>
    //    /// <param name="timeOfEventUtc">When to subtract it, in UTC time (if NULL, sets it to the current clock time)</param>
    //    public void Subtract(double amountToSubtract, DateTime? timeOfEventUtc = null)
    //    {
    //        SetValue(GetValue(timeOfEventUtc) - amountToSubtract, timeOfEventUtc);
    //    }


    //    ///
    //    /// Operators to simplify operations with doubles
    //    /// 

    //    /// <summary>
    //    /// Implement the + operator for adding a double to a half life score.
    //    /// </summary>
    //    /// <param name="c1">The half life score on the left side of the operator</param>
    //    /// <param name="c2">The double on the right side of the operator</param>
    //    /// <returns>A new half life score</returns>
    //    public static DoubleThatDecaysWithTime operator +(DoubleThatDecaysWithTime c1, double c2) =>
    //        new DoubleThatDecaysWithTime(c1.HalfLife, c1.Value + c2, DateTime.UtcNow);

    //    /// <summary>
    //    /// Implement the - operator for adding a double to a half life score.
    //    /// </summary>
    //    /// <param name="c1">The half life score on the left side of the operator</param>
    //    /// <param name="c2">The double on the right side of the operator</param>
    //    /// <returns>A new half life score</returns>
    //    public static DoubleThatDecaysWithTime operator -(DoubleThatDecaysWithTime c1, double c2) =>
    //        new DoubleThatDecaysWithTime(c1.HalfLife, c1.Value - c2, DateTime.UtcNow);
        
    //    public static DoubleThatDecaysWithTime operator *(DoubleThatDecaysWithTime c1, double c2) =>
    //        new DoubleThatDecaysWithTime(c1.HalfLife, c1.Value * c2, DateTime.UtcNow);
        
    //    public static DoubleThatDecaysWithTime operator /(DoubleThatDecaysWithTime c1, double c2) =>
    //        new DoubleThatDecaysWithTime(c1.HalfLife, c1.Value / c2, DateTime.UtcNow);
        
    //    public static bool operator <(DoubleThatDecaysWithTime c1, double c2) => c1.Value < c2;
    //    public static bool operator >(DoubleThatDecaysWithTime c1, double c2) => c1.Value > c2;
    //    public static bool operator <=(DoubleThatDecaysWithTime c1, double c2) => c1.Value <= c2;
    //    public static bool operator >=(DoubleThatDecaysWithTime c1, double c2) => c1.Value >= c2;
    //}
}

