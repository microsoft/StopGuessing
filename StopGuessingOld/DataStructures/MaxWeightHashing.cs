using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Interfaces;

namespace StopGuessing.DataStructures
{

    public class MaxWeightHashing<TMemberType> : IDistributedResponsibilitySet<TMemberType>
    {
        protected struct MemberAndItsHashFunction
        {
            public TMemberType Member;
            public UniversalHashFunction HashFunction;
        }

        protected Dictionary<string, MemberAndItsHashFunction> KeyToMemberAndItsHashFunction = new Dictionary<string, MemberAndItsHashFunction>();

        private MemberAndItsHashFunction[] _membersAndTheirHashFunctionsAsArray;

        protected MemberAndItsHashFunction[] MembersAndTheirHashFunctionsAsArray
        {
            get
            {
                MemberAndItsHashFunction[] result = _membersAndTheirHashFunctionsAsArray;
                if (result == null) {
                    _readWriteLock.EnterWriteLock();
                    try
                    {
                        if (result == null)
                        {
                            result = _membersAndTheirHashFunctionsAsArray = KeyToMemberAndItsHashFunction.Values.ToArray();
                        }
                    }
                    finally
                    {
                        _readWriteLock.ExitWriteLock();
                    }
                }
                return result;
            }
            set
            {
                _membersAndTheirHashFunctionsAsArray = value;
            }
        }

        public bool ContainsKey(string uniqueKeyIdentifiyingMember)
        {
            bool result;
            // Lock to ensure thread safety
            _readWriteLock.EnterReadLock();
            try
            {
                result = KeyToMemberAndItsHashFunction.ContainsKey(uniqueKeyIdentifiyingMember);
            }
            finally
            {
                _readWriteLock.ExitReadLock();
            }
            return result;
        }

        /// <summary>
        /// A lock for ensuring this class is thread safe.
        /// </summary>
        readonly System.Threading.ReaderWriterLockSlim _readWriteLock = new System.Threading.ReaderWriterLockSlim(System.Threading.LockRecursionPolicy.NoRecursion);

        readonly UniversalHashFunction _baseHashFunction;

        public int Count => KeyToMemberAndItsHashFunction.Count;

        readonly string _masterKey;

        public MaxWeightHashing(string key,
                                IEnumerable<KeyValuePair<string,TMemberType>> initialMembers = null)
        {
            _masterKey = key;
            _membersAndTheirHashFunctionsAsArray = null;

            _baseHashFunction = new UniversalHashFunction(key);

            // Add the initial members (if their are any)
            if (initialMembers != null)
                AddRange(initialMembers);
        }


        /// <summary>
        /// Add a new member to the ring.
        /// </summary>
        /// <param name="key">The key of the member to add to the ring.</param>
        /// <param name="member">The member to add to the ring</param>
        public void Add(string key, TMemberType member)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                KeyToMemberAndItsHashFunction[key] = new MemberAndItsHashFunction
                {
                    Member = member,
                    HashFunction = new UniversalHashFunction(_masterKey + ":" + key, 16)
                };
            
                // Reset the fast-access array of the members and their hash functions
                MembersAndTheirHashFunctionsAsArray = null;
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Add a set of members to the ring.
        /// </summary>
        /// <param name="newKeyMemberPairs">The set of members to add, along with their keys.</param>
        public void AddRange(IEnumerable<KeyValuePair<string, TMemberType>> newKeyMemberPairs)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                // We'll want to Add each member
                foreach (KeyValuePair<string, TMemberType> keyMemberPair in newKeyMemberPairs)
                {
                    KeyToMemberAndItsHashFunction[keyMemberPair.Key] = new MemberAndItsHashFunction
                    {
                        Member = keyMemberPair.Value,
                        HashFunction = new UniversalHashFunction(_masterKey + ":" + keyMemberPair.Key, 16)
                    };
                }
                // Reset the fast-access array of the members and their hash functions
                MembersAndTheirHashFunctionsAsArray = null;
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Remove a member from the ring.
        /// </summary>
        /// <param name="key">The key of the member to be removed.</param>
        public void Remove(string key)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                KeyToMemberAndItsHashFunction.Remove(key);
                // Reset the fast-access array of the members and their hash functions
                MembersAndTheirHashFunctionsAsArray = null;
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        public void RemoveRange(IEnumerable<string> keys)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                foreach (string key in keys)
                {
                    KeyToMemberAndItsHashFunction.Remove(key);
                }
                // Reset the fast-access array of the members and their hash functions
                MembersAndTheirHashFunctionsAsArray = null;
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }



        public TMemberType FindMemberResponsible(string key)
        {
            TMemberType highestScoringMember = default(TMemberType);
            ulong highestScore = 0;

            UInt32 intermediateHashValue = (UInt32)_baseHashFunction.Hash(key);

            MemberAndItsHashFunction[] localMembersAndTheirHashFunctions =
                MembersAndTheirHashFunctionsAsArray;

            foreach (MemberAndItsHashFunction memberAndHash in localMembersAndTheirHashFunctions)
            {
                ulong score = memberAndHash.HashFunction.Hash(intermediateHashValue, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
                if (score > highestScore)
                {
                    highestScore = score;
                    highestScoringMember = memberAndHash.Member;
                }
            }

            return highestScoringMember;
        }

        public List<TMemberType> FindMembersResponsible(string key, int numberOfUniqueMembersToFind)
        {
            TMemberType[] highestScoringMembers = new TMemberType[numberOfUniqueMembersToFind];
            ulong[] highestScores = new ulong[numberOfUniqueMembersToFind];
            
            UInt32 intermediateHashValue = (UInt32) _baseHashFunction.Hash(key);

            MemberAndItsHashFunction[] localMembersAndTheirHashFunctions =
                MembersAndTheirHashFunctionsAsArray;

            foreach (MemberAndItsHashFunction memberAndHash in localMembersAndTheirHashFunctions)
            {
                TMemberType member = memberAndHash.Member;
                UniversalHashFunction hashFunction = memberAndHash.HashFunction;
                ulong score = hashFunction.Hash(intermediateHashValue, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);

                int indexToWriteInto;
                for (indexToWriteInto = numberOfUniqueMembersToFind;
                     indexToWriteInto >= 1 && score > highestScores[indexToWriteInto - 1];
                     indexToWriteInto--) {
                }
                
                while (indexToWriteInto < numberOfUniqueMembersToFind)
                {
                    TMemberType evictedMember = highestScoringMembers[indexToWriteInto];
                    ulong evictedScore = highestScores[indexToWriteInto];
                    highestScoringMembers[indexToWriteInto] = member;
                    highestScores[indexToWriteInto] = score;
                    indexToWriteInto++;
                    member = evictedMember;
                    score = evictedScore;
                }
            }

            List<TMemberType> result = new List<TMemberType>(numberOfUniqueMembersToFind);
            for (int i = 0; i < numberOfUniqueMembersToFind && !highestScoringMembers[i].Equals(default(TMemberType)); i++) {
                result.Add(highestScoringMembers[i]);
            }

            return result;
        }

    }
}
