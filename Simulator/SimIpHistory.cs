using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace Simulator
{

    public class SimLoginAttemptSummaryForTypoAnalysis
    {
        public DateTime WhenUtc { get; set; }

        public string UsernameOrAccountId { get; set; }

        public bool WasPasswordFrequent { get; set; }

        /// <summary>
        /// When a login attempt is sent with an incorrect password, that incorrect password is encrypted
        /// with the UserAccount's EcPublicAccountLogKey.  That private key to decrypt is encrypted
        /// wiith the phase1 hash of the user's correct password.  If the correct password is provided in the future,
        /// we can go back and audit the incorrect password to see if it was within a short edit distance
        /// of the correct password--which would indicate it was likely a (benign) typo and not a random guess. 
        /// </summary>
        public string Password { get; set; }
    }

    public class SimIpHistory
    {
        public DecayingDouble SuccessfulLogins;

        public DecayingDouble AccountFailuresInfrequentPassword;
        public DecayingDouble AccountFailuresFrequentPassword;
        public DecayingDouble RepeatAccountFailuresInfrequentPassword;
        public DecayingDouble RepeatAccountFailuresFrequentPassword;

        public DecayingDouble PasswordFailuresNoTypoInfrequentPassword;
        public DecayingDouble PasswordFailuresNoTypoFrequentPassword;
        public DecayingDouble PasswordFailuresTypoInfrequentPassword;
        public DecayingDouble PasswordFailuresTypoFrequentPassword;
        public DecayingDouble RepeatPasswordFailuresNoTypoInfrequentPassword;
        public DecayingDouble RepeatPasswordFailuresNoTypoFrequentPassword;
        public DecayingDouble RepeatPasswordFailuresTypoInfrequentPassword;
        public DecayingDouble RepeatPasswordFailuresTypoFrequentPassword;

        public double[] GetAllScores(TimeSpan halfLife, DateTime whenUtc)
        {
            return new double[]
            {
                SuccessfulLogins.GetValue(halfLife, whenUtc),
                AccountFailuresInfrequentPassword.GetValue(halfLife, whenUtc),
                AccountFailuresFrequentPassword.GetValue(halfLife, whenUtc),
                RepeatAccountFailuresInfrequentPassword.GetValue(halfLife, whenUtc),
                RepeatAccountFailuresFrequentPassword.GetValue(halfLife, whenUtc),
                PasswordFailuresNoTypoInfrequentPassword.GetValue(halfLife, whenUtc),
                PasswordFailuresNoTypoFrequentPassword.GetValue(halfLife, whenUtc),
                PasswordFailuresTypoInfrequentPassword.GetValue(halfLife, whenUtc),
                PasswordFailuresTypoFrequentPassword.GetValue(halfLife, whenUtc),
                RepeatPasswordFailuresNoTypoInfrequentPassword.GetValue(halfLife, whenUtc),
                RepeatPasswordFailuresNoTypoFrequentPassword.GetValue(halfLife, whenUtc),
                RepeatPasswordFailuresTypoInfrequentPassword.GetValue(halfLife, whenUtc),
                RepeatPasswordFailuresTypoFrequentPassword.GetValue(halfLife, whenUtc)
            };
        }

        public SmallCapacityConstrainedSet<SimLoginAttemptSummaryForTypoAnalysis> RecentPotentialTypos;

        public SimIpHistory(int numberOfPastLoginsToKeepForTypoAnalysis)
        {
            RecentPotentialTypos =
                new SmallCapacityConstrainedSet<SimLoginAttemptSummaryForTypoAnalysis>(
                    numberOfPastLoginsToKeepForTypoAnalysis);
        }

        //public DecayingDouble AllFailures(TimeSpan halfLife) => AccountFailures.Add(halfLife, PasswordFailures);

        //public DecayingDouble AccountFailuresSubsetWithInfrequentPassword(TimeSpan halfLife) => AccountFailures.Subtract(halfLife, AccountFailuresSubsetWithFrequentPassword);
        //public DecayingDouble PasswordFailuresSubsetWithInfrequentPassword(TimeSpan halfLife) => PasswordFailures.Subtract(halfLife, PasswordFailuresSubsetWithFrequentPassword);
        //public DecayingDouble PasswordFailuresSubsetWithoutTypo(TimeSpan halfLife) => PasswordFailures.Subtract(halfLife, PasswordFailuresSubsetWithTypo);
        //public DecayingDouble PasswordFailuresSubsetWithoutEitherFrequentPasswordOrTypo(TimeSpan halfLife) => PasswordFailures.Subtract(halfLife, PasswordFailuresSubsetWithTypoAndFrequentPassword);

        /// <summary>
        /// This analysis will examine the client IP's previous failed attempts to login to this account
        /// to determine if any failed attempts were due to typos.  
        /// </summary>
        /// <param name="account">The account that the client is currently trying to login to.</param>
        /// <param name="whenUtc"></param>
        /// <param name="correctPassword">The correct password for this account.  (We can only know it because
        /// the client must have provided the correct one this loginAttempt.)</param>
        /// <returns></returns>
        public void AdjustBlockingScoreForPastTyposTreatedAsFullFailures(
            Simulator simulator,
            SimulatedUserAccount account,
            DateTime whenUtc,
            string correctPassword)
        {
            SimLoginAttemptSummaryForTypoAnalysis[] recentPotentialTypos =
                RecentPotentialTypos.MostRecentFirst.ToArray();
            foreach (SimLoginAttemptSummaryForTypoAnalysis potentialTypo in recentPotentialTypos)
            {
                if (account == null || potentialTypo.UsernameOrAccountId != account.UsernameOrAccountId)
                    continue;

                // Use an edit distance calculation to determine if it was a likely typo
                bool likelyTypo =
                    EditDistance.Calculate(potentialTypo.Password, correctPassword) <=
                    simulator._experimentalConfiguration.BlockingOptions.MaxEditDistanceConsideredATypo;

                TimeSpan halfLife = simulator._experimentalConfiguration.BlockingOptions.BlockScoreHalfLife;
                DecayingDouble value = new DecayingDouble(1d, potentialTypo.WhenUtc);
                // Add this to the list of changed attempts
                if (potentialTypo.WasPasswordFrequent)
                {
                    PasswordFailuresNoTypoFrequentPassword.SubtractInPlace(halfLife, value);
                    PasswordFailuresTypoFrequentPassword.AddInPlace(halfLife, value);
                }
                RecentPotentialTypos.Remove(potentialTypo);
            }
        }

    }
}
