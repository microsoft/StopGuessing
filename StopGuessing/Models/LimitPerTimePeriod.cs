using System;


namespace StopGuessing.Models
{

    /// <summary>
    /// This class represents the number a limit on the number of times something is allowed to occur,
    /// or may be given out, in a given time period.
    /// </summary>
    public class LimitPerTimePeriod
    {
        public TimeSpan TimePeriod;
        public float Limit;

        public LimitPerTimePeriod(TimeSpan timePeriod, float limit)
        {
            TimePeriod = timePeriod;
            Limit = limit;
        }
    }

}