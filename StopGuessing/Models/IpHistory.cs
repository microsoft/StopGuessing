//#define Simulation
// FIXME
using System;
using System.Net;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{

    public class IPReputationData
    {
        public DecayingDouble InvalidAccountFrequentlyGuessed;
        public DecayingDouble InvalidAccountRarelyGuessed;
        public DecayingDouble InvalidPasswordFrequentlyGuessed;
        public DecayingDouble InvalidPasswordRarelyGuessed;


        public double ToScore(BlockingAlgorithmOptions options, DateTime whenUtc)
        {
            double score = 0;
            score += options.PenaltyForInvalidAccount_Alpha*
                     (InvalidAccountRarelyGuessed.GetValue(options.BlockScoreHalfLife, whenUtc) +
                      InvalidAccountFrequentlyGuessed.GetValue(options.BlockScoreHalfLife, whenUtc) * options.PhiIfFrequent);
            score += options.PenaltyForInvalidPassword_Beta * InvalidPasswordFrequentlyGuessed.GetValue(options.BlockScoreHalfLife, whenUtc) +
                     InvalidPasswordRarelyGuessed.GetValue(options.BlockScoreHalfLife, whenUtc);

            return score;
        }
    }


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
#if Simulation
        public SimulationConditionIpHistoryState[] SimulationConditions;
#endif


        public IpHistory(//bool isIpAKnownAggregatorThatWeCannotBlock = false,
            IPAddress address,
            DateTime? currentDateTimeUtc,
            BlockingAlgorithmOptions options)
        {
            Address = address;
            CurrentBlockScore = new DecayingDouble(0, currentDateTimeUtc);
#if Simulation
            SimulationConditions = new SimulationConditionIpHistoryState[options.Conditions.Length];
            for (int i=0; i < SimulationConditions.Length; i++)
                SimulationConditions[i] = new SimulationConditionIpHistoryState(options.Conditions[i], currentDateTimeUtc);
#endif
            RecentPotentialTypos =
                new SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
        }
        
    }
}
