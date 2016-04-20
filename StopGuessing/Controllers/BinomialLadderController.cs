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

        /// <summary>
        /// Get the height of a key on its binomial ladder.
        /// The height of a key is the number of the H array elements that are associated with it that have value 1.
        /// </summary>
        /// <param name="key">The key to be measured to determine its height on its binomial ladder.</param>
        /// <param name="heightOfLadderInRungs">>If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.</param>
        /// <returns>The height of the key on the binomial ladder.</returns>
        [HttpGet("/Keys/{key}")]
        public int GetHeight([FromRoute] string key, [FromQuery] int? heightOfLadderInRungs)
        {
            return GetShard(key).GetHeight(key, heightOfLadderInRungs ?? LadderHeight);
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
        /// <returns>The height of the key on its binomial ladder before the Step operation executed.  The height of a
        /// key on the ladder is the number of the H elements associated with the key that have value 1.</returns>
        [HttpPost("/Keys/{key}")]
        public int DistributedStepAsync([FromRoute]string key, [FromQuery] int? heightOfLadderInRungs)
        {
            BinomialLadderSketch shard = GetShard(key);
            // Get the set of rungs
            List<int> rungs = shard.Elements.GetElementIndexesForKey(key, heightOfLadderInRungs ?? LadderHeight).ToList();
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

        /// <summary>
        /// Assign a random element with the binomial ladder sketch to either 0 or 1.
        /// </summary>
        /// <param name="valueToAssign">The random element will be set to 1 if this parameter is nonzero, and to 0 otherwise.</param>
        /// <param name="shardNumber">Optioanlly set this optional value to identify a shard number within to select a random element.
        /// If not set, this method will choose a shard at random.</param>
        [HttpPost("/Elements/{shardNumber}/{valueToAssign}")]
        public void AssignRandomElement([FromRoute] int shardNumber, [FromRoute] int valueToAssign)
        {
            BinomialLadderSketch shard = GetShard(shardNumber);
            shard.AssignRandomElement(valueToAssign);
        }

    }

}
