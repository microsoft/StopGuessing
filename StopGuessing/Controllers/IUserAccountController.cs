using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Interfaces;
using StopGuessing.Models;

namespace StopGuessing.Controllers
{
    public interface IUserAccountControllerFactory<TAccount> : IFactory<IUserAccountController<TAccount>>
        where TAccount : IUserAccount
    {
    };

    /// <summary>
    /// This interface specifies the methods that must be implemented by the controller class that
    /// modifies user accounts (which in turn must implement IUserAccount).
    /// 
    /// A default implementation of many of the methods is provided by the UserAccountController class,
    /// which implementers may optionally inherit from.
    /// </summary>
    /// <typeparam name="TAccount">The implementation of IUserAccount that the implementer will control.</typeparam>
    public interface IUserAccountController<in TAccount> where TAccount : IUserAccount
    {
        /// <summary>
        /// Sets the password of a user.
        /// <b>Important</b>: this does not authenticate the user but assumes the user has already been authenticated.
        /// The <paramref name="oldPassword"/> field is used only to optionally recover the EC symmetricKey, not to authenticate the user.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="oldPassword">If this optional field is provided and correct, the old password will allow us to re-use the old log decryption symmetricKey.
        /// <b>Providing this parameter will not cause this function to authenticate the user first.  The caller must do so beforehand.</b></param>
        /// <param name="nameOfExpensiveHashFunctionToUse">The name of the phase 1 (expenseive) hash to use.</param>
        /// <param name="numberOfIterationsToUseForPhase1Hash">The number of iterations that the hash should be performed.</param>
        void SetPassword(
            TAccount userAccount,
            string newPassword,
            string oldPassword = null,
            string nameOfExpensiveHashFunctionToUse = null,
            int? numberOfIterationsToUseForPhase1Hash = null);

        /// <summary>
        /// Compute the phase 1 (expensive, unsafe-to-store) hash of a user's password.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="password">The password to hash.</param>
        /// <returns>The (expensive, unsafe-to-store) phase 1 hash of the user's password salted with an account-specific salt.</returns>
        byte[] ComputePhase1Hash(
            TAccount userAccount,
            string password);

        /// <summary>
        /// Compute the phase 2 hash from the phase 1 hash of a user's password.
        /// The phase 2 hash is the safe-to-store inexpensive hash of the unsafe-to-store expensive phase 1 hash.
        /// </summary>
        /// <param name="account">The user's account record.</param>
        /// <param name="phase1Hash">The resulting inexpensive hash.</param>
        /// <returns></returns>
        string ComputePhase2HashFromPhase1Hash(
            TAccount account,
            byte[] phase1Hash);

        /// <summary>
        /// Set the user's EC crypto key for storing logs of information such as incorrect passwords submitted for this userAccount.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="accountLogKey">The new EC key to use for the userAccount log.</param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the user's correct password, which will
        /// be used as a symmetric encryption key with which to protec the secret EC key written to the user's userAccount record.</param>
        void SetAccountLogKey(
            TAccount userAccount,
            EncryptionPrimitives.Encryption.IPrivateKey accountLogKey,
            byte[] phase1HashOfCorrectPassword);

        /// <summary>
        /// Decrypts the user's private EC crypto key, which is stored symmetric-key encrypted using the phase 1 (expensive)
        /// hash of the user's correct password as the symmetric key. 
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 (expensive) hash of the user's correct password.</param>
        /// <returns></returns>
        EncryptionPrimitives.Encryption.IPrivateKey DecryptPrivateAccountLogKey(
            TAccount userAccount,
            byte[] phase1HashOfCorrectPassword);

        /// <summary>
        /// When a user has logged in successfully from a client that supports cookies, store the client's unique (secret) cookie
        /// so that, when the user logs in again from the same client, we can be sure it was indeed the same client.
        /// 
        /// If this method requires IO, it should be implemented to run in the background.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="hashOfCookie">The hash of the client's secret cookie.</param>
        /// <param name="whenSeenUtc">The UTC time of when the login took place.  If not provided or null, the current time is used.</param>
        void RecordHashOfDeviceCookieUsedDuringSuccessfulLoginBackground(
            TAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null);

        //
        // Must be implemented by those inheriting from UserAccountController
        //

        /// <summary>
        /// When a user has logged in successfully from a client that supports cookies, store the client's unique (secret) cookie
        /// so that, when the user logs in again from the same client, we can be sure it was indeed the same client.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="hashOfCookie">The hash of the client's secret cookie.</param>
        /// <param name="whenSeenUtc">The UTC time of when the login took place.  If not provided or null, the current time is used.</param>
        /// <param name="cancellationToken">An optional cancellation token to support asynchrony.</param>
        /// <returns></returns>
        Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(
            TAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken));


        /// <summary>
        /// Test to see if a client has logged into this account successfully in the past,
        /// using the hash of a secret unique cookie as the client identifier. 
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="hashOfCookie">The hash of the secret device-unique cookie submitted with the request</param>
        /// <param name="cancellationToken">An optional cancellation token to support asynchrony.</param>
        /// <returns></returns>
        Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            TAccount userAccount,
            string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken));


        /// <summary>
        /// Record the phase 2 hash of an incorrect password submitted in a failed login attempt so that we
        /// can avoid counting the same mistake twice.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="phase2Hash">The phase 2 (safe-to-store) hash of the user's password.</param>
        /// <param name="whenSeenUtc">The UTC time of when the login took place.  If not provided or null, the current time is used.</param>
        /// <param name="cancellationToken">An optional cancellation token to support asynchrony.</param>
        /// <returns>True if this incorrect password was submitted in a recent prior login attempt for this account.</returns>
        Task<bool> AddIncorrectPhaseTwoHashAsync(
            TAccount userAccount,
            string phase2Hash,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken));


        /// <summary>
        /// Try to get credit to be used for improving the reputation of a client IP address, which is given when an IP
        /// address successfully logs into an account.  Credits ensure that an account can only be used to help a small
        /// number of IP addresses, and can't be abused by attackers to improve the reputations of all their IP addresses.
        /// </summary>
        /// <param name="userAccount">The user's account record.</param>
        /// <param name="amountRequested">The amount of credit requested.</param>
        /// <param name="timeOfRequestUtc">The UTC time of the request.  If not provided or null, the current time is used.</param>
        /// <param name="cancellationToken">An optional cancellation token to support asynchrony.</param>
        /// <returns>The amount of credit given, which is deducted from the account.</returns>
        Task<double> TryGetCreditAsync(
            TAccount userAccount,
            double amountRequested,
            DateTime? timeOfRequestUtc = null,
            CancellationToken cancellationToken = default(CancellationToken));

    }
}
