using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using StopGuessing.Clients;
using StopGuessing.EncryptionPrimitives;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace StopGuessing.Controllers
{
    public class BlockingScoresForEachAlgorithm
    {
        public double Ours = 0;
        public double Industry = 0;
        public double SSH = 0;
    }

    [Route("api/[controller]")]
    public class LoginAttemptController : Controller
    {
        private readonly IStableStore _stableStore;
        private readonly BlockingAlgorithmOptions _options;
        private readonly IBinomialLadderSketch _binomialLadderSketch;
        private readonly IFrequenciesProvider<string> _incorrectPasswordFrequenciesProvider;
        private readonly IUserAccountContextFactory _userAccountContextFactory;
        //private readonly FixedSizeLruCache<string, LoginAttempt> _loginAttemptCache;
        private readonly AgingMembershipSketch _recentIncorrectPasswords;

        private readonly Dictionary<string, Task<Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>>>
            _loginAttemptsInProgress;

        private readonly SelfLoadingCache<IPAddress, IpHistory> _ipHistoryCache;
        private readonly LoginAttemptClient _loginAttemptClient;
        
        private TimeSpan DefaultTimeout { get; } = new TimeSpan(0, 0, 0, 0, 500); // FUTURE use configuration value

        public LoginAttemptController(
            LoginAttemptClient loginAttemptClient,
            IUserAccountContextFactory userAccountContextFactory,
            IBinomialLadderSketch binomialLadderSketch,
            IFrequenciesProvider<string> incorrectPasswordFrequenciesProvider,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions blockingOptions,
            IStableStore stableStore)
        {
            _options = blockingOptions; //optionsAccessor.Options;
            _stableStore = stableStore;
            _binomialLadderSketch = binomialLadderSketch;
            _incorrectPasswordFrequenciesProvider = incorrectPasswordFrequenciesProvider;

            _recentIncorrectPasswords = new AgingMembershipSketch(16, 128 * 1024); // FIXME -- more configurable?
            _userAccountContextFactory = userAccountContextFactory;
        //_passwordPopularityTracker = new PasswordPopularityTracker("FIXME-uniquekeyfromconfig"
        //FIXME -- use configuration to get options here"FIXME-uniquekeyfromconfig", thresholdRequiredToTrackPreciseOccurrences: 10);
        //);

        //_loginAttemptCache = new FixedSizeLruCache<string, LoginAttempt>(80000);
        // FIXME -- use configuration file for size
        _loginAttemptsInProgress =
                new Dictionary<string, Task<Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>>>();
            _ipHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>(
                (id, cancellationToken) =>
                {
                    return
                        Task.Run(
                            () => new IpHistory(id, _options),
                            cancellationToken);
                    // FUTURE -- option to load from stable store
                });
            _loginAttemptClient = loginAttemptClient;
            _loginAttemptClient.SetLocalLoginAttemptController(this);
            memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;
        }


        // GET: api/LoginAttempt
        [HttpGet]
        public IEnumerable<LoginAttempt> Get()
        {
            throw new NotImplementedException("Cannot enumerate all login attempts");
        }

        //// GET api/LoginAttempt/5
        //[HttpGet("{id}")]
        //public async Task<IActionResult> GetAsync(string id,
        //    [FromQuery] List<RemoteHost> serversResponsibleForCachingALoginAttempt = null,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    LoginAttempt result = await LocalGetAsync(id, serversResponsibleForCachingALoginAttempt, cancellationToken);
        //    return (result == null) ? (IActionResult) (new HttpNotFoundResult()) : (new ObjectResult(result));
        //}

        //public async Task<LoginAttempt> LocalGetAsync(
        //    string key,
        //    List<RemoteHost> serversResponsibleFOrCachingALoginAttempt = null,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    LoginAttempt loginAttempt;

        //    // If the requested LoginAttempt is in cache, return it immediately
        //    if (_loginAttemptCache.TryGetValue(key, out loginAttempt))
        //        return loginAttempt;

        //    // We'll have to load the login attempt from stable store
        //    loginAttempt = await _stableStore.ReadLoginAttemptAsync(key, cancellationToken);

        //    if (loginAttempt != null)
        //    {
        //        // We successfully read the login attempt from stable store.  We now need to ensure
        //        // that it enters the local cache and the other caches
        //        WriteLoginAttemptInBackground(loginAttempt, serversResponsibleFOrCachingALoginAttempt,
        //            updateTheLocalCache: true, updateRemoteCaches: true, updateStableStore: false,
        //            cancellationToken: cancellationToken);
        //    }

        //    return loginAttempt;
        //}

        //// WriteAccountAsync login attempts
        //// POST api/LoginAttempt/
        //[HttpPost]
        //public async Task<IActionResult> UpdateLoginAttemptOutcomesAsync(
        //    [FromBody] List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    await new Task(() =>
        //    {
        //        Parallel.ForEach(
        //            loginAttemptsWithUpdatedOutcomes.ToLookup(attempt => attempt.AddressOfClientInitiatingRequest),
        //            loginAttemptsWithUpdatedOutcomesByIp =>
        //            {
        //                // If there is a record of the IP address that the login attempt belongs to,
        //                // update that IP history with the new loginAttempt outcome.
        //                IpHistory ip;
        //                if (_ipHistoryCache.TryGetValue(loginAttemptsWithUpdatedOutcomesByIp.Key, out ip))
        //                {
        //                    ip.UpdateLoginAttemptsWithNewOutcomes(loginAttemptsWithUpdatedOutcomesByIp.ToList());
        //                }
        //            });
        //    });
        //    return new HttpOkResult();
        //}

        // PUT api/LoginAttempt/clientsIpHistory-address-datetime
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAsync(string id, [FromBody] LoginAttempt loginAttempt,
            [FromBody] string passwordProvidedByClient = null,
            [FromBody] List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            [FromBody] bool onlyUpdateTheInMemoryCacheOfTheLoginAttempt = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (id != loginAttempt.UniqueKey)
            {
                throw new Exception("The id assigned to the login does not match it's unique key.");
            }

            if (loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt == null)
            {
                // Unless the address of the server that received this login loginAttempt from the user client has already
                // been specified, we'll assume it was the server initiating this put request.
                loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt = HttpContext.Connection.RemoteIpAddress;
            }

            LoginAttempt result = await LocalPutAsync(loginAttempt, passwordProvidedByClient,
                serversResponsibleForCachingThisLoginAttempt,
                onlyUpdateTheInMemoryCacheOfTheLoginAttempt,
                cancellationToken);
            return new ObjectResult(result);
        }


        public async Task<LoginAttempt> LocalPutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            bool onlyUpdateTheInMemoryCacheOfTheLoginAttempt = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string key = loginAttempt.UniqueKey;

            bool updateTheLocalCache = true;
            bool updateRemoteCaches = !onlyUpdateTheInMemoryCacheOfTheLoginAttempt;
            bool updateStableStore = !onlyUpdateTheInMemoryCacheOfTheLoginAttempt;

            if (loginAttempt.Outcome == AuthenticationOutcome.Undetermined)
            {
                // The outcome of the loginAttempt is not known.  We need to calculate it
                Task<Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>> outcomeCalculationTask = null;

                lock (_loginAttemptsInProgress)
                {
                    LoginAttempt existingLoginAttempt;
                    if (_loginAttemptCache.TryGetValue(key, out existingLoginAttempt) &&
                        existingLoginAttempt.Outcome != AuthenticationOutcome.Undetermined)
                    {
                        // Another thread has already performed this PUT operation and determined the
                        // outcome.  There's nothing to do but to take that attempt from the cache
                        // so we can return it.
                        loginAttempt = existingLoginAttempt;
                        updateTheLocalCache = updateRemoteCaches = updateStableStore = false;
                    }
                    else if (_loginAttemptsInProgress.TryGetValue(key, out outcomeCalculationTask))
                    {
                        // Another thread already started this put, and will write the
                        // outcome to stable store.  We need only await the outcome and
                        // let the other thread write the outcome to cache and stable store.
                        updateTheLocalCache = updateRemoteCaches = updateStableStore = false;
                    }
                    else
                    {
                        // This thread will need to perform the outcome calculation, and will place
                        // the result in the cache.  We'll start that task off but await it outside
                        // the lock on _loginAttemptsInProgress so that we can release the lock.
                        LoginAttempt attemptToDetermineOutcomeOf = loginAttempt;
                        _loginAttemptsInProgress[key] = outcomeCalculationTask =
                            Task.Run(() => DetermineLoginAttemptOutcomeAsync(
                                attemptToDetermineOutcomeOf, passwordProvidedByClient,
                                cancellationToken: cancellationToken),
                                cancellationToken);
                        // The above call will update the local cache and remove _loginAttemptsInProgress[key]
                        // It's best to do add the LoginAttempt to the local cache there, and not below,
                        // because we want to ensure the value is in the cache before we remove the signal
                        // that no other thread needs to determine the outcome of this LoginAttempt.
                        // As a result, there's no need for us to update the local cache below.
                        updateTheLocalCache = false;
                    }
                }

                // If we need to update the loginAttempt based on the outcome calculation
                // (a Task running DetermineLoginAttemptOutcomeAsync), wait for that task
                // to complete and get the loginAttempt with its outcome.
                if (outcomeCalculationTask != null)
                    loginAttempt = (await outcomeCalculationTask).Item1;
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- helps for clarity
            if (updateTheLocalCache || updateRemoteCaches || updateStableStore)
            {
                WriteLoginAttemptInBackground(loginAttempt,
                    serversResponsibleForCachingThisLoginAttempt,
                    updateTheLocalCache: updateTheLocalCache,
                    updateRemoteCaches: updateRemoteCaches,
                    updateStableStore: updateStableStore,
                    cancellationToken: cancellationToken);
            }

            return loginAttempt;
        }

        // DELETE api/LoginAttempt/<key>
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            // no-op
            return new HttpNotFoundResult();
        }


        /// <summary>
        /// Store an updated LoginAttempt to the local cache, remote caches, and in stable store.
        /// </summary>
        /// <param name="loginAttempt">The loginAttempt to write to cache/stable store.</param>
        /// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        /// <param name="updateTheLocalCache"></param>
        /// <param name="updateRemoteCaches"></param>
        /// <param name="updateStableStore"></param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        protected async Task WriteLoginAttemptAsync(
            LoginAttempt loginAttempt,
            List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            bool updateTheLocalCache = true,
            bool updateRemoteCaches = true,
            bool updateStableStore = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task stableStoreTask = null;

            //if (updateTheLocalCache)
            //{
            //    // Write to the local cache on this server
            //    _loginAttemptCache.Add(loginAttempt.UniqueKey, loginAttempt);
            //}

            if (updateStableStore)
            {
                // Write to stable consistent storage (e.g. database) that the system is configured to use
                stableStoreTask = _stableStore.WriteLoginAttemptAsync(loginAttempt, cancellationToken);
            }

            if (updateRemoteCaches)
            {
                // Identify the servers that cache this LoginAttempt and will need their cache entries updated
                if (serversResponsibleForCachingThisLoginAttempt == null)
                {
                    serversResponsibleForCachingThisLoginAttempt =
                        _loginAttemptClient.GetServersResponsibleForCachingALoginAttempt(loginAttempt);
                }

                // Update the cache entries for this LoginAttempt on the remote servers.
                _loginAttemptClient.PutCacheOnlyBackground(loginAttempt,
                    serversResponsibleForCachingThisLoginAttempt,
                    cancellationToken: cancellationToken);
            }

            // If writing to stable store, wait until the write has completed before returning.
            if (stableStoreTask != null)
                await stableStoreTask;
        }

        /// <summary>
        /// Store an updated LoginAttempt to the local cache, remote caches, and in stable store.
        /// </summary>
        /// <param name="attempt">The loginAttempt to write to cache/stable store.</param>
        /// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        /// <param name="updateTheLocalCache"></param>
        /// <param name="updateRemoteCaches"></param>
        /// <param name="updateStableStore"></param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        protected void WriteLoginAttemptInBackground(LoginAttempt attempt,
            List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            bool updateTheLocalCache = true,
            bool updateRemoteCaches = true,
            bool updateStableStore = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run(() => WriteLoginAttemptAsync(attempt,
                serversResponsibleForCachingThisLoginAttempt,
                updateTheLocalCache,
                updateRemoteCaches,
                updateStableStore,
                cancellationToken),
                cancellationToken);
        }


        /// <summary>
        /// This analysis will examine the client IP's previous failed attempts to login to this account
        /// to determine if any failed attempts were due to typos.  
        /// </summary>
        /// <param name="clientsIpHistory">Records of this client's previous attempts to examine.</param>
        /// <param name="account">The account that the client is currently trying to login to.</param>
        /// <param name="correctPassword">The correct password for this account.  (We can only know it because
        /// the client must have provided the correct one this loginAttempt.)</param>
        /// <param name="phase1HashOfCorrectPassword">The phase1 hash of that correct password (which we could
        /// recalculate from the information in the previous parameters, but doing so would be expensive.)</param>
        /// <returns></returns>
        protected void UpdateOutcomesUsingTypoAnalysis(
            IpHistory clientsIpHistory,
            UserAccount account,
            string correctPassword,
            byte[] phase1HashOfCorrectPassword)
        {
            if (clientsIpHistory == null)
                return;

            List<LoginAttempt> loginAttemptsWithOutcompesUpdatedDueToTypoAnalysis =
                account.UpdateLoginAttemptOutcomeUsingTypoAnalysis(correctPassword,
                    phase1HashOfCorrectPassword,
                    _options.MaxEditDistanceConsideredATypo,
                    clientsIpHistory.RecentPotentialTypos.Where(
                        pt => pt.UsernameOrAccountId == account.UsernameOrAccountId &&
                              !string.IsNullOrEmpty(pt.EncryptedIncorrectPassword))
                    );

            foreach (LoginAttempt updatedLoginAttempt in loginAttemptsWithOutcompesUpdatedDueToTypoAnalysis)
            {
                WriteLoginAttemptInBackground(updatedLoginAttempt);
            }
        }

        /// <summary>
        /// Multiply the penalty to be applied to a failed login loginAttempt based on the popularity of the password that was guessed.
        /// </summary>
        /// <param name="popularityLevel">The popularity of the password as a fraction (e.g. 0.0001 means 1 in 10,000 incorrect
        /// passwords were this password.)</param>
        /// <returns></returns>
        private double PopularityPenaltyMultiplier(double popularityLevel)
        {
            double penalty = 1d;
            foreach (
                PenaltyForReachingAPopularityThreshold penaltyForReachingAPopularityThreshold in
                    _options.PenaltyForReachingEachPopularityThreshold)
            {
                if (penalty < penaltyForReachingAPopularityThreshold.Penalty &&
                    popularityLevel >= penaltyForReachingAPopularityThreshold.PopularityThreshold)
                    penalty = penaltyForReachingAPopularityThreshold.Penalty;
            }
            return penalty;
        }

        public double CalculatePenalty(IpHistory ip, LoginAttempt loginAttempt, UserAccount account, ref bool accountChanged)
        {
            // loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount;
            double penalty = 0d;
            double passwordsPopularityAmongGuesses = loginAttempt.PasswordsPopularityAmongFailedGuesses;
            double popularityMultiplier = PopularityPenaltyMultiplier(passwordsPopularityAmongGuesses);;
            switch (loginAttempt.Outcome)
            {
                case AuthenticationOutcome.CredentialsInvalidNoSuchAccount:
                    penalty = _options.PenaltyForInvalidAccount_Alpha * popularityMultiplier;
                    break;
                case AuthenticationOutcome.CredentialsInvalidIncorrectPassword:
                    penalty = _options.PenaltyForInvalidPassword_Beta * popularityMultiplier;
                    break;
                case AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword:
                    // We ignore repeats of incorrect passwords we've already accounted for
                    // No penalty
                    penalty = 0;
                    break;
                case AuthenticationOutcome.CredentialsValid:
                    if (ip.CurrentBlockScore > 0)
                    {
                        double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Gamma, ip.CurrentBlockScore);
                        double credit = account.TryGetCredit(desiredCredit);
                        if (credit > 0)
                            accountChanged = true;
                        penalty -=credit;
                    }
                    break;
                case AuthenticationOutcome.CredentialsValidButBlocked:
                break;
                    // case AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely:
                    //    penalty = _options.PenaltyForInvalidPassword_Beta * _options.PenaltyMulitiplierForTypo;
                    //    break;
                    // case AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely:
            }
            return penalty;
        }


        /// <returns></returns>
        /// <summary>
        /// Add a LoginAttempt, along the way determining whether that loginAttempt should be allowed
        /// (the user authenticated) or denied.
        /// </summary>
        /// <param name="loginAttempt">The login loginAttempt record to be stored.</param>
        /// <param name="passwordProvidedByClient">The plaintext password provided by the client.</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken">To allow this async method to be cancelled.</param>
        /// <returns>If the password is correct and the IP not blocked, returns AuthenticationOutcome.CredentialsValid.
        /// Otherwise, it returns a different AuthenticationOutcome.
        /// The client should not be made aware of any information beyond whether the login was allowed or not.</returns>
        public async Task<Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>> DetermineLoginAttemptOutcomeAsync(
            LoginAttempt loginAttempt,
            string passwordProvidedByClient,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {

            // We'll need to know more about the IP making this loginAttempt, so let's get the historical information
            // we've been keeping about it.
            Task<IpHistory> ipHistoryGetTask = _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest,
                cancellationToken);
            Task<ILadder> binomialLadderTask = _binomialLadderSketch.GetLadderAsync(passwordProvidedByClient, 
                cancellationToken: cancellationToken);

            string easyHashOfPassword =
                Convert.ToBase64String(ManagedSHA256.Hash(Encoding.UTF8.GetBytes(passwordProvidedByClient)));
            Task<IFrequencies> passwordFrequencyTask = _incorrectPasswordFrequenciesProvider.GetFrequenciesAsync(
                easyHashOfPassword,
                timeout: timeout,
                cancellationToken: cancellationToken);

            IStableStoreContext<string, UserAccount> userAccountContext = _userAccountContextFactory.Get();
            Task<UserAccount> userAccountRequestTask = userAccountContext.ReadAsync(
                loginAttempt.UsernameOrAccountId,
                cancellationToken);
            // FIXME -- need to save changes

            // Get a copy of the UserAccount record for the account that the client wants to authenticate as.
            UserAccount account = await userAccountRequestTask;
            bool accountChanged = false;

            //  Move later
            ILadder passwordLadder = await binomialLadderTask;
            IFrequencies passwordFrequencies = await passwordFrequencyTask;
            // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
            // as we are most concerned about frequently-guessed passwords.
            int ladderRungsWithConfidence = passwordLadder.CountObservationsForGivenConfidence(_options.PopularityConfidenceLevel);
            double ladderPopularity = (double) ladderRungsWithConfidence / (10d * 1000d);//FIXME
            double trackedPopularity = Proportion.GetLargest(passwordFrequencies.Proportions).AsDouble;
            double popularityOfPasswordAmongIncorrectPasswords = Math.Max(ladderPopularity, trackedPopularity);


            byte[] phase1HashOfProvidedPassword = account != null
                ? account.ComputePhase1Hash(passwordProvidedByClient)
                : ExpensiveHashFunctionFactory.Get(_options.DefaultExpensiveHashingFunction)(
                    passwordProvidedByClient,
                    Encoding.UTF8.GetBytes(loginAttempt.AddressOfClientInitiatingRequest.ToString()),
                    _options.DefaultExpensiveHashingFunctionIterations);
            string phase1HashOfProvidedPasswordAsString = Convert.ToBase64String(phase1HashOfProvidedPassword);

            bool didSketchIndicateThatTheSameGuessHasBeenMadeRecently = _recentIncorrectPasswords.AddMember(phase1HashOfProvidedPasswordAsString);

            if (account == null)
            {
                // This appears to be an loginAttempt to login to a non-existent account, and so all we need to do is
                // mark it as such.  However, since it's possible that users will forget their account names and
                // repeatedly loginAttempt to login to a nonexistent account, we'll want to track whether we've seen
                // this clientsIpHistory/account/password tripple before and note in the outcome if it's a repeat so that.
                // the IP need not be penalized for issuign a query that isn't getting it any information it
                // didn't already have.
                loginAttempt.Outcome = didSketchIndicateThatTheSameGuessHasBeenMadeRecently
                    ? AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount
                    : AuthenticationOutcome.CredentialsInvalidNoSuchAccount;
            }
            else
            {
                //
                // This is an loginAttempt to login to a valid (existent) account.
                //

                // Determine whether the client provided a cookie that indicate that it has previously logged
                // into this account successfully---a very strong indicator that it is a client used by the
                // legitimate user and not an unknown client performing a guessing attack.
                loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount =
                    account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Contains(
                        loginAttempt.HashOfCookieProvidedByBrowser);

                // Test to see if the password is correct by calculating the Phase2Hash and comparing it with the Phase2 hash
                // in this record
                //
                // First, the expensive (phase1) hash which is used to encrypt the EC public key for this account
                // (which we use to store the encryptions of incorrect passwords)
                //byte[] phase1HashOfProvidedPassword = account.ComputePhase1Hash(passwordProvidedByClient);
                // Since we can't store the phase1 hash (it can decrypt that EC key) we instead store a simple (SHA256)
                // hash of the phase1 hash.
                string phase2HashOfProvidedPassword =
                    Convert.ToBase64String(ManagedSHA256.Hash((phase1HashOfProvidedPassword)));

                // To determine if the password is correct, compare the phase2 has we just generated (phase2HashOfProvidedPassword)
                // with the one generated from the correct password when the user chose their password (account.PasswordHashPhase2).  
                bool isSubmittedPasswordCorrect = phase2HashOfProvidedPassword == account.PasswordHashPhase2;

                if (isSubmittedPasswordCorrect)
                {
                    // The password is corerct.
                    // While we'll tenatively set the outcome to CredentialsValid, the decision isn't yet final.
                    // Down below we will call UpdateOutcomeIfIpShouldBeBlockedAsync.  If we believe the login was from
                    // a malicous IP that just made a lucky guess, it may be revised to CrendtialsValidButBlocked.
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsValid;

                    // Determine if any of the outcomes for login attempts from the client IP for this request were the result of typos,
                    // as this might impact our decision about whether or not to block this client IP in response to its past behaviors.
                    UpdateOutcomesUsingTypoAnalysis(await ipHistoryGetTask,
                        account, passwordProvidedByClient, phase1HashOfProvidedPassword);
                }
                else
                {
                    //
                    // The password was invalid.  There's lots of work to do to facilitate future analysis
                    // about why this LoginAttempt failed.

                    // So that we can analyze this failed loginAttempt in the future, we'll store the (phase 2) hash of the 
                    // incorrect password along with the password itself, encrypted with the EcPublicAccountLogKey.
                    // (The decryption key to get the incorrect password plaintext back is encrypted with the
                    //  correct password, so you can't get to the plaintext of the incorrect password if you
                    //  don't already know the correct password.)
                    loginAttempt.Phase2HashOfIncorrectPassword = phase2HashOfProvidedPassword;
                    loginAttempt.EncryptAndWriteIncorrectPassword(passwordProvidedByClient,
                        account.EcPublicAccountLogKey);

                    // Next, if it's possible to declare more about this outcome than simply that the 
                    // user provided the incorrect password, let's do so.
                    // Since users who are unsure of their passwords may enter the same username/password twice, but attackers
                    // don't learn anything from doing so, we'll want to account for these repeats differently (and penalize them less).
                    // We actually have two data structures for catching this: A large sketch of clientsIpHistory/account/password triples and a
                    // tiny LRU cache of recent failed passwords for this account.  We'll check both.

                    bool repeatFailureIdentifiedByAccountHashes =
                        account.RecentIncorrectPhase2Hashes.Contains(phase2HashOfProvidedPassword);
                    if (!repeatFailureIdentifiedByAccountHashes)
                    {
                        account.RecentIncorrectPhase2Hashes.Add(phase2HashOfProvidedPassword);
                        accountChanged = true;
                    }

                    loginAttempt.Outcome = (repeatFailureIdentifiedByAccountHashes || didSketchIndicateThatTheSameGuessHasBeenMadeRecently)
                        ? AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPassword;
                }

                // When there's little data, we want to make sure the popularity is not overstated because           
                // (e.g., if we've only seen 10 account failures since we started watching, it would not be
                //  appropriate to conclude that something we've seen once before represents 10% of likely guesses.)
                loginAttempt.PasswordsPopularityAmongFailedGuesses = popularityOfPasswordAmongIncorrectPasswords;

            }

            // Preform an analysis of the IPs past beavhior to determine if the IP has been performing so many failed guesses
            // that we disallow logins even if it got the right password.  We call this even when the submitted password is
            // correct lest we create a timing indicator (slower responses for correct passwords) that attackers could use
            // to guess passwords even if we'd blocked their IPs.
            IpHistory ip = await ipHistoryGetTask;

            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsValid)
            {
                double blockingThreshold = _options.BlockThresholdPopularPassword;
                if (loginAttempt.PasswordsPopularityAmongFailedGuesses <
                    _options.ThresholdAtWhichAccountsPasswordIsDeemedPopular)
                    blockingThreshold *= _options.BlockThresholdMultiplierForUnpopularPasswords;
                if (ip.CurrentBlockScore > blockingThreshold)
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsValidButBlocked;
            }        
            
            double popularityMultiplier = PopularityPenaltyMultiplier(popularityOfPasswordAmongIncorrectPasswords); ;
            
            ip.CurrentBlockScore.Add(CalculatePenalty(ip, loginAttempt,account, ref accountChanged));

            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidNoSuchAccount ||
                loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
            {
                if (passwordFrequencies.Proportions.Last().AsDouble > 0 ||
                    // FIXME with configuration values
                    passwordLadder.CountObservationsForGivenConfidence(1d/(1000d*1000d*1000d)) > 5)
                {
                    Task background1 = passwordFrequencies.RecordObservationAsync(cancellationToken: cancellationToken);
                }

                Task background2 = passwordLadder.StepAsync(cancellationToken);
            }

            //BlockingScoresForEachAlgorithm blockingScoresForEachAlgorithm = await UpdateOutcomeIfIpShouldBeBlockedAsync(
            //    loginAttempt, ip, serversResponsibleForCachingThisAccount, cancellationToken);

            // Add this LoginAttempt to our history of all login attempts for this IP address.
            //ip.RecordLoginAttempt(loginAttempt);

            // Update the account record to incorporate what we've learned as a result of processing this login loginAttempt.
            // If this is a success and there's a cookie, it will update the set of cookies that have successfully logged in
            // to include this one.
            // If it's a failure, it will add this to the list of failures that we may be able to learn about later when
            // we know what the correct password is and can determine if it was a typo.
            //_userAccountClient.UpdateForNewLoginAttemptInBackground(loginAttempt,
            //    timeout: DefaultTimeout,
            //    serversResponsibleForCachingThisAccount: serversResponsibleForCachingThisAccount,
            //    cancellationToken: cancellationToken);

            //// Mark this task as completed by removing it from the Dictionary of tasks storing loginAttemptsInProgress
            //// and by putting the login loginAttempt into our cache of recent login attempts.            
            //string key = loginAttempt.UniqueKey;
            //_loginAttemptCache.Add(key, loginAttempt);
            //lock (_loginAttemptsInProgress)
            //{
            //    if (_loginAttemptsInProgress.ContainsKey(key))
            //    {
            //        _loginAttemptsInProgress.Remove(key);
            //    }
            //}

            // We return the processed login loginAttempt so that the caller can determine its outcome and,
            // in the event that the caller wants to keep a copy of the record, ensure that it has the
            // most up-to-date copy.

            if (accountChanged)
            {
                Task backgroundTask = userAccountContext.SaveChangesAsync(new CancellationToken());
            }

            return new Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>(loginAttempt, blockingScoresForEachAlgorithm);
        }


        /// <summary>
        /// When memory runs low, call this function to remove a fraction of the space used by non-fixed-size data structures
        /// (In this case, it is the history of information about IP addresses)
        /// </summary>
        public void ReduceMemoryUsage(object sender, MemoryUsageLimiter.ReduceMemoryUsageEventParameters parameters)
        {
            _ipHistoryCache.RecoverSpace(parameters.FractionOfMemoryToTryToRemove);
        }
    }
}
