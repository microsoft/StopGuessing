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
    [Route("api/[controller]")]
    public class DistributedBinomialLadderSketchController
    {
        protected DistributedBinomialLadderClient Client;
        protected Dictionary<int,BinomialLadderSketch> VirtualNodeToSketch;
        protected int LadderHeight;
        protected int NumberOfElementsPerVirtualNode;

        public DistributedBinomialLadderSketchController(DistributedBinomialLadderClient distributedBinomialLadderClient,
            int ladderHeight, int numberOfElementsPerVirtualNode)
        {
            Client = distributedBinomialLadderClient;
            LadderHeight = ladderHeight;
            NumberOfElementsPerVirtualNode = numberOfElementsPerVirtualNode;
        }

        protected BinomialLadderSketch GetSketchForVirutalNode(int virtualNode)
        {
            if (!VirtualNodeToSketch.ContainsKey(virtualNode))
            {
                VirtualNodeToSketch[virtualNode] = new BinomialLadderSketch(NumberOfElementsPerVirtualNode, LadderHeight);
            }
            return VirtualNodeToSketch[virtualNode];
        }

        protected BinomialLadderSketch GetSketchForKey(string key)
            => GetSketchForVirutalNode(Client.GetVirtualNodeForKey(key));

        [HttpGet("/key/{key}")]
        public int GetHeight([FromRoute] string key, [FromQuery] int? heightOfLadderInRungs)
        {
            return GetSketchForKey(key).GetHeight(key, heightOfLadderInRungs);
        }

        [HttpPost("/key/{key}")]
        public int DistributedStepAsync([FromRoute]string key, [FromQuery] int? heightOfLadderInRungs)
        {
            BinomialLadderSketch sketch = GetSketchForKey(key);
            // Get the set of rungs
            List<int> rungs = sketch.Elements.GetElementIndexesForKey(key, heightOfLadderInRungs).ToList();
            // Select the subset of rungs that have value zero (that are above the key in the ladder)
            List<int> rungsAbove = rungs.Where(rung => !sketch.Elements[rung]).ToList();

            // Identify an element of the array to set
            if (rungsAbove.Count > 0)
            {
                // If there are rungs with value value zero/false (rungs above the key), pick one at random
                sketch.Elements.SetElement(
                    rungsAbove[(int) (StrongRandomNumberGenerator.Get32Bits((uint) rungsAbove.Count))]);
            }
            else
            {
                // Set an average of one element by picking two random elements, each of which should have p=0.5
                // of being zero/false, and setting them to 1/true regardless of their previous value.
                Client.SetRandomElement();
                Client.SetRandomElement();
            }

            // Clear an average of one element by picking two random elements, each of which should have p=0.5
            // of being one/true, and setting them to 0/false regardless of their previous value.
            Client.ClearRandomElement();
            Client.ClearRandomElement();

            // Return the height of the ladder before the step
            return rungs.Count - rungsAbove.Count;
        }

        [HttpPost("/SetRandomElement/{virtualNode}")]
        public void DistributedSetRandomElement([FromRoute] int virtualNode)
        {
            BinomialLadderSketch sketch = GetSketchForVirutalNode(virtualNode);
            sketch.SetRandomElement();
        }

        [HttpPost("/ClearRandomElement/{virtualNode}")]
        public void DistributedClearRandomElement([FromRoute] int virtualNode)
        {
            BinomialLadderSketch sketch = GetSketchForVirutalNode(virtualNode);
            sketch.ClearRandomElement();
        }
    }

}
