using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using StopGuessing.Azure;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using StopGuessing.Utilities;

namespace StopGuessing.Memory
{
    public class MemoryUserAccountControllerFactory : IUserAccountControllerFactory<MemoryUserAccount>
    {

        public IUserAccountController<MemoryUserAccount> Create()
        {
            return new MemoryUserAccountController();
        }
    }

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
            MemoryUserAccount account = new MemoryUserAccount();
            
            account.UsernameOrAccountId = usernameOrAccountId;

            Initialize(account, password, numberOfIterationsToUseForHash, passwordHashFunctionName);

            account.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount =
                new SmallCapacityConstrainedSet<string>(maxNumberOfCookiesToTrack ?? DefaultMaxNumberOfCookiesToTrack);
            account.RecentIncorrectPhase2Hashes = new SmallCapacityConstrainedSet<string>(maxFailedPhase2HashesToTrack ?? DefaultMaxFailedPhase2HashesToTrack);
            account.ConsumedCredits = new DecayingDouble(0, currentDateTimeUtc);

            return account;
        }

        public override Task<bool> AddIncorrectPhaseTwoHashAsync(MemoryUserAccount account, string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken)) =>
            TaskHelper.PretendToBeAsync(account.RecentIncorrectPhase2Hashes.Add(phase2Hash));

        public override Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            MemoryUserAccount account, 
            string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken)) =>
            TaskHelper.PretendToBeAsync(account.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Contains(hashOfCookie));

#pragma warning disable 1998
        public override async Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(MemoryUserAccount account, string hashOfCookie,
#pragma warning restore 1998
            DateTime? whenSeenUtc = null, CancellationToken cancellationToken = new CancellationToken())
        {
            account.HashesOfCookiesOfClientsThatHaveSuccessfullyLoggedIntoThisAccount.Add(hashOfCookie);
        }


#pragma warning disable 1998
        public override async Task<double> TryGetCreditAsync(MemoryUserAccount account, 
            double amountRequested,
            DateTime timeOfRequestUtc,
            CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            double amountAvailable = Math.Min(0, account.CreditLimit - account.ConsumedCredits.GetValue(account.CreditHalfLife, timeOfRequestUtc));
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            account.ConsumedCredits.SubtractInPlace(account.CreditHalfLife, amountConsumed, timeOfRequestUtc);
            return amountConsumed;
        }
    }
}
