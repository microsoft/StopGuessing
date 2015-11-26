#define Simulation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
#if Simulation
    public class SimulationCondition
    {
        private readonly BlockingAlgorithmOptions _options;
        public string Name;
        public DoubleThatDecaysWithTime Score;
        public CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos;
        public bool IgnoresRepeats;
        public bool RewardsClientCookies;
        public bool CreditsValidLogins;
        public bool UsesAlphaForAccountFailures;
        public bool FixesTypos;
        public bool ProtectsAccountsWithPopularPasswords;
        public bool PunishesPopularGuesses;

        public double GetThresholdAdjustedScore(double popularityOfPassword, bool hasCookieProvingPriorLogin)
        {
            double score = Score;
            if (hasCookieProvingPriorLogin && RewardsClientCookies)
                score = 0;
            else if (ProtectsAccountsWithPopularPasswords && popularityOfPassword > _options.ThresholdAtWhichAccountsPasswordIsDeemedPopular)
                score /= _options.BlockThresholdMultiplierForUnpopularPasswords;
            return score;
        }

        public SimulationCondition(BlockingAlgorithmOptions options, string name, bool ignoresRepeats, bool rewardsClientCookies, bool creditsValidLogins,
            bool usesAlphaForAccountFailures, bool fixesTypos, bool protectsAccountsWithPopularPasswords, bool punishesPopularGuesses)
        {
            _options = options;
               Score = new DoubleThatDecaysWithTime(options.BlockScoreHalfLife);
            RecentPotentialTypos = !FixesTypos ? null:
                new CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(
                    options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
            Name = name;
            IgnoresRepeats = ignoresRepeats;
            RewardsClientCookies = rewardsClientCookies;
            CreditsValidLogins = creditsValidLogins;
            UsesAlphaForAccountFailures = usesAlphaForAccountFailures;
            FixesTypos = fixesTypos;
            PunishesPopularGuesses = punishesPopularGuesses;
            ProtectsAccountsWithPopularPasswords = protectsAccountsWithPopularPasswords;
        }

        public void AdjustScoreForPastTyposTreatedAsFullFailures(
            SimulationCondition condition,
            ref ECDiffieHellmanCng ecPrivateAccountLogKey,
            UserAccount account,
            DateTime whenUtc,
            string correctPassword, 
            byte[] phase1HashOfCorrectPassword)
        {
            if (condition.RecentPotentialTypos == null || condition.FixesTypos == false)
                return;
            LoginAttemptSummaryForTypoAnalysis[] recentPotentialTypos = condition.RecentPotentialTypos.ToArray();
            double credit = 0;
            foreach (LoginAttemptSummaryForTypoAnalysis potentialTypo in recentPotentialTypos)
            {
                if (potentialTypo.UsernameOrAccountId != account.UsernameOrAccountId)
                    continue;

                if (ecPrivateAccountLogKey == null)
                {
                    // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
                    try
                    {
                        ecPrivateAccountLogKey = Encryption.DecryptAesCbcEncryptedEcPrivateKey(
                            account.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                            phase1HashOfCorrectPassword);
                    }
                    catch (Exception)
                    {
                        // There's a problem with the key that prevents us from decrypting it.  We won't be able to do this analysis.                            
                        return;
                    }
                }

                // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
                try
                {
                    // Attempt to decrypt the password.
                    EcEncryptedMessageAesCbcHmacSha256 messageDeserializedFromJson =
                        JsonConvert.DeserializeObject<EcEncryptedMessageAesCbcHmacSha256>(potentialTypo.EncryptedIncorrectPassword);
                    byte[] passwordAsUtf8 = messageDeserializedFromJson.Decrypt(ecPrivateAccountLogKey);
                    string incorrectPasswordFromPreviousAttempt = Encoding.UTF8.GetString(passwordAsUtf8);

                    // Use an edit distance calculation to determine if it was a likely typo
                    bool likelyTypo = EditDistance.Calculate(incorrectPasswordFromPreviousAttempt, correctPassword) <=
                                        _options.MaxEditDistanceConsideredATypo;

                    // Update the outcome based on this information.
                    AuthenticationOutcome newOutocme = likelyTypo
                        ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;

                    // Add this to the list of changed attempts
                    credit += potentialTypo.Penalty.GetValue(whenUtc) * (1d - _options.PenaltyMulitiplierForTypo);

                    // FUTURE -- find and update the login attempt in the background

                }
                catch (Exception)
                {
                    // An exception is likely due to an incorrect key (perhaps outdated).
                    // Since we simply can't do anything with a record we can't Decrypt, we carry on
                    // as if nothing ever happened.  No.  Really.  Nothing to see here.
                }

                condition.RecentPotentialTypos.Remove(potentialTypo);
            }
            Score.Add(-credit, whenUtc);
            return;
        }
    }
#endif

    /// <summary>
    /// This class keeps track of recent login successes and failures for a given client IP so that
    /// we can try to determine if this client should be blocked due to likely-password-guessing
    /// behaviors.
    /// </summary>
    public class IpHistory
    {
        public IPAddress Address;

        /// <summary>
        /// Past login successes, ordered from most recent to least recent, with a fixed limit on the history stored.
        /// </summary>
        //public Sequence<LoginAttempt> RecentLoginSuccessesAtMostOnePerAccount;
        //const int DefaultNumberOfLoginSuccessesToTrack = 32;

        /// <summary>
        /// Past login failures, ordered from most recent to least recent, with a fixed limit on the history stored.
        /// </summary>
        //public Sequence<LoginAttempt> RecentLoginFailures;

        const int DefaultNumberOfPotentailTyposToTrack = 8;
        public CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos; 

        public DoubleThatDecaysWithTime CurrentBlockScore;
#if Simulation
        public List<SimulationCondition> SimulationConditions = new List<SimulationCondition>();
#endif


        public IpHistory(//bool isIpAKnownAggregatorThatWeCannotBlock = false,
            IPAddress address,
            BlockingAlgorithmOptions options)
        {
            Address = address;
            CurrentBlockScore = new DoubleThatDecaysWithTime(options.BlockScoreHalfLife);
#if Simulation
            SimulationConditions.Add(new SimulationCondition(options, "Baseline", false, false, false, false, false, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "NoRepeats", true, false, false, false, false, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "Cookies", true, true, false, false, false, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "Credits", true, true, true, false, false, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "Alpha", true, true, true, true, false, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "Typos", true, true, true, true, true, false, false));
            SimulationConditions.Add(new SimulationCondition(options, "PopularThreshold", true, true, true, true, true, true, false));
            SimulationConditions.Add(new SimulationCondition(options, "PunishPopularGuesses", true, true, true, true, true, true, true));
#endif
            RecentPotentialTypos =
                new CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
        }
        
    }
}
