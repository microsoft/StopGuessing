using System;
using System.Collections.Concurrent;
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

    public interface IUserAccountContextFactory : IStableStoreFactory<string, UserAccount>
    { }

    public class MemoryOnlyAccountStore : IStableStoreContext<string, UserAccount>
    {
        private readonly ConcurrentDictionary<string, UserAccount> _store = new ConcurrentDictionary<string, UserAccount>();

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

    public class MemoryOnlyAccountContextFactory : IUserAccountContextFactory
    {
        private readonly MemoryOnlyAccountStore _memoryOnlyAccountStore = new MemoryOnlyAccountStore();
        public IStableStoreContext<string, UserAccount> Get()
        {
            return _memoryOnlyAccountStore;
        }
    }


}
