#define Simulation
// FIXME remove
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public class UserAccount : IDisposable
    {

        /// <summary>
        /// A string that uniquely identifies the account.
        /// </summary>
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
        public string PasswordHashPhase1FunctionName { get; set; } = ExpensiveHashFunctionFactory.DefaultFunctionName;

        /// <summary>
        /// The number of iterations to use for the phase 1 hash to make it more expensive.
        /// </summary>
        public int NumberOfIterationsToUseForPhase1Hash { get; set; } = ExpensiveHashFunctionFactory.DefaultNumberOfIterations;

        /// <summary>
        /// An EC public encryption symmetricKey used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private symmetricKey.
        /// </summary>
//public ECDiffieHellmanPublicKey EcPublicAccountLogKey { get; set; }
#if !Simulation
        public byte[] EcPublicAccountLogKey { get; set; }

        /// <summary>
        /// The EC private symmetricKey encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; private set; }
#endif
        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption symmetricKey for the log. 
        /// </summary>
        public string PasswordHashPhase2 { get; set; }

        /// <summary>
        /// A recency set of the device cookies (hashed via SHA256 and converted to Base64)
        /// that have successfully logged into this account.
        /// </summary>
        public SmallCapacityConstrainedSet<string> HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount { get; set; }

        ///// <summary>
        ///// A length-limited sequence of records describing failed login attempts (invalid passwords) 
        ///// </summary>
        //public Sequence<LoginAttempt> PasswordVerificationFailures { get; set; }

        public SmallCapacityConstrainedSet<string> RecentIncorrectPhase2Hashes { get; set; }

        /// <summary>
        /// The account's credit limit for offsetting penalties for IP addresses from which
        /// the account has logged in successfully.
        /// </summary>
        public double CreditLimit { get; set; }

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        public DoubleThatDecaysWithTime ConsumedCredits { get; set; }

#if Simulation
        public DoubleThatDecaysWithTime[] ConsumedCreditsForSimulation;
#endif

        ///// <summary>
        ///// A member used exclusively to set the password.  This is primarily a convenience member for testing.
        ///// If the old password is available, it is better to use the SetPassword() method and provide the old passowrd
        ///// so that information encrypted with the old password can be recovered.
        ///// </summary>
        //[IgnoreDataMember]
        //[JsonIgnore]
        //public string Password { set { SetPassword(value); } }


        /// <summary>
        /// Computes the phase1 (expensive) hash of a password using the algorithm specified for
        /// this account and the unique salt for this account.
        /// </summary>
        /// <param name="password">The password to hash.</param>
        /// <returns>The hash of the password with the specified algorithm using this account's unique salt.</returns>
        public byte[] ComputePhase1Hash(string password)
        {
            return ExpensiveHashFunctionFactory.Get(PasswordHashPhase1FunctionName)(
                password, SaltUniqueToThisAccount, NumberOfIterationsToUseForPhase1Hash);
        }


        public static string ComputerPhase2HashFromPhase1Hash(byte[] phase1Hash)
        {
            return Convert.ToBase64String(ManagedSHA256.Hash(phase1Hash));
        }

        ///// <summary>
        ///// This analysis will examine LoginAttempts that failed due to an incorrect password
        ///// and, where it can be determined if the failure was due to a typo or not,
        ///// update the outcomes to reflect whether it was or was not a typo.
        ///// </summary>
        ///// <param name="correctPassword">The correct password for this account.  (We can only know it because
        ///// the client must have provided the correct one this attempt.)</param>
        ///// <param name="phase1HashOfCorrectPassword">The phase1 hash of that correct password---which we could
        ///// recalculate from the information in the previous parameters, but doing so would be expensive.</param>
        ///// <param name="maxEditDistanceConsideredATypo">The maximum edit distance between the correct and incorrect password
        ///// that is considered a typo (inclusive).</param>
        ///// <param name="attemptsToAnalyze">The set of LoginAttempts to analyze.</param>
        ///// <returns>Returns the set of LoginAttempt records that were modified so that the updates
        ///// to their outcomes can be written to stable store or other actions taken.</returns>
        //public List<LoginAttempt> UpdateLoginAttemptOutcomeUsingTypoAnalysis(
        //    string correctPassword,
        //    byte[] phase1HashOfCorrectPassword,
        //    float maxEditDistanceConsideredATypo,
        //    IEnumerable<LoginAttempt> attemptsToAnalyze)
        //{
        //    List<LoginAttempt> changedAttempts = new List<LoginAttempt>();

        //    ECDiffieHellmanCng ecPrivateAccountLogKey = null;

        //    foreach (LoginAttempt attempt in attemptsToAnalyze)
        //    {

        //        // If we haven't yet decrypted the EC key, which we will in turn use to decrypt the password
        //        // provided in this login attempt, do it now.  (We don't do it in advance as we don't want to
        //        // do the work unless we find at least one record to analyze.)
        //        if (ecPrivateAccountLogKey == null)
        //        {
        //            // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
        //            try
        //            {
        //                ecPrivateAccountLogKey = Encryption.DecryptAesCbcEncryptedEcPrivateKey(
        //                    EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
        //            }
        //            catch (Exception)
        //            {
        //                // There's a problem with the key that prevents us from decrypting it.  We won't be able to do this analysis.                            
        //                return changedAttempts;
        //            }
        //        }

        //        // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
        //        try
        //        {
        //            // Attempt to decrypt the password.
        //            string incorrectPasswordFromPreviousAttempt =
        //                attempt.DecryptAndGetIncorrectPassword(ecPrivateAccountLogKey);

        //            // Use an edit distance calculation to determine if it was a likely typo
        //            bool likelyTypo = EditDistance.Calculate(incorrectPasswordFromPreviousAttempt, correctPassword) <=
        //                              maxEditDistanceConsideredATypo;

        //            // Update the outcome based on this information.
        //            attempt.Outcome = likelyTypo
        //                ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
        //                : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;

        //            // Add this to the list of changed attempts
        //            changedAttempts.Add(attempt);
        //        }
        //        catch (Exception)
        //        {
        //            // An exception is likely due to an incorrect key (perhaps outdated).
        //            // Since we simply can't do anything with a record we can't Decrypt, we carry on
        //            // as if nothing ever happened.  No.  Really.  Nothing to see here.
        //        }
        //    }
        //    return changedAttempts;
        //}


        /// <summary>
        /// Sets the password of a user.
        /// <b>Important</b>: this does not authenticate the user but assumes the user has already been authenticated.
        /// The <paramref name="oldPassword"/> field is used only to optionally recover the EC symmetricKey, not to authenticate the user.
        /// </summary>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="oldPassword">If this optional field is provided and correct, the old password will allow us to re-use the old log decryption symmetricKey.
        /// <b>Providing this parameter will not cause this function to authenticate the user first.  The caller must do so beforehand.</b></param>
        /// <param name="nameOfExpensiveHashFunctionToUse">The name of the phase 1 (expenseive) hash to use.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations that the hash should be performed.</param>
        public void SetPassword(string newPassword, string oldPassword = null,
            string nameOfExpensiveHashFunctionToUse = null,
            int? numberOfIterationsToUseForPhase1Hash = null)
        {

            // If the caller also wants to change the hash function or the number of iterations,
            // make that change here now that we're done hashing the old password and are about to hash the new one.
            if (nameOfExpensiveHashFunctionToUse != null)
            {
                PasswordHashPhase1FunctionName = nameOfExpensiveHashFunctionToUse;
            }

            if (numberOfIterationsToUseForPhase1Hash.HasValue)
            {
                NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForPhase1Hash.Value;
            }

            // Calculate the Phase1 hash, which is a computationally-heavy hash of the password
            // We will use this for encrypting the EC account log symmetricKey.
            byte[] newPasswordHashPhase1 = ComputePhase1Hash(newPassword);

            // Calculate the Phase2 hash by hasing the phase 1 hash with SHA256.
            // We can store this without revealing the phase 1 hash used to encrypt the EC account log symmetricKey.
            // We can use it to verify whether a provided password is correct
            PasswordHashPhase2 = ComputerPhase2HashFromPhase1Hash(newPasswordHashPhase1);

#if !Simulation
            // Store the EC UsernameOrAccountId log symmetricKey encrypted with the phase 1 hash.
            byte[] oldPasswordHashPhase1;
            if (oldPassword != null &&
                ComputerPhase2HashFromPhase1Hash(oldPasswordHashPhase1 = ComputePhase1Hash(oldPassword)) ==
                PasswordHashPhase2)
            {
                // If we have a valid old password, Decrypt the private log decryption symmetricKey so we can re-encrypt it
                // with the new password and continue to use it on future logins. 
                try
                {
                    using (ECDiffieHellmanCng ecAccountLogKey =
                        Encryption.DecryptAesCbcEncryptedEcPrivateKey(
                            EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                            oldPasswordHashPhase1))
                    {
                        SetAccountLogKey(ecAccountLogKey, newPasswordHashPhase1);
                        return;
                    }
                }
                catch (Exception)
                {
                    // Ignore crypto failures.  They just mean we were unsuccessful in decrypting the symmetricKey and should create a new one.
                }
            }
            // We were unable to use an old EC UsernameOrAccountId Log Key,
            // so we'll create a new one
            using (ECDiffieHellmanCng ecAccountLogKey =
                new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null,
                    new CngKeyCreationParameters {ExportPolicy = CngExportPolicies.AllowPlaintextExport})))
            {
                SetAccountLogKey(ecAccountLogKey, newPasswordHashPhase1);
            }
#endif
        }


#if !Simulation
        /// <summary>
        /// Derive the EC private account log key from the phase 1 hash of the correct password.
        /// </summary>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password</param>
        /// <returns></returns>
        protected ECDiffieHellmanCng DecryptPrivateAccountLogKey(byte[] phase1HashOfCorrectPassword)
        {
            return Encryption.DecryptAesCbcEncryptedEcPrivateKey(EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
        }
#endif

#if !Simulation
        /// <summary>
        /// Set the EC account log key
        /// </summary>
        /// <param name="ecAccountLogKey"></param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password</param>
        /// <returns></returns>
        protected void SetAccountLogKey(ECDiffieHellmanCng ecAccountLogKey, byte[] phase1HashOfCorrectPassword)
        {
            EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 = Encryption.EncryptEcPrivateKeyWithAesCbc(ecAccountLogKey,
                phase1HashOfCorrectPassword);
            using (ECDiffieHellmanPublicKey publicKey = ecAccountLogKey.PublicKey)
            {
                EcPublicAccountLogKey = publicKey.ToByteArray();
            }
        }
#endif


        public double TryGetCredit(double amountRequested, DateTime timeOfRequestUtc)
        {
            double amountAvailable = CreditLimit - ConsumedCredits.GetValue(timeOfRequestUtc);
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            ConsumedCredits.Add(amountConsumed);

            return amountConsumed;
        }

#if Simulation
        public double TryGetCreditForSimulation(int simulationIndex, double amountRequested, DateTime timeOfRequestUtc)
        {
            lock (ConsumedCreditsForSimulation)
            {
                double amountAvailable = CreditLimit - ConsumedCreditsForSimulation[simulationIndex].GetValue(timeOfRequestUtc);
                double amountConsumed = Math.Min(amountRequested, amountAvailable);
                ConsumedCreditsForSimulation[simulationIndex].Add(amountConsumed);
                return amountConsumed;
            }
        }
#endif


        private const int DefaultSaltLength = 8;
        private const int DefaultMaxFailedPhase2HashesToTrack = 8;
        private const int DefaultMaxNumberOfCookiesToTrack = 24;

        /// <summary>
        /// Create a UserAccount record to match a given username or account id.
        /// </summary>
        /// <param name="usernameOrAccountId">A unique identifier for this account, such as a username, email address, or data index for the account record.</param>
        /// <param name="creditLimit"></param>
        /// <param name="creditHalfLife"></param>
        /// <param name="password">The password for the account.  If null or not provided, no password is set.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations to use when hashing the password.</param>
        /// <param name="saltUniqueToThisAccount">The salt for this account.  If null or not provided, a random salt is generated with length determined
        /// by parameter <paramref name="saltLength"/>.</param>
        /// <param name="maxNumberOfCookiesToTrack">This class tracks cookies associated with browsers that have 
        /// successfully logged into this account.  This parameter, if set, overrides the default maximum number of such cookies to track.</param>
        /// <param name="maxFailedPhase2HashesToTrack">Phase2hashes of recent failed passwords so that we can avoid counting
        /// repeat failures with the same incorrect password against a client.</param>
        /// <param name="phase1HashFunctionName">A hash function that is expensive enough to calculate to make offline dictionary attacks 
        /// expensive, but not so expensive as to slow the authentication system to a halt.  If not specified, a default function will be
        /// used.</param>
        /// <param name="saltLength">If <paramref name="saltUniqueToThisAccount"/>is not specified or null, the constructor will create
        /// a random salt of this length.  If this length is not specified, a default will be used.</param>
        public static UserAccount Create(string usernameOrAccountId,
#if Simulation
            int numberOfConditions,
#endif
            double creditLimit,
            TimeSpan creditHalfLife,
            string password = null,
            string phase1HashFunctionName = ExpensiveHashFunctionFactory.DefaultFunctionName,
            int numberOfIterationsToUseForPhase1Hash = ExpensiveHashFunctionFactory.DefaultNumberOfIterations,
            byte[] saltUniqueToThisAccount = null,
            int maxNumberOfCookiesToTrack = DefaultMaxNumberOfCookiesToTrack,
            int maxFailedPhase2HashesToTrack = DefaultMaxFailedPhase2HashesToTrack,
            int saltLength = DefaultSaltLength)
        {

            if (saltUniqueToThisAccount == null)
            {
                saltUniqueToThisAccount = new byte[DefaultSaltLength];
                StrongRandomNumberGenerator.GetBytes(saltUniqueToThisAccount);
            }

            UserAccount newAccount = new UserAccount
            {
                UsernameOrAccountId = usernameOrAccountId,
                SaltUniqueToThisAccount = saltUniqueToThisAccount,
                HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount =
                    new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack),
                //PasswordVerificationFailures =
                //    new Sequence<LoginAttempt>(maxAccountPasswordVerificationFailuresToTrack),
                RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack),
                ConsumedCredits = new DoubleThatDecaysWithTime(creditHalfLife),
                CreditLimit = creditLimit,
                PasswordHashPhase1FunctionName = phase1HashFunctionName,
                NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForPhase1Hash
                //Password = password
#if Simulation
        , ConsumedCreditsForSimulation = new DoubleThatDecaysWithTime[numberOfConditions]
#endif
    };

            if (password != null)
            {
                newAccount.SetPassword(password);
            }

            return newAccount;
        }

        public virtual void Dispose()
        {
            // Should write data back to storage
        }
    }
}
