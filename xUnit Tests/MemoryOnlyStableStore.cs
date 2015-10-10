using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing;
using StopGuessing.Models;

namespace xUnit_Tests
{
    public class MemoryOnlyStableStore : IStableStore
    {
        public Dictionary<string, UserAccount> Accounts = new Dictionary<string, UserAccount>();
        public Dictionary<string, LoginAttempt> LoginAttempts = new Dictionary<string, LoginAttempt>();

        public async Task<bool> IsIpAddressAlwaysPermittedAsync(System.Net.IPAddress clientIpAddress, CancellationToken cancelToken = default(CancellationToken))
        {
            return await Task.Run( () => false, cancelToken);
        }

        public async Task<UserAccount> ReadAccountAsync(string usernameOrAccountId, CancellationToken cancelToken)
        {
            return await Task.Run( () =>
            {
                lock (Accounts)
                {
                    UserAccount account;
                    Accounts.TryGetValue(usernameOrAccountId, out account);
                    return account;
                }
            }, cancelToken);
        }

        public async Task<LoginAttempt> ReadLoginAttemptAsync(string key, CancellationToken cancelToken)
        {
            return await Task.Run(() =>
            {
                lock (LoginAttempts)
                {
                    LoginAttempt attempt;
                    LoginAttempts.TryGetValue(key, out attempt);
                    return attempt;
                }
            }, cancelToken);
        }

        public async Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(System.Net.IPAddress clientIpAddress, int numberToRead, 
            bool includeSuccesses = true, bool includeFailures = true, CancellationToken cancelToken = default(CancellationToken))
        {
            // fail on purpose
            return await Task.Run(() => new List<LoginAttempt>(), cancelToken);
        }

        public async Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(string usernameOrAccountId, int numberToRead, 
            bool includeSuccesses = true, bool includeFailures = true, CancellationToken cancelToken = default(CancellationToken))
        {
            // fail on purpose
            return await Task.Run(() => new List<LoginAttempt>(), cancelToken);
        }

        public async Task WriteAccountAsync(UserAccount account, CancellationToken cancelToken)
        {
            await Task.Run(() =>
            {
                lock (Accounts)
                {
                    Accounts[account.UsernameOrAccountId] = account;
                }
            }, cancelToken);
        }

        public async Task WriteLoginAttemptAsync(LoginAttempt attempt, CancellationToken cancelToken)
        {
            await Task.Run(() =>
            {
                lock (LoginAttempts)
                {
                    LoginAttempts[attempt.UniqueKey] = attempt;
                }
            }, cancelToken);
        }
    }
}
