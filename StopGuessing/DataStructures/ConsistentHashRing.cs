using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Interfaces;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// Thread safe consistent hash ring that maps values to members of a set.
    /// 
    /// </summary>
    /// <typeparam name="TMember">The member type must have a ToString() function that generates a unique string for their value that is
    /// consistent across devices. (It may not be a description of a pointer value or a string that
    /// is the same regardless of value.)</typeparam>
    public class ConsistentHashRing<TMember> : IDistributedResponsibilitySet<TMember>
    {

        readonly HashSet<string> _membersKeys;

        /// <summary>
        /// A mapping of points on the ring on to the values that they represent.
        /// </summary>
        internal Dictionary<ulong, KeyValuePair<string, TMember>> PointsToMembers;

        /// <summary>
        /// A list of all points on the ring, sorted so that we can perform a binary sort on it.
        /// The invariant that this list is sorted should hold except when the reader/writer lock
        /// (ReadWriteLock) is locked for write.  The class is responsible for re-sorting the list
        /// by calling SortedPoints.Sort() when writing changes that might cause the list to become
        /// temporarily unsorted while holding the write lock.
        /// </summary>
        internal List<ulong> SortedPoints;

        /// <summary>
        /// The number of points to be allocated into the ring for each member.
        /// </summary>
        readonly int _numberOfPointsOnRingForEachMember;

        /// <summary>
        /// A universal hash function for hashing the string representation of members in the ring
        /// and for finding the point on the ring that representings a key string.
        /// </summary>
        readonly UniversalHashFunction _baseHashFunction;

        /// <summary>
        /// A set of hash functions for quickly re-hashing the first-degree hash to create
        /// NumberOfPointsOnRingForEachValue points for each member.
        /// </summary>
        readonly UniversalHashFunction[] _universalHashFunctionsForEachPoint;

        /// <summary>
        /// A lock for ensuring this class is thread safe.
        /// </summary>
        readonly System.Threading.ReaderWriterLockSlim _readWriteLock = new System.Threading.ReaderWriterLockSlim(System.Threading.LockRecursionPolicy.NoRecursion);


        int _numberOfMembers;

        public int Count => _numberOfMembers;

        // Default values
        const int DefaultMaxInputLengthInBytesForUniversalHashFunction = 256;
        const int DefaultNumberOfPointsOnRingForEachMember = 512;

        /// <summary>
        /// Create a hash ring
        /// </summary>
        /// <param name="key">A key used for creating hash functions.  The key must be kept secret
        /// from adversaries that might attempt to perform algorithmic complexity attacks to do such
        /// things as ensuring all the important values that one might hash map to the same member.</param>
        /// <param name="initialMembers">An optional set of members to place onto the ring.</param>
        /// <param name="maxInputLengthInBytesForUniversalHashFunction"></param>
        /// <param name="numberOfPointsOnRingForEachMember"></param>
        public ConsistentHashRing(string key,
                                   IEnumerable<KeyValuePair<string, TMember>> initialMembers = null,
                                   int maxInputLengthInBytesForUniversalHashFunction = DefaultMaxInputLengthInBytesForUniversalHashFunction,
                                   int numberOfPointsOnRingForEachMember = DefaultNumberOfPointsOnRingForEachMember)
        {
            _numberOfPointsOnRingForEachMember = numberOfPointsOnRingForEachMember;

            // Initialize empty data types
            _membersKeys = new HashSet<string>();
            PointsToMembers = new Dictionary<ulong, KeyValuePair<string, TMember>>();
            SortedPoints = new List<ulong>();

            // Create universal hash functions using the provided key
            _baseHashFunction = new UniversalHashFunction(key, maxInputLengthInBytesForUniversalHashFunction);
            _universalHashFunctionsForEachPoint = new UniversalHashFunction[numberOfPointsOnRingForEachMember];
            for (int i = 0; i < _universalHashFunctionsForEachPoint.Length; i++)
            {
                _universalHashFunctionsForEachPoint[i] = new UniversalHashFunction(key + ":" + i.ToString(), 16);
            }

            // Add the initial members (if their are any)
            if (initialMembers != null)
                AddRange(initialMembers);
        }


        /// <summary>
        /// Map a member to a set of NumberOfPointsOnRingForEachMember points.
        /// </summary>
        /// <param name="membersKey">The key of the member to get the ring points for.</param>
        /// <returns>An array of points of length NumberOfPointsOnRingForEachMember.</returns>
        internal ulong[] GetPointsForMember(string membersKey)
        {
            // Allocate the array
            ulong[] points = new ulong[_numberOfPointsOnRingForEachMember];
            // Calculate a initial hash of the string (with time linear in the size of the string)
            ulong initialHash = _baseHashFunction.Hash(membersKey, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
            // Generate each point by re-hashing the initial hash value using a constant-time
            // universal hash function (since the initial hash is constant size: a 64-bit UInt).
            for (int i = 0; i < points.Length; i++)
            {
                //                Points[i] = UniversalHashFunctionsForEachPoint[i].Hash(Member.ToString(), UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
                points[i] = _universalHashFunctionsForEachPoint[i].Hash(initialHash, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
            }
            // Return the set of points.
            return points;
        }



        public bool ContainsKey(string key)
        {
            _readWriteLock.EnterReadLock();
            try
            {
                return _membersKeys.Contains(key);
            }
            finally
            {
                _readWriteLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Add a new member to the ring.
        /// </summary>
        /// <param name="uniqueKeyIdentifiyingMember">The key of the member to Add</param>
        /// <param name="member">The member to Add</param>
        public void Add(string uniqueKeyIdentifiyingMember, TMember member)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                if (!_membersKeys.Contains(uniqueKeyIdentifiyingMember))
                {
                    // Track that this key is now a member so we don't re-Add it when present
                    _membersKeys.Add(uniqueKeyIdentifiyingMember);

                    // Keep count of the number of members
                    _numberOfMembers++;

                    // Get the set of points  for the member
                    ulong[] points = GetPointsForMember(uniqueKeyIdentifiyingMember);

                    // WriteAccountToStableStoreAsync the mapping of points to members to include the new points
                    // and map them to this new member.
                    foreach (ulong point in points)
                    {
                        PointsToMembers[point] = new KeyValuePair<string, TMember>(uniqueKeyIdentifiyingMember, member);
                    }

                    // WriteAccountToStableStoreAsync the sorted list of points to include the new points,
                    // leaving the list temporarily unsorted.
                    SortedPoints.AddRange(points);

                    // Ensure the set of SortedPoints is actually sorted.
                    SortedPoints.Sort();
                }
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Add a set of members to the ring.
        /// </summary>
        /// <param name="newKeyMemberPairs">The members to be added along with their keys.</param>
        public void AddRange(IEnumerable<KeyValuePair<string, TMember>> newKeyMemberPairs)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                // We'll want to Add each member
                foreach (KeyValuePair<string, TMember> keyAndMember in newKeyMemberPairs)
                {
                    if (!_membersKeys.Contains(keyAndMember.Key))
                    {
                        // Track that this key is now a member so we don't re-Add it when present
                        _membersKeys.Add(keyAndMember.Key);

                        // Track the number of members.  Doing this one-by-one in case of exceptions
                        _numberOfMembers++;

                        // Get the set of points  for the member
                        ulong[] points = GetPointsForMember(keyAndMember.Key);
                        // WriteAccountToStableStoreAsync the mapping of points to members to include the new points
                        // and map them to this new member.
                        foreach (ulong point in points)
                        {
                            PointsToMembers[point] = keyAndMember;
                        }
                        // WriteAccountToStableStoreAsync the sorted list of points to include the new points,
                        // leaving the list unsorted until we've added them for all the new members.
                        SortedPoints.AddRange(points);
                    }
                }
                // Now that all the new members have been added, ensure the list of SortedPoints
                // is actually sorted.
                SortedPoints.Sort();
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }



        protected void RemoveFromSortedPoints_WithoutLocking(IEnumerable<ulong> pointsToRemove)
        {
            List<ulong> sortedPointsToRemove = new List<ulong>(pointsToRemove);
            sortedPointsToRemove.Sort();

            // Remove the points from the PointsToValues array
            foreach (ulong point in sortedPointsToRemove)
                PointsToMembers.Remove(point);


            // The lineary-time way to remove a set of items from a sorted array is to create a new list
            // constructed one-by-one from the items that were not removed.
            List<ulong> newSortedPoints = new List<ulong>(Math.Max(0, SortedPoints.Count - sortedPointsToRemove.Count));

            // Copy into the new array only those values in SortedPoints that are not in the SortedPointsToRemove array.
            // (We'll then replace the SortedPoints array with the NewSortedPoints array.)
            int indexIntoPointsToRemove = 0;
            for (int indexIntoAllPoints = 0; indexIntoAllPoints < SortedPoints.Count;)
            {
                if (indexIntoPointsToRemove >= sortedPointsToRemove.Count)
                {
                    // We've already removed all the points to remove, so all the remaining SortedPoints are copied
                    // into the NewSortedPoints array to ensure we keep them.
                    newSortedPoints.Add(SortedPoints[indexIntoAllPoints++]);
                }
                else if (SortedPoints[indexIntoAllPoints] == sortedPointsToRemove[indexIntoPointsToRemove])
                {
                    // This is one of the points to remove, so increment the index into all points without copying
                    // it into the NewSortedPoints array (effectively removing it.)
                    indexIntoAllPoints++;
                }
                else if (SortedPoints[indexIntoAllPoints] < sortedPointsToRemove[indexIntoPointsToRemove])
                {
                    // We've yet to reach the next sorted point to remove, so the current point is one we're keeping.
                    // Copy it into the NewSortedPoints array to ensure we keep it.
                    newSortedPoints.Add(SortedPoints[indexIntoAllPoints++]);
                }
                else
                {
                    // Oddly, we seem to have reached a number in SortedPoints that is greater than the current one
                    // in SortedPointsToRemove, indicating that we may be trying to remove a point that is not there.
                    // Perhaps we removed it already.  Regardless, we need to increment the
                    // SortedPointsToRemoveIndex so that we reach a point that is greater than or equal to the
                    // current location in the SortedPoints array.
                }

                // Replace SortedPoints with the copy that has the SortedPointsToRemoved removed from it.
                SortedPoints = newSortedPoints;

            }
        }


        /// <summary>
        /// Remove a member from the ring.
        /// </summary>
        /// <param name="uniqueKeyIdentifiyingMember">The key of the member to be removed.</param>
        public void Remove(string uniqueKeyIdentifiyingMember)
        {
            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                if (_membersKeys.Contains(uniqueKeyIdentifiyingMember))
                {
                    // Track that this key is no longer a member so it can be added again the future
                    _membersKeys.Remove(uniqueKeyIdentifiyingMember);

                    // Remove all the points in the ring that are associated with the removed member
                    RemoveFromSortedPoints_WithoutLocking(GetPointsForMember(uniqueKeyIdentifiyingMember));
                }            
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Remove a set of members by providng their keys.
        /// </summary>
        /// <param name="uniqueKeysIdentifiyingMember">A set of keys of members to remove.</param>
        public void RemoveRange(IEnumerable<string> uniqueKeysIdentifiyingMember)
        {
            List<ulong> pointsToRemove = new List<ulong>();

            // Lock to ensure thread safety
            _readWriteLock.EnterWriteLock();
            try
            {
                foreach (string uniqueKeyIdentifiyingMember in uniqueKeysIdentifiyingMember) {
                    if (_membersKeys.Contains(uniqueKeyIdentifiyingMember))
                    {
                        // Track that this key is no longer a member so it can be added again the future
                        _membersKeys.Remove(uniqueKeyIdentifiyingMember);

                        // Track the set of points to remove
                        pointsToRemove.AddRange(GetPointsForMember(uniqueKeyIdentifiyingMember));

                    // Fetch and sort the set of points for the item to be removed
                    }
                }

                // Remove all the points in the ring that are associated with the removed members
                RemoveFromSortedPoints_WithoutLocking(pointsToRemove);
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }


        public Dictionary<string, double> FractionalCoverage
        {
            get
            {
                Dictionary<string, ulong> counts = new Dictionary<string, ulong>();
                ulong lastPoint = 0;
                counts[PointsToMembers[SortedPoints[0]].Key] = (lastPoint - SortedPoints[SortedPoints.Count - 1]) + 1;
                foreach (ulong point in SortedPoints)
                {
                    ulong sinceLast = point - lastPoint;
                    string key = PointsToMembers[point].Key;
                    if (!counts.ContainsKey(key))
                        counts[key] = 0;
                    counts[key] += sinceLast;
                    lastPoint = point;
                }
                List<string> keys = counts.Keys.ToList();
                Dictionary<string, double> result = new Dictionary<string, double>();
                foreach (string key in keys)
                    result[key] = counts[key] / (double)ulong.MaxValue;
                return result;
            }
        }

        /// <summary>
        /// Find the nearest node at a point greater than or equal to the point to find
        /// </summary>
        /// <param name="pointToFind">The point to find</param>
        /// <returns></returns>
        private TMember BinarySearchWithoutLocking(ulong pointToFind) 
        {            
            int minIndex = 0;
            int maxIndex = SortedPoints.Count - 1;

            // Handle special case where the point to find is greater than all the values on the ring
            if (pointToFind > SortedPoints[maxIndex])
            {
                return PointsToMembers[SortedPoints[0]].Value;
            }
            else
            {
                // Use a binary search to find the first point in the ring with value 
                // greater than or equal to this key's point
                while (maxIndex > minIndex)
                {
                    int midPointIndex = (maxIndex + minIndex) / 2;
                    ulong midPointValue = SortedPoints[midPointIndex];
                    if (midPointValue < pointToFind)
                    {
                        // The midpoint has a value that is smaller than point to find, and so 
                        // the index to return must be at least one greater than it.
                        minIndex = midPointIndex + 1;
                    }
                    else
                    {
                        // The midpoint value is at least as large as the point to find, and so
                        // the index to return may not be greater than it.
                        maxIndex = midPointIndex;
                    }
                }
                // Since MinIndex == MaxIndex, we can use either as an index into the point
                // that we've found to be the closest point on the ring >= to the key's point.                
                if (pointToFind > SortedPoints[0] && SortedPoints[minIndex] < pointToFind)
                    throw new Exception("Illegal");

                return PointsToMembers[SortedPoints[minIndex]].Value;
            }

        }

        /// <summary>
        /// Find the member at the nearest point on the ring in front of (>=) the point identified by the given key.
        /// </summary>
        /// <param name="key">A string value to be mapped onto the ring so that it can be associated
        /// with the nearest member ahead of it on the ring.</param>
        /// <returns></returns>
        public TMember FindMemberResponsible(string key)
        {
            // Find the first point in the ring with value greater than or equal to this key's Point
            ulong pointToFind = _baseHashFunction.Hash("0:" + key, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);

            TMember memberWithPointClosestToPointToFind;

            _readWriteLock.EnterReadLock();
            try
            {
                memberWithPointClosestToPointToFind = BinarySearchWithoutLocking(pointToFind);
            }
            finally
            {
                _readWriteLock.ExitReadLock();
            }
        
            // Return the member associated with the closest point.
            return memberWithPointClosestToPointToFind;
        }

        public List<TMember> FindMembersResponsible(string key, int numberOfUniqueMembersToFind)
        {
            // Find the first point in the ring with value greater than or equal to this key's Point
            List<TMember> result = new List<TMember>(numberOfUniqueMembersToFind);

            _readWriteLock.EnterReadLock();
            try
            {
                while (result.Count < numberOfUniqueMembersToFind && result.Count < _numberOfMembers) {
                    ulong pointToFind = _baseHashFunction.Hash(result.Count.ToString() + ":" + key, UniversalHashFunction.MaximumNumberOfResultBitsAllowing32BiasedBits);
                    TMember member = BinarySearchWithoutLocking(pointToFind);
                    if (!result.Contains(member))
                        result.Add(member);
                }
            }
            finally
            {
                _readWriteLock.ExitReadLock();
            }

            // Return the list of members.
            return result;
        }



    }
}
