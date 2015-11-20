using System.Linq;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;

namespace StopGuessing.Controllers
{

    [Route("api/[controller]")]
    public class BinomialLadderController
    {
        protected BinomialLadderSketch LadderSketch;

        public BinomialLadderController(BinomialLadderSketch ladderSketch)
        {
            LadderSketch = ladderSketch;
        }

        // GET api/LoginAttempt/password?numberOfRungs=16
        [HttpGet("{key}")]
        public int[] GetRungs(string key, [FromQuery] int numberOfRungs)
        {
            return LadderSketch.GetRungsAbove(key, numberOfRungs).ToArray();
        }
    }

}
