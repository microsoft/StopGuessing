//#define Simulation
// FIXME remove
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public interface IUserAccountStore : IDisposable
    {
        Task<IUserAccount> LoadAsync(CancellationToken? cancellationToken);
        Task SaveChangesAsync(CancellationToken? cancellationToken);
    }

    public interface IUserAccountFactory : IDisposable
    {
        IUserAccountStore Create(string usernameOrAccountId);        
    }
    
}
