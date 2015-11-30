#define Simulation
using System;
using System.Collections.Generic;
using StopGuessing.EncryptionPrimitives;


namespace StopGuessing.Models
{


    public class PenaltyForReachingAPopularityThreshold
    {
        public double PopularityThreshold { get; set; }
        public double Penalty { get; set; }
    }

    public class BlockingAlgorithmOptions
    {
        public int NumberOfRedundantHostsToCacheIPs = 1;
        public int NumberOfRedundantHostsToCachePasswordPopularity = 1;
        public int NumberOfRungsInBinomialLadder = 96;
        
        public int NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos = 8;

        public float MaxEditDistanceConsideredATypo { get; set; } = 2f;
        public double PenaltyMulitiplierForTypo { get; set; } = .1d;
        public double PenaltyForInvalidPassword_Beta { get; set; } = 1d;
        public double PenaltyForInvalidAccount_Alpha { get; set; } = 2d; // 2 * PenaltyForInvalidPassword_Beta;

        public double RewardForCorrectPasswordPerAccount_Gamma { get; set; } = 30d;

        public double BlockThresholdPopularPassword { get; set; } = 50d;
        public double BlockThresholdMultiplierForUnpopularPasswords { get; set; } = 20d;

        public TimeSpan BlockScoreHalfLife = new TimeSpan(12,0,0); // 12 hours


        // For tracking popular passwords with a set of frequency trackers
        public  uint LengthOfShortestPopularityMeasurementPeriod = 10 * 1000;
        public  uint FactorOfGrowthBetweenPopularityMeasurementPeriods = 10;
        public  int NumberOfPopularityMeasurementPeriods = 4;

        public string DefaultExpensiveHashingFunction = ExpensiveHashFunctionFactory.DefaultFunctionName;
        public int DefaultExpensiveHashingFunctionIterations = ExpensiveHashFunctionFactory.DefaultNumberOfIterations;

        public double AccountCreditLimit { get; set; } = 50d;
        public TimeSpan AccountCreditLimitHalfLife = new TimeSpan(12,0,0); // 12 hours

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

#if Simulation
        public SimulationCondition[] Conditions;
#endif

    }
}
