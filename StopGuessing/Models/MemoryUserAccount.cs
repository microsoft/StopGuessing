using System;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public class MemoryUserAccount : UserAccount
    {
        /// <summary>
        /// A recency set of the device cookies (hashed via SHA256 and converted to Base64)
        /// that have successfully logged into this account.
        /// </summary>
        protected SmallCapacityConstrainedSet<string> HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount { get; set; }

        ///// <summary>
        ///// A length-limited sequence of records describing failed login attempts (invalid passwords) 
        ///// </summary>
        protected SmallCapacityConstrainedSet<string> RecentIncorrectPhase2Hashes { get; set; }

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        protected DoubleThatDecaysWithTime ConsumedCredits { get; set; }



        public override bool AddIncorrectPhase2Hash(string phase2Hash) => RecentIncorrectPhase2Hashes.Add(phase2Hash);

        public override bool HasClientWithThisHashedCookieSuccessfullyLoggedInBefore(string hashOfCookie) =>
                HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Contains(hashOfCookie);

        public override void RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(string hashOfCookie)
        {
            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Add(hashOfCookie);
        }

        public override double GetCreditsConsumed(DateTime asOfTimeUtc) => ConsumedCredits.GetValue(asOfTimeUtc);

        public override void ConsumeCredit(double amountConsumed, DateTime timeOfConsumptionUtc)
        {
            ConsumedCredits.Add(amountConsumed, timeOfConsumptionUtc);
        }
        /// <summary>
        /// Create a UserAccount record to match a given username or account id.
        /// </summary>
        /// <param name="usernameOrAccountId">A unique identifier for this account, such as a username, email address, or data index for the account record.</param>
        /// <param name="creditLimit"></param>
        /// <param name="creditHalfLife"></param>
        /// <param name="password">The password for the account.  If null or not provided, no password is set.</param>
        /// <param name="maxNumberOfCookiesToTrack">This class tracks cookies associated with browsers that have 
        /// successfully logged into this account.  This parameter, if set, overrides the default maximum number of such cookies to track.</param>
        /// <param name="maxFailedPhase2HashesToTrack">Phase2hashes of recent failed passwords so that we can avoid counting
        /// repeat failures with the same incorrect password against a client.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations to use when hashing the password.</param>
        /// <param name="saltUniqueToThisAccount">The salt for this account.  If null or not provided, a random salt is generated with length determined
        /// by parameter <paramref name="saltLength"/>.</param>
        /// <param name="currentDateTimeUtc">The current UTC time on the instant this record has been created</param>
        /// <param name="phase1HashFunctionName">A hash function that is expensive enough to calculate to make offline dictionary attacks 
        /// expensive, but not so expensive as to slow the authentication system to a halt.  If not specified, a default function will be
        /// used.</param>
        /// <param name="saltLength">If <paramref name="saltUniqueToThisAccount"/>is not specified or null, the constructor will create
        /// a random salt of this length.  If this length is not specified, a default will be used.</param>
        public void Initialize(
            string usernameOrAccountId,
#if Simulation
            int numberOfConditions,
#endif
            string password = null,
            double creditLimit = DefaultCreditLimit,
            TimeSpan? creditHalfLife = null,
            string phase1HashFunctionName = null,
            int numberOfIterationsToUseForPhase1Hash = 0,
            byte[] saltUniqueToThisAccount = null,
            DateTime? currentDateTimeUtc = null,
            int maxNumberOfCookiesToTrack = DefaultMaxNumberOfCookiesToTrack,
            int maxFailedPhase2HashesToTrack = DefaultMaxFailedPhase2HashesToTrack,
            int saltLength = DefaultSaltLength)
        {
            base.Initialize(usernameOrAccountId, password, creditLimit,creditHalfLife, phase1HashFunctionName,
                numberOfIterationsToUseForPhase1Hash, saltUniqueToThisAccount, currentDateTimeUtc, saltLength);

            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount =
                new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack);
            RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack);
            ConsumedCredits = new DoubleThatDecaysWithTime(CreditHalfLife, 0, currentDateTimeUtc);
        }

    }
}
