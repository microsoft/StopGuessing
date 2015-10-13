using System.Collections.Generic;
using System.Net;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{
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
        public Sequence<LoginAttempt> RecentLoginSuccessesAtMostOnePerAccount;
        const int DefaultNumberOfLoginSuccessesToTrack = 32;

        /// <summary>
        /// Past login failures, ordered from most recent to least recent, with a fixed limit on the history stored.
        /// </summary>
        public Sequence<LoginAttempt> RecentLoginFailures;
        const int DefaultNumberOfLoginFailuresToTrack = 32;


        public IpHistory(//bool isIpAKnownAggregatorThatWeCannotBlock = false,
            IPAddress address,
                                 int numberOfLoginSuccessesToTrack = DefaultNumberOfLoginSuccessesToTrack,
                                 int numberOfLoginFailuresToTrack = DefaultNumberOfLoginFailuresToTrack)
        {
            //this.IsIpAKnownAggregatorThatWeCannotBlock = isIpAKnownAggregatorThatWeCannotBlock;
            Address = address;
            RecentLoginSuccessesAtMostOnePerAccount =
                new Sequence<LoginAttempt>(numberOfLoginSuccessesToTrack);
            RecentLoginFailures =
                new Sequence<LoginAttempt>(numberOfLoginFailuresToTrack);
        }


        public void RecordLoginAttempt(LoginAttempt attempt)
        {
            //Record login attempts
            if (attempt.Outcome == AuthenticationOutcome.CredentialsValid ||
                attempt.Outcome == AuthenticationOutcome.CredentialsValidButBlocked)
            {
                // If there was a prior success from the same account, remove it, as we only need to track
                // successes to counter failures and we only counter failures once per account.
                //Stuart please review this
                lock (RecentLoginSuccessesAtMostOnePerAccount)
                {
                    for (int i = 0; i < RecentLoginSuccessesAtMostOnePerAccount.Count; i++)
                    {
                        if (attempt.UsernameOrAccountId == RecentLoginSuccessesAtMostOnePerAccount[i].UsernameOrAccountId)
                        {
                            // We found a prior success from the same account.  Remove it.
                            RecentLoginSuccessesAtMostOnePerAccount.RemoveAt(i);
                            break;
                        }
                    }

                    RecentLoginSuccessesAtMostOnePerAccount.Add(attempt);
                }
            }
            else
            {
                RecentLoginFailures.Add(attempt);
            }
        }


        /// <summary>
        /// Update LoginAttempts cached for this IP with new outcomes
        /// </summary>
        /// <param name="loginAttempts">Copies of the login attempts that have changed</param>
        /// <returns></returns>
        public int UpdateLoginAttemptsWithNewOutcomes(IEnumerable<LoginAttempt> loginAttempts)
        {
            // Fot the attempts provided in the paramters, create a dictionary mapping the attempt keys to
            // the attempt so that we can look them up quickly when going through the recent failures
            // for this IP.
            Dictionary<string, LoginAttempt> keyToAttempt = new Dictionary<string, LoginAttempt>();
            foreach (LoginAttempt attempt in loginAttempts)
            {
                keyToAttempt[attempt.UniqueKey] = attempt;
            }

            lock(RecentLoginFailures)
            {
                // Now walk through the failures for this IP and, if any match the keys for the
                // accounts in the loginAttempts parameter, change the outcome to match the 
                // one provided.
                int numberOfOutcomesChanged = 0;
                foreach (LoginAttempt attempt in RecentLoginFailures.MostRecentToOldest)
                {
                    string uniqueKey = attempt.UniqueKey;
                    if (keyToAttempt.ContainsKey(uniqueKey))
                    {
                        attempt.Outcome = keyToAttempt[uniqueKey].Outcome;
                        if (++numberOfOutcomesChanged >= keyToAttempt.Count)
                            break;
                    }
                }
                return numberOfOutcomesChanged;
            }
        }


    }
}
