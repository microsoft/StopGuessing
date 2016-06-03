using System.Collections.Generic;

namespace StopGuessing.Interfaces
{

    /// <summary>
    /// Keep track of a set of members such that each will be responsible for some fraction
    /// of keys.
    /// 
    /// Generalizes the concept of Highest Random Weight (HRW, a.k.a. Rendezvous) hashing and
    /// consistent hashing (rings) so that one can utilize whichever is more appropriate for evenness
    /// of distribution and efficiency for small sets (where HRW is superior) or scalability for
    /// large sets (where consistent hashing superior).
    /// Consistent hashing becomes more efficient somewhere between 20 members (for a low-optimized .NET
    /// implementation of HRW) and 500 members (for an HRW implementation that takes advantage of processor
    /// parallelism to perform lots of fast universal hashes.)
    /// </summary>
    /// <typeparam name="TMember"></typeparam>
    public interface IDistributedResponsibilitySet<TMember>
    {
        int Count { get; }

        bool ContainsKey(string key);

        void Add(string uniqueKeyIdentifiyingMember, TMember member);

        void AddRange(IEnumerable<KeyValuePair<string, TMember>> newKeyMemberPairs);

        void Remove(string uniqueKeyIdentifiyingMember);

        void RemoveRange(IEnumerable<string> uniqueKeysIdentifiyingMember);

        TMember FindMemberResponsible(string key);

        List<TMember> FindMembersResponsible(string key, int numberOfUniqueMembersToFind);
    }

}
