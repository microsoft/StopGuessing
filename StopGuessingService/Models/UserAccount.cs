using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    [DataContract]
    public class UserAccount
    {
        [DataContract]
        public class ConsumedCredit
        {
            [DataMember]
            public DateTimeOffset WhenCreditConsumed { get; set; }

            [DataMember]
            public float AmountConsumed { get; set; }
        }

        /// <summary>
        /// A string that uniquely identifies the account.
        /// </summary>
        [DataMember]
        public string UsernameOrAccountId { get; set; }

        /// <summary>
        /// The salt is a random unique sequence of bytes that is included when the password is hashed (phase 1 of hashing)
        /// to ensure that attackers who might obtain the set of account hashes cannot hash a password once and then compare
        /// the hash against every account.
        /// </summary>
        [DataMember]
        public byte[] SaltUniqueToThisAccount { get; set; }

        /// <summary>
        /// An EC public encryption symmetricKey used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private symmetricKey.
        /// </summary>
        [DataMember]
        public ECDiffieHellmanPublicKey EcPublicAccountLogKey { get; set; }

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// </summary>
        [DataMember]
        public string PasswordHashPhase1FunctionName { get; set; }

        /// <summary>
        /// The EC private symmetricKey encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        [DataMember]
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; private set; }

        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption symmetricKey for the log. 
        /// </summary>
        [DataMember]
        public string PasswordHashPhase2 { get; set; }

        /// <summary>
        /// A recency set of the device cookies (hashed via SHA256 and converted to Base64)
        /// that have successfully logged into this account.
        /// </summary>
        [DataMember]
        public CapacityConstrainedSet<string> HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount { get; set; }


        /// <summary>
        /// A length-limited sequence of records describing failed login attempts (invalid passwords) 
        /// </summary>
        [DataMember]
        public Sequence<LoginAttempt> PasswordVerificationFailures { get; set; }

        /// <summary>
        /// A sequence of credits consumed in order to use successful logins from this account
        /// to counter evidence that that logged into this account is malicious.
        /// </summary>
        [DataMember]
        public Sequence<ConsumedCredit> ConsumedCredits { get; set; }

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
            return ExpensiveHashFunctionFactory.Get(PasswordHashPhase1FunctionName)(password, SaltUniqueToThisAccount);
        }


        public static string ComputerPhase2HashFromPhase1Hash(byte[] phase1Hash)
        {
            return Convert.ToBase64String(SHA256.Create().ComputeHash(phase1Hash));
        }

        /// <summary>
        /// This analysis will examine LoginAttempts that failed due to an incorrect password
        /// and, where it can be determined if the failure was due to a typo or not,
        /// update the outcomes to reflect whether it was or was not a typo.
        /// </summary>
        /// <param name="correctPassword">The correct password for this account.  (We can only know it because
        /// the client must have provided the correct one this attempt.)</param>
        /// <param name="phase1HashOfCorrectPassword">The phase1 hash of that correct password---which we could
        /// recalculate from the information in the previous parameters, but doing so would be expensive.</param>
        /// <param name="maxEditDistanceConsideredATypo">The maximum edit distance between the correct and incorrect password
        /// that is considered a typo (inclusive).</param>
        /// <param name="attemptsToAnalyze">The set of LoginAttempts to analyze.</param>
        /// <returns>Returns the set of LoginAttempt records that were modified so that the updates
        /// to their outcomes can be written to stable store or other actions taken.</returns>
        public List<LoginAttempt> UpdateLoginAttemptOutcomeUsingTypoAnalysis(
            string correctPassword,
            byte[] phase1HashOfCorrectPassword,
            float maxEditDistanceConsideredATypo,
            IEnumerable<LoginAttempt> attemptsToAnalyze)
        {
            List<LoginAttempt> changedAttempts = new List<LoginAttempt>();

            ECDiffieHellmanCng ecPrivateAccountLogKey = null;

            foreach (LoginAttempt attempt in attemptsToAnalyze)
            {

                // If we haven't yet decrypted the EC key, which we will in turn use to decrypt the password
                // provided in this login attempt, do it now.  (We don't do it in advance as we don't want to
                // do the work unless we find at least one record to analyze.)
                if (ecPrivateAccountLogKey == null)
                {
                    // Get the EC decryption key, which is stored encrypted with the Phase1 password hash
                    try
                    {
                        ecPrivateAccountLogKey = Encryption.DecryptAesCbcEncryptedEcPrivateKey(
                            EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
                    }
                    catch (Exception)
                    {
                        // There's a problem with the key that prevents us from decrypting it.  We won't be able to do this analysis.                            
                        return changedAttempts;
                    }
                }

                // Now try to decrypt the incorrect password from the previous attempt and perform the typo analysis
                try
                {
                    // Attempt to decrypt the password.
                    string incorrectPasswordFromPreviousAttempt =
                        attempt.DecryptAndGetIncorrectPassword(ecPrivateAccountLogKey);

                    // Use an edit distance calculation to determine if it was a likely typo
                    bool likelyTypo = EditDistance.Calculate(incorrectPasswordFromPreviousAttempt, correctPassword) <=
                                      maxEditDistanceConsideredATypo;

                    // Update the outcome based on this information.
                    attempt.Outcome = likelyTypo
                        ? AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoLikely
                        : AuthenticationOutcome.CredentialsInvalidIncorrectPasswordTypoUnlikely;

                    // Add this to the list of changed attempts
                    changedAttempts.Add(attempt);
                }
                catch (Exception)
                {
                    // An exception is likely due to an incorrect key (perhaps outdated).
                    // Since we simply can't do anything with a record we can't Decrypt, we carry on
                    // as if nothing ever happened.  No.  Really.  Nothing to see here.
                }
            }
            return changedAttempts;
        }


        /// <summary>
        /// Sets the password of a user.
        /// <b>Important</b>: this does not authenticate the user but assumes the user has already been authenticated.
        /// The <paramref name="oldPassword"/> field is used only to optionally recover the EC symmetricKey, not to authenticate the user.
        /// </summary>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="oldPassword">If this optional field is provided and correct, the old password will allow us to re-use the old log decryption symmetricKey.
        /// <b>Providing this parameter will not cause this function to authenticate the user first.  The caller must do so beforehand.</b></param>
        /// <param name="nameOfExpensiveHashFunctionToUse"></param>
        public void SetPassword(string newPassword, string oldPassword = null, string nameOfExpensiveHashFunctionToUse = null)
        {
            ECDiffieHellmanCng ecAccountLogKey = null;
            byte[] oldPasswordHashPhase1;


            if (oldPassword != null &&
                ComputerPhase2HashFromPhase1Hash(oldPasswordHashPhase1 = ComputePhase1Hash(oldPassword)) == PasswordHashPhase2)
            {
                // If we have a valid old password, Decrypt the private log decryption symmetricKey so we can re-encrypt it
                // with the new password and continue to use it on future logins. 
                try
                {
                    ecAccountLogKey = Encryption.DecryptAesCbcEncryptedEcPrivateKey(EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                            oldPasswordHashPhase1);
                }
                catch (Exception)
                {
                    // Ignore crypto failures.  They just mean we were unsuccessful in decrypting the symmetricKey and should create a new one.
                }
            }
            if (ecAccountLogKey == null)
            {
                // We were unable to use an old EC UsernameOrAccountId Log Key,
                // so we'll create a new one
                ecAccountLogKey =
                    new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null,
                        new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport }));
            }

            // If the caller also wants to change the hash function, make that change here now that we're
            // done hashing the old password and are about to hash the new one.
            if (nameOfExpensiveHashFunctionToUse != null)
            {
                PasswordHashPhase1FunctionName = nameOfExpensiveHashFunctionToUse;
            }
            // Calculate the Phase1 hash, which is a computationally-heavy hash of the password
            // We will use this for encrypting the EC account log symmetricKey.
            byte[] newPasswordHashPhase1 = ComputePhase1Hash(newPassword);

            // Calculate the Phase2 hash by hasing the phase 1 hash with SHA256.
            // We can store this without revealing the phase 1 hash used to encrypt the EC account log symmetricKey.
            // We can use it to verify whether a provided password is correct
            PasswordHashPhase2 = ComputerPhase2HashFromPhase1Hash(newPasswordHashPhase1);

            // Store the EC UsernameOrAccountId log symmetricKey encrypted with the phase 1 hash.
            SetAccountLogKey(ecAccountLogKey, newPasswordHashPhase1);
        }



        /// <summary>
        /// Derive the EC private account log key from the phase 1 hash of the correct password.
        /// </summary>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password</param>
        /// <returns></returns>
        protected ECDiffieHellmanCng DecryptPrivateAccountLogKey(byte[] phase1HashOfCorrectPassword)
        {
            return Encryption.DecryptAesCbcEncryptedEcPrivateKey(EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
        }

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
            EcPublicAccountLogKey = ecAccountLogKey.PublicKey;
        }

    }
}
