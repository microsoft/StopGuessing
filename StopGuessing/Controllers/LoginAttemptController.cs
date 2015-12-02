#define Simulation
// FIXME above
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
using Newtonsoft.Json;
using StopGuessing.EncryptionPrimitives;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace StopGuessing.Controllers
{
    public interface ILoginAttemptController
    {
        Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken));

    }


    public class BlockingScoresForEachAlgorithm
    {
        public double Ours = 0;
        public double Industry = 0;
        public double SSH = 0;
    }

    [Route("api/[controller]")]
    public class LoginAttemptController : Controller, ILoginAttemptController
    {
        //private readonly IStableStore _stableStore;
        private readonly BlockingAlgorithmOptions _options;
        private readonly IBinomialLadderSketch _binomialLadderSketch;
        private readonly IFrequenciesProvider<string> _incorrectPasswordFrequenciesProvider;
        private readonly IStableStoreFactory<string, UserAccount> _userAccountContextFactory;
        //private readonly FixedSizeLruCache<string, LoginAttempt> _loginAttemptCache;
        private readonly AgingMembershipSketch _recentIncorrectPasswords;

        //private readonly Dictionary<string, Task<Tuple<LoginAttempt, BlockingScoresForEachAlgorithm>>>
        //    _loginAttemptsInProgress;

        private readonly SelfLoadingCache<IPAddress, IpHistory> _ipHistoryCache;
        //private readonly LoginAttemptClient _loginAttemptClient;

        private TimeSpan DefaultTimeout { get; } = new TimeSpan(0, 0, 0, 0, 500); // FUTURE use configuration value

        public LoginAttemptController(
            //LoginAttemptClient loginAttemptClient,
            IStableStoreFactory<string, UserAccount> userAccountContextFactory,
            IBinomialLadderSketch binomialLadderSketch,
            IFrequenciesProvider<string> incorrectPasswordFrequenciesProvider,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions blockingOptions
            //IStableStore stableStore
            )
        {
            _options = blockingOptions; //optionsAccessor.Options;
            //_stableStore = stableStore;
            _binomialLadderSketch = binomialLadderSketch;
            _incorrectPasswordFrequenciesProvider = incorrectPasswordFrequenciesProvider;

            _recentIncorrectPasswords = new AgingMembershipSketch(16, 128*1024); // FIXME -- more configurable?
            _userAccountContextFactory = userAccountContextFactory;
            _ipHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>( (address, cancellationToken) => Task.Run( () => new IpHistory(address, _options)));

            memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;
        }


        //// GET: api/LoginAttempt
        //[HttpGet]
        //public IEnumerable<LoginAttempt> Get()
        //{
        //    throw new NotImplementedException("Cannot enumerate all login attempts");
        //}

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
        public async Task<IActionResult> PutAsync(string id,
            [FromBody] LoginAttempt loginAttempt,
            [FromBody] string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (id != loginAttempt.UniqueKey)
            {
                throw new Exception("The id assigned to the login does not match it's unique key.");
            }

            return new ObjectResult(await PutAsync(loginAttempt,
                passwordProvidedByClient,
                cancellationToken: cancellationToken));
        }

        public async Task<LoginAttempt> PutAsync(
            LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
#if Simulation
            await DetermineLoginAttemptOutcomeAsync(
                loginAttempt,
                passwordProvidedByClient,
                cancellationToken: cancellationToken);
            return loginAttempt;
#else
            return await DetermineLoginAttemptOutcomeAsync(
                loginAttempt,
                passwordProvidedByClient,
                cancellationToken: cancellationToken);
#endif
        }

        public async Task PrimeCommonPasswordAsync(string passwordProvidedByClient,
            int numberOfTimesToPrime,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ILadder ladder = await _binomialLadderSketch.GetLadderAsync(passwordProvidedByClient, cancellationToken: cancellationToken);

            string easyHashOfPassword =
                Convert.ToBase64String(ManagedSHA256.Hash(Encoding.UTF8.GetBytes(passwordProvidedByClient)));
            IFrequencies frequencies = await _incorrectPasswordFrequenciesProvider.GetFrequenciesAsync(
                easyHashOfPassword,
                cancellationToken: cancellationToken);

            for (int i = 0; i < numberOfTimesToPrime; i++)
            {
                await ladder.StepAsync(cancellationToken);
                await frequencies.RecordObservationAsync(cancellationToken: cancellationToken);
            }
        }


        //public async Task<LoginAttempt> LocalPutAsync(LoginAttempt loginAttempt,
        //    string passwordProvidedByClient = null,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    return await DetermineLoginAttemptOutcomeAsync(
        //        loginAttempt,
        //        passwordProvidedByClient,
        //        cancellationToken: cancellationToken);
        //}

        // DELETE api/LoginAttempt/<key>
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            // no-op
            return new HttpNotFoundResult();
        }


        ///// <summary>
        ///// Store an updated LoginAttempt to the local cache, remote caches, and in stable store.
        ///// </summary>
        ///// <param name="loginAttempt">The loginAttempt to write to cache/stable store.</param>
        ///// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        ///// <param name="updateTheLocalCache"></param>
        ///// <param name="updateRemoteCaches"></param>
        ///// <param name="updateStableStore"></param>
        ///// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        //protected async Task WriteLoginAttemptAsync(
        //    LoginAttempt loginAttempt,
        //    List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
        //    bool updateTheLocalCache = true,
        //    bool updateRemoteCaches = true,
        //    bool updateStableStore = true,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    Task stableStoreTask = null;

        //    //if (updateTheLocalCache)
        //    //{
        //    //    // Write to the local cache on this server
        //    //    _loginAttemptCache.Add(loginAttempt.UniqueKey, loginAttempt);
        //    //}

        //    if (updateStableStore)
        //    {
        //        // Write to stable consistent storage (e.g. database) that the system is configured to use
        //        stableStoreTask = _stableStore.WriteLoginAttemptAsync(loginAttempt, cancellationToken);
        //    }

        //    if (updateRemoteCaches)
        //    {
        //        // Identify the servers that cache this LoginAttempt and will need their cache entries updated
        //        if (serversResponsibleForCachingThisLoginAttempt == null)
        //        {
        //            serversResponsibleForCachingThisLoginAttempt =
        //                _loginAttemptClient.GetServersResponsibleForCachingALoginAttempt(loginAttempt);
        //        }

        //        // Update the cache entries for this LoginAttempt on the remote servers.
        //        _loginAttemptClient.PutCacheOnlyBackground(loginAttempt,
        //            serversResponsibleForCachingThisLoginAttempt,
        //            cancellationToken: cancellationToken);
        //    }

        //    // If writing to stable store, wait until the write has completed before returning.
        //    if (stableStoreTask != null)
        //        await stableStoreTask;
        //}

        ///// <summary>
        ///// Store an updated LoginAttempt to the local cache, remote caches, and in stable store.
        ///// </summary>
        ///// <param name="attempt">The loginAttempt to write to cache/stable store.</param>
        ///// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        ///// <param name="updateTheLocalCache"></param>
        ///// <param name="updateRemoteCaches"></param>
        ///// <param name="updateStableStore"></param>
        ///// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        //protected void WriteLoginAttemptInBackground(LoginAttempt attempt,
        //    List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
        //    bool updateTheLocalCache = true,
        //    bool updateRemoteCaches = true,
        //    bool updateStableStore = true,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    Task.Run(() => WriteLoginAttemptAsync(attempt,
        //        serversResponsibleForCachingThisLoginAttempt,
        //        updateTheLocalCache,
        //        updateRemoteCaches,
        //        updateStableStore,
        //        cancellationToken),
        //        cancellationToken);
        //}


        /// <summary>
        /// This analysis will examine the client IP's previous failed attempts to login to this account
        /// to determine if any failed attempts were due to typos.  
        /// </summary>
        /// <param name="clientsIpHistory">Records of this client's previous attempts to examine.</param>
        /// <param name="account">The account that the client is currently trying to login to.</param>
        /// <param name="whenUtc"></param>
        /// <param name="correctPassword">The correct password for this account.  (We can only know it because
        /// the client must have provided the correct one this loginAttempt.)</param>
        /// <param name="phase1HashOfCorrectPassword">The phase1 hash of that correct password (which we could
        /// recalculate from the information in the previous parameters, but doing so would be expensive.)</param>
        /// <returns></returns>
        protected void AdjustBlockingScoreForPastTyposTreatedAsFullFailures(
            IpHistory clientsIpHistory,
            UserAccount account,
            DateTime whenUtc,
            string correctPassword,
            byte[] phase1HashOfCorrectPassword)
        {
            double credit = 0d;

            if (clientsIpHistory == null)
                return;

            LoginAttemptSummaryForTypoAnalysis[] recentPotentialTypos = clientsIpHistory.RecentPotentialTypos.ToArray();
            ECDiffieHellmanCng ecPrivateAccountLogKey = null;
            try
            {
#if Simulation
                foreach (SimulationConditionData cond in clientsIpHistory.SimulationConditions)
                    cond.AdjustScoreForPastTyposTreatedAsFullFailures(ref ecPrivateAccountLogKey, account,
                        whenUtc, correctPassword, phase1HashOfCorrectPassword);
#endif
                foreach (LoginAttemptSummaryForTypoAnalysis potentialTypo in recentPotentialTypos)
                {
                    if (potentialTypo.UsernameOrAccountId != account.UsernameOrAccountId)
                        continue;

                    if (ecPrivateAccountLogKey == null)
                    {
                        // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
                        try
                        {
                            ecPrivateAccountLogKey = Encryption.DecryptAesCbcEncryptedEcPrivateKey(
                                account.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                                phase1HashOfCorrectPassword);
                        }
                        catch (Exception)
                        {
                            // There's a problem with the key that prevents us from decrypting it.  We won't be able to do this analysis.                            
                            return;
                        }
                    }

                    // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
                    try
                    {
                        // Attempt to decrypt the password.
                        EcEncryptedMessageAesCbcHmacSha256 messageDeserializedFromJson =
                            JsonConvert.DeserializeObject<EcEncryptedMessageAesCbcHmacSha256>(
                                potentialTypo.EncryptedIncorrectPassword);
                        byte[] passwordAsUtf8 = messageDeserializedFromJson.Decrypt(ecPrivateAccountLogKey);
                        string incorrectPasswordFromPreviousAttempt = Encoding.UTF8.GetString(passwordAsUtf8);

                        // Use an edit distance calculation to determine if it was a likely typo
                        bool likelyTypo =
                            EditDistance.Calculate(incorrectPasswordFromPreviousAttempt, correctPassword) <=
                            _options.MaxEditDistanceConsideredATypo;

                        // Update the outcome based on this information.
                        AuthenticationOutcome newOutocme = likelyTypo
                            ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                            : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;

                        // Add this to the list of changed attempts
                        credit += potentialTypo.Penalty.GetValue(whenUtc)*(1d - _options.PenaltyMulitiplierForTypo);

                        // FUTURE -- find and update the login attempt in the background

                    }
                    catch (Exception)
                    {
                        // An exception is likely due to an incorrect key (perhaps outdated).
                        // Since we simply can't do anything with a record we can't Decrypt, we carry on
                        // as if nothing ever happened.  No.  Really.  Nothing to see here.
                    }
                    clientsIpHistory.RecentPotentialTypos.Remove(potentialTypo);
                }
                clientsIpHistory.CurrentBlockScore.Add(-credit, whenUtc);
            }
            finally
            {
                ecPrivateAccountLogKey?.Dispose();
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

        protected void UpdateBlockScore(DoubleThatDecaysWithTime currentBlockScore,
            CapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> recentPotentialTypos,
            LoginAttempt loginAttempt, UserAccount account, ref bool accountChanged)
        {
            double passwordsPopularityAmongGuesses = loginAttempt.PasswordsPopularityAmongFailedGuesses;
            double popularityMultiplier = PopularityPenaltyMultiplier(passwordsPopularityAmongGuesses);;
            switch (loginAttempt.Outcome)
            {
                case AuthenticationOutcome.CredentialsInvalidNoSuchAccount:
                    currentBlockScore.Add(_options.PenaltyForInvalidAccount_Alpha*popularityMultiplier, loginAttempt.TimeOfAttemptUtc);
                    return;
                case AuthenticationOutcome.CredentialsInvalidIncorrectPassword:
                    double penalty = _options.PenaltyForInvalidPassword_Beta * popularityMultiplier;
                    currentBlockScore.Add(penalty, loginAttempt.TimeOfAttemptUtc);
                    if (account != null)
                    {
                        recentPotentialTypos.Add(new LoginAttemptSummaryForTypoAnalysis()
                        {
                            EncryptedIncorrectPassword = loginAttempt.EncryptedIncorrectPassword,
                            Penalty = new DoubleThatDecaysWithTime(currentBlockScore.HalfLife, penalty, loginAttempt.TimeOfAttemptUtc),
                            UsernameOrAccountId = loginAttempt.UsernameOrAccountId
                        });
                    }
                    return;
                case AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword:
                    // We ignore repeats of incorrect passwords we've already accounted for
                    // No penalty
                    return;
                case AuthenticationOutcome.CredentialsValid:
                    if (currentBlockScore > 0)
                    {
                        double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Gamma, currentBlockScore.GetValue(loginAttempt.TimeOfAttemptUtc));
                        double credit = account.TryGetCredit(desiredCredit, loginAttempt.TimeOfAttemptUtc);
                        if (credit > 0)
                            accountChanged = true;
                        currentBlockScore.Add(-credit, loginAttempt.TimeOfAttemptUtc);
                    }
                    break;
                case AuthenticationOutcome.CredentialsValidButBlocked:
                default:
                    return;
            }
        }


        protected void SimUpdateBlockScores(IEnumerable<SimulationConditionData> conditions, LoginAttempt loginAttempt, UserAccount account,
            ref bool accountChanged)
        {
            foreach (SimulationConditionData condition in conditions)
            {
                SimUpdateBlockScore(condition,loginAttempt,account,ref accountChanged);
            }
        }

        protected void SimUpdateBlockScore(SimulationConditionData cond, LoginAttempt loginAttempt, UserAccount account, ref bool accountChanged)
        {
            double passwordsPopularityAmongGuesses = loginAttempt.PasswordsPopularityAmongFailedGuesses;
            double popularityMultiplier = PopularityPenaltyMultiplier(passwordsPopularityAmongGuesses); ;
            switch (loginAttempt.Outcome)
            {
                case AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount:
                case AuthenticationOutcome.CredentialsInvalidNoSuchAccount:
                {
                    if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount &&
                        cond.Condition.IgnoresRepeats)
                        return;
                    double penalty = cond.Condition.UsesAlphaForAccountFailures
                        ? _options.PenaltyForInvalidAccount_Alpha
                        : _options.PenaltyForInvalidPassword_Beta;
                    if (cond.Condition.PunishesPopularGuesses)
                        penalty *= popularityMultiplier;
                    cond.Score.Add(penalty, loginAttempt.TimeOfAttemptUtc);
                    return;
                }
                case AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword:
                case AuthenticationOutcome.CredentialsInvalidIncorrectPassword:
                {
                    if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword &&
                        cond.Condition.IgnoresRepeats)
                        return;
                    double penalty = _options.PenaltyForInvalidPassword_Beta;
                    if (cond.Condition.PunishesPopularGuesses)
                        penalty *= popularityMultiplier;
                    cond.Score.Add(penalty, loginAttempt.TimeOfAttemptUtc);
                    if (account != null && cond.RecentPotentialTypos != null)
                    {
                        cond.RecentPotentialTypos.Add(new LoginAttemptSummaryForTypoAnalysis()
                        {
                            EncryptedIncorrectPassword = loginAttempt.EncryptedIncorrectPassword,
                            Penalty = new DoubleThatDecaysWithTime(
                                cond.Score.HalfLife,
                                penalty,
                                loginAttempt.TimeOfAttemptUtc),
                            UsernameOrAccountId = loginAttempt.UsernameOrAccountId
                        });
                    }
                    return;
                }
                case AuthenticationOutcome.CredentialsValid:                    
                    if (cond.Score > 0)
                    {
                        double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Gamma, cond.Score.GetValue(loginAttempt.TimeOfAttemptUtc));
                        double credit = account.TryGetCreditForSimulation(cond.Condition.Index, desiredCredit, loginAttempt.TimeOfAttemptUtc);
                        if (credit > 0)
                            accountChanged = true;
                        cond.Score.Add(-credit, loginAttempt.TimeOfAttemptUtc);
                    }
                    return;
                default:
                    return;
            }
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
#if Simulation
        public async Task<double[]> DetermineLoginAttemptOutcomeAsync(
#else
        public async Task<LoginAttempt> DetermineLoginAttemptOutcomeAsync(
#endif
            LoginAttempt loginAttempt,
            string passwordProvidedByClient,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {

            //
            // In parallel fetch information we'll need to determine the outcome
            //

            // Get information about the client's IP
            Task<IpHistory> ipHistoryGetTask = _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest,
                cancellationToken);

            // Get information about the account the client is trying to login to
            IStableStoreContext<string, UserAccount> userAccountContext = _userAccountContextFactory.Get();
            Task<UserAccount> userAccountRequestTask = userAccountContext.ReadAsync(
                loginAttempt.UsernameOrAccountId,
                cancellationToken);

            // Get a binomial ladder to estimate if the password is common
            Task<ILadder> binomialLadderTask = _binomialLadderSketch.GetLadderAsync(passwordProvidedByClient,
                cancellationToken: cancellationToken);

            // Get a more-accurate count of the passwords' frequency if it is already known to be common
            string easyHashOfPassword =
                Convert.ToBase64String(ManagedSHA256.Hash(Encoding.UTF8.GetBytes(passwordProvidedByClient)));
            Task<IFrequencies> passwordFrequencyTask = _incorrectPasswordFrequenciesProvider.GetFrequenciesAsync(
                easyHashOfPassword,
                timeout: timeout,
                cancellationToken: cancellationToken);

            //
            // End parallel operations
            //

            // We'll need the salt from the account record before we can calculate the expensive hash,
            // so await that task first 
            UserAccount account = await userAccountRequestTask;
            bool accountChanged = false;

            byte[] phase1HashOfProvidedPassword = account != null
                ? account.ComputePhase1Hash(passwordProvidedByClient)
                : ExpensiveHashFunctionFactory.Get(_options.DefaultExpensiveHashingFunction)(
                    passwordProvidedByClient,
                    Encoding.UTF8.GetBytes(loginAttempt.AddressOfClientInitiatingRequest.ToString()),
                    _options.ExpensiveHashingFunctionIterations);
            string phase1HashOfProvidedPasswordAsString = Convert.ToBase64String(phase1HashOfProvidedPassword);

            bool didSketchIndicateThatTheSameGuessHasBeenMadeRecently = _recentIncorrectPasswords.AddMember(phase1HashOfProvidedPasswordAsString);


            // Preform an analysis of the IPs past beavhior to determine if the IP has been performing so many failed guesses
            // that we disallow logins even if it got the right password.  We call this even when the submitted password is
            // correct lest we create a timing indicator (slower responses for correct passwords) that attackers could use
            // to guess passwords even if we'd blocked their IPs.
            IpHistory ip = await ipHistoryGetTask;


            // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
            // as we are most concerned about frequently-guessed passwords.
            ILadder passwordLadder = await binomialLadderTask;
            int ladderRungsWithConfidence = passwordLadder.CountObservationsForGivenConfidence(_options.PopularityConfidenceLevel);
            double ladderPopularity = (double)ladderRungsWithConfidence / (10d * 1000d);//FIXME

            IFrequencies passwordFrequencies = await passwordFrequencyTask;
            double trackedPopularity = Proportion.GetLargest(passwordFrequencies.Proportions).AsDouble;
            double popularityOfPasswordAmongIncorrectPasswords = Math.Max(ladderPopularity, trackedPopularity);


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
                    Convert.ToBase64String(ManagedSHA256.Hash(phase1HashOfProvidedPassword));

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
                    AdjustBlockingScoreForPastTyposTreatedAsFullFailures(
                        ip, account, loginAttempt.TimeOfAttemptUtc, passwordProvidedByClient, phase1HashOfProvidedPassword);
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

            double[] conditionScores = ip.SimulationConditions.Select( cond =>
                    cond.GetThresholdAdjustedScore(loginAttempt.PasswordsPopularityAmongFailedGuesses,
                        loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount, loginAttempt.TimeOfAttemptUtc)).ToArray();

            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsValid)
            {
                // We only need to decide whether to block if the credentials provided were valid.
                // We'll get the blocking threshold, blocking condition, and block if the condition exceeds the threshold.
                double blockingThreshold = _options.BlockThresholdPopularPassword;
                if (loginAttempt.PasswordsPopularityAmongFailedGuesses <
                    _options.ThresholdAtWhichAccountsPasswordIsDeemedPopular)
                    blockingThreshold *= _options.BlockThresholdMultiplierForUnpopularPasswords;
                double blockScore = ip.CurrentBlockScore.GetValue(loginAttempt.TimeOfAttemptUtc);
                // If the client provided a cookie proving a past successful login, we'll ignore the block condition
                if (loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                    blockScore *= 0; // FUTURE
                if (blockScore > blockingThreshold)
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsValidButBlocked;
            }
            
            double popularityMultiplier = PopularityPenaltyMultiplier(popularityOfPasswordAmongIncorrectPasswords); ;

            UpdateBlockScore(ip.CurrentBlockScore, ip.RecentPotentialTypos, loginAttempt, account, ref accountChanged);
            SimUpdateBlockScores(ip.SimulationConditions, loginAttempt, account, ref accountChanged);
            //ip.CurrentBlockScore.Add(penalty, loginAttempt.TimeOfAttemptUtc);

            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidNoSuchAccount ||
                loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
            {
                if (passwordFrequencies.Proportions.Last().AsDouble > 0 ||
                    // FIXME with configuration values
                    passwordLadder.CountObservationsForGivenConfidence(1d/(1000d*1000d*1000d)) > 5)
                {
                    // FIXME
                    //Task background1 = 
                        await passwordFrequencies.RecordObservationAsync(cancellationToken: cancellationToken);
                }

                // FIXME
                //Task background2 = 
                    await passwordLadder.StepAsync(cancellationToken);
            }

            if (accountChanged)
            {
                Task backgroundTask = userAccountContext.SaveChangesAsync(new CancellationToken());
            }

#if Simulation
            return conditionScores;
#else
            return loginAttempt;
#endif
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
