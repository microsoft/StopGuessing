using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Interfaces;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace StopGuessing.Clients
{
    /// <summary>
    /// The client side of a distributed binomial ladder frequency filter.
    /// Binomial ladder filters are used to identify frequently-occuring elements in streams while
    /// minimizing the data revealed/stored about infrequently-occuring elements.
    /// 
    /// This client is utilized both by applications using a distributed binomial ladder filter and
    /// is also by the servers implementing the server to communicate amongst themselves.
    /// 
    /// For more information about the binomial ladder filter, search its name and "Microsoft Research"
    /// to find detailed publications/tech reports.
    /// </summary>
    public class DistributedBinomialLadderFilterClient : IBinomialLadderFilter
    {
        /// <summary>
        /// The rest path for the controller.
        /// </summary>
        public const string ControllerPath = "/api/DBLS/";
        /// <summary>
        /// The path under the controller path for REST requests that set individual bits of the filter array.
        /// </summary>
        public const string BitsPath = ControllerPath + "Bits/";
        /// <summary>
        /// The path under the controller path for REST requests for Height (via rest GET) and STEP (via rest POST)
        /// </summary>
        public const string ElementsPath = ControllerPath + "Elements/";

        /// <summary>
        /// The number of shards that the filter array is evenly divided into.
        /// </summary>
        public readonly int NumberOfShards;
        /// <summary>
        /// The maximum (and default) height of elements' ladders.
        /// </summary>
        public readonly int MaxLadderHeight;

        /// <summary>
        /// The maximum amount of time an element should allowed to be treated as at the top of its ladder
        /// due to caching when it may actually have fallen down the ladder since the value was cached.
        /// </summary>
        public readonly TimeSpan MinimumCacheFreshnessRequired;

        /// <summary>
        /// The hash function used to map elements to shards.
        /// </summary>
        protected UniversalHashFunction ShardHashFunction;

        /// <summary>
        /// Records for each shard that is (or at one time has been) stored on this server.
        /// </summary>
        public IDistributedResponsibilitySet<RemoteHost> ShardToHostMapping;

        /// <summary>
        /// A LRU cache of elements that are at the top of their ladder, used less to improve performance
        /// and more to prevent a single node from being overloaded if all requests target a single element.
        /// </summary>
        protected FixedSizeLruCache<string, DateTime> CacheOfElementsAtTopOfLadder;

        /// <summary>
        /// Create a client for a distributed binomial ladder filter
        /// </summary>
        /// <param name="numberOfShards">The number of shards that the bit array of the binomial ladder filter will be divided into.
        /// The greater the number of shards, the more evently it can be distributed.  However, the number of shards should still
        /// be a few orders of magnitude smaller than the ladder height.</param>
        /// <param name="defaultHeightOfLadder">The default ladder height for elements on the ladder.</param>
        /// <param name="shardToHostMapping">An object that maps each shard number to the host responsible for that shard.</param>
        /// <param name="configurationKey">A key used to protect the hashing from algorithmic complexity attacks.
        /// This key should not be unique to the application using the filter and should not be known to any untrusted
        /// systems that might control which elements get sent to the filter.  If an attacker could submit elements to the filter
        /// and knew this key, the attacker could arrange for all elements to go to the same shard and in so doing overload that shard.</param>
        /// <param name="mininmumCacheFreshnessRequired">The maximum time that an element should be kept in the cache of elements at the top of their ladder.
        /// In other words, how long to bound the possible time that an element may still appear to be at the top of its ladder in the cache
        /// when it is no longer at the top of the ladder based on the filter array.  Defaults to one minute.</param>
        public DistributedBinomialLadderFilterClient(int numberOfShards, int defaultHeightOfLadder, IDistributedResponsibilitySet<RemoteHost> shardToHostMapping, string configurationKey, TimeSpan? mininmumCacheFreshnessRequired = null)
        {
            NumberOfShards = numberOfShards;
            MaxLadderHeight = defaultHeightOfLadder;
            MinimumCacheFreshnessRequired = mininmumCacheFreshnessRequired ?? new TimeSpan(0,0,1);
            CacheOfElementsAtTopOfLadder = new FixedSizeLruCache<string, DateTime>(2*NumberOfShards);
            ShardHashFunction = new UniversalHashFunction(configurationKey);
            ShardToHostMapping = shardToHostMapping;
        }


        /// <summary>
        /// Get the shard index associated with an element.
        /// </summary>
        /// <param name="key">The element to match to a shard index.</param>
        /// <returns></returns>
        public int GetShardIndex(string key)
            => (int)(ShardHashFunction.Hash(key) % (uint)NumberOfShards);


        /// <summary>
        /// Get a random shard index from the set of shards that make up the array elements of the binomial ladder filter.
        /// </summary>
        /// <returns>The random shard index</returns>
        public int GetRandomShardIndex()
        {
            return (int)StrongRandomNumberGenerator.Get32Bits(NumberOfShards);
        }

        /// <summary>
        /// Assign a random element with the binomial ladder filter to either 0 or 1.
        /// </summary>
        /// <param name="valueToAssign">The random element will be set to 1 if this parameter is nonzero, and to 0 otherwise.</param>
        /// <param name="shardNumber">Optioanlly set this optional value to identify a shard number within to select a random element.
        /// If not set, this method will choose a shard at random.</param>
        public void AssignRandomBit(int valueToAssign, int? shardNumber = null)
        {
            int shard = shardNumber ?? GetRandomShardIndex();
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());
            RestClientHelper.PostBackground(host.Uri, BitsPath + shard + '/' + valueToAssign);
        }

        /// <summary>
        /// The Step operation records the occurrence of an element by increasing the element's height on the binomial ladder.
        /// It does this by identify the shard of the filter array associated with the element,
        /// fetching the H elements within that shard associated with the element (the element's rungs),
        /// and setting one of the elements with value 0 (a rung above the element) to have value 1 (to put it below the element).
        /// It then clears two random elements from the entire array (not just the shard).
        /// If none of the H rungs associated with an element have value 0, Step will instead set two random elements from
        /// within the full array to have value 1.
        /// </summary>
        /// <param name="key">The element to take a step for.</param>
        /// <param name="heightOfLadderInRungs">If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.)</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The height of the element on its binomial ladder before the Step operation executed.  The height of a
        /// element on the ladder is the number of the H elements associated with the element that have value 1.</returns>
        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            DateTime whenAddedUtc;
            int topOfLadder = heightOfLadderInRungs ?? MaxLadderHeight;

            bool cacheIndicatesTopOfLadder = CacheOfElementsAtTopOfLadder.TryGetValue(key, out whenAddedUtc);
            if (cacheIndicatesTopOfLadder && DateTime.UtcNow - whenAddedUtc < MinimumCacheFreshnessRequired)
            {
                // The cache is fresh and indicates that the element is already at the top of the ladder

                // Since the element is at the top of the lader, if we were to ask the host responsible for this shard to perform this Step, 
                // it would clear two random elements from the entire filter and set two random elements from the entire filter.
                // To avoid creating a hot-spot at the host associated with this element, we'll have the client do the two random 
                // clears and two random sets on its own.
                AssignRandomBit(1);
                AssignRandomBit(1);
                AssignRandomBit(0);
                AssignRandomBit(0);

                return topOfLadder;
            }

            // We will first identify the shard associated with this element, and the host responsible for storing that shard
            int shard = GetShardIndex(key);
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());

            // Call the host responsible for the shard to perform the step operation
            int heightBeforeStep = await RestClientHelper.PostAsync<int>(host.Uri, ElementsPath + Uri.EscapeUriString(key), 
                timeout: timeout, cancellationToken: cancellationToken, 
                parameters: (!heightOfLadderInRungs.HasValue) ? null : new object[]
                        {
                            new KeyValuePair<string, int>("heightOfLadderInRungs", topOfLadder)
                        } );

            if (heightBeforeStep < topOfLadder && cacheIndicatesTopOfLadder)
            {
                // The cache is no longer accurate as the element is no longer at the top of the ladder,
                // so remove the element from the cache
                CacheOfElementsAtTopOfLadder.Remove(key);
            }
            else if (heightBeforeStep == topOfLadder)
            {
                // Store the current element in the cache indicating with the time of this operation
                CacheOfElementsAtTopOfLadder[key] = DateTime.UtcNow;
            }

            // Return the height of the element on the binomial ladder before the Step took place.
            return heightBeforeStep;
        }


        /// <summary>
        /// Get the height of an element on its binomial ladder.
        /// The height of an element is the number of the H array elements that are associated with it that have value 1.
        /// </summary>
        /// <param name="element">The element to be measured to determine its height on its binomial ladder.</param>
        /// <param name="heightOfLadderInRungs">>If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The height of the element on the binomial ladder.</returns>
        public async Task<int> GetHeightAsync(string element, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            DateTime whenAddedUtc;
            int topOfLadder = heightOfLadderInRungs ?? MaxLadderHeight;

            bool cacheIndicatesTopOfLadder = CacheOfElementsAtTopOfLadder.TryGetValue(element, out whenAddedUtc);
            if (cacheIndicatesTopOfLadder && DateTime.UtcNow - whenAddedUtc < MinimumCacheFreshnessRequired)
            {
                // The cache is fresh and indicates that the element is already at the top of the ladder
                return topOfLadder;
            }

            int shard = GetShardIndex(element);
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());
            int height = await RestClientHelper.GetAsync<int>(host.Uri, ElementsPath + Uri.EscapeUriString(element), cancellationToken: cancellationToken,
                uriParameters: (!heightOfLadderInRungs.HasValue) ? null : new[]
                        {
                            new KeyValuePair<string, string>("heightOfLadderInRungs", heightOfLadderInRungs.Value.ToString())
                        });

            if (height < topOfLadder && cacheIndicatesTopOfLadder)
            {
                // The cache is no longer accurate as the element is no longer at the top of the ladder
                CacheOfElementsAtTopOfLadder.Remove(element);
            }
            else if (height == topOfLadder)
            {
                // Store the current element in the cache indicating with the time of the last fetch.
                CacheOfElementsAtTopOfLadder[element] = DateTime.UtcNow;
            }

            return height;
        }

    }

}
