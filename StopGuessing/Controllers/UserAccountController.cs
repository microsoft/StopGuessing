using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Interfaces;
using StopGuessing.Models;
using StopGuessing.Utilities;

namespace StopGuessing.Controllers
{
    public abstract class UserAccountController<TAccount> : IUserAccountController<TAccount> where TAccount : IUserAccount
    {
        public const int DefaultIterationsForPasswordHash = 1000;
        public const int DefaultSaltLength = 8;
        public const int DefaultMaxFailedPhase2HashesToTrack = 8;
        public const int DefaultMaxNumberOfCookiesToTrack = 24;
        public const int DefaultCreditHalfLifeInHours = 12;
        public const double DefaultCreditLimit = 50;

        /// <summary>
        /// Set up the password hasing defaults for an account and set the password.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="password">The password to set</param>
        /// <param name="numberOfIterationsToUseForHash">Optional number of iterations for hash function.</param>
        /// <param name="passwordHashFunctionName">Optional name of hash function to use.</param>
        public void Initialize(TAccount userAccount,
            string password = null,
            int numberOfIterationsToUseForHash = 0,
            string passwordHashFunctionName = null)
        {
            if (numberOfIterationsToUseForHash > 0)
            {
                userAccount.NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForHash;
            }
            if (passwordHashFunctionName != null)
            {
                userAccount.PasswordHashPhase1FunctionName = passwordHashFunctionName;
            }

            if (password != null)
            {
                SetPassword(userAccount, password);
            }
        }


        public virtual byte[] ComputePhase1Hash(TAccount userAccount, string password)
        {
            return ExpensiveHashFunctionFactory.Get(userAccount.PasswordHashPhase1FunctionName)(
                password, userAccount.SaltUniqueToThisAccount, userAccount.NumberOfIterationsToUseForPhase1Hash);
        }

        public virtual string ComputePhase2HashFromPhase1Hash(TAccount account, byte[] phase1Hash)
        {
            return Convert.ToBase64String(ManagedSHA256.Hash(phase1Hash));
        }

        /// <summary>
        /// Sets the password of a user.
        /// <b>Important</b>: this does not authenticate the user but assumes the user has already been authenticated.
        /// The <paramref name="oldPassword"/> field is used only to optionally recover the EC symmetricKey, not to authenticate the user.
        /// </summary>
        /// <param name="userAccount"></param>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="oldPassword">If this optional field is provided and correct, the old password will allow us to re-use the old log decryption symmetricKey.
        /// <b>Providing this parameter will not cause this function to authenticate the user first.  The caller must do so beforehand.</b></param>
        /// <param name="nameOfExpensiveHashFunctionToUse">The name of the phase 1 (expenseive) hash to use.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations that the hash should be performed.</param>
        public virtual void SetPassword(
            TAccount userAccount,
            string newPassword,
            string oldPassword = null,
            string nameOfExpensiveHashFunctionToUse = null,
            int? numberOfIterationsToUseForPhase1Hash = null)
        {
            // Calculate the old passwords' phase 1 hash before making any changes to the hash function name or number of iterations.
            byte[] oldPasswordHashPhase1 = oldPassword == null ? null : ComputePhase1Hash(userAccount, oldPassword);

            // If the caller also wants to change the hash function or the number of iterations,
            // make that change here now that we're done hashing the old password and are about to hash the new one.
            if (nameOfExpensiveHashFunctionToUse != null)
            {
                userAccount.PasswordHashPhase1FunctionName = nameOfExpensiveHashFunctionToUse;
            }

            if (numberOfIterationsToUseForPhase1Hash.HasValue)
            {
                userAccount.NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForPhase1Hash.Value;
            }

            // Calculate the Phase1 hash, which is a computationally-heavy hash of the password
            // We will use this for encrypting the EC userAccount log symmetricKey.
            byte[] newPasswordHashPhase1 = ComputePhase1Hash(userAccount, newPassword);

            // Calculate the Phase2 hash by hasing the phase 1 hash with SHA256.
            // We can store this without revealing the phase 1 hash used to encrypt the EC userAccount log symmetricKey.
            // We can use it to verify whether a provided password is correct
            userAccount.PasswordHashPhase2 = ComputePhase2HashFromPhase1Hash(userAccount, newPasswordHashPhase1);

            // Store the ecAccountLogKey encrypted encrypted using symmetric encryption with the phase 1 password hash as its key.

            // First try using the ecAccountLogKey from the prior password
            if (oldPassword != null &&
                ComputePhase2HashFromPhase1Hash(userAccount, oldPasswordHashPhase1) == userAccount.PasswordHashPhase2)
            {
                // If we have a valid old password, Decrypt the private log decryption symmetricKey so we can re-encrypt it
                // with the new password and continue to use it on future logins. 
                try
                {
                    using (ECDiffieHellmanCng ecAccountLogKey =
                        Encryption.DecryptAesCbcEncryptedEcPrivateKey(
                            userAccount.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1,
                            oldPasswordHashPhase1))
                    {
                        SetAccountLogKey(userAccount, ecAccountLogKey, newPasswordHashPhase1);
                        return;
                    }
                }
                catch (Exception)
                {
                    // Ignore crypto failures.  They just mean we were unsuccessful in decrypting the symmetricKey and should create a new one.
                }
            }

            // We were unable to use an old EC Account Log Key,
            // so we'll create a new one
            using (ECDiffieHellmanCng ecAccountLogKey =
                new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null,
                    new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport })))
            {
                SetAccountLogKey(userAccount, ecAccountLogKey, newPasswordHashPhase1);
            }
        }


        public virtual void SetAccountLogKey(
            TAccount userAccount,
            ECDiffieHellmanCng ecAccountLogKey,
            byte[] phase1HashOfCorrectPassword)
        {
            userAccount.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 = Encryption.EncryptEcPrivateKeyWithAesCbc(ecAccountLogKey,
                phase1HashOfCorrectPassword);
            using (ECDiffieHellmanPublicKey publicKey = ecAccountLogKey.PublicKey)
            {
                userAccount.EcPublicAccountLogKey = publicKey.ToByteArray();
            }
        }

        /// <summary>
        /// Derive the EC private userAccount log key from the phase 1 hash of the correct password.
        /// </summary>
        /// <param name="userAccount"></param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password</param>
        /// <returns></returns>
        public virtual ECDiffieHellmanCng DecryptPrivateAccountLogKey(
            TAccount userAccount,
            byte[] phase1HashOfCorrectPassword)
        {
            return Encryption.DecryptAesCbcEncryptedEcPrivateKey(userAccount.EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1, phase1HashOfCorrectPassword);
        }

        public virtual void RecordHashOfDeviceCookieUsedDuringSuccessfulLoginBackground(
            TAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null)
        {
            TaskHelper.RunInBackground(
                RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(userAccount, hashOfCookie, whenSeenUtc));
        }
        public abstract Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            TAccount userAccount,
            string hashOfCookie,
            CancellationToken cancellationToken = new CancellationToken());

        public abstract Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(
            TAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null, CancellationToken cancellationToken = new CancellationToken());

        public abstract Task<bool> AddIncorrectPhaseTwoHashAsync(
            TAccount userAccount,
            string phase2Hash,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = new CancellationToken());

        public abstract Task<double> TryGetCreditAsync(
            TAccount userAccount,
            double amountRequested,
            DateTime? timeOfRequestUtc = null,
            CancellationToken cancellationToken = new CancellationToken());


    }
}
