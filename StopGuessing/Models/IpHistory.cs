//#define Simulation
// FIXME
using System;
using System.Net;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{



    /// <summary>
    /// This class keeps track of recent login successes and failures for a given client IP so that
    /// we can try to determine if this client should be blocked due to likely-password-guessing
    /// behaviors.
    /// </summary>
        public class IpHistory
    {
        public readonly IPAddress Address;
        
        public SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos; 

        public DecayingDouble CurrentBlockScore;

        public IpHistory(
            IPAddress address,
            BlockingAlgorithmOptions options)
        {
            Address = address;
            CurrentBlockScore = new DecayingDouble();
            RecentPotentialTypos =
                new SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
        }
        
    }
}
