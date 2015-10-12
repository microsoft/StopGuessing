using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// When a password exceeds the threshold of commonality in the BinomialSketchOfFailedPasswords sketch,
        /// we start tracking its hash using this dictionary to get a precise occurrence count for future occurrences.
        /// This filters out the rare false positives so that we don't track their plaintext values
        /// </summary>
        protected Dictionary<string, uint[]> PreciseOccurrencesOfFailedUnsaltedHashedPassword;

        /// <summary>
        /// We track a sedquence of unsalted failed passwords so that we can determine their pouplarity
        /// within different historical frequencies.  We need this sequence because to know how often
        /// a password occurred among the past n failed passwords, we need to add a count each time we
        /// see it and remove the count when n new failed passwords have been recorded. 
        /// </summary>
        protected Sequence<string> SequenceOfFailedUnsaltedHashedPassword;

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
        public AgingMembershipSketch SketchForTestingIfFailedAccountPasswordPairHasBeenSeenBefore;

        public double FailedPasswordsRecordedSoFar;

        readonly double _minCountRequiredToTrackPreciseOccurrences;
        readonly double _minPercentRequiredToTrackPreciseOccurrences;

        const int DefaultMinCountRequiredToTrackPreciseOccurrences = 20;
        const double DefaultMinPercentRequiredToTrackPreciseOccurrences = 1d/1000000;

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
        public uint[] LengthOfHistoricalPeriods;


        public PasswordPopularityTracker(
            string keyToPreventAlgorithmicComplexityAttacks,
            long estimatedNumberOfAccounts = DefaultEstimatedNumberOfAccounts,
            int thresholdRequiredToTrackPreciseOccurrences = DefaultMinCountRequiredToTrackPreciseOccurrences,
            double minPercentRequiredToTrackPreciseOccurrences = DefaultMinPercentRequiredToTrackPreciseOccurrences,
            uint thresholdRequiredToStorePlaintext = DefaultMinCountRequiredToStorePlaintext,
            double minPercentRequiredToStorePlaintext = DefaultMinPercentRequiredToStorePlaintext)
        {
            int numberOfHistoricalPeriods = DefaultNumberOfHistoricalPeriods;
            uint factorOfGrowthBetweenHistoricalPeriods = DefaultFactorOfGrowthBetweenHistoricalPeriods;

            LengthOfHistoricalPeriods = new uint[numberOfHistoricalPeriods];
            
            uint currentPeriodLength = DefaultLengthOfShortestHistoricalPeriod;
            for (int period = 0; period < DefaultNumberOfHistoricalPeriods; period++)
            {
                LengthOfHistoricalPeriods[period] = currentPeriodLength;
                currentPeriodLength *= factorOfGrowthBetweenHistoricalPeriods;
            }
            long conservativelyHighEstimateOfRowsNeeded = 4 * estimatedNumberOfAccounts / LowEndEstimateOfLoginsBetweenBenignUsersEnteringWrongPasswordRepeatedly;
            _minCountRequiredToTrackPreciseOccurrences = thresholdRequiredToTrackPreciseOccurrences;
            _minPercentRequiredToStorePlaintext = minPercentRequiredToStorePlaintext;
            _minCountRequiredToStorePlaintext = thresholdRequiredToStorePlaintext;
            _minPercentRequiredToTrackPreciseOccurrences = minPercentRequiredToTrackPreciseOccurrences;
            FailedPasswordsRecordedSoFar = 0d;


            long numberOfColumns = DefaultNumberOfSketchColumns;
            long numberOfRows = 2 * ((long)(1d / minPercentRequiredToTrackPreciseOccurrences));
            int bitsPerElement = 5;

            SketchForTestingIfFailedAccountPasswordPairHasBeenSeenBefore =
                new AgingMembershipSketch(DefaultNumberOfSketchColumns, conservativelyHighEstimateOfRowsNeeded);
            BinomialSketchOfFailedPasswords = new BinomialSketch(1024*1024*1024, 64, keyToPreventAlgorithmicComplexityAttacks);
                new AgingSketch(numberOfColumns, numberOfRows, bitsPerElement);
                new Dictionary<string, uint[]>();
            PreciseOccurrencesOfFailedUnsaltedHashedPassword =
                new Dictionary<string, uint[]>();
            SequenceOfFailedUnsaltedHashedPassword =
                new Sequence<string>((int) LengthOfHistoricalPeriods.Last());
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
        public bool HasFailedIpAccountPasswordTripleBeenSeenBefore(System.Net.IPAddress clientIpAddress, string account, string password)
        {
            string accountPasswordPairAsString = UrlEncoder.Default.UrlEncode(clientIpAddress.ToString()) + "&" +
                                                 UrlEncoder.Default.UrlEncode(account) + "&" +
                                                 UrlEncoder.Default.UrlEncode(password);
            return SketchForTestingIfFailedAccountPasswordPairHasBeenSeenBefore.AddMember(accountPasswordPairAsString);
        }

        public Proportion GetPopularityOfPasswordAmongFailures(string password, bool wasPasswordCorrect)
        {
            Proportion approximatePopularity;
            Proportion greatestDictionaryPopularity = new Proportion(0, ulong.MaxValue);
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
            approximatePopularity = new Proportion(
                (ulong)BinomialSketchOfFailedPasswords.CountObservationsForGivenConfidence(sketchBitsSet,0.001), // FIXME parameterize?
                (ulong) BinomialSketchOfFailedPasswords.NumberOfObservationsAccountingForAging);

            string passwordHash = Convert.ToBase64String(SimplePasswordHash(password));
            // FUTURE -- always allow to enter loop if PreciseOccurrencesOfFailedUnsaltedHashedPassword.ContainsKey(passwordHash) since
            // the aging sketch might have lost this?
            if (PreciseOccurrencesOfFailedUnsaltedHashedPassword.ContainsKey(passwordHash) ||
                BinomialSketchOfFailedPasswords.CountObservationsForGivenConfidence(sketchBitsSet, 0.000001d) >
                _minCountRequiredToTrackPreciseOccurrences)
            {
                lock (PreciseOccurrencesOfFailedUnsaltedHashedPassword)
                {
                    if (!PreciseOccurrencesOfFailedUnsaltedHashedPassword.ContainsKey(passwordHash))
                        PreciseOccurrencesOfFailedUnsaltedHashedPassword[passwordHash] =
                            new uint[LengthOfHistoricalPeriods.Length];

                    for (int period = 0; period < LengthOfHistoricalPeriods.Length; period++)
                    {
                        // Increment the counter for this period
                        uint countBeforeIncrement =
                            PreciseOccurrencesOfFailedUnsaltedHashedPassword[passwordHash][period];
                        if (!wasPasswordCorrect)
                        {
                            PreciseOccurrencesOfFailedUnsaltedHashedPassword[passwordHash][period]++;
                        }
                        // Track the pouplarity of the password hash as a fraction of the number of times the hash has
                        // occurred during each period.  Keep the poularity for the period with the highest popularity.
                        ulong lengthOfThisPeriod = LengthOfHistoricalPeriods[period];
                        Proportion dictionaryPopularityForThisPeriod = new Proportion(countBeforeIncrement,
                            lengthOfThisPeriod);
                        if (dictionaryPopularityForThisPeriod.AsDouble > greatestDictionaryPopularity.AsDouble)
                            greatestDictionaryPopularity = dictionaryPopularityForThisPeriod;
                    }

                    if (!wasPasswordCorrect)
                    {
                        // Decrement the counters for hashes that fall off the ends of each historical period
                        for (int period = 0; period < LengthOfHistoricalPeriods.Length; period++)
                        {
                            uint periodLength = LengthOfHistoricalPeriods[period];
                            // We only need to pull a hash count out of the sequence the sequence is long
                            // enough to contain the end of the period, and the hash at that point in the sequence
                            // is non-null.
                            if (SequenceOfFailedUnsaltedHashedPassword.Count >= periodLength)
                            {
                                string hashAtEndOfPeriod = SequenceOfFailedUnsaltedHashedPassword[(int) periodLength];
                                if (hashAtEndOfPeriod != null)
                                {
                                    if (PreciseOccurrencesOfFailedUnsaltedHashedPassword.ContainsKey(hashAtEndOfPeriod))
                                    {
                                        uint countsRemaining =
                                            --
                                                PreciseOccurrencesOfFailedUnsaltedHashedPassword[hashAtEndOfPeriod][
                                                    period];
                                        if (countsRemaining <= 0 && period == LengthOfHistoricalPeriods.Length - 1)
                                        {
                                            // There are no longer any occurrences of this hash in the entire sequence,
                                            // wo can stop tracking the occurrence counts.
                                            PreciseOccurrencesOfFailedUnsaltedHashedPassword.Remove(passwordHash);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Add this most recent hash to the sequence so that we can track when it falls
                // outside of a period boundary.
                SequenceOfFailedUnsaltedHashedPassword.Add(passwordHash);

                if (greatestDictionaryPopularity.Numerator >= _minCountRequiredToStorePlaintext &&
                    greatestDictionaryPopularity.AsDouble >= _minPercentRequiredToStorePlaintext)
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

            approximatePopularity = approximatePopularity.MinDenominator(greatestDictionaryPopularity.Denominator);
            return approximatePopularity.AsDouble > greatestDictionaryPopularity.AsDouble
                ? approximatePopularity
                : greatestDictionaryPopularity;
        }
    }


}
