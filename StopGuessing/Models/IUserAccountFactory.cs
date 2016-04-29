//#define Simulation
// FIXME remove
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity.Query.Internal;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
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


    

    //public interface IUserAccountFactory : IDisposable
    //{
    //    IRepository<string, IUserAccount> Create();        
    //}
    
}
