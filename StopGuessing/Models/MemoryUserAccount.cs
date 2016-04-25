using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Utilities;
using System.Threading;

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
        public byte[] SaltUniqueToThisAccount { get; set; } =
            StrongRandomNumberGenerator.GetBytes(UserAccountController.DefaultSaltLength);

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// </summary>
        public string PasswordHashPhase1FunctionName { get; set; } =
            ExpensiveHashFunctionFactory.DefaultFunctionName;

        /// <summary>
        /// The number of iterations to use for the phase 1 hash to make it more expensive.
        /// </summary>
        public int NumberOfIterationsToUseForPhase1Hash { get; set; } =
            UserAccountController.DefaultIterationsForPasswordHash;
        
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
        public double CreditLimit { get; set; } = 
            UserAccountController.DefaultCreditLimit;

        /// <summary>
        /// The half life with which used credits are removed from the system freeing up new credit
        /// </summary>
        public TimeSpan CreditHalfLife { get; set; } =
            new TimeSpan( TimeSpan.TicksPerHour * UserAccountController.DefaultCreditHalfLifeInHours);

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        public DecayingDouble ConsumedCredits { get; set; }

        public MemoryUserAccount(
            string usernameOrAccountId, 
            string password = null, 
            int numberOfIterationsToUseForHash = 0,
            string passwordHashFunctionName = null,
            int? maxNumberOfCookiesToTrack = null,
            int? maxFailedPhase2HashesToTrack = null, 
            DateTime? currentDateTimeUtc = null)
        {
            UsernameOrAccountId = usernameOrAccountId;
            if (numberOfIterationsToUseForHash > 0)
            {
                NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForHash;
            }
            if (passwordHashFunctionName != null)
            {
                PasswordHashPhase1FunctionName = passwordHashFunctionName;
            }

            if (password != null)
            {
                UserAccountController.SetPassword(this, password);
            }
            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount =
                new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack ?? UserAccountController.DefaultMaxNumberOfCookiesToTrack);
            RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack ?? UserAccountController.DefaultMaxFailedPhase2HashesToTrack);
            ConsumedCredits = new DecayingDouble(0, currentDateTimeUtc);
        }

        /// <summary>
        /// A recency set of the device cookies (hashed via SHA256 and converted to Base64)
        /// that have successfully logged into this account.
        /// </summary>
        protected SmallCapacityConstrainedSet<string> HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount { get; set; }

        ///// <summary>
        ///// A length-limited sequence of records describing failed login attempts (invalid passwords) 
        ///// </summary>
        protected SmallCapacityConstrainedSet<string> RecentIncorrectPhase2Hashes { get; set; }

        public Task<bool> AddIncorrectPhase2HashAsync(string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken)) => 
            TaskHelper.PretendToBeAsync(RecentIncorrectPhase2Hashes.Add(phase2Hash));

        public Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken)) =>
            TaskHelper.PretendToBeAsync(HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Contains(hashOfCookie));

        public void RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(string hashOfCookie, DateTime? whenSeenUtc = null)
        {
            HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Add(hashOfCookie);
        }

        public double GetCreditsConsumed(DateTime asOfTimeUtc) => ConsumedCredits.GetValue(CreditHalfLife, asOfTimeUtc);


#pragma warning disable 1998
        public async Task<double> TryGetCreditAsync(double amountRequested, DateTime timeOfRequestUtc,
            CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            double amountAvailable = Math.Min(0, CreditLimit - ConsumedCredits.GetValue(CreditHalfLife, timeOfRequestUtc));
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            ConsumedCredits.SubtractInPlace(CreditHalfLife, amountConsumed, timeOfRequestUtc);
            return amountConsumed;
        }

        public void Dispose()
        {
            // no-op.
        }
    }
}
