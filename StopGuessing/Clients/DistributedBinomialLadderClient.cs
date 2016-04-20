using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace StopGuessing.Clients
{

    public class DistributedBinomialLadderClient : IBinomialLadderSketch
    {
        public const string ControllerPath = "/api/DBLS/";
        public const string SketchElementsPath = ControllerPath + "Elements/";
        public const string KeyPath = ControllerPath + "Keys/";

        public readonly int NumberOfShards;
        public readonly int DefaultHeightOfLadder;
        public readonly TimeSpan MinimumCacheFreshnessRequired;
        protected UniversalHashFunction ShardHashFunction;
        public IDistributedResponsibilitySet<RemoteHost> ShardToHostMapping;

        public FixedSizeLruCache<string, DateTime> CacheOfKeysAtTopOfLadder; 

        public DistributedBinomialLadderClient(int numberOfShards, int defaultHeightOfLadder, TimeSpan mininmumCacheFreshnessRequired, IDistributedResponsibilitySet<RemoteHost> shardToHostMapping, string configurationKey)
        {
            NumberOfShards = numberOfShards;
            DefaultHeightOfLadder = defaultHeightOfLadder;
            MinimumCacheFreshnessRequired = mininmumCacheFreshnessRequired;
            CacheOfKeysAtTopOfLadder = new FixedSizeLruCache<string, DateTime>(2*NumberOfShards);
            ShardHashFunction = new UniversalHashFunction(configurationKey);
            ShardToHostMapping = shardToHostMapping;
        }

        /// <summary>
        /// Get a random shard index from the set of shards that make up the array elements of the binomial ladder sketch.
        /// </summary>
        /// <returns>The random shard index</returns>
        public int GetRandomShard()
        {
            return (int)StrongRandomNumberGenerator.Get32Bits(NumberOfShards);
        }

        /// <summary>
        /// Assign a random element with the binomial ladder sketch to either 0 or 1.
        /// </summary>
        /// <param name="valueToAssign">The random element will be set to 1 if this parameter is nonzero, and to 0 otherwise.</param>
        /// <param name="shardNumber">Optioanlly set this optional value to identify a shard number within to select a random element.
        /// If not set, this method will choose a shard at random.</param>
        public void AssignRandomElementToValue(int valueToAssign, int? shardNumber = null)
        {
            int shard = shardNumber ?? GetRandomShard();
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());
            RestClientHelper.PostBackground(host.Uri, SketchElementsPath + shard + '/' + valueToAssign);
        }

        /// <summary>
        /// The Step operation records the occurrence of a key by increasing the key's height on the binomial ladder.
        /// It does this by identify the shard of the sketch array associated with the key,
        /// fetching the H elements within that shard associated with the key (the key's rungs),
        /// and setting one of the elements with value 0 (a rung above the key) to have value 1 (to put it below the key).
        /// It then clears two random elements from the entire array (not just the shard).
        /// If none of the H rungs associated with a key have value 0, Step will instead set two random elements from
        /// within the full array to have value 1.
        /// </summary>
        /// <param name="key">The key to take a step for.</param>
        /// <param name="heightOfLadderInRungs">If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.)</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The height of the key on its binomial ladder before the Step operation executed.  The height of a
        /// key on the ladder is the number of the H elements associated with the key that have value 1.</returns>
        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            DateTime whenAddedUtc;
            int topOfLadder = heightOfLadderInRungs ?? DefaultHeightOfLadder;

            bool cacheIndicatesTopOfLadder = CacheOfKeysAtTopOfLadder.TryGetValue(key, out whenAddedUtc);
            if (cacheIndicatesTopOfLadder && DateTime.UtcNow - whenAddedUtc < MinimumCacheFreshnessRequired)
            {
                // The cache is fresh and indicates that the key is already at the top of the ladder

                // Since the key is at the top of the lader, if we were to ask the host responsible for this shard to perform this Step, 
                // it would clear two random elements from the entire sketch and set two random elements from the entire sketch.
                // To avoid creating a hot-spot at the host associated with this key, we'll have the client do the two random 
                // clears and two random sets on its own.
                AssignRandomElementToValue(1);
                AssignRandomElementToValue(1);
                AssignRandomElementToValue(0);
                AssignRandomElementToValue(0);

                return topOfLadder;
            }

            // We will first identify the shard associated with this key, and the host responsible for storing that shard
            int shard = GetShard(key);
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());

            // Call the host responsible for the shard to perform the step operation
            int heightBeforeStep = await RestClientHelper.PostAsync<int>(host.Uri, KeyPath + Uri.EscapeUriString(key), 
                timeout: timeout, cancellationToken: cancellationToken, 
                parameters: (!heightOfLadderInRungs.HasValue) ? null : new object[]
                        {
                            new KeyValuePair<string, int>("heightOfLadderInRungs", topOfLadder)
                        } );

            if (heightBeforeStep < topOfLadder && cacheIndicatesTopOfLadder)
            {
                // The cache is no longer accurate as the key is no longer at the top of the ladder,
                // so remove the key from the cache
                CacheOfKeysAtTopOfLadder.Remove(key);
            }
            else if (heightBeforeStep == topOfLadder)
            {
                // Store the current key in the cache indicating with the time of this operation
                CacheOfKeysAtTopOfLadder[key] = DateTime.UtcNow;
            }

            // Return the height of the key on the binomial ladder before the Step took place.
            return heightBeforeStep;
        }

        /// <summary>
        /// Get the shard index associated with a key.
        /// </summary>
        /// <param name="key">The key to match to a shard index.</param>
        /// <returns></returns>
        public int GetShard(string key)
            => (int) (ShardHashFunction.Hash(key)%(uint) NumberOfShards);


        /// <summary>
        /// Get the height of a key on its binomial ladder.
        /// The height of a key is the number of the H array elements that are associated with it that have value 1.
        /// </summary>
        /// <param name="key">The key to be measured to determine its height on its binomial ladder.</param>
        /// <param name="heightOfLadderInRungs">>If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.</param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The height of the key on the binomial ladder.</returns>
        public async Task<int> GetHeightAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            DateTime whenAddedUtc;
            int topOfLadder = heightOfLadderInRungs ?? DefaultHeightOfLadder;

            bool cacheIndicatesTopOfLadder = CacheOfKeysAtTopOfLadder.TryGetValue(key, out whenAddedUtc);
            if (cacheIndicatesTopOfLadder && DateTime.UtcNow - whenAddedUtc < MinimumCacheFreshnessRequired)
            {
                // The cache is fresh and indicates that the key is already at the top of the ladder
                return topOfLadder;
            }

            int shard = GetShard(key);
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());
            int height = await RestClientHelper.GetAsync<int>(host.Uri, KeyPath + Uri.EscapeUriString(key), cancellationToken: cancellationToken,
                uriParameters: (!heightOfLadderInRungs.HasValue) ? null : new KeyValuePair<string, string>[]
                        {
                            new KeyValuePair<string, string>("heightOfLadderInRungs", heightOfLadderInRungs.Value.ToString())
                        });

            if (height < topOfLadder && cacheIndicatesTopOfLadder)
            {
                // The cache is no longer accurate as the key is no longer at the top of the ladder
                CacheOfKeysAtTopOfLadder.Remove(key);
            }
            else if (height == topOfLadder)
            {
                // Store the current key in the cache indicating with the time of the last fetch.
                CacheOfKeysAtTopOfLadder[key] = DateTime.UtcNow;
            }

            return height;
        }

    }

}
