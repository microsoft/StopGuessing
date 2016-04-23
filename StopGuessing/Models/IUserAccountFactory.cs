//#define Simulation
// FIXME remove
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public interface IUserAccountFactory
    {
        Task<IUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken? cancellationToken);
    }
    
}
