using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using System.Security.Cryptography;
using System.Threading;
using StopGuessing.Clients;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace StopGuessing.Controllers
{
    
    [Route("api/[controller]")]
    public class LoginAttemptController : Controller
    {
        private readonly IStableStore _stableStore;
        private readonly BlockingAlgorithmOptions _options;
        private readonly PasswordPopularityTracker _passwordPopularityTracker;
        private readonly FixedSizeLruCache<string, LoginAttempt> _cacheOfRecentLoginAttempts;
        private readonly Dictionary<string, Task<LoginAttempt>> _loginAttemptsInProgress;
        private UserAccountClient _userAccountClient;
        private readonly SelfLoadingCache<IPAddress, IpHistory> _ipHistoryCache;

        public LoginAttemptController(
            LoginAttemptClient loginAttemptClient,
            UserAccountClient userAccountClient,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions blockingOptions,
            IStableStore stableStore)
        {
            _options = blockingOptions;//optionsAccessor.Options;
            _stableStore = stableStore;
            _passwordPopularityTracker = new PasswordPopularityTracker("FIXME-uniquekeyfromconfig"
                //FIXME -- use configuration to get options here"FIXME-uniquekeyfromconfig", thresholdRequiredToTrackPreciseOccurrences: 10);
                );
            
            _cacheOfRecentLoginAttempts = new FixedSizeLruCache<string, LoginAttempt>(80000);  // FIXME -- use configuration file for size
            _loginAttemptsInProgress = new Dictionary<string, Task<LoginAttempt>>();
            _ipHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>(
                (id, cancellationToken) =>
                {
                    return Task.Run(() => new IpHistory(id), cancellationToken);
                    // FUTURE -- option to load from stable store
                });
            loginAttemptClient.SetLoginAttemptController(this);     
            SetUserAccountClient(userAccountClient);
            memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;
        }


        public void SetUserAccountClient(UserAccountClient userAccountClient)
        {
            _userAccountClient = userAccountClient;
        }

        // GET: api/LoginAttempt
        [HttpGet]
        public IEnumerable<LoginAttempt> Get()
        {
            throw new NotImplementedException("Cannot enumerate all login attempts");
        }

        // GET api/LoginAttempt/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAsync(string id,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // FUTURE -- if we ever have a client that would call this, we'd probably want it to go to stable store and such
            return await Task.Run(() =>
            {
                LoginAttempt result;
                _cacheOfRecentLoginAttempts.TryGetValue(id, out result);
                if (result == null)
                {
                    return HttpNotFound();
                }
                else
                {
                    return (IActionResult) new ObjectResult(result);
                }
            }, cancellationToken);
        }

        // WriteAccountAsync login attempts
        // POST api/LoginAttempt/
        [HttpPost]
        public async Task<IActionResult> UpdateLoginAttemptOutcomesAsync([FromBody]List<LoginAttempt> loginAttemptsWithUpdatedOutcomes, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await new Task( () =>
            {
                Parallel.ForEach(
                    loginAttemptsWithUpdatedOutcomes.ToLookup(attempt => attempt.AddressOfClientInitiatingRequest),
                    loginAttemptsWithUpdatedOutcomesByIp =>
                    {
                        IpHistory ip = _ipHistoryCache.GetAsync(loginAttemptsWithUpdatedOutcomesByIp.Key, cancellationToken).Result;
                        ip.UpdateLoginAttemptsWithNewOutcomes(loginAttemptsWithUpdatedOutcomesByIp.ToList());
                    });
            });
            return new HttpOkResult();
        }

        // PUT api/LoginAttempt/clientsIpHistory-address-datetime
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAsync(string id, [FromBody] LoginAttempt loginAttempt,
            [FromBody] string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (id != loginAttempt.UniqueKey)
            {
                throw new Exception("The id assigned to the login does not match it's unique key.");
            }

            if (loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt == null)
            {
                // Unless the address of the server that received this login attempt from the user client has already
                // been specified, we'll assume it was the server initiating this put request.
                loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt = HttpContext.Connection.RemoteIpAddress;
            }

            LoginAttempt result = await PutAsync(loginAttempt, passwordProvidedByClient, cancellationToken);
            return new ObjectResult(result);
        }


        public async Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string key = loginAttempt.UniqueKey;

            if (loginAttempt.Outcome != AuthenticationOutcome.Undetermined)
            {
                // The outcome is already set so we simply write to the cache and stable store.
                WriteLoginAttemptInBackground(loginAttempt, cancellationToken);
                return loginAttempt;
            }
            else
            {
                // We need to calculate the outcome before we can write to the stable store
                Task<LoginAttempt> putTask;

                lock (_loginAttemptsInProgress)
                {
                    if (_cacheOfRecentLoginAttempts.ContainsKey(key))
                    {
                        // Another thread already did this put
                        return _cacheOfRecentLoginAttempts[key];
                    }
                    else if (_loginAttemptsInProgress.TryGetValue(key, out putTask))
                    {
                        // Another thread already started this put, and we are waiting for the outcome.
                    }
                    else
                    {
                        // This thread will need to perform the put
                        // FUTURE -- does this release the lock fast enough?
                        _loginAttemptsInProgress[key] = putTask =
                            ExecutePutAsync(loginAttempt, passwordProvidedByClient, cancellationToken);
                    }
                }
                LoginAttempt result = await putTask;
                return result;
            }
        }

        // DELETE api/LoginAttempt/<key>
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            // no-op
            return new HttpNotFoundResult();
        }


        /// <summary>
        /// Combines update the local attempt cache with an asyncronous write to stable store.
        /// </summary>
        /// <param name="attempt">The attempt to write to cache/stable store.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        protected async Task WriteAttemptAsync(LoginAttempt attempt, CancellationToken cancellationToken) // = default(CancellationToken)
        {
            _cacheOfRecentLoginAttempts[attempt.UniqueKey] = attempt;
            await _stableStore.WriteLoginAttemptAsync(attempt, cancellationToken);
        }

        /// <summary>
        /// Combines update the local attempt cache with a background write to stable store.
        /// </summary>
        /// <param name="attempt">The attempt to write to cache/stable store.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        protected void WriteLoginAttemptInBackground(LoginAttempt attempt, CancellationToken cancellationToken)
        {
            Task.Run( () => WriteAttemptAsync(attempt, cancellationToken), cancellationToken);
        }


        /// <summary>
        /// This analysis will examine the client IP's previous failed attempts to login to this account
        /// to determine if any failed attempts were due to typos.  
        /// </summary>
        /// <param name="clientsIpHistory">Records of this client's previous attempts to examine.</param>
        /// <param name="account">The account that the client is currently trying to login to.</param>
        /// <param name="correctPassword">The correct password for this account.  (We can only know it because
        /// the client must have provided the correct one this attempt.)</param>
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
                clientsIpHistory.RecentLoginFailures.MostRecentToOldest.Where(
                    attempt => attempt.UsernameOrAccountId == account.UsernameOrAccountId &&
                               attempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword &&
                               !string.IsNullOrEmpty(attempt.EncryptedIncorrectPassword))
                );

            foreach (LoginAttempt updatedLoginAttempt in loginAttemptsWithOutcompesUpdatedDueToTypoAnalysis)
            {
                WriteLoginAttemptInBackground(updatedLoginAttempt, default(CancellationToken));
            }
        }

        /// <summary>
        /// Multiply the penalty to be applied to a failed login attempt based on the popularity of the password that was guessed.
        /// </summary>
        /// <param name="popularityLevel">The popularity of the password as a fraction (e.g. 0.0001 means 1 in 10,000 incorrect
        /// passwords were this password.)</param>
        /// <returns></returns>
        private double PopularityPenaltyMultiplier(double popularityLevel)
        {
            double penalty = 1d;
            foreach (PenaltyForReachingAPopularityThreshold penaltyForReachingAPopularityThreshold in _options.PenaltyForReachingEachPopularityThreshold)
            {
                if (penalty < penaltyForReachingAPopularityThreshold.Penalty &&
                    popularityLevel >= penaltyForReachingAPopularityThreshold.PopularityThreshold)
                    penalty = penaltyForReachingAPopularityThreshold.Penalty;
            }
            return penalty;
        }

        /// <returns></returns>
        public async Task UpdateOutcomeIfIpShouldBeBlockedAsync(LoginAttempt loginAttempt, IpHistory ip)
        {
            // Always allow a login if there's a valid device cookie associate with this account
            // FUTURE -- we probably want to do something at the account level to track targetted attacks
            //          against individual accounts and lock them out
            if (loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                return;

            // Choose a block threshold based on whether the provided password was popular or not.
            // (If the actual password isn't popular, the attempt will be blocked either way.)
            double blockThreshold = loginAttempt.PasswordsPopularityAmongFailedGuesses >= _options.ThresholdAtWhichAccountsPasswordIsDeemedPopular ?
                _options.BlockThresholdPopularPassword : _options.BlockThresholdUnpopularPassword;


            // As we account for successes, we'll want to make sure we never give credit for more than one success
            // per account.  This set tracks the accounts we've already given credit for
            HashSet<string> accountsUsedForSuccessCredit = new HashSet<string>();

            // Start the scoring at zero, with a higher score indicating a greater chance this is a brute-force
            // attack.  (We'll conclude it's a brute force attack if the score goes over the BlockThreshold.)
            double bruteLikelihoodScore = 0;


            // This algoirthm estimates the likelihood that the IP is engaged in a brute force attack and should be
            // blocked by examining login failures from the IP from most-recent to least-recent, adjusting (increasing) the
            // BruteLikelihoodScore to account for each failure based on its type (e.g., we penalize known
            // typos less than other login attempts that use popular password guesses).
            //
            // Successful logins reduce our estimated likelihood that the IP address.
            // We also account for successful logins in reverse chronological order, but do so _lazily_:
            // we only only examine the minimum number of successes needed to take the likelihood score below
            // the block threshold.  We do so because we want there to be a cost to an account of having its
            // successes used to prevent an IP from being blocked, otherwise attackers could use a few fake
            // accounts to intersperse lots of login successes between every failure and never be detected.

            // These counters track how many successes we have stepped through in search of login successes
            // that can be used to offset login failures when accounting for the likelihood the IP is attacking
            int successesWithoutCreditsIndex = 0;
            int successesWithCreditsIndex = 0;

            List<LoginAttempt> copyOfRecentLoginFailures;
            List<LoginAttempt> copyOfRecentLoginSuccessesAtMostOnePerAccount;
            lock (ip.RecentLoginFailures)
            {
                copyOfRecentLoginFailures = 
                    ip.RecentLoginFailures.MostRecentToOldest.ToList();
                copyOfRecentLoginSuccessesAtMostOnePerAccount = 
                    ip.RecentLoginSuccessesAtMostOnePerAccount.MostRecentToOldest.ToList();
            }

            // We step through failures in reverse chronological order (from the 0th element of the sequence on up)
            for (int failureIndex = 0;
                failureIndex < copyOfRecentLoginFailures.Count && bruteLikelihoodScore <= blockThreshold;
                failureIndex++)
            {

                // Get the failure at the index in the sequence.
                LoginAttempt failure = copyOfRecentLoginFailures[failureIndex];

                // Stop tracking failures that are too old in order to forgive IPs that have tranferred to benign owner
                if ((DateTimeOffset.Now - failure.TimeOfAttempt) > _options.ExpireFailuresAfter)
                    break;

                // Increase the brute-force likelihood score based on the type of failure.
                // (Failures that indicate a greater chance of being a brute-force attacker, such as those
                //  using popular passwords, warrant higher scores.)
                switch (failure.Outcome)
                {
                    case AuthenticationOutcome.CredentialsInvalidNoSuchAccount:
                        bruteLikelihoodScore += _options.PenaltyForInvalidAccount *
                                                PopularityPenaltyMultiplier(failure.PasswordsPopularityAmongFailedGuesses);
                        break;
                    case AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely:
                        bruteLikelihoodScore += _options.PenaltyForInvalidPasswordPerLoginTypo;
                        break;
                    case AuthenticationOutcome.CredentialsInvalidIncorrectPassword:
                    case AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely:
                        bruteLikelihoodScore += _options.PenaltyForInvalidPasswordPerLoginRarePassword *
                                                PopularityPenaltyMultiplier(failure.PasswordsPopularityAmongFailedGuesses);
                        break;
                    case AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword:
                        // We ignore repeats of incorrect passwords we've already accounted for
                        // No penalty
                        break;                    
                }

                if (bruteLikelihoodScore > blockThreshold)
                {
                    // The most recent failure took us above the threshold at which we would make the decision to block
                    // this login.  However, there are successes we have yet to account for that might reduce the likelihood score.
                    // We'll account for successes that are more recent than that last failure until we either
                    //    (a) run out of successes, or
                    //    (b) reduce the score below the threshold
                    //        (in which case we'll save any remaining successes to use if we again go over the threshold.)

                    while (bruteLikelihoodScore > blockThreshold &&
                           successesWithCreditsIndex < copyOfRecentLoginSuccessesAtMostOnePerAccount.Count &&
                           copyOfRecentLoginSuccessesAtMostOnePerAccount[successesWithCreditsIndex].TimeOfAttempt >
                           failure.TimeOfAttempt)
                    {
                        // Start with successes for which, on a prior calculation of ShouldBlock, we already removed
                        // a credit from the account that logged in via a call to TryGetCredit.

                        LoginAttempt success =
                            copyOfRecentLoginSuccessesAtMostOnePerAccount[successesWithCreditsIndex];

                        if ( // We have not already used this account to reduce the BruteLikelihoooScore
                             // earlier in this calculation (during this call to ShouldBlock)
                            !accountsUsedForSuccessCredit.Contains(success.UsernameOrAccountId) &&
                            // We HAVE received the credit during a prior recalculation
                            // (during a prior call to ShouldBlock)                        
                            success.HasReceivedCreditForUseToReduceBlockingScore)
                        {
                            // Ensure that we don't count this success more than once
                            accountsUsedForSuccessCredit.Add(success.UsernameOrAccountId);

                            // Reduce the brute-force attack likelihood score to account for this past successful login
                            bruteLikelihoodScore += _options.RewardForCorrectPasswordPerAccount;
                        }
                        successesWithCreditsIndex++;
                    }

                    while (bruteLikelihoodScore > blockThreshold &&
                           successesWithoutCreditsIndex < copyOfRecentLoginSuccessesAtMostOnePerAccount.Count &&
                           copyOfRecentLoginSuccessesAtMostOnePerAccount[successesWithoutCreditsIndex].TimeOfAttempt >
                           failure.TimeOfAttempt)
                    {
                        // If we still are above the threshold, use successes for which we will need to remove a new credit
                        // from the account responsible for the success via TryGetCredit.

                        LoginAttempt success =
                            copyOfRecentLoginSuccessesAtMostOnePerAccount[successesWithoutCreditsIndex];

                        if ( // We have not already used this account to reduce the BruteLikelihoodScore
                             // earlier in this calculation (during this call to ShouldBlock)
                            !accountsUsedForSuccessCredit.Contains(success.UsernameOrAccountId) &&
                            // We have NOT received the credit during a prior recalculation
                            // (during a prior call to ShouldBlock)                        
                            !success.HasReceivedCreditForUseToReduceBlockingScore)
                        {
                            // FUTURE -- We may wnat to parallelize to get rid of the latency.  However, it may well not be worth
                            // worth the added complexity, since requests for credits should rarely (if ever) occur more than
                            // once per login

                            // Reduce credit from the account for the login so that the account cannot be used to generate
                            // an unlimited number of login successes.
                            if (await _userAccountClient.TryGetCreditAsync(success.UsernameOrAccountId))
                            {
                                // There exists enough credit left in the account for us to use this success.

                                // Ensure that we don't count this success more than once
                                accountsUsedForSuccessCredit.Add(success.UsernameOrAccountId);

                                // Reduce the brute-force attack likelihood score to account for this past successful login
                                bruteLikelihoodScore += _options.RewardForCorrectPasswordPerAccount;
                            }

                        }
                        successesWithoutCreditsIndex++;
                    }

                }

                // The brute-force attack likelihood score should never fall below 0, even after a success credit.
                if (bruteLikelihoodScore < 0d)
                    bruteLikelihoodScore = 0d;

                if (bruteLikelihoodScore >= blockThreshold)
                {
                    if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsValid)
                    {
                        loginAttempt.Outcome = AuthenticationOutcome.CredentialsValidButBlocked;
                    }
                    break;
                }

            }
        }


        /// <summary>
        /// Add a LoginAttempt, along the way determining whether that attempt should be allowed
        /// (the user authenticated) or denied.
        /// </summary>
        /// <param name="loginAttempt">The login attempt record to be stored.</param>
        /// <param name="passwordProvidedByClient">The plaintext password provided by the client.</param>
        /// <param name="cancellationToken">To allow this async method to be cancelled.</param>
        /// <returns>If the password is correct and the IP not blocked, returns AuthenticationOutcome.CredentialsValid.
        /// Otherwise, it returns a different AuthenticationOutcome.
        /// The client should not be made aware of any information beyond whether the login was allowed or not.</returns>
        protected async Task<LoginAttempt> ExecutePutAsync(LoginAttempt loginAttempt, string passwordProvidedByClient, CancellationToken cancellationToken)
        {   
            // We'll need to know more about the IP making this attempt, so let's get the historical information
            // we've been keeping about it.
            Task<IpHistory> ipHistoryGetTask = _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest, cancellationToken);

            // Get a copy of the UserAccount record for the account that the client wants to authenticate as.
            UserAccount account = await _userAccountClient.GetAsync(loginAttempt.UsernameOrAccountId, cancellationToken);

            if (account == null)
            {
                // This appears to be an attempt to login to a non-existent account, and so all we need to do is
                // mark it as such.  However, since it's possible that users will forget their account names and
                // repeatedly attempt to login to a nonexistent account, we'll want to track whether we've seen
                // this clientsIpHistory/account/password tripple before and note in the outcome if it's a repeat so that.
                // the IP need not be penalized for issuign a query that isn't getting it any information it
                // didn't already have.
                loginAttempt.Outcome = _passwordPopularityTracker.HasNonexistentAccountIpPasswordTripleBeenSeenBefore(
                    loginAttempt.AddressOfClientInitiatingRequest, loginAttempt.UsernameOrAccountId, passwordProvidedByClient)
                    ? AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount
                    : AuthenticationOutcome.CredentialsInvalidNoSuchAccount;
            }
            else
            {
                //
                // This is an attempt to login to a valid (existent) account.
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
                byte[] phase1HashOfProvidedPassword = account.ComputePhase1Hash(passwordProvidedByClient);
                // Since we can't store the phase1 hash (it can decrypt that EC key) we instead store a simple (SHA256)
                // hash of the phase1 hash.
                string phase2HashOfProvidedPassword = Convert.ToBase64String(SHA256.Create().ComputeHash((phase1HashOfProvidedPassword)));

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
                }
                else
                {
                    //
                    // The password was invalid.  There's lots of work to do to facilitate future analysis
                    // about why this LoginAttempt failed.

                    // So that we can analyze this failed attempt in the future, we'll store the (phase 2) hash of the 
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

                    // The triple sketch will automatically record that we saw this triple when we check to see if we've seen it before.
                    bool repeatFailureIdentifiedBySketch =
                        _passwordPopularityTracker.HasNonexistentAccountIpPasswordTripleBeenSeenBefore(
                            loginAttempt.AddressOfClientInitiatingRequest, loginAttempt.UsernameOrAccountId,
                            passwordProvidedByClient);

                    bool repeatFailureIdentifiedByAccountHashes =
                        account.PasswordVerificationFailures.Count(failedAttempt =>
                            failedAttempt.Phase2HashOfIncorrectPassword == phase2HashOfProvidedPassword) > 0;

                    loginAttempt.Outcome = (repeatFailureIdentifiedByAccountHashes || repeatFailureIdentifiedBySketch)
                        ? AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPassword;
                }
                
                // If the password is correct, we can decrypt the EcPrivateAccountKey and perform analysis to provide
                // enlightenment into past failures that may help us to evaluate whether they were malicious.  Specifically,
                // we may be able to detrmine if past failures were due to typos.
                if (isSubmittedPasswordCorrect)
                {
                    // Determine if any of the outcomes for login attempts from the client IP for this request were the result of typos,
                    // as this might impact our decision about whether or not to block this client IP in response to its past behaviors.
                    UpdateOutcomesUsingTypoAnalysis(await ipHistoryGetTask,
                        account, passwordProvidedByClient, phase1HashOfProvidedPassword);

                    // In the background, update any outcomes for logins to this account from other IPs, so that if those
                    // IPs attempt to login to any account in the future we can gain insight as to whether those past logins
                    // were typos or non-typos.
                    _userAccountClient.UpdateOutcomesUsingTypoAnalysisInBackground(account.UsernameOrAccountId,
                        passwordProvidedByClient, phase1HashOfProvidedPassword,
                        loginAttempt.AddressOfClientInitiatingRequest, cancellationToken);
                }

                // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
                // as we are most concerned about frequently-guessed passwords.
                Proportion popularity = _passwordPopularityTracker.GetPopularityOfPasswordAmongFailures(
                    passwordProvidedByClient, isSubmittedPasswordCorrect, confidenceLevel: _options.PopularityConfidenceLevel,
                    minDenominatorForPasswordPopularity: _options.MinDenominatorForPasswordPopularity);
                // When there's little data, we want to make sure the popularity is not overstated because           
                // (e.g., if we've only seen 10 account failures since we started watching, it would not be
                //  appropriate to conclude that something we've seen once before represents 10% of likely guesses.)
                loginAttempt.PasswordsPopularityAmongFailedGuesses =
                    popularity.MinDenominator(_options.MinDenominatorForPasswordPopularity).AsDouble;

                // Preform an analysis of the IPs past beavhior to determine if the IP has been performing so many failed guesses
                // that we disallow logins even if it got the right password.  We call this even when the submitted password is
                // correct lest we create a timing indicator (slower responses for correct passwords) that attackers could use
                // to guess passwords even if we'd blocked their IPs.
                IpHistory ip = await ipHistoryGetTask;
               await UpdateOutcomeIfIpShouldBeBlockedAsync(loginAttempt, ip);

                // Add this LoginAttempt to our history of all login attempts for this IP address.
                ip.RecordLoginAttempt(loginAttempt);

                // Update the account record to incorporate what we've learned as a result of processing this login attempt.
                // If this is a success and there's a cookie, it will update the set of cookies that have successfully logged in
                // to include this one.
                // If it's a failure, it will add this to the list of failures that we may be able to learn about later when
                // we know what the correct password is and can determine if it was a typo.
                _userAccountClient.UpdateForNewLoginAttemptInBackground(account.UsernameOrAccountId, loginAttempt,
                    cancellationToken);
            }

            // Write the login attempt to stable store for future long-term auditing and analysis.
            WriteLoginAttemptInBackground(loginAttempt, cancellationToken);

            // Mark this task as completed by removing it from the Dictionary of tasks storing loginAttemptsInProgress
            // and by putting the login attempt into our cache of recent login attempts.            
            string key = loginAttempt.UniqueKey;
            _cacheOfRecentLoginAttempts.Add(key, loginAttempt);
            lock (_loginAttemptsInProgress)
            {
                _loginAttemptsInProgress.Remove(key);
            }

            // We return the processed login attempt so that the caller can determine its outcome and,
            // in the event that the caller wants to keep a copy of the record, ensure that it has the
            // most up-to-date copy.
            return loginAttempt;
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
