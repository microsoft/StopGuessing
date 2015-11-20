using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing.Controllers
{

    [Route("api/[controller]")]
    public class IncorrectPasswordFrequencyController
    {
        ///// <summary>
        ///// We track a sedquence of unsalted failed passwords so that we can determine their pouplarity
        ///// within different historical frequencies.  We need this sequence because to know how often
        ///// a password occurred among the past n failed passwords, we need to add a count each time we
        ///// see it and remove the count when n new failed passwords have been recorded. 
        ///// </summary>
        protected MultiperiodFrequencyTracker<string> MultiperiodFrequencyTracker;

        public IncorrectPasswordFrequencyController(BlockingAlgorithmOptions options)
        {
            MultiperiodFrequencyTracker = new MultiperiodFrequencyTracker<string>(
                options.NumberOfPopularityMeasurementPeriods,
                options.LengthOfShortestPopularityMeasurementPeriod,
                options.FactorOfGrowthBetweenPopularityMeasurementPeriods);
        }

        // GET api/IncorrectPasswordFrequency/
        [HttpGet("{hash}")]
        public Proportion[] Get(string hash)
        {
            return MultiperiodFrequencyTracker.Get(hash);
        }

        [HttpPost("{hash}")]
        public void RecordObservation(string hash)
        {
            MultiperiodFrequencyTracker.RecordObservation(hash);
        }
    }

}
