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
        public int GetRandomShard()
        {
            return (int)StrongRandomNumberGenerator.Get32Bits(NumberOfShards);
        }

        public void AssignRandomElementToValue(int valueToAssign, int? shardNumber = null)
        {
            int shard = shardNumber ?? GetRandomShard();
            RemoteHost host = ShardToHostMapping.FindMemberResponsible(shard.ToString());
            RestClientHelper.PostBackground(host.Uri, SketchElementsPath + shard + '/' + valueToAssign);
        }

        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
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
            int heightBeforeStep = await RestClientHelper.PostAsync<int>(host.Uri, KeyPath + Uri.EscapeUriString(key), 
                timeout: timeout, cancellationToken: cancellationToken, 
                parameters: (!heightOfLadderInRungs.HasValue) ? null : new object[]
                        {
                            new KeyValuePair<string, int>("heightOfLadderInRungs", heightOfLadderInRungs.Value)
                        } );

            if (heightBeforeStep < topOfLadder && cacheIndicatesTopOfLadder)
            {
                // The cache is no longer accurate as the key is no longer at the top of the ladder
                CacheOfKeysAtTopOfLadder.Remove(key);
            }
            else if (heightBeforeStep == topOfLadder)
            {
                // Store the current key in the cache indicating with the time of the last fetch.
                CacheOfKeysAtTopOfLadder[key] = DateTime.UtcNow;
            }

            return heightBeforeStep;
        }


        public int GetShard(string key)
            => (int) (ShardHashFunction.Hash(key)%(uint) NumberOfShards);


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
