using System;
using System.Linq;
using System.Security.Cryptography;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{
    public class SimulationConditionIpHistoryState
    {
        public SimulationCondition Condition;
        public DoubleThatDecaysWithTime Score;
        public SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos;

        public double GetThresholdAdjustedScore(double popularityOfPassword, bool hasCookieProvingPriorLogin,
            ILadder ladder, IUpdatableFrequency frequency, DateTime timeOfAttemptUtc)
        {
            double score = Score.GetValue(timeOfAttemptUtc);
            if (hasCookieProvingPriorLogin && Condition.RewardsClientCookies)
                score = 0;
            else if (Condition.ProtectsAccountsWithPopularPasswords)
                score /= Condition.Options.PopularityBasedThresholdMultiplier_T_multiplier(ladder, frequency);
            if (double.IsNaN(score))
            {
                //FIXME
                Console.Error.WriteLine("here");
            }
            return score;
        }

        public SimulationConditionIpHistoryState(SimulationCondition condition, DateTime? currentDateTimeUtc)
        {
            Condition = condition;
            Score = new DoubleThatDecaysWithTime(Condition.Options.BlockScoreHalfLife, 0, currentDateTimeUtc);
            RecentPotentialTypos = !Condition.FixesTypos ? null :
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
                // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
                try
                {
                    string incorrectPasswordFromPreviousAttempt = potentialTypo.EncryptedIncorrectPassword.Read(ecPrivateAccountLogKey);
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
}
