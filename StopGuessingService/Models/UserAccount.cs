using System;
using System.Linq;
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
        /// The salt used to hash the password
        /// </summary>
        [DataMember]
        public byte[] SaltUniqueToThisAccount { get; set; }

        /// <summary>
        /// An EC public encryption key used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private key.
        /// </summary>
        [DataMember]
        public ECDiffieHellmanPublicKey EcPublicAccountLogKey { get; set; }

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// Make sure the function has been added. -- FIXME -- explainhow
        /// </summary>
        [DataMember]
        public string PasswordHashPhase1FunctionName { get; set; }

        /// <summary>
        /// The EC private key encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        [DataMember]
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; set; }

        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption key for the log. 
        /// </summary>
        [DataMember]
        public byte[] PasswordHashPhase2 { get; set; }

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
        /// The phase 2 hashes of the last few incorrect passwords, which we can use to check
        /// to see if the same client is trying to login over and over again with an incorrect
        /// password (perhaps the old password or a misconfigured password).
        /// </summary>
        [DataMember]
        public Sequence<byte[]> Phase2HashesOfRecentlyIncorrectPasswords { get; set;  }

        /// <summary>
        /// A sequence of credits consumed in order to use successful logins from this account
        /// to counter evidence that that logged into this account is malicious.
        /// </summary>
        [DataMember]
        public Sequence<ConsumedCredit> ConsumedCredits { get; set; }




        /// <summary>
        /// Sets the password of a user.
        /// <b>Important</b>: this does not authenticate the user but assumes the user has already been authenticated.
        /// The <paramref name="oldPassword"/> field is used only to optionally recover the EC key, not to authenticate the user.
        /// </summary>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="oldPassword">If this optional field is provided and correct, the old password will allow us to re-use the old log decryption key.
        /// <b>Providing this parameter will not cause this function to authenticate the user first.  The caller must do so beforehand.</b></param>
        /// <param name="nameOfExpensiveHashFunctionToUse"></param>
        public void SetPassword(string newPassword, string oldPassword = null,
            string nameOfExpensiveHashFunctionToUse = null)
        {
            ECDiffieHellmanCng ecAccountLogKey = null;
            byte[] oldPasswordHash1;


            if (oldPassword != null &&
                SHA256.Create().ComputeHash((oldPasswordHash1 = ExpensiveHashFunctionFactory.Get(PasswordHashPhase1FunctionName)(
                                oldPassword, SaltUniqueToThisAccount)))
                    .SequenceEqual(PasswordHashPhase2))
            {
                // If we have a valid old password, Decrypt it so we can keep using it
                try
                {
                    byte[] privateLogDecryptionKey =
                        Encryption.DecryptAescbc(EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, oldPasswordHash1,
                            checkAndRemoveHmac: true);
                    ecAccountLogKey =
                        new ECDiffieHellmanCng(CngKey.Import(privateLogDecryptionKey, CngKeyBlobFormat.EccPrivateBlob));
                }
                catch (Exception)
                {
                    // Ignore crypto failures.  They just mean we were unsuccessful in decrypting the key and should create a new one.
                }
            }
            if (ecAccountLogKey == null)
            {
                // We were unable to use an old EC Account Log Key,
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
            // We will use this for encrypting the EC account log key.
            byte[] newPasswordHash1 = ExpensiveHashFunctionFactory.Get(PasswordHashPhase1FunctionName)(
                newPassword, SaltUniqueToThisAccount);

            // Calculate the Phase2 hash by hasing the phase 1 hash with SHA256.
            // We can store this without revealing the phase 1 hash used to encrypt the EC account log key.
            // We can use it to verify whether a provided password is correct
            PasswordHashPhase2 = SHA256.Create().ComputeHash(newPasswordHash1);

            // Store the EC Account log key encrypted with the phase 1 hash.
            byte[] ecAccountLogKeyAsBytes = ecAccountLogKey.Key.Export(CngKeyBlobFormat.EccPrivateBlob);
            EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 =
                Encryption.EncryptAesCbc(ecAccountLogKeyAsBytes, newPasswordHash1.Take(16).ToArray(), addHmac: true);
            EcPublicAccountLogKey = ecAccountLogKey.PublicKey;
        }
    }
}
