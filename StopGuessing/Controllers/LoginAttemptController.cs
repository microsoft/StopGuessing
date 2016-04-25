using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Utilities;

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
        Controller, 
        ILoginAttemptController
    {
        private readonly BlockingAlgorithmOptions _options;
        private readonly IBinomialLadderSketch _binomialLadderSketch;
        private readonly IUserAccountFactory _userAccountFactory;
        private readonly AgingMembershipSketch _recentIncorrectPasswords;

        private readonly SelfLoadingCache<IPAddress, IpHistory> _ipHistoryCache;

        public LoginAttemptController(
            IUserAccountFactory userAccountFactory,
            IBinomialLadderSketch binomialLadderSketch,
            MemoryUsageLimiter memoryUsageLimiter,
            BlockingAlgorithmOptions blockingOptions
            )
        {
            _options = blockingOptions; //optionsAccessor.Options;
            _binomialLadderSketch = binomialLadderSketch;

            _recentIncorrectPasswords = new AgingMembershipSketch(blockingOptions.AgingMembershipSketchTables, blockingOptions.AgingMembershipSketchTableSize);
            _userAccountFactory = userAccountFactory;
            _ipHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>(address => new IpHistory(address, _options));

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
            return await DetermineLoginAttemptOutcomeAsync(
                loginAttempt,
                passwordProvidedByClient,
                cancellationToken: cancellationToken);
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
            for (int i = 0; i < numberOfTimesToPrime; i++)
            {
                await _binomialLadderSketch.StepAsync(passwordToTreatAsFrequent, cancellationToken: cancellationToken);
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
            IUserAccount account,
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
                        string incorrectPasswordFromPreviousAttempt =
                            potentialTypo.EncryptedIncorrectPassword.Read(ecPrivateAccountLogKey);

                        // Use an edit distance calculation to determine if it was a likely typo
                        bool likelyTypo =
                            EditDistance.Calculate(incorrectPasswordFromPreviousAttempt, correctPassword) <=
                            _options.MaxEditDistanceConsideredATypo;

                        // Add this to the list of changed attempts
                        if (likelyTypo)
                        {
                            credit += potentialTypo.Penalty.GetValue(_options.AccountCreditLimitHalfLife, whenUtc)*
                                      (1d - _options.PenaltyMulitiplierForTypo);
                        }

                    }
                    catch (Exception)
                    {
                        // An exception is likely due to an incorrect key (perhaps outdated).
                        // Since we simply can't do anything with a record we can't Decrypt, we carry on
                        // as if nothing ever happened.  No.  Really.  Nothing to see here.
                    }

                    // Now that we know whether this past event was a typo or not, we no longer need to keep track
                    // of it (and we should remove it so we don't double credit it).
                    clientsIpHistory.RecentPotentialTypos.Remove(potentialTypo);
                }

                // Remove the amount to be credited from the block score due to the discovery of typos
                clientsIpHistory.CurrentBlockScore.SubtractInPlace(account.CreditHalfLife, credit, whenUtc);
            }
            finally
            {
                ecPrivateAccountLogKey?.Dispose();
            }
        }


        /// <returns></returns>
        /// <summary>
        /// Add a LoginAttempt, along the way determining whether that loginAttempt should be allowed
        /// (the user authenticated) or denied.
        /// </summary>
        /// <param name="loginAttempt">The login loginAttempt record to be stored.</param>
        /// <param name="passwordProvidedByClient">The plaintext password provided by the client.</param>
        /// <param name="phase1HashOfProvidedPassword">If the caller has already computed the phase 1 (expensive) hash of the submitted password,
        /// it can supply it via this optional parameter to avoid incurring the cost of having incurring the expense of this calculationg a second time.</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken">To allow this async method to be cancelled.</param>
        /// <returns>If the password is correct and the IP not blocked, returns AuthenticationOutcome.CredentialsValid.
        /// Otherwise, it returns a different AuthenticationOutcome.
        /// The client should not be made aware of any information beyond whether the login was allowed or not.</returns>
        public async Task<LoginAttempt> DetermineLoginAttemptOutcomeAsync(
            LoginAttempt loginAttempt,
            string passwordProvidedByClient,
            byte[] phase1HashOfProvidedPassword = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            //
            // In parallel, fetch information we'll need to determine the outcome
            //

            // Get information about the client's IP
            Task<IpHistory> ipHistoryGetTask = _ipHistoryCache.GetAsync(loginAttempt.AddressOfClientInitiatingRequest,
                cancellationToken);

            // Get information about the account the client is trying to login to
            //IStableStoreContext<string, UserAccount> userAccountContext = _userAccountContextFactory.Get();
            IUserAccountStore userAccountStore = _userAccountFactory.Create(loginAttempt.UsernameOrAccountId);
            Task<IUserAccount> userAccountRequestTask = userAccountStore.LoadAsync(cancellationToken);

            // Get a binomial ladder to estimate if the password is common
            Task<int> passwordsHeightOnBinomialLadderTask =
                _binomialLadderSketch.GetHeightAsync(passwordProvidedByClient, cancellationToken: cancellationToken);


            //
            // Start processing information as it comes in
            //

            // Preform an analysis of the IPs past beavhior to determine if the IP has been performing so many failed guesses
            // that we disallow logins even if it got the right password.  We call this even when the submitted password is
            // correct lest we create a timing indicator (slower responses for correct passwords) that attackers could use
            // to guess passwords even if we'd blocked their IPs.
            IpHistory ip = await ipHistoryGetTask;

            // We'll need the salt from the account record before we can calculate the expensive hash,
            // so await that task first 
            IUserAccount account = await userAccountRequestTask;
            if (account != null)
            {
                try
                {
                    //
                    // This is an login attempt for a valid (existent) account.
                    //

                    // Determine whether the client provided a cookie to indicate that it has previously logged
                    // into this account successfully---a very strong indicator that it is a client used by the
                    // legitimate user and not an unknown client performing a guessing attack.
                    loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount = await
                        account.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
                            loginAttempt.HashOfCookieProvidedByBrowser);

                    // Test to see if the password is correct by calculating the Phase2Hash and comparing it with the Phase2 hash
                    // in this record.  The expensive (phase1) hash which is used to encrypt the EC public key for this account
                    // (which we use to store the encryptions of incorrect passwords)
                    if (phase1HashOfProvidedPassword == null)
                    {
                        phase1HashOfProvidedPassword =
                            UserAccountController.ComputePhase1Hash(account, passwordProvidedByClient);
                    }

                    // Since we can't store the phase1 hash (it can decrypt that EC key) we instead store a simple (SHA256)
                    // hash of the phase1 hash, which we call the phase 2 hash, and use that to compare the provided password
                    // with the correct password.
                    string phase2HashOfProvidedPassword =
                        UserAccountController.ComputePhase2HashFromPhase1Hash(phase1HashOfProvidedPassword);

                    // To determine if the password is correct, compare the phase2 has we just generated (phase2HashOfProvidedPassword)
                    // with the one generated from the correct password when the user chose their password (account.PasswordHashPhase2).  
                    bool isSubmittedPasswordCorrect = phase2HashOfProvidedPassword == account.PasswordHashPhase2;

                    // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
                    // as we are most concerned about frequently-guessed passwords.
                    loginAttempt.PasswordsHeightOnBinomialLadder = await passwordsHeightOnBinomialLadderTask;

                    if (isSubmittedPasswordCorrect)
                    {
                        // The password is corerct.

                        // Determine if any of the outcomes for login attempts from the client IP for this request were the result of typos,
                        // as this might impact our decision about whether or not to block this client IP in response to its past behaviors.
                        AdjustBlockingScoreForPastTyposTreatedAsFullFailures(
                            ip, account, loginAttempt.TimeOfAttemptUtc, passwordProvidedByClient,
                            phase1HashOfProvidedPassword);

                        // We'll get the blocking threshold, blocking condition, and block if the condition exceeds the threshold.
                        double blockingThreshold = _options.BlockThresholdPopularPassword_T_base*
                                                   _options.PopularityBasedThresholdMultiplier_T_multiplier(
                                                       loginAttempt);
                        double blockScore = ip.CurrentBlockScore.GetValue(_options.BlockScoreHalfLife,
                            loginAttempt.TimeOfAttemptUtc);

                        // If the client provided a cookie proving a past successful login, we'll ignore the block condition
                        if (loginAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount)
                            blockScore *= _options.MultiplierIfClientCookieIndicatesPriorSuccessfulLogin_Kappa;

                        if (blockScore > blockingThreshold)
                        {
                            // While this login attempt had valid credentials, the circumstances
                            // are so suspicious that we should block the login and pretend the 
                            // credentials were invalid
                            loginAttempt.Outcome = AuthenticationOutcome.CredentialsValidButBlocked;
                        }
                        else
                        {
                            // This login attempt has valid credentials and no reason to block, so the
                            // client will be authenticated.
                            loginAttempt.Outcome = AuthenticationOutcome.CredentialsValid;
                            account.RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(
                                loginAttempt.HashOfCookieProvidedByBrowser);

                            // FIXME -- move to background?  better sync for credits?

                            // Use this login attempt to offset harm caused by prior login failures
                            if (
                                ip.CurrentBlockScore.GetValue(_options.AccountCreditLimitHalfLife,
                                    loginAttempt.TimeOfAttemptUtc) > 0)
                            {
                                double desiredCredit = Math.Min(_options.RewardForCorrectPasswordPerAccount_Sigma,
                                    ip.CurrentBlockScore.GetValue(_options.AccountCreditLimitHalfLife,
                                        loginAttempt.TimeOfAttemptUtc));
                                double credit = UserAccountController.TryGetCredit(account, desiredCredit,
                                    loginAttempt.TimeOfAttemptUtc);
                                ip.CurrentBlockScore.SubtractInPlace(_options.AccountCreditLimitHalfLife, credit,
                                    loginAttempt.TimeOfAttemptUtc);
                            }
                        }

                    }
                    else
                    {
                        //
                        // The password was invalid.  Do bookkeeping of information about this failure so that we can
                        // block the origin IP if it appears to be engaged in guessing and so that we can track
                        // frequently guessed passwords.

                        // We'll not only store the (phase 2) hash of the incorrect password, but we'll also store
                        // the incorrect passwords itself, encrypted with the EcPublicAccountLogKey.
                        // (The decryption key to get the incorrect password plaintext back is encrypted with the
                        //  correct password, so you can't get to the plaintext of the incorrect password if you
                        //  don't already know the correct password.)
                        loginAttempt.Phase2HashOfIncorrectPassword = phase2HashOfProvidedPassword;
                        loginAttempt.EncryptedIncorrectPassword.Write(passwordProvidedByClient,
                            account.EcPublicAccountLogKey);

                        // Next, if it's possible to declare more about this outcome than simply that the 
                        // user provided the incorrect password, let's do so.
                        // Since users who are unsure of their passwords may enter the same username/password twice, but attackers
                        // don't learn anything from doing so, we'll want to account for these repeats differently (and penalize them less).
                        // We actually have two data structures for catching this: A large sketch of clientsIpHistory/account/password triples and a
                        // tiny LRU cache of recent failed passwords for this account.  We'll check both.
                        if (await account.AddIncorrectPhase2HashAsync(phase2HashOfProvidedPassword))
                        {
                            // The same incorrect password was recently used for this account, indicate this so that we
                            // do not penalize the IP further (as attackers don't gain anything from guessing the wrong password again).
                            loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword;
                        }
                        else
                        {
                            // This is the first time we've (at least recently) seen this incorrect password attempted for the account,
                            loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidIncorrectPassword;

                            // Penalize the IP for the invalid password
                            double invalidPasswordPenalty = _options.PenaltyForInvalidPassword_Beta*
                                                            _options.PopularityBasedPenaltyMultiplier_phi(
                                                                loginAttempt);
                            ip.CurrentBlockScore.AddInPlace(_options.AccountCreditLimitHalfLife,
                                invalidPasswordPenalty,
                                loginAttempt.TimeOfAttemptUtc);
                            // Record the penalty so that it can be reduced if this incorrect password is later discovered to be a typo.
                            ip.RecentPotentialTypos.Add(new LoginAttemptSummaryForTypoAnalysis()
                            {
                                EncryptedIncorrectPassword = loginAttempt.EncryptedIncorrectPassword,
                                Penalty = new DecayingDouble(invalidPasswordPenalty, loginAttempt.TimeOfAttemptUtc),
                                UsernameOrAccountId = loginAttempt.UsernameOrAccountId
                            });
                        }

                    }
                }
                finally
                {
                    // Save changes to the user account record in the background (so that we don't hold up returning the result)
                    TaskHelper.RunInBackground(userAccountStore.SaveChangesAsync(cancellationToken));
                }
            }
            else
            {
                // account == null
                // This is an login attempt for an INvalid (NONexistent) account.
                //
                if (phase1HashOfProvidedPassword == null)
                {
                    phase1HashOfProvidedPassword =
                        ExpensiveHashFunctionFactory.Get(_options.DefaultExpensiveHashingFunction)(
                            passwordProvidedByClient,
                            ManagedSHA256.Hash(Encoding.UTF8.GetBytes(loginAttempt.UsernameOrAccountId)),
                            _options.ExpensiveHashingFunctionIterations);
                }

                // This appears to be an loginAttempt to login to a non-existent account, and so all we need to do is
                // mark it as such.  However, since it's possible that users will forget their account names and
                // repeatedly loginAttempt to login to a nonexistent account, we'll want to track whether we've seen
                // this account/password double before and note in the outcome if it's a repeat so that.
                // the IP need not be penalized for issuign a query that isn't getting it any information it
                // didn't already have.
                loginAttempt.Outcome =
                    _recentIncorrectPasswords.AddMember(Convert.ToBase64String(phase1HashOfProvidedPassword))
                        ? AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount
                        : AuthenticationOutcome.CredentialsInvalidNoSuchAccount;

                // Get the popularity of the password provided by the client among incorrect passwords submitted in the past,
                // as we are most concerned about frequently-guessed passwords.
                loginAttempt.PasswordsHeightOnBinomialLadder = await passwordsHeightOnBinomialLadderTask;

                if (_recentIncorrectPasswords.AddMember(Convert.ToBase64String(phase1HashOfProvidedPassword)))
                {
                    // Don't penalize the incorrect <invalid account/password> pair if we've seen the same
                    // pair recently
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount;
                }
                else
                {
                    // Penalize the IP for a login attempt with an invalid account
                    loginAttempt.Outcome = AuthenticationOutcome.CredentialsInvalidNoSuchAccount;
                    double invalidAccontPenalty = _options.PenaltyForInvalidAccount_Alpha*
                                                  _options.PopularityBasedPenaltyMultiplier_phi(loginAttempt);
                    ip.CurrentBlockScore.AddInPlace(_options.BlockScoreHalfLife, invalidAccontPenalty,
                        loginAttempt.TimeOfAttemptUtc);
                }
            }


            if (loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidNoSuchAccount ||
                loginAttempt.Outcome == AuthenticationOutcome.CredentialsInvalidIncorrectPassword)
            {
                // Record the invalid password into the binomial ladder sketch that tracks freqeunt-incorrect passwords
                // Since we don't need to know the result, we'll run it in the background (so that we don't hold up returning the result)
                TaskHelper.RunInBackground(_binomialLadderSketch.StepAsync(passwordProvidedByClient, cancellationToken: cancellationToken));
            }

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
