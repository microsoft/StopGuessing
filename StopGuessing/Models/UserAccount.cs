//#define Simulation
// FIXME remove
using System;
using System.Security.Cryptography;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{

    public abstract class UserAccount : IDisposable
    {
        /// <summary>
        /// A string that uniquely identifies the account.
        /// </summary>
        public string UsernameOrAccountId { get; protected set; }

        /// <summary>
        /// The salt is a random unique sequence of bytes that is included when the password is hashed (phase 1 of hashing)
        /// to ensure that attackers who might obtain the set of account hashes cannot hash a password once and then compare
        /// the hash against every account.
        /// </summary>
        public byte[] SaltUniqueToThisAccount { get; protected set; }

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// </summary>
        public string PasswordHashPhase1FunctionName { get; protected set; } = ExpensiveHashFunctionFactory.DefaultFunctionName;

        /// <summary>
        /// The number of iterations to use for the phase 1 hash to make it more expensive.
        /// </summary>
        public int NumberOfIterationsToUseForPhase1Hash { get; protected set; } = ExpensiveHashFunctionFactory.DefaultNumberOfIterations;

        /// <summary>
        /// An EC public encryption symmetricKey used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private symmetricKey.
        /// </summary>
        public byte[] EcPublicAccountLogKey { get; protected set; }

        /// <summary>
        /// The EC private symmetricKey encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; private set; }

        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption symmetricKey for the log. 
        /// </summary>
        public string PasswordHashPhase2 { get; protected set; }

        /// <summary>
        /// The account's credit limit for offsetting penalties for IP addresses from which
        /// the account has logged in successfully.
        /// </summary>
        public double CreditLimit { get; protected set; } = double.MinValue;

        /// <summary>
        /// The half life with which used credits are removed from the system freeing up new credit
        /// </summary>
        public TimeSpan CreditHalfLife { get; protected set; } = TimeSpan.Zero;

#if Simulation
        public DoubleThatDecaysWithTime[] ConsumedCreditsForSimulation;
#endif

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

        public static string ComputePhase2HashFromPhase1Hash(byte[] phase1Hash)
        {
            return Convert.ToBase64String(ManagedSHA256.Hash(phase1Hash));
        }

        public abstract bool AddIncorrectPhase2Hash(string phase2Hash, DateTime? whenSeenUtc = null);

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
            PasswordHashPhase2 = ComputePhase2HashFromPhase1Hash(newPasswordHashPhase1);

#if !Simulation
            // Store the EC UsernameOrAccountId log symmetricKey encrypted with the phase 1 hash.
            byte[] oldPasswordHashPhase1;
            if (oldPassword != null &&
                ComputePhase2HashFromPhase1Hash(oldPasswordHashPhase1 = ComputePhase1Hash(oldPassword)) ==
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
        public abstract bool HasClientWithThisHashedCookieSuccessfullyLoggedInBefore(string hashOfCookie);
        public abstract void RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(string hashOfCookie, DateTime? whenSeenUtc = null);

        public abstract double GetCreditsConsumed(DateTime asOfTimeUtc);
        public abstract void ConsumeCredit(double amountConsumed, DateTime timeOfConsumptionUtc);


        public double TryGetCredit(double amountRequested, DateTime timeOfRequestUtc)
        {
            double amountAvailable = CreditLimit - GetCreditsConsumed(timeOfRequestUtc);
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            ConsumeCredit(amountConsumed, timeOfRequestUtc);
            return amountConsumed;
        }

#if Simulation
        public double TryGetCreditForSimulation(int simulationIndex, double amountRequested, DateTime timeOfRequestUtc)
        {
            lock (ConsumedCreditsForSimulation)
            {
                double amountAvailable = CreditLimit - ConsumedCreditsForSimulation[simulationIndex].GetValue(timeOfRequestUtc);
                double amountConsumed = Math.Min(amountRequested, amountAvailable);
                ConsumedCreditsForSimulation[simulationIndex].Add(amountConsumed, timeOfRequestUtc);
                return amountConsumed;
            }
        }
#endif


        protected const int DefaultSaltLength = 8;
        protected const int DefaultMaxFailedPhase2HashesToTrack = 8;
        protected const int DefaultMaxNumberOfCookiesToTrack = 24;
        protected const int DefaultCreditHalfLifeInHours = 12;
        protected const double DefaultCreditLimit = 50;

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
        /// <param name="currentDateTimeUtc">The current UTC time on the instant this record has been created</param>
        /// <param name="phase1HashFunctionName">A hash function that is expensive enough to calculate to make offline dictionary attacks 
        /// expensive, but not so expensive as to slow the authentication system to a halt.  If not specified, a default function will be
        /// used.</param>
        /// <param name="saltLength">If <paramref name="saltUniqueToThisAccount"/>is not specified or null, the constructor will create
        /// a random salt of this length.  If this length is not specified, a default will be used.</param>
        protected void Initialize(
            string usernameOrAccountId = null,
#if Simulation
            int numberOfConditions,
#endif
            string password = null,
            double creditLimit = double.NaN,
            TimeSpan? creditHalfLife = null,
            string phase1HashFunctionName = null,
            int numberOfIterationsToUseForPhase1Hash = 0,
            byte[] saltUniqueToThisAccount = null,
            DateTime? currentDateTimeUtc = null,
            int saltLength = DefaultSaltLength)
        {
            // Set values if specified.
            // If no field value specified, and the data structure's field is not yet initialized to a valid value,
            // then set it to the default value.
            if (usernameOrAccountId != null && usernameOrAccountId != UsernameOrAccountId)
                UsernameOrAccountId = usernameOrAccountId;
            
            if (!double.IsNaN(creditLimit) )
                CreditLimit = creditLimit;
            else if (CreditLimit < 0)
                CreditLimit = DefaultCreditLimit;
            
            if (creditHalfLife.HasValue)
                CreditHalfLife = creditHalfLife.Value;
            else if (CreditHalfLife == TimeSpan.Zero)
                CreditHalfLife = new TimeSpan(DefaultCreditHalfLifeInHours, 0, 0);

            if (saltUniqueToThisAccount != null)
            {
                SaltUniqueToThisAccount = saltUniqueToThisAccount;
            }
            else if (SaltUniqueToThisAccount == null)
            {
                SaltUniqueToThisAccount = new byte[saltLength];
                StrongRandomNumberGenerator.GetBytes(SaltUniqueToThisAccount);
            }

            if (phase1HashFunctionName != null)
                PasswordHashPhase1FunctionName = phase1HashFunctionName;
            else if (PasswordHashPhase1FunctionName == null)
                PasswordHashPhase1FunctionName = ExpensiveHashFunctionFactory.DefaultFunctionName;

            if (numberOfIterationsToUseForPhase1Hash > 0)
                NumberOfIterationsToUseForPhase1Hash = numberOfIterationsToUseForPhase1Hash;
            else if (NumberOfIterationsToUseForPhase1Hash == 0)
                NumberOfIterationsToUseForPhase1Hash = ExpensiveHashFunctionFactory.DefaultNumberOfIterations;

#if Simulation
        , ConsumedCreditsForSimulation = new DoubleThatDecaysWithTime[numberOfConditions]
#endif

            if (password != null)
                SetPassword(password);

        }

        public virtual void Dispose()
        {
            // Should write data back to storage
        }
    }
}
