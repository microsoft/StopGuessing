using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing
{

    public interface IStableStoreContext<in TId, TValue>
    {
        Task<TValue> ReadAsync(TId id, CancellationToken cancellationToken = default(CancellationToken));

        Task WriteNewAsync(TValue value, CancellationToken cancellationToken = default(CancellationToken));

        Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken));
    }


    public interface IStableStoreFactory<in TId, TValue>
    {
        IStableStoreContext<TId, TValue> Get();
    }

    public interface IUserAccountContext : IStableStoreContext<string, UserAccount>
    {}

    public interface IUserAccountContextFactory : IStableStoreFactory<string, UserAccount>
    { }

    public class MemoryOnlyAccountStore : IUserAccountContext
    {
        private readonly Dictionary<string, UserAccount> _store = new Dictionary<string, UserAccount>();

        public async Task<UserAccount> ReadAsync(string usernameOrAccountId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                UserAccount account = null;
                _store.TryGetValue(usernameOrAccountId, out account);
                return account;
            }, cancellationToken);
        }

        public async Task WriteNewAsync(UserAccount account, CancellationToken cancellationToken = new CancellationToken())
        {
            await Task.Run(() =>
                _store[account.UsernameOrAccountId] = account, cancellationToken);
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            await Task.Run(() => { }, cancellationToken);
        }
    }

    public class MemoryOnlyAccountStoreFactory : IUserAccountContextFactory
    {
        private readonly MemoryOnlyAccountStore _memoryOnlyAccountStore = new MemoryOnlyAccountStore();
        public IStableStoreContext<string, UserAccount> Get()
        {
            return _memoryOnlyAccountStore;
        }
    }


    public interface IAccountStableStore
    {
        Task<UserAccount> GetAccountAsync(string usernameOrAccountId, CancellationToken cancellationToken);
        Task WriteNewAccountAsync(UserAccount account, CancellationToken cancellationToken);
    }

    public interface IStableStore
    {
        Task WriteAccountAsync(UserAccount account, CancellationToken cancellationToken);
        Task<UserAccount> ReadAccountAsync(string usernameOrAccountId, CancellationToken cancellationToken);
        Task WriteLoginAttemptAsync(LoginAttempt attempt, CancellationToken cancellationToken);
        Task<LoginAttempt> ReadLoginAttemptAsync(string key, CancellationToken cancellationToken);
        Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(string usernameOrAccountId, int numberToRead, bool includeSuccesses = true,
            bool includeFailures = true, CancellationToken cancellationToken = default (CancellationToken));
        Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(System.Net.IPAddress clientIpAddress, int numberToRead, bool includeSuccesses = true, 
            bool includeFailures = true, CancellationToken cancellationToken = default(CancellationToken));
        Task<bool> IsIpAddressAlwaysPermittedAsync(System.Net.IPAddress clientIpAddress, CancellationToken cancellationToken);
    }


}
