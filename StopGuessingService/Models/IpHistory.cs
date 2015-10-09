using System.Collections.Generic;
using System.Net;
using StopGuessing.DataStructures;

namespace StopGuessing.Models
{

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
                        if (attempt.Account.Equals(RecentLoginSuccessesAtMostOnePerAccount[i].Account))
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
                lock (RecentLoginFailures)
                {
                    RecentLoginFailures.Add(attempt);
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="loginAttempts"></param>
        /// <returns></returns>
        public int UpdateLoginAttemptsWithNewOutcomes(IEnumerable<LoginAttempt> loginAttempts)
        {
            Dictionary<string, LoginAttempt> keyToAttempt = new Dictionary<string, LoginAttempt>();
            foreach (LoginAttempt attempt in loginAttempts)
            {
                keyToAttempt[attempt.ToUniqueKey()] = attempt;
            }

            lock(RecentLoginFailures)
            {
                int numberOfOutcomesChanged = 0;
                foreach (LoginAttempt attempt in RecentLoginFailures.MostRecentToOldest)
                {
                    string uniqueKey = attempt.ToUniqueKey();
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
