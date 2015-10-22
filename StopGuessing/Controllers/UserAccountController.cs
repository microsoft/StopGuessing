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
        private readonly BlockingAlgorithmOptions _options;
        private readonly SelfLoadingCache<string, UserAccount> _userAccountCache;
        private LimitPerTimePeriod[] CreditLimits { get; }

        public UserAccountController(
            UserAccountClient userAccountClient,
            LoginAttemptClient loginAttemptClient,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions options,
            IStableStore stableStore,
            LimitPerTimePeriod[] creditLimits)
        {
//            _options = optionsAccessor.Options;
            _options = options;
            _stableStore = stableStore;
            CreditLimits = creditLimits;
            _userAccountCache = new SelfLoadingCache<string, UserAccount>(_stableStore.ReadAccountAsync);
            SetLoginAttemptClient(loginAttemptClient);
            userAccountClient.SetLocalUserAccountController(this);
            memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;
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

        /// <summary>
        /// Get a user account record by looking up it's unique username/account ID.
        /// 
        /// This method may look in the cache first and only go to stable store if it doesn't find it.
        /// </summary>
        /// <param name="id">The unique identifier of the record to UserAccount fetch</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The account record or null if there is no such account.</returns>
        // GET api/UserAccount/stuart
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAsync(string id,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount result = await _userAccountCache.GetAsync(id, cancellationToken);
            return (result == null) ? (IActionResult) (new HttpNotFoundResult()) : (new ObjectResult(result));
        }

        public async Task<UserAccount> LocalGetAsync(string id,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _userAccountCache.GetAsync(id, cancellationToken);
        }

        /// <summary>
        /// PUT an account record into stable store (and any in-memory caching)
        /// </summary>
        /// <param name="id">The uniuqe username/accoount id of the account,
        ///  which should be equal to account.UsernameOrAccountId,
        ///  but is provided in the URL for RESTfullness</param>
        /// <param name="account">The account record to store</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        // PUT api/UserAccount/stuart
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAsync(string id, [FromBody] UserAccount account,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (id != account.UsernameOrAccountId)
            {
                throw new Exception(
                    "The user/account name in the PUT url must match the UsernameOrAccountID in the account record in the body.");
            }
            UserAccount result = await PutAsync(account, cancellationToken);
            return new ObjectResult(result);
        }

        public async Task<UserAccount> PutAsync(UserAccount account,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await WriteAccountAsync(account, cancellationToken);
            return account;
        }


        /// <summary>
        /// When a user has provided the correct password for an account, use it to decrypt the key that stores
        /// previous failed password attempts, use that key to decrypt that passwords used in those attempts,
        /// and determine whether they passwords were incorrect because they were typos--passwords similar to,
        /// but a small edit distance away from, the correct password.
        /// </summary>
        /// <param name="id">The username or account ID of the account for which the client has authenticated using the correct password.</param>
        /// <param name="correctPassword">The correct password provided by the client.</param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password
        /// (we could re-derive this, the hash should be expensive to calculate and so we don't want to replciate the work unnecessarily.)</param>
        /// <param name="ipAddressToExcludeFromAnalysis">This is used to prevent the analysis fro examining LoginAttempts from this IP.
        /// We use it because it's more efficient to perform the analysis for that IP as part of the process of evaluting whether
        /// that IP should be blocked or not.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns>The number of LoginAttempts updated as a result of the analyis.</returns>
        [HttpPost("{id}")]
        public async Task<IActionResult> UpdateOutcomesUsingTypoAnalysisAsync(
            string id,
            [FromBody] string correctPassword,
            [FromBody] byte[] phase1HashOfCorrectPassword,
            [FromBody] System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await LocalGetAsync(id, cancellationToken);

            List<LoginAttempt> attemptsToUpdate = account.UpdateLoginAttemptOutcomeUsingTypoAnalysis(
                correctPassword,
                phase1HashOfCorrectPassword,
                _options.MaxEditDistanceConsideredATypo,
                account.PasswordVerificationFailures.Where(attempt =>
                    attempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword &&
                    (!string.IsNullOrEmpty(attempt.EncryptedIncorrectPassword)) &&
                    (!attempt.AddressOfClientInitiatingRequest.Equals(ipAddressToExcludeFromAnalysis))
                    )
                );

            if (attemptsToUpdate.Count > 0)
            {
                // Update this UserAccount in stable store.
                WriteAccountInBackground(account, cancellationToken);

                // Update the primary copies of the LoginAttempt records with outcomes we've modified using
                // our typo analysis. 
                _loginAttemptClient.UpdateLoginAttemptOutcomesInBackground(attemptsToUpdate, cancellationToken);
            }

            return new ObjectResult(attemptsToUpdate.Count);
        }


        /// <summary>
        /// Update to UserAccount record to incoroprate what we've learned from a LoginAttempt.
        /// 
        /// If the login attempt was successful (Outcome==CrednetialsValid) then we will want to
        /// track the cookie used by the client as we're more likely to trust this client in the future.
        /// If the login attempt was a failure, we'll want to add this attempt to the length-limited
        /// sequence of faield login attempts.
        /// </summary>
        /// <param name="id">The username or account id that uniquely identifies the account to update.</param>
        /// <param name="attempt">The attempt to incorporate into the account's records</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        [HttpPost("{id}")]
        public async Task<IActionResult> UpdateForNewLoginAttemptAsync(string id, [FromBody] LoginAttempt attempt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await LocalGetAsync(id, cancellationToken);
            switch (attempt.Outcome)
            {
                case AuthenticationOutcome.CredentialsValid:
                    // If the login attempt was successful (Outcome==CrednetialsValid) then we will want to
                    // track the cookie used by the client as we're more likely to trust this client in the future.
                    if (!string.IsNullOrEmpty(attempt.HashOfCookieProvidedByBrowser))
                    {
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(
                            attempt.HashOfCookieProvidedByBrowser);
                        WriteAccountInBackground(account, cancellationToken);
                    }
                    break;
                case AuthenticationOutcome.CredentialsValidButBlocked:
                    break;
                default:
                    // Add this login attempt to the length-limited sequence of failed login attempts.
                    account.PasswordVerificationFailures.Add(attempt);
                    WriteAccountInBackground(account, cancellationToken);
                    break;
            }
            return new HttpOkResult();
        }


        /// <summary>
        /// Try to get a credit that can be used to allow this account's successful login from an IP addresss to undo some
        /// of the reputational damage caused by failed attempts.
        /// </summary>
        /// <param name="id">The username or account id that uniquely identifies the account to get a credit from.</param>
        /// <param name="amountToGet">The amount of credit needed.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns></returns>
        [HttpPost("{id}")]
        public async Task<IActionResult> TryGetCreditAsync(string id, [FromBody] float amountToGet = 1f,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            UserAccount account = await LocalGetAsync(id, cancellationToken);
            bool result = TryGetCredit(account, amountToGet, cancellationToken);
            return new ObjectResult(result);
        }

        public bool TryGetCredit(UserAccount account, 
            float amountToGet = 1f, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
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
            WriteAccountInBackground(account, cancellationToken);
            return true;
        }


        // DELETE api/values/5
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            return new HttpOkResult();
        }


        /// <summary>
        /// Combines update the local account cache with an asyncronous write to stable store.
        /// </summary>
        /// <param name="account">The account to write to cache/stable store.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        protected async Task WriteAccountAsync(UserAccount account, CancellationToken cancellationToken = default(CancellationToken))
        {           
            _userAccountCache[account.UsernameOrAccountId] = account;
            await _stableStore.WriteAccountAsync(account, cancellationToken);
        }

        /// <summary>
        /// Combines update the local account cache with a background write to stable store.
        /// </summary>
        /// <param name="account">The account to write to cache/stable store.</param>
        /// <param name="cancellationToken"></param>
        protected void WriteAccountInBackground(UserAccount account, CancellationToken cancellationToken = default(CancellationToken))
        {
            // ReSharper disable once UnusedVariable -- unused variable used to signify background task
            Task.Run(() => WriteAccountAsync(account, cancellationToken), cancellationToken);
        }




        private const int DefaultSaltLength = 8;
        private const int DefaultMaxAccountPasswordVerificationFailuresToTrack = 32;
        private const int DefaultMaxNumberOfCookiesToTrack = 24;

        /// <summary>
        /// Create a UserAccount record to match a given username or account id.
        /// </summary>
        /// <param name="usernameOrAccountId">A unique identifier for this account, such as a username, email address, or data index for the account record.</param>
        /// <param name="password">The password for the account.  If null or not provided, no password is set.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations to use when hashing the password.</param>
        /// <param name="saltUniqueToThisAccount">The salt for this account.  If null or not provided, a random salt is generated with length determined
        /// by parameter <paramref name="saltLength"/>.</param>
        /// <param name="maxNumberOfCookiesToTrack">This class tracks cookies associated with browsers that have 
        /// successfully logged into this account.  This parameter, if set, overrides the default maximum number of such cookies to track.</param>
        /// <param name="maxAccountPasswordVerificationFailuresToTrack">This class keeps information about failed attempts to login
        /// to this account that can be examined on the next successful login.  This parameter ovverrides the default number of failures to track.</param>
        /// <param name="phase1HashFunctionName">A hash function that is expensive enough to calculate to make offline dictionary attacks 
        /// expensive, but not so expensive as to slow the authentication system to a halt.  If not specified, a default function will be
        /// used.</param>
        /// <param name="saltLength">If <paramref name="saltUniqueToThisAccount"/>is not specified or null, the constructor will create
        /// a random salt of this length.  If this length is not specified, a default will be used.</param>
        public UserAccount CreateUserAccount(string usernameOrAccountId,
            string password = null,
            string phase1HashFunctionName = ExpensiveHashFunctionFactory.DefaultFunctionName,
            int numberOfIterationsToUseForPhase1Hash = 10000,
            byte[] saltUniqueToThisAccount = null,
            int maxNumberOfCookiesToTrack = DefaultMaxNumberOfCookiesToTrack,
            int maxAccountPasswordVerificationFailuresToTrack = DefaultMaxAccountPasswordVerificationFailuresToTrack,
            int saltLength = DefaultSaltLength)
        {

            if (saltUniqueToThisAccount == null)
            {
                saltUniqueToThisAccount = new byte[DefaultSaltLength];
                RandomNumberGenerator.Create().GetBytes(saltUniqueToThisAccount);
            }

            UserAccount newAccount = new UserAccount
            {
                UsernameOrAccountId = usernameOrAccountId,
                SaltUniqueToThisAccount = saltUniqueToThisAccount,
                HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount =
                    new CapacityConstrainedSet<string>(maxNumberOfCookiesToTrack),
                PasswordVerificationFailures =
                    new Sequence<LoginAttempt>(maxAccountPasswordVerificationFailuresToTrack),
                ConsumedCredits = new Sequence<UserAccount.ConsumedCredit>((int) CreditLimits.Last().Limit),
                PasswordHashPhase1FunctionName = phase1HashFunctionName,
                NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForPhase1Hash
                //Password = password
            };

            if (password != null)
            {
                newAccount.SetPassword(password);
            }

            return newAccount;
        }



        /// <summary>
        /// When memory runs low, call this function to remove a fraction of the space used by non-fixed-size data structures
        /// (In this case, it is the cache of user accounts)
        /// </summary>
        public void ReduceMemoryUsage(object sender, MemoryUsageLimiter.ReduceMemoryUsageEventParameters parameters)
        {
            _userAccountCache.RecoverSpace(parameters.FractionOfMemoryToTryToRemove);
        }



    }
}
