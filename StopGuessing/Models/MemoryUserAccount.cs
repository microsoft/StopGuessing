using System;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public class MemoryUserAccount : IUserAccount
    {
        public string UsernameOrAccountId { get; set; }

        /// <summary>
        /// The salt is a random unique sequence of bytes that is included when the password is hashed (phase 1 of hashing)
        /// to ensure that attackers who might obtain the set of account hashes cannot hash a password once and then compare
        /// the hash against every account.
        /// </summary>
        public byte[] SaltUniqueToThisAccount { get; set; }

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// </summary>
        public string PasswordHashPhase1FunctionName { get; set; }

        /// <summary>
        /// The number of iterations to use for the phase 1 hash to make it more expensive.
        /// </summary>
        public int NumberOfIterationsToUseForPhase1Hash { get; set; }

        /// <summary>
        /// An EC public encryption symmetricKey used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private symmetricKey.
        /// </summary>
        public byte[] EcPublicAccountLogKey { get; set; }

        /// <summary>
        /// The EC private symmetricKey encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; set; }

        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption symmetricKey for the log. 
        /// </summary>
        public string PasswordHashPhase2 { get; set; }

        /// <summary>
        /// The account's credit limit for offsetting penalties for IP addresses from which
        /// the account has logged in successfully.
        /// </summary>
        public double CreditLimit { get; set; }

        /// <summary>
        /// The half life with which used credits are removed from the system freeing up new credit
        /// </summary>
        public TimeSpan CreditHalfLife { get; set; }
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
        protected DecayingDouble ConsumedCredits { get; set; }
        
        public bool AddIncorrectPhase2Hash(string phase2Hash, DateTime? whenSeenUtc = null) => 
            RecentIncorrectPhase2Hashes.Add(phase2Hash);

        public bool HasClientWithThisHashedCookieSuccessfullyLoggedInBefore(string hashOfCookie) =>
                HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Contains(hashOfCookie);

        public void RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(string hashOfCookie, DateTime? whenSeenUtc = null)
        {
            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Add(hashOfCookie);
        }

        public double GetCreditsConsumed(DateTime asOfTimeUtc) => ConsumedCredits.GetValue(CreditHalfLife, asOfTimeUtc);

        public void ConsumeCredit(double amountConsumed, DateTime timeOfConsumptionUtc)
        {
            ConsumedCredits.Add(amountConsumed, CreditHalfLife, timeOfConsumptionUtc);
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
            string password = null,
            double creditLimit = UserAccountController.DefaultCreditLimit,
            TimeSpan? creditHalfLife = null,
            string phase1HashFunctionName = null,
            int numberOfIterationsToUseForPhase1Hash = 0,
            byte[] saltUniqueToThisAccount = null,
            DateTime? currentDateTimeUtc = null,
            int maxNumberOfCookiesToTrack = UserAccountController.DefaultMaxNumberOfCookiesToTrack,
            int maxFailedPhase2HashesToTrack = UserAccountController.DefaultMaxFailedPhase2HashesToTrack,
            int saltLength = UserAccountController.DefaultSaltLength)
        {
            if (usernameOrAccountId != null && usernameOrAccountId != UsernameOrAccountId)
                UsernameOrAccountId = usernameOrAccountId;
            UserAccountController.Initialize(this, password, creditLimit,creditHalfLife, phase1HashFunctionName,
                numberOfIterationsToUseForPhase1Hash, saltUniqueToThisAccount, currentDateTimeUtc, saltLength);

            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount =
                new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack);
            RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack);
            ConsumedCredits = new DecayingDouble(0, currentDateTimeUtc);
        }

        public void Dispose()
        {
            // no-op.
        }
    }
}
