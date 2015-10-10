using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Newtonsoft.Json;
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

        [IgnoreDataMember]
        [JsonIgnore]
        public string Password { set { SetPassword(value); } }


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


        public byte[] ComputePhase1Hash(string password)
        {
            return ExpensiveHashFunctionFactory.Get(PasswordHashPhase1FunctionName)(password, SaltUniqueToThisAccount);
        }


        public static string ComputerPhase2HashFromPhase1Hash(byte[] phase1Hash)
        {
            return Convert.ToBase64String(SHA256.Create().ComputeHash(phase1Hash));
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
            // We will use this for encrypting the EC account log symmetricKey.
            byte[] newPasswordHashPhase1 = ComputePhase1Hash(newPassword);

            // Calculate the Phase2 hash by hasing the phase 1 hash with SHA256.
            // We can store this without revealing the phase 1 hash used to encrypt the EC account log symmetricKey.
            // We can use it to verify whether a provided password is correct
            PasswordHashPhase2 = ComputerPhase2HashFromPhase1Hash(newPasswordHashPhase1);

            // Store the EC Account log symmetricKey encrypted with the phase 1 hash.
            SetAccountLogKey(ecAccountLogKey, newPasswordHashPhase1);
        }
    }
}
