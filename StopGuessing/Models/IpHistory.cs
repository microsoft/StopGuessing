#define Simulation
using System;
using System.Collections.Generic;
using System.Net;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{
#if Simulation
    public class SimulationCondition
    {
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

        public SimulationCondition(BlockingAlgorithmOptions options, string name, bool ignoresRepeats, bool rewardsClientCookies, bool creditsValidLogins,
            bool usesAlphaForAccountFailures, bool fixesTypos, bool protectsAccountsWithPopularPasswords, bool punishesPopularGuesses)
        {
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
        List<SimulationCondition> simScores = new List<SimulationCondition>();
#endif


        public IpHistory(//bool isIpAKnownAggregatorThatWeCannotBlock = false,
            IPAddress address,
            BlockingAlgorithmOptions options)
        {
            Address = address;
            CurrentBlockScore = new DoubleThatDecaysWithTime(options.BlockScoreHalfLife);
#if Simulation
            simScores.Add(new SimulationCondition(options, "Baseline", false, false, false, false, false, false, false));
            simScores.Add(new SimulationCondition(options, "NoRepeats", true, false, false, false, false, false, false));
            simScores.Add(new SimulationCondition(options, "Cookies", true, true, false, false, false, false, false));
            simScores.Add(new SimulationCondition(options, "Credits", true, true, true, false, false, false, false));
            simScores.Add(new SimulationCondition(options, "Alpha", true, true, true, true, false, false, false));
            simScores.Add(new SimulationCondition(options, "Typos", true, true, true, true, true, false, false));
            simScores.Add(new SimulationCondition(options, "PopularThreshold", true, true, true, true, true, true, false));
            simScores.Add(new SimulationCondition(options, "PunishPopularGuesses", true, true, true, true, true, true, true));
#endif
            RecentPotentialTypos =
                new CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis>(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos);
        }


        //public void RecordLoginAttempt(LoginAttempt attempt, double penalty)
        //{
        //    //Record login attempts
        //    if (attempt.Outcome == AuthenticationOutcome.CredentialsValid ||
        //        attempt.Outcome == AuthenticationOutcome.CredentialsValidButBlocked)
        //    {
        //    }
        //    else
        //    {
        //        RecentPotentialTypos.Add(new LoginAttemptSummaryForTypoAnalysis()
        //        {
        //            UsernameOrAccountId = attempt.UsernameOrAccountId,
        //            Penalty = penalty,
        //            TimeOfAttemptUtc = attempt.TimeOfAttemptUtc.UtcDateTime,
        //            EncryptedIncorrectPassword = attempt.EncryptedIncorrectPassword
        //        });
        //    }
        //}


        ///// <summary>
        ///// Update LoginAttempts cached for this IP with new outcomes
        ///// </summary>
        ///// <param name="changedLoginAttempts">Copies of the login attempts that have changed</param>
        ///// <returns></returns>
        //public int UpdateLoginAttemptsWithNewOutcomes(IEnumerable<LoginAttempt> changedLoginAttempts)
        //{
        //    // Fot the attempts provided in the paramters, create a dictionary mapping the attempt keys to
        //    // the attempt so that we can look them up quickly when going through the recent failures
        //    // for this IP.
        //    Dictionary<string, LoginAttempt> keyToChangedAttempts = new Dictionary<string, LoginAttempt>();
        //    foreach (LoginAttempt attempt in changedLoginAttempts)
        //    {
        //        keyToChangedAttempts[attempt.UniqueKey] = attempt;
        //    }

        //    // Now walk through the failures for this IP and, if any match the keys for the
        //    // accounts in the changedLoginAttempts parameter, change the outcome to match the 
        //    // one provided.
        //    List<LoginAttempt> attemptsThatNeedToBeUpdated =
        //        RecentLoginFailures.MostRecentToOldest.Where(
        //            attempt => keyToChangedAttempts.ContainsKey(attempt.UniqueKey)).ToList();
        //    foreach (LoginAttempt attemptThatNeedsToBeUpdated in attemptsThatNeedToBeUpdated)
        //    {
        //        LoginAttempt changedAttempt = keyToChangedAttempts[attemptThatNeedsToBeUpdated.UniqueKey];
        //        // This is where we update the attempt that needs to be updated with the outcome in the attempt that's already
        //        // been changed to include the latest outcome.
        //        attemptThatNeedsToBeUpdated.Outcome = changedAttempt.Outcome;
        //    }
        //    return attemptsThatNeedToBeUpdated.Count;
        //}


    }
}
