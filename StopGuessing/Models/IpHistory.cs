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
        /// <summary>
        /// The IP address being tracked.
        /// </summary>
        public readonly IPAddress Address;
        
        /// <summary>
        /// A set of recent login attempts that have failed due to incorrect passwords that are kept around so that,
        /// when the correct password is provided, we can see if those passwords were typos and adjust the block
        /// score to reduce past penalities given to failed logins that were typos.
        /// 
        /// This is implemented as a capacity constrained set, similar to a cache, where newer values push out old values.
        /// </summary>
        public SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos; 

        /// <summary>
        /// The current block score for this IP, in the form of a number that decays with time.
        /// </summary>
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
