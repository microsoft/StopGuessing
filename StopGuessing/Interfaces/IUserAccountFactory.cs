//#define Simulation
// FIXME remove

using System;
using System.Threading;
using System.Threading.Tasks;

namespace StopGuessing.Interfaces
{
    public interface IRepository<TKey,T> : IDisposable
    {
        Task<T> LoadAsync(TKey key, CancellationToken cancellationToken = default(CancellationToken));
        Task AddAsync(T itemToAdd, CancellationToken cancellationToken = default(CancellationToken));
        Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken));
        
    }

    public interface IUserAccountRepositoryFactory<TUserAccount> : IFactory<IRepository<String, TUserAccount>> where TUserAccount : IUserAccount
    {        
    }
        
}
