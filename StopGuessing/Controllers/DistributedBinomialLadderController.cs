using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using StopGuessing.Clients;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Controllers
{
    /// <summary>
    /// The server/controller side of a distributed binomial ladder frequency filter.
    ///  
    /// Binomial ladder filters are used to identify frequently-occuring elements in streams while
    /// minimizing the data revealed/stored about infrequently-occuring elements.
    /// 
    /// The server uses a client to communicate with the other servers that together implement
    /// this distributed data structure.
    /// 
    /// For more information about the binomial ladder filter, search its name and "Microsoft Research"
    /// to find detailed publications/tech reports.
    /// </summary>
    [Route("api/DBLS")]
    public class DistributedBinomialLadderFilterController
    {
        /// <summary>
        /// Since the filter is distributed into shards which are stored on many hosts,
        /// we need a client to access the shards of the filter that are stored on other hosts.
        /// </summary>
        protected DistributedBinomialLadderFilterClient FilterClient;

        /// <summary>
        /// The shards of the filter that this host has a (not-necessarily-up-to-date) copy of.
        /// </summary>
        protected Dictionary<int,FilterArray> ShardsByIndex;

        /// <summary>
        /// The number of bits stored in each shard.
        /// </summary>
        protected int NumberOfBitsPerShard;

        /// <summary>
        /// A secret string used to salt hashes so as to prevent algorithmic complexity attacks.
        /// </summary>
        protected string SecretSaltToPreventAlgorithmicComplexityAttacks;

        /// <summary>
        /// The max ladder height (H in the paper).
        /// </summary>
        protected int MaxLadderHeight => FilterClient.MaxLadderHeight;


        /// <summary>
        /// Construct the controller (server) for requests to this host for its shards of the binomial ladder filter.
        /// </summary>
        /// <param name="distributedBinomialLadderFilterClient">A client used by this server to access the servers hosting other shards of the filter.</param>
        /// <param name="numberOfBitsPerShard">The number of bits that each shard should contain.</param>
        /// <param name="secretSaltToPreventAlgorithmicComplexityAttacks">
        /// A secret used to salt the filter's hash functions so as to prevent attacks that might try to find collisions in the small space we are hashing to.</param>
        public DistributedBinomialLadderFilterController(DistributedBinomialLadderFilterClient distributedBinomialLadderFilterClient,
            int numberOfBitsPerShard, string secretSaltToPreventAlgorithmicComplexityAttacks)
        {
            FilterClient = distributedBinomialLadderFilterClient;
            NumberOfBitsPerShard = numberOfBitsPerShard;
            SecretSaltToPreventAlgorithmicComplexityAttacks = secretSaltToPreventAlgorithmicComplexityAttacks;
        }

        /// <summary>
        /// Get the fitler array that stores a shard based on the shard number.
        /// </summary>
        /// <param name="shardNumber">The shard number (index) to get.</param>
        /// <returns>A filter array containing all of the bits for a given shard.</returns>
        protected FilterArray GetShard(int shardNumber)
        {
            if (!ShardsByIndex.ContainsKey(shardNumber))
            {
                // If there are not bits for this shard, create one.
                ShardsByIndex[shardNumber] = new FilterArray(NumberOfBitsPerShard, MaxLadderHeight, true, SecretSaltToPreventAlgorithmicComplexityAttacks);
            }
            return ShardsByIndex[shardNumber];
        }

        /// <summary>
        /// Get the fitler array that stores a shard associated with a given element.
        /// </summary>
        /// <param name="element">The element to associate with a shard.</param>
        /// <returns>A filter array containing all of the bits for a given shard.</returns>
        protected FilterArray GetShard(string element)
            => GetShard(FilterClient.GetShardIndex(element));

        /// <summary>
        /// Get the height of an element on its binomial ladder.
        /// The height of an element is the number of the H array elements that are associated with it that have value 1.
        /// </summary>
        /// <param name="element">The element to be measured to determine its height on its binomial ladder.</param>
        /// <param name="heightOfLadderInRungs">>If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.</param>
        /// <returns>The height of the element on the binomial ladder.</returns>
        [HttpGet("/Elements/{element}")]
        public int GetHeight([FromRoute] string element, [FromQuery] int? heightOfLadderInRungs = null)
        {
            FilterArray shard = GetShard(element);
            return shard.GetIndexesAssociatedWithAnElement(element, heightOfLadderInRungs).Count(
                index => shard[index]);
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
        /// <param name="element">The element to take a step for.</param>
        /// <param name="heightOfLadderInRungs">If set, use a binomial ladder of this height rather than the default height.
        /// (Must not be greater than the default height that the binmomial ladder was initialized with.)</param>
        /// <returns>The height of the element on its binomial ladder before the Step operation executed.  The height of a
        /// element on the ladder is the number of the H elements associated with the element that have value 1.</returns>
        [HttpPost("/Elements/{element}")]
        public int DistributedStepAsync([FromRoute]string element, [FromQuery] int? heightOfLadderInRungs)
        {
            FilterArray shard = GetShard(element);
            // Get the set of rungs
            List<int> rungIndexes = shard.GetIndexesAssociatedWithAnElement(element, heightOfLadderInRungs ?? MaxLadderHeight).ToList();
            // Select the subset of rungs that have value zero (that are above the element in the ladder)
            List<int> rungsAbove = rungIndexes.Where(rung => !shard[rung]).ToList();

            // Identify an element of the array to set
            if (rungsAbove.Count > 0)
            {
                // If there are rungs with value value zero/false (rungs above the element), pick one at random
                shard.SetBitToOne(
                    rungsAbove[(int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count))]);
            }
            else
            {
                // Set an average of one element by picking two random elements, each of which should have p=0.5
                // of being zero/false, and setting them to 1/true regardless of their previous value.
                FilterClient.AssignRandomBit(1);
                FilterClient.AssignRandomBit(1);
            }

            // Clear an average of one element by picking two random elements, each of which should have p=0.5
            // of being one/true, and setting them to 0/false regardless of their previous value.
            FilterClient.AssignRandomBit(0);
            FilterClient.AssignRandomBit(0);

            // Return the height of the ladder before the step
            return rungIndexes.Count - rungsAbove.Count;
        }

        /// <summary>
        /// Assign a random bit within the filter's array to either 0 or 1.
        /// </summary>
        /// <param name="valueToAssign">The random element will be set to 1 if this parameter is nonzero, and to 0 otherwise.</param>
        /// <param name="shardNumber">Optioanlly set this optional value to identify a shard number within to select a random element.
        /// If not set, this method will choose a shard at random.</param>
        [HttpPost("/Bits/{shardNumber}/{valueToAssign}")]
        public void AssignRandomBit([FromRoute] int shardNumber, [FromRoute] int valueToAssign)
        {
            FilterArray shard = GetShard(shardNumber);
            shard.AssignRandomBit(valueToAssign);
        }

    }

}
