using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using StopGuessing.EncryptionPrimitives;
using System.Security.Cryptography;
using System.Threading;
using StopGuessing.Clients;

namespace StopGuessing.Controllers
{
    [Route("api/[controller]")]
    public class UserAccountController : Controller
    {
        private readonly IStableStore _stableStore;
        private LoginAttemptClient _loginAttemptClient;
        private readonly SelfLoadingCache<string, UserAccount> _userAccountCache;
        private LimitPerTimePeriod[] CreditLimits { get; }

        public UserAccountController(IStableStore stableStore, SelfLoadingCache<string, UserAccount> userAccountCache, LimitPerTimePeriod[] creditLimits)
        {
            _stableStore = stableStore;
            _userAccountCache = userAccountCache;
            CreditLimits = creditLimits;
        }

        public void SetLoginAttemptClient(LoginAttemptClient loginAttemptClient)
        {
            _loginAttemptClient = loginAttemptClient;
        }

        // GET: api/UserAccount
        [HttpGet]
        public IEnumerable<UserAccount> Get()
        {
            throw new NotImplementedException("Cannot enumerate all accounts");
        }

        // GET api/UserAccount/stuart
        [HttpGet("{id:string}")]
        public async Task<UserAccount> GetAsync(string id, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _userAccountCache.GetAsync(id, cancellationToken);
        }


        // PUT api/UserAccount/stuart
        [HttpPut("{id:string}")]
        public async Task PutAsync(string id, [FromBody] UserAccount account, CancellationToken cancellationToken = default(CancellationToken))
        {
            await WriteAccountAsync(account, cancellationToken);
        }

        // UpdateOutcomesUsingTypoAnalysisAsync(passwordProvidedByClient, phase1HashOfProvidedPassword, excludeIp: this.AddressOfClientInitiatingRequest);
        [HttpPost("{id:string}")]
        public async Task UpdateOutcomesUsingTypoAnalysisAsync(string id, [FromBody] string correctPassword,
            [FromBody] byte[] phase1HashOfCorrectPassword,
            [FromBody] System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await GetAsync(id, cancellationToken);
            await UpdateLoginAttemptsUsingTypoAnalysisAsync(account,
                correctPassword, phase1HashOfCorrectPassword, ipAddressToExcludeFromAnalysis, cancellationToken);
            await WriteAccountAsync(account, cancellationToken);
        }


        //AddDeviceCookieFromSuccessfulLogin(cookie)
        [HttpPost("{id:string}")]
        public async Task AddDeviceCookieFromSuccessfulLoginAsync(string id, string cookie, CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await GetAsync(id, cancellationToken);
            account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(cookie);
            await WriteAccountAsync(account, cancellationToken);
        }

        // AddLoginAttemptFailure(loginAttempt);
        [HttpPost("{id:string}")]
        public async Task AddLoginAttemptFailureAsync(string id, [FromBody] LoginAttempt failedLoginAttempt, CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await GetAsync(id, cancellationToken);
            account.PasswordVerificationFailures.Add(failedLoginAttempt);
            await WriteAccountAsync(account, cancellationToken);
        }

        // AddHashOfRecentIncorrectPasswordAsync(phase2HashOfProvidedPassword)
        [HttpPost("{id:string}")]
        public async Task AddHashOfRecentIncorrectPasswordAsync(string id, [FromBody] byte[] phase2HashOfProvidedPassword, CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await GetAsync(id, cancellationToken);
            account.Phase2HashesOfRecentlyIncorrectPasswords.Add(phase2HashOfProvidedPassword);
            await WriteAccountAsync(account, cancellationToken);
        }


        [HttpPost("{id:string}")]
        public async Task<bool> TryGetCreditAsync(string id, [FromBody] float amountToGet = 1f, CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await GetAsync(id, cancellationToken);

            float amountConsumed = amountToGet;

            DateTimeOffset timeAtStartOfMethod = DateTimeOffset.Now;
            int limitIndex = 0;
            foreach (UserAccount.ConsumedCredit consumedCredit in account.ConsumedCredits)
            {
                TimeSpan age = timeAtStartOfMethod - consumedCredit.WhenCreditConsumed;
                while (limitIndex < CreditLimits.Length && age > CreditLimits[limitIndex].TimePeriod)
                {
                    // If the consumed credit is older than the time period for the current limit,
                    // we've not exceeded that limit within that time period.  Check the next limit down the line.
                    limitIndex++;
                }
                if (limitIndex >= CreditLimits.Length)
                {
                    // The age of this consumed credit is older than the longest limit duration, which means
                    // we've not exceeded the limit at any duration.
                    // We can exit this for loop knowing there is credit available.
                    break;
                }
                amountConsumed += consumedCredit.AmountConsumed;
                if (amountConsumed > CreditLimits[limitIndex].Limit)
                {
                    // We've exceeded the limit for this time period.
                    return false;
                }
                else
                {
                    // We were able to accomodate this credit within the limits so far.
                    // Move on to the next one.
                }
            }

            // We never exceeded a limit.  We have a credit to consume.
            // Add it and return true to indicate that a credit was retrieved.
            account.ConsumedCredits.Add(new UserAccount.ConsumedCredit()
            {
                WhenCreditConsumed = timeAtStartOfMethod,
                AmountConsumed = amountToGet
            });
            return true;
        }


        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }

        //public async Task<UserAccount> ReadAccountAsync(string usernameOrAccountId)
        //{
        //    UserAccount account = await ToBeReplaced.GetLocalAccountAsync(usernameOrAccountId);
        //    return account;
        //    //return await _StableStore.ReadAccountAsync(usernameOrAccountId);
        //}
        public async Task WriteAccountAsync(UserAccount account, CancellationToken cancellationToken) // = default(CancellationToken)
        {           
            _userAccountCache[account.UsernameOrAccountId] = account;
            await _stableStore.WriteAccountAsync(account, cancellationToken);
        }

        public void WriteAccountInBackground(UserAccount account)
        {
            // ReSharper disable once UnusedVariable -- unused variable used to signify background task
            Task dontwaitforme = WriteAccountAsync(account, default(CancellationToken));
        }




        private const int DefaultSaltLength = 8;
        private const int DefaultMaxAccountPasswordVerificationFailuresToTrack = 32;
        private const int DefaultMaxNumberOfCookiesToTrack = 24;
        private const int DefaultMaxRecentlyIncorrectPasswordsToTrack = 2;

        /// <summary>
        /// Create a UserAccount record to match a given username or account id.
        /// </summary>
        /// <param name="usernameOrAccountId">A unique identifier for this account, such as a username, email address, or data index for the account record.</param>
        /// <param name="password">The password for the account.  If null or not provided, no password is set.</param>
        /// <param name="saltUniqueToThisAccount">The salt for this account.  If null or not provided, a random salt is generated with length determined
        /// by parameter <paramref name="saltLength"/>.</param>
        /// <param name="maxNumberOfCookiesToTrack">This class tracks cookies associated with browsers that have 
        /// successfully logged into this account.  This parameter, if set, overrides the default maximum number of such cookies to track.</param>
        /// <param name="maxAccountPasswordVerificationFailuresToTrack">This class keeps information about failed attempts to login
        /// to this account that can be examined on the next successful login.  This parameter ovverrides the default number of failures to track.</param>
        /// <param name="maxRecentlyIncorrectPasswordsToTrack">How many incorrect passwords (well, their hashes) should be tracked in order to 
        /// detect when the same incorrect password has been sent repeatedly?</param>
        /// <param name="phase1HashFunctionName">A hash function that is expensive enough to calculate to make offline dictionary attacks 
        /// expensive, but not so expensive as to slow the authentication system to a halt.  If not specified, a default function will be
        /// used.</param>
        /// <param name="saltLength">If <paramref name="saltUniqueToThisAccount"/>is not specified or null, the constructor will create
        /// a random salt of this length.  If this length is not specified, a default will be used.</param>
        public UserAccount CreateUserAccount(string usernameOrAccountId,
            string password = null,
            string phase1HashFunctionName = ExpensiveHashFunctionFactory.DefaultFunctionName,
            byte[] saltUniqueToThisAccount = null,
            int maxNumberOfCookiesToTrack = DefaultMaxNumberOfCookiesToTrack,
            int maxAccountPasswordVerificationFailuresToTrack = DefaultMaxAccountPasswordVerificationFailuresToTrack,
            int maxRecentlyIncorrectPasswordsToTrack = DefaultMaxRecentlyIncorrectPasswordsToTrack,
            int saltLength = DefaultSaltLength)
        {

            UserAccount newAccount = new UserAccount
            {
                UsernameOrAccountId = usernameOrAccountId,
                SaltUniqueToThisAccount = saltUniqueToThisAccount,
                Phase2HashesOfRecentlyIncorrectPasswords =
                    new Sequence<byte[]>(maxRecentlyIncorrectPasswordsToTrack),
                HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount =
                    new CapacityConstrainedSet<string>(maxNumberOfCookiesToTrack),
                PasswordVerificationFailures =
                    new Sequence<LoginAttempt>(maxAccountPasswordVerificationFailuresToTrack),
                ConsumedCredits = new Sequence<UserAccount.ConsumedCredit>((int)CreditLimits.Last().Limit)
            };

            if (newAccount.SaltUniqueToThisAccount == null)
            {
                newAccount.SaltUniqueToThisAccount = new byte[DefaultSaltLength];
                RandomNumberGenerator.Create().GetBytes(newAccount.SaltUniqueToThisAccount);
            }

            newAccount.PasswordHashPhase1FunctionName = phase1HashFunctionName;

            if (password != null)
            {
                newAccount.SetPassword(password);
            }

            return newAccount;
        }

        
        public async Task UpdateLoginAttemptsUsingTypoAnalysisAsync(
            UserAccount account,
            string passwordProvidedByClient,
            byte[] phase1HashOfProvidedPassword,
            System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<LoginAttempt> attemptsToUpdate = new List<LoginAttempt>();

            // Decrypt any account log entries for analysis

            // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
            byte[] ecPrivateAccountLogKeyAsBytes =
                Encryption.DecryptAescbc(account.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                    phase1HashOfProvidedPassword.Take(16).ToArray(),
                    checkAndRemoveHmac: true);
            ECDiffieHellmanCng ecPrivateAccountLogKey =
                new ECDiffieHellmanCng(CngKey.Import(ecPrivateAccountLogKeyAsBytes, CngKeyBlobFormat.EccPrivateBlob));


            // Identify which login failures due to incorrect passwords were the result of likely typos
            // and which were not, organizing them by (1) IP and then (2) the time of the event
            foreach (LoginAttempt previousFailedLoginAttempt in account.PasswordVerificationFailures)
            {
                if (previousFailedLoginAttempt.Outcome != AuthenticationOutcome.CredentialsInvalidIncorrectPassword ||
                    previousFailedLoginAttempt.EncryptedIncorrectPassword == null ||
                    previousFailedLoginAttempt.AddressOfClientInitiatingRequest.Equals(ipAddressToExcludeFromAnalysis))
                    continue;
                try
                {
                    string passwordProvidedInPreviousLoginFailure =
                        previousFailedLoginAttempt.DecryptAndGetIncorrectPassword(ecPrivateAccountLogKey);

                    bool likelyTypo =
                        EditDistance.Calculate(passwordProvidedInPreviousLoginFailure, passwordProvidedByClient) <=
                        2;
                    previousFailedLoginAttempt.Outcome = likelyTypo
                        ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;
                    attemptsToUpdate.Add(previousFailedLoginAttempt);
                }
                catch (Exception)
                {
                    // An exception is likely due to an incorrect key (perhaps outdated).
                    // Since we simply can't do anything with a record we can't Decrypt, we carry on
                    // as if nothing ever happened.  No.  Really.  Nothing to see here.
                }
            }

            if (attemptsToUpdate.Count > 0)
            {
                await _loginAttemptClient.UpdateLoginAttemptOutcomesAsync(attemptsToUpdate, cancellationToken);
            }

        }



    }
}
