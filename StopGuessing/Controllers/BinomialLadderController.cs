using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using StopGuessing.Clients;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace StopGuessing.Controllers
{
    [Route("api/DBLS")]
    public class DistributedBinomialLadderSketchController
    {
        protected DistributedBinomialLadderClient Client;
        protected Dictionary<int,BinomialLadderSketch> ShardsByIndex;
        protected int LadderHeight;
        protected int NumberOfElementsPerShard;

        public DistributedBinomialLadderSketchController(DistributedBinomialLadderClient distributedBinomialLadderClient,
            int ladderHeight, int numberOfElementsPerShard)
        {
            Client = distributedBinomialLadderClient;
            LadderHeight = ladderHeight;
            NumberOfElementsPerShard = numberOfElementsPerShard;
        }

        protected BinomialLadderSketch GetShard(int shardNumber)
        {
            if (!ShardsByIndex.ContainsKey(shardNumber))
            {
                ShardsByIndex[shardNumber] = new BinomialLadderSketch(NumberOfElementsPerShard, LadderHeight);
            }
            return ShardsByIndex[shardNumber];
        }

        protected BinomialLadderSketch GetShard(string key)
            => GetShard(Client.GetShard(key));

        [HttpGet("/Keys/{key}")]
        public int GetHeight([FromRoute] string key, [FromQuery] int? heightOfLadderInRungs)
        {
            return GetShard(key).GetHeight(key, heightOfLadderInRungs);
        }

        [HttpPost("/Keys/{key}")]
        public int DistributedStepAsync([FromRoute]string key, [FromQuery] int? heightOfLadderInRungs)
        {
            BinomialLadderSketch shard = GetShard(key);
            // Get the set of rungs
            List<int> rungs = shard.Elements.GetElementIndexesForKey(key, heightOfLadderInRungs).ToList();
            // Select the subset of rungs that have value zero (that are above the key in the ladder)
            List<int> rungsAbove = rungs.Where(rung => !shard.Elements[rung]).ToList();

            // Identify an element of the array to set
            if (rungsAbove.Count > 0)
            {
                // If there are rungs with value value zero/false (rungs above the key), pick one at random
                shard.Elements.SetElement(
                    rungsAbove[(int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count))]);
            }
            else
            {
                // Set an average of one element by picking two random elements, each of which should have p=0.5
                // of being zero/false, and setting them to 1/true regardless of their previous value.
                Client.AssignRandomElementToValue(1);
                Client.AssignRandomElementToValue(1);
            }

            // Clear an average of one element by picking two random elements, each of which should have p=0.5
            // of being one/true, and setting them to 0/false regardless of their previous value.
            Client.AssignRandomElementToValue(0);
            Client.AssignRandomElementToValue(0);

            // Return the height of the ladder before the step
            return rungs.Count - rungsAbove.Count;
        }

        [HttpPost("/Elements/{shardNumber}/{value}")]
        public void AssignRandomElement([FromRoute] int shardNumber, [FromRoute] int value)
        {
            BinomialLadderSketch shard = GetShard(shardNumber);
            shard.AssignRandomElement(value);
        }

    }

}
