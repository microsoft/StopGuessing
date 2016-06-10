using System;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Utilities;

namespace StopGuessing.AccountStorage.Memory
{
    public class MemoryUserAccountControllerFactory : IUserAccountControllerFactory<MemoryUserAccount>
    {

        public IUserAccountController<MemoryUserAccount> Create()
        {
            return new MemoryUserAccountController();
        }
    }

    /// <summary>
    /// An in-memory implementation of a user-account store that can be used for testing purposes only.
    /// </summary>
    public class MemoryUserAccountController : UserAccountController<MemoryUserAccount>
    {
        public MemoryUserAccountController()
        {
        }

        public MemoryUserAccount Create(
            string usernameOrAccountId,
            string password = null,
            int numberOfIterationsToUseForHash = 0,
            string passwordHashFunctionName = null,
            int? maxNumberOfCookiesToTrack = null,
            int? maxFailedPhase2HashesToTrack = null,
            DateTime? currentDateTimeUtc = null)
        {
            MemoryUserAccount account = new MemoryUserAccount {UsernameOrAccountId = usernameOrAccountId};

            Initialize(account, password, numberOfIterationsToUseForHash, passwordHashFunctionName);

            account.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount =
                new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack ?? DefaultMaxNumberOfCookiesToTrack);
            account.RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack ?? DefaultMaxFailedPhase2HashesToTrack);
            account.ConsumedCredits = new DecayingDouble(0, currentDateTimeUtc);

            return account;
        }

        public override Task<bool> AddIncorrectPhaseTwoHashAsync(MemoryUserAccount userAccount, string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken)) =>
            TaskHelper.PretendToBeAsync(userAccount.RecentIncorrectPhase2Hashes.Add(phase2Hash));

        public override Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            MemoryUserAccount userAccount, 
            string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken)) =>
            TaskHelper.PretendToBeAsync(userAccount.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Contains(hashOfCookie));

#pragma warning disable 1998
        public override async Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(MemoryUserAccount account, string hashOfCookie,
#pragma warning restore 1998
            DateTime? whenSeenUtc = null, CancellationToken cancellationToken = new CancellationToken())
        {
            account.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Add(hashOfCookie);
        }


#pragma warning disable 1998
        public override async Task<double> TryGetCreditAsync(MemoryUserAccount userAccount, 
            double amountRequested,
            DateTime? timeOfRequestUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            DateTime timeOfRequestOrNowUtc = timeOfRequestUtc ?? DateTime.UtcNow;
            double amountAvailable = Math.Max(0, userAccount.CreditLimit - userAccount.ConsumedCredits.GetValue(userAccount.CreditHalfLife, timeOfRequestOrNowUtc));
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            userAccount.ConsumedCredits.SubtractInPlace(userAccount.CreditHalfLife, amountConsumed, timeOfRequestOrNowUtc);
            return amountConsumed;
        }
    }
}
