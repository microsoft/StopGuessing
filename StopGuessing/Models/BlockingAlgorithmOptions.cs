#define Simulation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;


namespace StopGuessing.Models
{

    public class ThresholdAndValue
    {
        public double Threshold { get; set; }
        public double Value { get; set; }

        public static double EvaluteAbove(IEnumerable<ThresholdAndValue> thresholdValuePairs, double point,  double defaultResult)
        {
            return thresholdValuePairs.Where(tvp => point >= tvp.Threshold).Select(thresholdValuePair => thresholdValuePair.Value).Concat(new[] {defaultResult}).Max();
        }

        public static double EvaluteBelow(IEnumerable<ThresholdAndValue> thresholdValuePairs, double point, double defaultResult)
        {
            return thresholdValuePairs.Where(tvp => point <= tvp.Threshold).Select(thresholdValuePair => thresholdValuePair.Value).Concat(new[] { defaultResult }).Max();
        }

    }

    public delegate double PasswordPopularityFunction(ILadder binomialLadder, IUpdatableFrequency frequency);

    public class BlockingAlgorithmOptions
    {
        public int NumberOfRedundantHostsToCacheIPs = 1;
        public int NumberOfRedundantHostsToCachePasswordPopularity = 1;
        public int NumberOfRungsInBinomialLadder_K = 48;
        public int NumberOfElementsInBinomialLadderSketch_N = 1 << 29;

        public int NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos = 8;

        public float MaxEditDistanceConsideredATypo { get; set; } = 2f;
        public double PenaltyMulitiplierForTypo { get; set; } = .1d;
        public double PenaltyForInvalidPassword_Beta { get; set; } = 1d;
        public double PenaltyForInvalidAccount_Alpha { get; set; } = 2d; // 2 * PenaltyForInvalidPassword_Beta;

        public double RewardForCorrectPasswordPerAccount_Sigma { get; set; } = 30d;

        public double BlockThresholdPopularPassword_T_base { get; set; } = 50d;

        public TimeSpan BlockScoreHalfLife = new TimeSpan(12,0,0); // 12 hours


        // For tracking popular passwords with a set of frequency trackers
        public  uint LengthOfShortestPopularityMeasurementPeriod = 10 * 1000;
        public  uint FactorOfGrowthBetweenPopularityMeasurementPeriods = 10;
        public  int NumberOfPopularityMeasurementPeriods = 4;

        public string DefaultExpensiveHashingFunction { get; set; } = ExpensiveHashFunctionFactory.DefaultFunctionName;
        public int ExpensiveHashingFunctionIterations { get; set; } = ExpensiveHashFunctionFactory.DefaultNumberOfIterations;

        public double AccountCreditLimit { get; set; } = 50d;
        public TimeSpan AccountCreditLimitHalfLife = new TimeSpan(12,0,0); // 12 hours

        /// <summary>
        /// Until we've observed enough failure attempts, we use the following denominator for popularity calculations.
        /// This ensures that a small burst of something rare doesn't look popular when we've only had a few observations.
        /// </summary>
        //public ulong MinDenominatorForPasswordPopularity { get; set; } = 100*1000; // 1 million

        public double PopularityConfidenceLevel { get; set; } = 0.001d; // 1 in 100,000


        public PasswordPopularityFunction PopularityBasedPenaltyMultiplier_h = (binomialLadder, frequency) =>
        {
            int ladderObservations = binomialLadder.CountObservationsForGivenConfidence(0.001d);
            double popularity = Math.Max((double) ladderObservations/(10d*1000d), frequency.Proportion.AsDouble);
            if (popularity > .01d)
                return 10;
            else if (popularity > .001d)
                return 9;
            else if (popularity > .0001d)
                return 7;
            else if (popularity > .00001d)
                return 5;
            else if (popularity > .000001d)
                return 3;
            return 1;
        };


        public PasswordPopularityFunction PopularityBasedThresholdMultiplier_T_multiplier = (binomialLadder, frequency) =>
        {
            if ((frequency.Proportion.AsDouble) > 0)
            {
                double popularity = frequency.Proportion.AsDouble;
                if (popularity > .0001d)
                    return 1;
                else if (popularity > .00001d)
                    return 2;
                else if (popularity > .000001d)
                    return 3;
                return 5;
            }
            int midpoint = binomialLadder.HeightOfLadderInRungs/2;
            int heightOfKey = binomialLadder.HeightOfKeyInRungs;
            if (heightOfKey <= midpoint)
                return 100;
            int heightAboveMidpoint = heightOfKey - midpoint;
            if (heightAboveMidpoint == 1)
                return 50;
            else if (heightAboveMidpoint == 2)
                return 45;
            else if (heightAboveMidpoint == 3)
                return 40;
            else if (heightAboveMidpoint == 4)
                return 35;
            else if (heightAboveMidpoint == 5)
                return 30;
            else if (heightAboveMidpoint == 6)
                return 26;
            else if (heightAboveMidpoint == 7)
                return 23;
            else if (heightAboveMidpoint == 8)
                return 20;
            else if (heightAboveMidpoint == 9)
                return 17;
            else if (heightAboveMidpoint == 10)
                return 14;
            else if (heightAboveMidpoint == 11)
                return 11;
            else if (heightAboveMidpoint == 12)
                return 11;
            else if (heightAboveMidpoint == 13)
                return 11;
            else if (heightAboveMidpoint == 14)
                return 10;
            else if (heightAboveMidpoint == 15)
                return 9;
            else if (heightAboveMidpoint == 16)
                return 8;
            else if (heightAboveMidpoint == 17)
                return 7.5;
            else if (heightAboveMidpoint == 16)
                return 7;
            return 5;
        };

        //public double ThresholdAtWhichAccountsPasswordIsDeemedPopular { get; set; } = 1d / (100d * 1000d); // default 1 in 100,000
        //public double BlockThresholdMultiplierForUnpopularPasswords { get; set; } = 20d;



#if Simulation
        public SimulationCondition[] Conditions;
#endif

    }
}
