using System;
using System.Collections.Generic;


namespace StopGuessing.Models
{


    public class PenaltyForReachingAPopularityThreshold
    {
        public double PopularityThreshold { get; set; }
        public double Penalty { get; set; }
    }

    public class BlockingAlgorithmOptions
    {
        //FIXME
        public bool FOR_SIMULATION_ONLY_TURN_ON_SSH_STUPID_MODE = false;

        public int NumberOfRedundentHostsToCacheIPs = 3;
        public int NumberOfRedundentHostsToCachePasswordPopularity = 3;

        public int NumberOfSuccessesToTrackPerIp = 200;
        public int NumberOfFailuresToTrackPerIp = 200;

        public float MaxEditDistanceConsideredATypo { get; set; } = 2f;
        public double PenaltyMulitiplierForTypo { get; set; } = .1d;
        public double BasePenaltyForInvalidPassword { get; set; } = 1d;
        public double PenaltyForInvalidAccount { get; set; } = 2d; // 2 * BasePenaltyForInvalidPassword;

        public double RewardForCorrectPasswordPerAccount { get; set; } = -30d;

        public double BlockThresholdPopularPassword { get; set; } = 50d;
        public double BlockThresholdMultiplierForUnpopularPasswords { get; set; } = 20d;
        public TimeSpan ExpireFailuresAfter { get; set; } = new TimeSpan(24, 0, 0); // 24 hours

        /// <summary>
        /// Until we've observed enough failure attempts, we use the following denominator for popularity calculations.
        /// This ensures that a small burst of something rare doesn't look popular when we've only had a few observations.
        /// </summary>
        public ulong MinDenominatorForPasswordPopularity { get; set; } = 100*1000; // 1 million

        public double PopularityConfidenceLevel { get; set; } = 0.00001d; // 1 in 100,000

        /// <summary>
        /// This threshold determines whether we consider an account's password to be among the set of 
        /// popularly-guessed passwords.  Those accounts with popular passwords will block out suspicious
        /// IPs at a lower threshold than those with unpopular passwords.
        /// </summary>
        public double ThresholdAtWhichAccountsPasswordIsDeemedPopular { get; set; } = 1d/(100d*1000d); // default 1 in 100,000

        public List<PenaltyForReachingAPopularityThreshold> PenaltyForReachingEachPopularityThreshold { get; set; } = new List<PenaltyForReachingAPopularityThreshold>
        {
            new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10},
            new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20},
            new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25},
            new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30},
        };


    }
}
