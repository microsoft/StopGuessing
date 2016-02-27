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

        Task WriteNewAsync(TId id, TValue value, CancellationToken cancellationToken = default(CancellationToken));

        Task SaveChangesAsync(TId id, TValue value, CancellationToken cancellationToken = default(CancellationToken));
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

#pragma warning disable 1998
        public async Task<UserAccount> ReadAsync(string usernameOrAccountId, CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserAccount account = null;
            _store.TryGetValue(usernameOrAccountId, out account);
            return account;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task WriteNewAsync(string id, UserAccount account, CancellationToken cancellationToken = new CancellationToken())
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            cancellationToken.ThrowIfCancellationRequested();
            _store[id] = account;
        }
        
#pragma warning disable 1998
        public async Task SaveChangesAsync(string id, UserAccount account, CancellationToken cancellationToken = new CancellationToken())
#pragma warning restore 1998
        {
            // FUTURE -- remove async and return Task.CompletedTask when this .NET 4.6 property is availble
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
