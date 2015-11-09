using System;
using System.Collections.Generic;
using Microsoft.Framework.WebEncoders;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{

    public class PasswordPopularityTracker
    {
        /// <summary>
        /// A sketch that estimates the number of times a failed password has been observed, while minimizing
        /// privacy risk since all estimates are probabilistic and any password has a small chance of
        /// appearing to have been observed.
        /// </summary>
        protected BinomialSketch BinomialSketchOfFailedPasswords;

        ///// <summary>
        ///// When a password exceeds the threshold of commonality in the BinomialSketchOfFailedPasswords sketch,
        ///// we start tracking its hash using this dictionary to get a precise occurrence count for future occurrences.
        ///// This filters out the rare false positives so that we don't track their plaintext values
        ///// </summary>
        //protected Dictionary<string, uint[]> PreciseOccurrencesOfFailedUnsaltedHashedPassword;

        ///// <summary>
        ///// We track a sedquence of unsalted failed passwords so that we can determine their pouplarity
        ///// within different historical frequencies.  We need this sequence because to know how often
        ///// a password occurred among the past n failed passwords, we need to add a count each time we
        ///// see it and remove the count when n new failed passwords have been recorded. 
        ///// </summary>
        //protected Sequence<string> SequenceOfFailedUnsaltedHashedPassword;
        protected List<FrequencyTracker<string>> PasswordFrequencyEstimatesForDifferentPeriods; 

        /// <summary>
        /// When an unsalted password hash is observed to exceed the threshold of commonality for the
        /// PreciseOccurrencesOfFailedUnsaltedHashedPassword, we record its plaintext value.
        /// The reason this is safe is because so many attackers are guessing it that we can conclude
        /// it is a known-comon password.  We're not learning anything about the distribution of actual
        /// passwords--just the distribution of what attackers guess in hopes that they are approximating
        /// the distribution of actual passwords.
        /// </summary>
        protected Dictionary<string, string> MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords;

        /// <summary>
        /// Tracks whether we've seen a failed account/password pair before so that we can determine
        /// whether to disregard this failure as the result of a system acting on behalf of a benign
        /// user who has an outdated password or as a benign user who is convinced their password is
        /// something that it isn't.  (An attacker doesn't benefit from guessing the same wrong password
        /// a second time, so there's no reason to penalize anyone for doing so.)
        /// </summary>
        public AgingMembershipSketch SketchForTestingIfNonexistentAccountIpPasswordHasBeenSeenBefore;

        public double FailedPasswordsRecordedSoFar;

        readonly double _minCountRequiredToTrackPreciseOccurrences;
//        readonly double _minPercentRequiredToTrackPreciseOccurrences;

        const int DefaultMinCountRequiredToTrackPreciseOccurrences = 20;
//        const double DefaultMinPercentRequiredToTrackPreciseOccurrences = 1d/1000000;

        readonly uint _minCountRequiredToStorePlaintext;
        const uint DefaultMinCountRequiredToStorePlaintext = 100;
        readonly double _minPercentRequiredToStorePlaintext;
        const double DefaultMinPercentRequiredToStorePlaintext = 1d / 100000;

        

        private static byte[] SimplePasswordHash(string password)
        {
            return System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        }


        protected const long DefaultNumberOfSketchColumns = 16;
        public const long DefaultEstimatedNumberOfAccounts = 100 * 1000 * 1000; // One hundred million
        public const long LowEndEstimateOfLoginsBetweenBenignUsersEnteringWrongPasswordRepeatedly = 20;

        public const uint DefaultLengthOfShortestHistoricalPeriod = 10 * 1000;
        public const uint DefaultFactorOfGrowthBetweenHistoricalPeriods = 10;
        public const int DefaultNumberOfHistoricalPeriods = 4;

        public const int HeightOfBinomialLadder = 96;
        public const int SizeOfBinomialLadder = 1024*1024*1024;
        public const int HeightOfLadderDeemedPopular = 79;

        public uint[] LengthOfHistoricalPeriods;


        public PasswordPopularityTracker(
            string keyToPreventAlgorithmicComplexityAttacks,
            long estimatedNumberOfAccounts = DefaultEstimatedNumberOfAccounts,
            int thresholdRequiredToTrackPreciseOccurrences = DefaultMinCountRequiredToTrackPreciseOccurrences,
            uint thresholdRequiredToStorePlaintext = DefaultMinCountRequiredToStorePlaintext,
            double minPercentRequiredToStorePlaintext = DefaultMinPercentRequiredToStorePlaintext)
        {
            int numberOfHistoricalPeriods = DefaultNumberOfHistoricalPeriods;
            uint factorOfGrowthBetweenHistoricalPeriods = DefaultFactorOfGrowthBetweenHistoricalPeriods;

            LengthOfHistoricalPeriods = new uint[numberOfHistoricalPeriods];

            PasswordFrequencyEstimatesForDifferentPeriods = new List<FrequencyTracker<string>>(DefaultNumberOfHistoricalPeriods);
            uint currentPeriodLength = DefaultLengthOfShortestHistoricalPeriod;
            for (int period = 0; period < DefaultNumberOfHistoricalPeriods; period++)
            {
                LengthOfHistoricalPeriods[period] = currentPeriodLength;
                PasswordFrequencyEstimatesForDifferentPeriods.Add(
                    new FrequencyTracker<string>((int) currentPeriodLength));
                currentPeriodLength *= factorOfGrowthBetweenHistoricalPeriods;
            }
            // Reverese the frequency trackers so that the one that tracks the most items is first on the list.
            PasswordFrequencyEstimatesForDifferentPeriods.Reverse();

            long conservativelyHighEstimateOfRowsNeeded = 4 * estimatedNumberOfAccounts / LowEndEstimateOfLoginsBetweenBenignUsersEnteringWrongPasswordRepeatedly;
            _minCountRequiredToTrackPreciseOccurrences = thresholdRequiredToTrackPreciseOccurrences;
            _minPercentRequiredToStorePlaintext = minPercentRequiredToStorePlaintext;
            _minCountRequiredToStorePlaintext = thresholdRequiredToStorePlaintext;
//            _minPercentRequiredToTrackPreciseOccurrences = minPercentRequiredToTrackPreciseOccurrences;
            FailedPasswordsRecordedSoFar = 0d;
            

            SketchForTestingIfNonexistentAccountIpPasswordHasBeenSeenBefore =
                new AgingMembershipSketch(DefaultNumberOfSketchColumns, conservativelyHighEstimateOfRowsNeeded);
            BinomialSketchOfFailedPasswords = new BinomialSketch(SizeOfBinomialLadder, HeightOfBinomialLadder, keyToPreventAlgorithmicComplexityAttacks); // FIXME configuration parameters

            MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords =
                new Dictionary<string, string>();
        }


        /// <summary>
        /// Test to see if an incorrect IP/account/password triple has been seen before.
        /// </summary>
        /// <param name="clientIpAddress">The IP address of the client that issued the request</param>
        /// <param name="account">The unique string identifier for the account.</param>
        /// <param name="password">The plaintext account password.</param>
        /// <returns>True if the pair has been seen before.</returns>
        public bool HasNonexistentAccountIpPasswordTripleBeenSeenBefore(System.Net.IPAddress clientIpAddress, string account, string password)
        {
            string ipNonexistentAccountPasswordTripleAsString = 
                UrlEncoder.Default.UrlEncode(clientIpAddress.ToString()) + "&" +
                UrlEncoder.Default.UrlEncode(account) + "&" +
               UrlEncoder.Default.UrlEncode(password);
            // FIXME - run through expensive hash function, or use binomial bloom filter with high false positive rate?
            // The latter seems attractive as of 2015/10/29
            return SketchForTestingIfNonexistentAccountIpPasswordHasBeenSeenBefore.AddMember(ipNonexistentAccountPasswordTripleAsString);
        }

        /// <summary>
        /// Estimate the popularity of a password among past incorrect passwords.
        /// This will also record the observation of the incorrect password if wasPasswordCorrect is false
        /// </summary>
        /// <param name="password">The password to estimate the popularity of.</param>
        /// <param name="wasPasswordCorrect">True if the passowrd provided was the correct password for this account</param>
        /// <param name="confidenceLevel">The confidence level required for the minimum threshold of popularity.</param>
        /// <param name="minDenominatorForPasswordPopularity">When there are few samples observations, use this minimum denomoninator
        /// to prevent something appearing popular just becausae we haven't seen much else yet.</param>
        /// <returns></returns>
        public Proportion GetPopularityOfPasswordAmongFailures(string password,
            bool wasPasswordCorrect, 
            double confidenceLevel = 0.001,
            ulong minDenominatorForPasswordPopularity = 10000)
        {
            int sketchBitsSet;

            if (!wasPasswordCorrect)
            {
                FailedPasswordsRecordedSoFar += 1d;
                sketchBitsSet = BinomialSketchOfFailedPasswords.Observe(password);
            }
            else
            {
                sketchBitsSet = BinomialSketchOfFailedPasswords.GetNumberOfIndexesSet(password);
            }
            Proportion highestPopularity = new Proportion(
                (ulong)BinomialSketchOfFailedPasswords.CountObservationsForGivenConfidence(sketchBitsSet, confidenceLevel),
                Math.Min((ulong) BinomialSketchOfFailedPasswords.NumberOfObservationsAccountingForAging, 
                                 minDenominatorForPasswordPopularity));
            
            string passwordHash = Convert.ToBase64String(SimplePasswordHash(password));

            // If we're already tracking this unsalted hash, or we've seen enough observations
            // in the binomial sketch that we're confident it's common enough to to be a very
            // good secret, we'll continue to track the unsalted hash
            if (PasswordFrequencyEstimatesForDifferentPeriods[0].Get(passwordHash).Numerator > 0 ||
                sketchBitsSet >= HeightOfLadderDeemedPopular)
                // FIXME BinomialSketchOfFailedPasswords.CountObservationsForGivenConfidence(sketchBitsSet, 0.000001d) >
                //_minCountRequiredToTrackPreciseOccurrences)
            {
                foreach (var passwordTrackerForThisPeriod in PasswordFrequencyEstimatesForDifferentPeriods)
                {
                    Proportion popularityForThisPeriod;
                    lock (passwordTrackerForThisPeriod)
                    {
                        popularityForThisPeriod = wasPasswordCorrect ? 
                            passwordTrackerForThisPeriod.Get(passwordHash) :
                            passwordTrackerForThisPeriod.Observe(passwordHash);
                    }
                    popularityForThisPeriod = popularityForThisPeriod.MinDenominator(minDenominatorForPasswordPopularity);
                    if (popularityForThisPeriod.AsDouble > highestPopularity.AsDouble)
                        highestPopularity = popularityForThisPeriod;
                }

                if (highestPopularity.Numerator >= _minCountRequiredToStorePlaintext &&
                    highestPopularity.AsDouble >= _minPercentRequiredToStorePlaintext)
                {
                    lock (MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords)
                    {
                        if (!MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords.ContainsKey(passwordHash))
                        {
                            MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords[passwordHash] = password;
                        }
                    }
                }
            }
            
            return highestPopularity;
        }
    }


}
