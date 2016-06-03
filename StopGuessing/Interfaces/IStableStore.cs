using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.AccountStorage.Memory;

namespace StopGuessing.Interfaces
{


    //public interface IUserAccountContextFactory : IStableStoreFactory<string, UserAccount>
    //{ }

    public class MemoryOnlyUserAccountRepository : IRepository<string, MemoryUserAccount> // IStableStoreContext<string, UserAccount>
    {
        private ConcurrentDictionary<string, MemoryUserAccount> _store;

        public MemoryOnlyUserAccountRepository(ConcurrentDictionary<string, MemoryUserAccount> store)
        {
            _store = store;
        }

#pragma warning disable 1998
        public async Task<MemoryUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            cancellationToken.ThrowIfCancellationRequested();
            MemoryUserAccount result;
            _store.TryGetValue(usernameOrAccountId, out result);
            return result;
        }

#pragma warning disable 1998
        public async Task AddAsync(MemoryUserAccount itemToAdd, CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore 1998
        {
            _store.TryAdd(itemToAdd.UsernameOrAccountId, itemToAdd);
        }

#pragma warning disable 1998
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
#pragma warning disable 1998
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        public void Dispose()
        {
            
        }

    }

    public class MemoryOnlyUserAccountFactory : IUserAccountRepositoryFactory<MemoryUserAccount>
    {
        private readonly ConcurrentDictionary<string, MemoryUserAccount> _store = new ConcurrentDictionary<string, MemoryUserAccount>();

        public IRepository<string, MemoryUserAccount> Create()
        {
            return new MemoryOnlyUserAccountRepository(_store);
        }

        public void Add(MemoryUserAccount account)
        {
            _store[account.UsernameOrAccountId] = account;
        }

        public void Dispose()
        {
        }

    }



}
