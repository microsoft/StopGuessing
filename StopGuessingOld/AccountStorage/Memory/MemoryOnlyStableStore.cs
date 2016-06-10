using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing
{
    //public class MemoryOnlyStableStore : IStableStore
    //{
    //    public ConcurrentDictionary<string, UserAccount> Accounts = new ConcurrentDictionary<string, UserAccount>();
    //    public ConcurrentDictionary<string, LoginAttempt> LoginAttempts = new ConcurrentDictionary<string, LoginAttempt>();


    //    public async Task<bool> IsIpAddressAlwaysPermittedAsync(System.Net.IPAddress clientIpAddress, CancellationToken cancelToken = default(CancellationToken))
    //    {
    //        return await Task.RunInBackground( () => false, cancelToken);
    //    }

    //    public async Task<UserAccount> ReadAccountAsync(string usernameOrAccountId, CancellationToken cancelToken)
    //    {
    //        if (Accounts == null)
    //            return null;
    //        return await Task.RunInBackground( () =>
    //        {
    //            UserAccount account;
    //            Accounts.TryGetValue(usernameOrAccountId, out account);
    //            return account;
    //        }, cancelToken);
    //    }

    //    public async Task<LoginAttempt> ReadLoginAttemptAsync(string key, CancellationToken cancelToken)
    //    {
    //        if (LoginAttempts == null)
    //            return null;
    //        return await Task.RunInBackground(() =>
    //        {
    //            LoginAttempt attempt;
    //            LoginAttempts.TryGetValue(key, out attempt);
    //            return attempt;
    //        }, cancelToken);
    //    }

    //    public async Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(System.Net.IPAddress clientIpAddress, int numberToRead, 
    //        bool includeSuccesses = true, bool includeFailures = true, CancellationToken cancelToken = default(CancellationToken))
    //    {
    //        // fail on purpose
    //        return await Task.RunInBackground(() => new List<LoginAttempt>(), cancelToken);
    //    }

    //    public async Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(string usernameOrAccountId, int numberToRead, 
    //        bool includeSuccesses = true, bool includeFailures = true, CancellationToken cancelToken = default(CancellationToken))
    //    {
    //        // fail on purpose
    //        return await Task.RunInBackground(() => new List<LoginAttempt>(), cancelToken);
    //    }

    //    public async Task WriteAccountAsync(UserAccount account, CancellationToken cancelToken)
    //    {
    //        if (Accounts == null)
    //            return;

    //        // REMOVE FOR PRODUCTION
    //        // For testing the mipact of Task.RunInBackground() on performance
    //        //if (true)
    //        //{
    //        //    Accounts[account.UsernameOrAccountId] = account;
    //        //    return;
    //        //}

    //        await Task.RunInBackground(() =>
    //        {
    //            Accounts[account.UsernameOrAccountId] = account;
    //        }, cancelToken);
    //    }

    //    public async Task WriteLoginAttemptAsync(LoginAttempt attempt, CancellationToken cancelToken)
    //    {
    //        if (LoginAttempts == null)
    //            return;
    //        await Task.RunInBackground(() =>
    //        {
    //            LoginAttempts[attempt.UniqueKey] = attempt;
    //        }, cancelToken);
    //    }
    //}
}
