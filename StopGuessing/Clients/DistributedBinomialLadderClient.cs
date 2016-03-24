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
        public readonly int NumberOfVirtualNodes;
        protected UniversalHashFunction VirtualNodeHash;
        public IDistributedResponsibilitySet<RemoteHost> VirtualNodeToHostMapping;

        public DistributedBinomialLadderClient(int numberOfVirtualNodes, IDistributedResponsibilitySet<RemoteHost> virtualNodeToHostMapping,  string configurationKey)
        {
            NumberOfVirtualNodes = numberOfVirtualNodes;
            VirtualNodeHash = new UniversalHashFunction(configurationKey);
            VirtualNodeToHostMapping = virtualNodeToHostMapping;
        }
        public int GetRandomVirtualNode()
        {
            return (int)StrongRandomNumberGenerator.Get32Bits(NumberOfVirtualNodes);
        }

        public void ClearRandomElement(int? virtualNode = null)
        {
            int virtualNodeForClear = virtualNode ?? GetRandomVirtualNode();
            RemoteHost host = VirtualNodeToHostMapping.FindMemberResponsible(virtualNodeForClear.ToString());
            RestClientHelper.PostBackground(host.Uri, "/api/DistributedBinomialLadderSketch/ClearRandomElement/" + virtualNodeForClear);
        }

        public void SetRandomElement(int? virtualNode = null)
        {
            int virtualNodeForSet = virtualNode ?? GetRandomVirtualNode();
            RemoteHost host = VirtualNodeToHostMapping.FindMemberResponsible(virtualNodeForSet.ToString());
            RestClientHelper.PostBackground(host.Uri, "/api/DistributedBinomialLadderSketch/SetRandomElement/" + virtualNodeForSet);
        }

        public async Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            int virtualNode = GetVirtualNodeForKey(key);
            RemoteHost host = VirtualNodeToHostMapping.FindMemberResponsible(virtualNode.ToString());
            return await RestClientHelper.PostAsync<int>(host.Uri, "/api/DistributedBinomialLadderSketch/Key/" + Uri.EscapeUriString(key), 
                timeout: timeout, cancellationToken: cancellationToken, 
                parameters: (!heightOfLadderInRungs.HasValue) ? null : new object[]
                        {
                            new KeyValuePair<string, int>("heightOfLadderInRungs", heightOfLadderInRungs.Value)
                        } );
        }
        public int GetVirtualNodeForKey(string key)
            => (int) (VirtualNodeHash.Hash(key)%(uint) NumberOfVirtualNodes);


        public async Task<int> GetHeightAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null, CancellationToken cancellationToken = new CancellationToken())
        {
            int virtualNode = GetVirtualNodeForKey(key);
            RemoteHost host = VirtualNodeToHostMapping.FindMemberResponsible(virtualNode.ToString());
            Task<int> heightTask = RestClientHelper.GetAsync<int>(host.Uri, "/api/DistributedBinomialLadderSketch/Key/" + Uri.EscapeUriString(key), cancellationToken: cancellationToken,
                uriParameters: (!heightOfLadderInRungs.HasValue) ? null :  new KeyValuePair<string,string>[]
                        {
                            new KeyValuePair<string, string>("heightOfLadderInRungs", heightOfLadderInRungs.Value.ToString())
                        } );
            return await heightTask;
        }

    }

}
