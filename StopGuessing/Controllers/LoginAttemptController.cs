//#define Simulation
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
    

    [Route("api/[controller]")]
    public class LoginAttemptController :
#if !Simulation
        Controller, 
#endif
        ILoginAttemptController
    {
        private readonly BlockingAlgorithmOptions _options;
        private readonly IBinomialLadderSketch _binomialLadderSketch;
        private readonly IFrequenciesProvider<string> _incorrectPasswordFrequenciesProvider;
        private readonly IStableStoreFactory<string, UserAccount> _userAccountContextFactory;
        private readonly AgingMembershipSketch _recentIncorrectPasswords;

        private readonly SelfLoadingCache<IPAddress, IpHistory> _ipHistoryCache;

        private TimeSpan DefaultTimeout { get; } = new TimeSpan(0, 0, 0, 0, 500); // FUTURE use configuration value

        public LoginAttemptController(
            IStableStoreFactory<string, UserAccount> userAccountContextFactory,
            //IStableStoreFactory<string, IpHistory> ipHistoryContextFactory,
            IBinomialLadderSketch binomialLadderSketch,
            IFrequenciesProvider<string> incorrectPasswordFrequenciesProvider,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions blockingOptions,
            DateTime? currentDateTimeUtc = null
            )
        {
            _options = blockingOptions; //optionsAccessor.Options;
            //_stableStore = stableStore;
            _binomialLadderSketch = binomialLadderSketch;
            _incorrectPasswordFrequenciesProvider = incorrectPasswordFrequenciesProvider;

            _recentIncorrectPasswords = new AgingMembershipSketch(16, 128*1024); // FIXME -- more configurable?
            _userAccountContextFactory = userAccountContextFactory;
            _ipHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>(address => new IpHistory(address, currentDateTimeUtc, _options));

            memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;
        }


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

        /// <summary>
        /// Ensure that a known-common password is treated as frequent
        /// </summary>
        /// <param name="passwordToTreatAsFrequent">The password to treat as frequent</param>
        /// <param name="numberOfTimesToPrime">The number of consecutive occurrences to simulate</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task PrimeCommonPasswordAsync(string passwordToTreatAsFrequent,
            int numberOfTimesToPrime,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ILadder ladder = await _binomialLadderSketch.GetLadderAsync(passwordToTreatAsFrequent, cancellationToken: cancellationToken);

            string easyHashOfPassword =
                Convert.ToBase64String(ManagedSHA256.Hash(Encoding.UTF8.GetBytes(passwordToTreatAsFrequent)));
            IUpdatableFrequency frequencies = await _incorrectPasswordFrequenciesProvider.GetFrequencyAsync(
                easyHashOfPassword,
                cancellationToken: cancellationToken);

            for (int i = 0; i < numberOfTimesToPrime; i++)
            {
                await ladder.StepAsync(cancellationToken);
                await frequencies.RecordObservationAsync(cancellationToken: cancellationToken);
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

            LoginAttemptSummaryForTypoAnalysis[] recentPotentialTypos = clientsIpHistory.RecentPotentialTypos.MostRecentFirst.ToArray();
            ECDiffieHellmanCng ecPrivateAccountLogKey = null;
            try
            {
#if Simulation
                foreach (SimulationConditionIpHistoryState cond in clientsIpHistory.SimulationConditions)
                    cond.AdjustScoreForPastTyposTreatedAsFullFailures(ref ecPrivateAccountLogKey, account,
                        whenUtc, correctPassword, phase1HashOfCorrectPassword);
#endif
                foreach (LoginAttemptSummaryForTypoAnalysis potentialTypo in recentPotentialTypos)
                {
                    if (potentialTypo.UsernameOrAccountId != account.UsernameOrAccountId)
                        continue;

#if !Simulation
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
#endif
                    // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
                    try
                    {
#if Simulation
                        string incorrectPasswordFromPreviousAttempt = potentialTypo.EncryptedIncorrectPassword;
#else
                        // Attempt to decrypt the password.
                        string incorrectPasswordFromPreviousAttempt =
                            potentialTypo.EncryptedIncorrectPassword.Read(ecPrivateAccountLogKey);
#endif

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


        protected void UpdateBlockScore(DoubleThatDecaysWithTime currentBlockScore,
            SmallCapacityConstrainedSet<LoginAttemptSummaryForTypoAnalysis> recentPotentialTypos,
            LoginAttempt loginAttempt, UserAccount account, ILadder ladder, IUpdatableFrequency frequency, ref bool accountChanged)
        {
            switch (loginAttempt.Outcome)
            {
                case AuthenticationOutcome.CredentialsInvalidNoSuchAccount:
                    double invalidAccontPenalty = _options.PenaltyForInvalidAccount_Alpha*
                                     _options.PopularityBasedPenaltyMultiplier_phi(ladder, frequency);
                    currentBlockScore.Add(invalidAccontPenalty, loginAttempt.TimeOfAttemptUtc);
                    return;
                case AuthenticationOutcome.CredentialsInvalidIncorrectPassword:
                    double invalidPasswordPenalty = _options.PenaltyForInvalidPassword_Beta *
                                    _options.PopularityBasedPenaltyMultiplier_phi(ladder,frequency);
                    currentBlockScore.Add(invalidPasswordPenalty, loginAttempt.TimeOfAttemptUtc);
                    if (account != null)
                    {
                        recentPotentialTypos.Add(new LoginAttemptSummaryForTypoAnalysis()
                        {
                            EncryptedIncorrectPassword = loginAttempt.EncryptedIncorrectPassword,
                            Penalty = new DoubleThatDecaysWithTime(currentBlockScore.HalfLife, invalidPasswordPenalty, loginAttempt.TimeOfAttemptUtc),
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
                        double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Sigma, currentBlockScore.GetValue(loginAttempt.TimeOfAttemptUtc));
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

#if Simulation

        protected void SimUpdateBlockScores(IEnumerable<SimulationConditionIpHistoryState> conditions, LoginAttempt loginAttempt, UserAccount account,
            ILadder ladder, IUpdatableFrequency frequency,
            ref bool accountChanged)
        {
            foreach (SimulationConditionIpHistoryState condition in conditions)
            {
                SimUpdateBlockScore(condition,loginAttempt,account, ladder, frequency, ref accountChanged);
            }
        }
        protected void SimUpdateBlockScore(SimulationConditionIpHistoryState cond, LoginAttempt loginAttempt, UserAccount account,
            ILadder ladder, IUpdatableFrequency frequency, ref bool accountChanged)
        {
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
                        penalty *= _options.PopularityBasedPenaltyMultiplier_phi(ladder, frequency);
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
                        penalty *= _options.PopularityBasedPenaltyMultiplier_phi(ladder, frequency);
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
                        double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Sigma, cond.Score.GetValue(loginAttempt.TimeOfAttemptUtc));
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
#endif


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
            Task<IUpdatableFrequency> passwordFrequencyTask = _incorrectPasswordFrequenciesProvider.GetFrequencyAsync(
                easyHashOfPassword,
                timeout: timeout,
                cancellationToken: cancellationToken);

            //
            // Start processing information as it comes in
            //

            // We'll need the salt from the account record before we can calculate the expensive hash,
            // so await that task first 
            UserAccount account = await userAccountRequestTask;
            bool accountChanged = false;

            // Preform an analysis of the IPs past beavhior to determine if the IP has been performing so many failed guesses
            // that we disallow logins even if it got the right password.  We call this even when the submitted password is
            // correct lest we create a timing indicator (slower responses for correct passwords) that attackers could use
            // to guess passwords even if we'd blocked their IPs.
            //IpHistory ip = await ipHistoryGetTask;

            if (account == null)
            {
                //
                // This is an login attempt for an INvalid (NONexistent) account.
                //
                byte[] phase1HashOfProvidedPassword = 
                    ExpensiveHashFunctionFactory.Get(_options.DefaultExpensiveHashingFunction)(
                                                     passwordProvidedByClient,
                                                     ManagedSHA256.Hash(Encoding.UTF8.GetBytes(loginAttempt.UsernameOrAccountId)),
                                                     _options.ExpensiveHashingFunctionIterations);

                // This appears to be an loginAttempt to login to a non-existent account, and so all we need to do is
                // mark it as such.  However, since it's possible that users will forget their account names and
                // repeatedly loginAttempt to login to a nonexistent account, we'll want to track whether we've seen
                // this account/password double before and note in the outcome if it's a repeat so that.
                // the IP need not be penalized for issuign a query that isn't getting it any information it
                // didn't already have.
                loginAttempt.Outcome = _recentIncorrectPasswords.AddMember(Convert.ToBase64String(phase1HashOfProvidedPassword))
                    ? AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount
                    : AuthenticationOutcome.CredentialsInvalidNoSuchAccount;
            }
            else
            {
                //
                // This is an login attempt for a valid (existent) account.
                //

                // Test to see if the password is correct by calculating the Phase2Hash and comparing it with the Phase2 hash
                // in this record.  The expensive (phase1) hash which is used to encrypt the EC public key for this account
                // (which we use to store the encryptions of incorrect passwords)
                byte[] phase1HashOfProvidedPassword = account.ComputePhase1Hash(passwordProvidedByClient);

                // Determine whether the client provided a cookie to indicate that it has previously logged
                // into this account successfully---a very strong indicator that it is a client used by the
                // legitimate user and not an unknown client performing a guessing attack.
                loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount =
                    account.HasClientWithThisHashedCookieSuccessfullyLoggedInBefore(loginAttempt.HashOfCookieProvidedByBrowser);

                // Since we can't store the phase1 hash (it can decrypt that EC key) we instead store a simple (SHA256)
                // hash of the phase1 hash, which we call the phase 2 hash, and use that to compare the provided password
                // with the correct password.
                string phase2HashOfProvidedPassword = UserAccount.ComputePhase2HashFromPhase1Hash(phase1HashOfProvidedPassword);

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
                        await ipHistoryGetTask, account, loginAttempt.TimeOfAttemptUtc, passwordProvidedByClient, phase1HashOfProvidedPassword);
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
#if Simulation
                    loginAttempt.EncryptedIncorrectPassword = passwordProvidedByClient;
#else
                    loginAttempt.EncryptedIncorrectPassword.Write(passwordProvidedByClient, account.EcPublicAccountLogKey);
#endif
                    // Next, if it's possible to declare more about this outcome than simply that the 
                    // user provided the incorrect password, let's do so.
                    // Since users who are unsure of their passwords may enter the same username/password twice, but attackers
                    // don't learn anything from doing so, we'll want to account for these repeats differently (and penalize them less).
                    // We actually have two data structures for catching this: A large sketch of clientsIpHistory/account/password triples and a
                    // tiny LRU cache of recent failed passwords for this account.  We'll check both.

                    if (account.AddIncorrectPhase2Hash(phase2HashOfProvidedPassword))
                    {
                        loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword;
                    }
                    else
                    {
                        loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidIncorrectPassword;
                        accountChanged = true;
                    }

                }

            }

            // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
            // as we are most concerned about frequently-guessed passwords.
            ILadder passwordLadder = await binomialLadderTask;
            int ladderRungsWithConfidence = passwordLadder.CountObservationsForGivenConfidence(_options.PopularityConfidenceLevel);

            IUpdatableFrequency passwordFrequency = await passwordFrequencyTask;
            double trackedPopularity = passwordFrequency.Proportion.AsDouble;
            double popularityOfPasswordAmongIncorrectPasswords = Math.Max((double)ladderRungsWithConfidence / (10d * 1000d), trackedPopularity);

            // When there's little data, we want to make sure the popularity is not overstated because           
            // (e.g., if we've only seen 10 account failures since we started watching, it would not be
            //  appropriate to conclude that something we've seen once before represents 10% of likely guesses.)
            loginAttempt.PasswordsPopularityAmongFailedGuesses = popularityOfPasswordAmongIncorrectPasswords;

            IpHistory ip = await ipHistoryGetTask;
#if Simulation
            double[] conditionScores = ip.SimulationConditions.Select( cond =>
                    cond.GetThresholdAdjustedScore(loginAttempt.PasswordsPopularityAmongFailedGuesses,
                        loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount,
                        passwordLadder, passwordFrequency, loginAttempt.TimeOfAttemptUtc)).ToArray();
#endif

            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsValid)
            {
                // We only need to decide whether to block if the credentials provided were valid.
                // We'll get the blocking threshold, blocking condition, and block if the condition exceeds the threshold.
                double blockingThreshold = _options.BlockThresholdPopularPassword_T_base *
                    _options.PopularityBasedThresholdMultiplier_T_multiplier(passwordLadder, passwordFrequency);
                double blockScore = ip.CurrentBlockScore.GetValue(loginAttempt.TimeOfAttemptUtc);
                // If the client provided a cookie proving a past successful login, we'll ignore the block condition
                if (loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                    blockScore *= _options.MultiplierIfClientCookieIndicatesPriorSuccessfulLogin_Kappa;
                if (blockScore > blockingThreshold)
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsValidButBlocked;
                else
                {
                    account?.RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(
                        loginAttempt.HashOfCookieProvidedByBrowser);
                    accountChanged = true;
                }
            }
            
            UpdateBlockScore(ip.CurrentBlockScore, ip.RecentPotentialTypos, loginAttempt, account, passwordLadder, passwordFrequency,  ref accountChanged);
#if Simulation
            SimUpdateBlockScores(ip.SimulationConditions, loginAttempt, account, passwordLadder, passwordFrequency, ref accountChanged);
#endif
            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidNoSuchAccount ||
                loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
            {
                if (trackedPopularity > 0 ||
                    // FIXME with configuration values
                    passwordLadder.HeightOfKeyInRungs == passwordLadder.HeightOfLadderInRungs)
                {
                    // FIXME
                    //Task background1 = 
                        await passwordFrequency.RecordObservationAsync(cancellationToken: cancellationToken);
                }

                // FIXME
                //Task background2 = 
                    await passwordLadder.StepAsync(cancellationToken);
            }

            if (accountChanged && account != null)
            {
                Task backgroundTask = userAccountContext.SaveChangesAsync(account.UsernameOrAccountId, account, new CancellationToken());
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
