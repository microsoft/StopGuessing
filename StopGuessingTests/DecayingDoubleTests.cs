using System;
using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{
    public class DecayingDoubleTests
    {
        readonly static TimeSpan OneDay = new TimeSpan(1,0,0,0);
        readonly static DateTime FirstDayOfCentury = new DateTime(2000, 1, 1, 0,0,0, DateTimeKind.Utc);
        readonly static DateTime OneDayLater = FirstDayOfCentury + OneDay;
        readonly static DateTime TwoDaysLater = FirstDayOfCentury + new TimeSpan(OneDay.Ticks * 2L);
        readonly static DateTime FourDaysLater = FirstDayOfCentury + new TimeSpan(OneDay.Ticks * 4L);

        [Fact]
        public void DecayOverTwoHalfLives()
        {
            double shouldBeVeryCloseTo1 = DecayingDouble.Decay(4, OneDay, FirstDayOfCentury, TwoDaysLater);
            Assert.InRange(shouldBeVeryCloseTo1, .99999,1.000001);
        }

        [Fact]
        public void AddInPlace()
        {
            DecayingDouble d = new DecayingDouble(4, FirstDayOfCentury);
            // Two days later the double should be 1, so adding 1 should yield 4
            d.AddInPlace(OneDay, 3, TwoDaysLater);
            Assert.InRange(d.ValueAtTimeOfLastUpdate, 3.99999, 4.000001);
            double shouldBeVeryCloseTo1 = d.GetValue(OneDay, FourDaysLater);
            Assert.InRange(shouldBeVeryCloseTo1, .99999, 1.000001);
        }
        [Fact]
        public void SubtractInPlace()
        {
            DecayingDouble d = new DecayingDouble(4, FirstDayOfCentury);
            // One day later the double should be 2, so subtracting 1 should yield 1
            d.SubtractInPlace(OneDay, 1, OneDayLater);
            Assert.InRange(d.ValueAtTimeOfLastUpdate, .99999, 1.000001);
        }

    }
}
