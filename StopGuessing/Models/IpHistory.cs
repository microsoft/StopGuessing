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
        public BlockingAlgorithmOptions Options;
        public bool IgnoresRepeats;
        public bool RewardsClientCookies;
        public bool CreditsValidLogins;
        public bool UsesAlphaForAccountFailures;
        public bool FixesTypos;
        public bool ProtectsAccountsWithPopularPasswords;
        public bool PunishesPopularGuesses;
        public string Name;
        public int Index;

        public SimulationCondition(BlockingAlgorithmOptions options, int index, string name, bool ignoresRepeats, bool rewardsClientCookies, bool creditsValidLogins,
    bool usesAlphaForAccountFailures, bool fixesTypos, bool protectsAccountsWithPopularPasswords, bool punishesPopularGuesses)
        {
            Options = options;
            //Score = new DoubleThatDecaysWithTime(options.BlockScoreHalfLife);
            //RecentPotentialTypos = !FixesTypos ? null :
            //    new CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(
            //        options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
            Index = index;
            Name = name;
            IgnoresRepeats = ignoresRepeats;
            RewardsClientCookies = rewardsClientCookies;
            CreditsValidLogins = creditsValidLogins;
            UsesAlphaForAccountFailures = usesAlphaForAccountFailures;
            FixesTypos = fixesTypos;
            PunishesPopularGuesses = punishesPopularGuesses;
            ProtectsAccountsWithPopularPasswords = protectsAccountsWithPopularPasswords;
        }
    }


    public class SimulationConditionData
    {
        public SimulationCondition Condition;
        public DoubleThatDecaysWithTime Score;
        public SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos;

        public double GetThresholdAdjustedScore(double popularityOfPassword, bool hasCookieProvingPriorLogin)
        {
            double score = Score;
            if (hasCookieProvingPriorLogin && Condition.RewardsClientCookies)
                score = 0;
            else if (Condition.ProtectsAccountsWithPopularPasswords && popularityOfPassword > Condition.Options.ThresholdAtWhichAccountsPasswordIsDeemedPopular)
                score /= Condition.Options.BlockThresholdMultiplierForUnpopularPasswords;
            return score;
        }

        public SimulationConditionData(SimulationCondition condition)
        {
            Condition = condition;   
            Score = new DoubleThatDecaysWithTime(Condition.Options.BlockScoreHalfLife);
            RecentPotentialTypos = !Condition.FixesTypos ? null:
                new SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(
                    Condition.Options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);

        }

        public void AdjustScoreForPastTyposTreatedAsFullFailures(
            ref ECDiffieHellmanCng ecPrivateAccountLogKey,
            UserAccount account,
            DateTime whenUtc,
            string correctPassword, 
            byte[] phase1HashOfCorrectPassword)
        {
            if (RecentPotentialTypos == null || Condition.FixesTypos == false)
                return;
            LoginAttemptSummaryForTypoAnalysis[] recentPotentialTypos = RecentPotentialTypos.LeastRecentFirst.ToArray();
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
                                        Condition.Options.MaxEditDistanceConsideredATypo;

                    // Update the outcome based on this information.
                    AuthenticationOutcome newOutocme = likelyTypo
                        ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;

                    // Add this to the list of changed attempts
                    credit += potentialTypo.Penalty.GetValue(whenUtc) * (1d - Condition.Options.PenaltyMulitiplierForTypo);

                    // FUTURE -- find and update the login attempt in the background

                }
                catch (Exception)
                {
                    // An exception is likely due to an incorrect key (perhaps outdated).
                    // Since we simply can't do anything with a record we can't Decrypt, we carry on
                    // as if nothing ever happened.  No.  Really.  Nothing to see here.
                }

                RecentPotentialTypos.Remove(potentialTypo);
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
        public SimulationConditionData[] SimulationConditions;
#endif


        public IpHistory(//bool isIpAKnownAggregatorThatWeCannotBlock = false,
            IPAddress address,
            BlockingAlgorithmOptions options)
        {
            Address = address;
            CurrentBlockScore = new DoubleThatDecaysWithTime(options.BlockScoreHalfLife);
#if Simulation
            SimulationConditions = new SimulationConditionData[options.Conditions.Length];
            for (int i=0; i < SimulationConditions.Length; i++)
                SimulationConditions[i] = new SimulationConditionData(options.Conditions[i]);
#endif
            RecentPotentialTypos =
                new CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
        }
        
    }
}
