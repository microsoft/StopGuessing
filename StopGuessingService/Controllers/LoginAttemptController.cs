using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using StopGuessing.EncryptionPrimitives;
using Microsoft.Framework.OptionsModel;
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
        private readonly SelfLoadingCache<System.Net.IPAddress, IpHistory> _ipHistoryCache;

        public LoginAttemptController(
            IOptions<BlockingAlgorithmOptions> optionsAccessor, IStableStore stableStore, 
            PasswordPopularityTracker passwordPopularityTracker, FixedSizeLruCache<string, LoginAttempt> cacheOfRecentLoginAttempts,
            Dictionary<string, Task<LoginAttempt>> loginAttemptsInProgress, 
            SelfLoadingCache<System.Net.IPAddress, IpHistory> ipHistoryCache)
        {
            _options = optionsAccessor.Options;
            _stableStore = stableStore;
            _passwordPopularityTracker = passwordPopularityTracker;
            _cacheOfRecentLoginAttempts = cacheOfRecentLoginAttempts;
            _loginAttemptsInProgress = loginAttemptsInProgress;
            _ipHistoryCache = ipHistoryCache;
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

        // GET api/values/5
        [HttpGet("{id:string}")]
        public async Task<LoginAttempt> GetAsync(string id,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // FUTURE -- if we ever have a client that would call this, we'd probably want it to go to stable store and such
            return await new Task<LoginAttempt>(() =>
            {
                LoginAttempt result;
                lock (_cacheOfRecentLoginAttempts)
                {
                    _cacheOfRecentLoginAttempts.TryGetValue(id, out result);
                }
                return result;
            });
        }

        // WriteAccountAsync login attempts
        // POST api/values
        [HttpPost]
        public async Task UpdateLoginAttemptOutcomesAsync([FromBody]List<LoginAttempt> loginAttemptsWithUpdatedOutcomes, 
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
        }

        // PUT api/LoginAttempt/ip-address-datetime
        [HttpPut("{id:string}")]
        public async Task<LoginAttempt> PutAsync(string id, [FromBody]LoginAttempt loginAttempt, [FromBody]string passwordProvidedByClient,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt == null)
            {
                // Unless the address of the server that received this login attempt from the user client has already
                // been specified, we'll assume it was the server initiating this put request.
                loginAttempt.AddressOfServerThatInitiallyReceivedLoginAttempt = Context.Connection.RemoteIpAddress;
            }

            // To ensure idempotency, make sure that this put has not already been performed or is not already
            // in progress by a concurrent thread.

            string key = loginAttempt.ToUniqueKey();
            if (id != key)
            {
                throw new Exception("The id assigned to the login does not match it's unique key.");
            }

            if (loginAttempt.Outcome == AuthenticationOutcome.Undetermined)
            {
                // We need to calculate the outcome before we can write to the stable store
                Task<LoginAttempt> putTask;

                lock (_cacheOfRecentLoginAttempts)
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
                return await putTask;
            }
            else
            {
                // The outcome is already set so we simply write to the cache and stable store.
                lock (_cacheOfRecentLoginAttempts)
                {
                    _cacheOfRecentLoginAttempts[loginAttempt.ToUniqueKey()] = loginAttempt;
                }
                await _stableStore.WriteLoginAttemptAsync(loginAttempt, cancellationToken);
                return loginAttempt;
            }
        }

        // DELETE api/LoginAttempt/<key>
        [HttpDelete("{id:string}")]
        public void Delete(string id)
        {
            // no-op
        }





        protected void UpdateOutcomeUsingTypoAnalysis(LoginAttempt loginAttempt, string correctPassword, ECDiffieHellmanCng ecPrivateAccountLogKey)
        {
            if (loginAttempt.Outcome != AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
                return;
            if (loginAttempt.EncryptedIncorrectPassword == null)
                return;
            try
            {
                string incorrectPasswordFromThisAttempt = loginAttempt.DecryptAndGetIncorrectPassword(ecPrivateAccountLogKey);

                bool likelyTypo = EditDistance.Calculate(incorrectPasswordFromThisAttempt, correctPassword) <= 2;
                loginAttempt.Outcome = likelyTypo ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                                     : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;
            }
            catch (Exception)
            {
                // An exception is likely due to an incorrect key (perhaps outdated).
                // Since we simply can't do anything with a record we can't Decrypt, we carry on
                // as if nothing ever happened.  No.  Really.  Nothing to see here.
            }

        }

        protected ECDiffieHellmanCng DecryptEcPrivateAccountLogKey(
            byte[] ecPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
            byte[] phase1HashOfCorrectPassword)
        {
            byte[] ecPrivateAccountLogKeyAsBytes = Encryption.DecryptAescbc(
                                ecPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                                phase1HashOfCorrectPassword.Take(16).ToArray(),
                                checkAndRemoveHmac: true);
            return new ECDiffieHellmanCng(CngKey.Import(ecPrivateAccountLogKeyAsBytes, CngKeyBlobFormat.EccPrivateBlob));
        }


        protected async Task UpdateOutcomesUsingTypoAnalysisAsync(
            LoginAttempt loginAttempt,
            byte[] ecPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
            string correctPassword,
            byte[] phase1HashOfCorrectPassword)
        {

            // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
            ECDiffieHellmanCng ecPrivateAccountLogKey = null;

            IpHistory ip = await _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest);
            if (ip != null)
            {
                foreach (LoginAttempt previousAttempt in ip.RecentLoginFailures.MostRecentToOldest)
                {
                    if (previousAttempt.Account == loginAttempt.Account &&
                        previousAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
                    {
                        if (ecPrivateAccountLogKey == null)
                        {
                            try {
                                ecPrivateAccountLogKey = DecryptEcPrivateAccountLogKey(ecPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
                            }
                            catch (Exception)
                            {
                                // There's a problem with the key that prevents us from decrypting it.  We won't be able to do this analysis.
                                // FUTURE -- genrate a new EC key in this situation?
                                return;
                            }
                        }
                        UpdateOutcomeUsingTypoAnalysis(previousAttempt, correctPassword, ecPrivateAccountLogKey);
                    }
                }
            }
        }

        public double PopularityPenaltyMultiplier(double popularityLevel)
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
                            !accountsUsedForSuccessCredit.Contains(success.Account) &&
                            // We HAVE received the credit during a prior recalculation
                            // (during a prior call to ShouldBlock)                        
                            success.HasReceivedCreditForUseToReduceBlockingScore)
                        {
                            // Ensure that we don't count this success more than once
                            accountsUsedForSuccessCredit.Add(success.Account);

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
                            !accountsUsedForSuccessCredit.Contains(success.Account) &&
                            // We have NOT received the credit during a prior recalculation
                            // (during a prior call to ShouldBlock)                        
                            !success.HasReceivedCreditForUseToReduceBlockingScore)
                        {
                            // FUTURE Stuart asked on 2015-09-23 -- should we parallelize to get rid of the latency?
                            // Current thinking is not worth complexity, since requests for credits should rarely (if ever) occur more than
                            // once per login

                            // Reduce credit from the account for the login so that the account cannot be used to generate
                            // an unlimited number of login successes.
                            if (await _userAccountClient.TryGetCreditAsync(success.Account))
                            {
                                // There exists enough credit left in the account for us to use this success.

                                // Ensure that we don't count this success more than once
                                accountsUsedForSuccessCredit.Add(success.Account);

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
            // Check only uses recent cache.
            UserAccount account = await _userAccountClient.GetAsync(loginAttempt.Account, cancellationToken);
            if (account == null)
            {
                loginAttempt.Outcome = _passwordPopularityTracker.HasFailedIpAccountPasswordTripleBeenSeenBefore(
                    loginAttempt.AddressOfClientInitiatingRequest, loginAttempt.Account, passwordProvidedByClient) ? 
                    AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount : AuthenticationOutcome.CredentialsInvalidNoSuchAccount;
            }
            else
            {

                if (loginAttempt.CookieProvidedByBrowser != null)
                {
                    // Replace the plaintext cookie with it's SHA256 hash in Base64 format
                    loginAttempt.CookieProvidedByBrowser =
                        Convert.ToBase64String(
                            SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(loginAttempt.CookieProvidedByBrowser)));
                    loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount =
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Contains(loginAttempt.CookieProvidedByBrowser);
                }

                // Test to see if the password is correct by calculating the Phase2Hash and comparing it with the Phase2 hash
                // in this record
                byte[] phase1HashOfProvidedPassword = ExpensiveHashFunctionFactory.Get(account.PasswordHashPhase1FunctionName)(
                    passwordProvidedByClient, account.SaltUniqueToThisAccount);
                byte[] phase2HashOfProvidedPassword = SHA256.Create().ComputeHash((phase1HashOfProvidedPassword));

                bool isSubmittedPasswordCorrect = phase2HashOfProvidedPassword.SequenceEqual(account.PasswordHashPhase2);

                if (isSubmittedPasswordCorrect)
                {
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsValid;
                }
                else
                {
                    bool repeatFailureIdentifiedBySketch =
                        _passwordPopularityTracker.HasFailedIpAccountPasswordTripleBeenSeenBefore(
                            loginAttempt.AddressOfClientInitiatingRequest, loginAttempt.Account, passwordProvidedByClient);
                    bool repeatFailureIdentifiedByAccountHashes =
                        account.Phase2HashesOfRecentlyIncorrectPasswords.Contains(phase2HashOfProvidedPassword);
                    if (!repeatFailureIdentifiedByAccountHashes)
                    {
                        // ReSharper disable once UnusedVariable
                        Task dontwaitformetocomplete = 
                            _userAccountClient.AddHashOfRecentIncorrectPasswordAsync(loginAttempt.Account, phase2HashOfProvidedPassword, cancellationToken);
                    }
                    loginAttempt.Outcome = (repeatFailureIdentifiedByAccountHashes || repeatFailureIdentifiedBySketch) ?
                        AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword :
                        AuthenticationOutcome.CredentialsInvalidIncorrectPassword;

                    loginAttempt.Phase2HashOfIncorrectPassword = phase2HashOfProvidedPassword;
                    loginAttempt.EncryptAndWriteIncorrectPassword(passwordProvidedByClient, account.EcPublicAccountLogKey);
                }

                //
                // If the password is correct, 
                // Record information about successes, failures.                
                if (isSubmittedPasswordCorrect)
                {
                    // WriteAccountAsync the outcomes for this IP address
                    await UpdateOutcomesUsingTypoAnalysisAsync(
                        loginAttempt, account.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, passwordProvidedByClient, phase1HashOfProvidedPassword);

                    // WriteAccountAsync outcomes for any other typos related to this account
                    _userAccountClient.UpdateOutcomesUsingTypoAnalysisInBackground(account.UsernameOrAccountId,
                        passwordProvidedByClient, phase1HashOfProvidedPassword, loginAttempt.AddressOfClientInitiatingRequest);
                }

                // Get the popularity of the current guess with respect to previous failed passwords
                Proportion popularity = _passwordPopularityTracker.GetPopularityOfPasswordAmongFailures(
                            passwordProvidedByClient, isSubmittedPasswordCorrect);
                loginAttempt.PasswordsPopularityAmongFailedGuesses =
                    popularity.MinDenominator(_options.MinDenominatorForPasswordPopularity).AsDouble;


                // Call the machine learning code here to revoke success status if
                // we believe this was a brute-forcer who got lucky.
                IpHistory ip = await _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest, cancellationToken);
                await UpdateOutcomeIfIpShouldBeBlockedAsync(loginAttempt, ip);
                ip.RecordLoginAttempt(loginAttempt);

                if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsValid &&
                    loginAttempt.CookieProvidedByBrowser != null)
                {
                    // Track browser cookies that have been used in successful logins
                    _userAccountClient.AddDeviceCookieFromSuccessfulLoginInBackground(account.UsernameOrAccountId, loginAttempt.CookieProvidedByBrowser);
                }

                if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
                {
                    // Note -- we currently only add the invalid passwords because the only reason the user account
                    // tracks these failures is to do a typo analysis. 

                    // Record account-specific failure information
                    _userAccountClient.AddLoginAttemptFailureInBackground(account.UsernameOrAccountId, loginAttempt);
                }

            }

            // WriteAccountAsync the login attempt to stable store
            // ReSharper disable once UnusedVariable -- unused variable being used to signify use of background task
            Task dontwaitforme =_stableStore.WriteLoginAttemptAsync(loginAttempt, cancellationToken);

            lock (_cacheOfRecentLoginAttempts)
            {
                string key = loginAttempt.ToUniqueKey();
                _cacheOfRecentLoginAttempts.Add(key, loginAttempt);
                _loginAttemptsInProgress.Remove(key);
            }

            await _stableStore.WriteLoginAttemptAsync(loginAttempt, cancellationToken);

            return loginAttempt;
        }

        public void Update()
        {

        }
    }
}
