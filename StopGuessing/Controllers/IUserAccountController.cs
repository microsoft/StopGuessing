using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing.Controllers
{
    public interface IUserAccountControllerFactory<TAccount> : IFactory<IUserAccountController<TAccount>>
        where TAccount : IUserAccount
    {
    };

    public interface IUserAccountController<TAccount> where TAccount : IUserAccount
    {
        //
        // Implemented by UserAccountController for those that choose to inherit from it 
        //

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
        void SetPassword(
            TAccount userAccount,
            string newPassword,
            string oldPassword = null,
            string nameOfExpensiveHashFunctionToUse = null,
            int? numberOfIterationsToUseForPhase1Hash = null);

        byte[] ComputePhase1Hash(
            TAccount userAccount,
            string password);

        string ComputePhase2HashFromPhase1Hash(
            TAccount account,
            byte[] phase1Hash);

        void SetAccountLogKey(
            TAccount userAccount,
            ECDiffieHellmanCng ecAccountLogKey,
            byte[] phase1HashOfCorrectPassword);

        ECDiffieHellmanCng DecryptPrivateAccountLogKey(
            TAccount userAccount,
            byte[] phase1HashOfCorrectPassword);

        void RecordHashOfDeviceCookieUsedDuringSuccessfulLoginBackground(
            TAccount account,
            string hashOfCookie,
            DateTime? whenSeenUtc = null);

        //
        // Must be implemented by those inheriting from UserAccountController
        //

        Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            TAccount account,
            string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken));

        Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(
            TAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> AddIncorrectPhaseTwoHashAsync(
            TAccount account,
            string phase2Hash,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<double> TryGetCreditAsync(
            TAccount account,
            double amountRequested,
            DateTime timeOfRequestUtc,
            CancellationToken cancellationToken = default(CancellationToken));

    }
}
