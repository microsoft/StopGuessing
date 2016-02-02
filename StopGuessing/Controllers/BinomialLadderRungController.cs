using System.Linq;
using Microsoft.AspNet.Mvc;
using StopGuessing.DataStructures;

namespace StopGuessing.Controllers
{

    [Route("api/[controller]")]
    public class BinomialLadderRungController
    {
        protected BinomialLadderSketch LadderSketch;
        public BinomialLadderRungController(BinomialLadderSketch ladderSketch)
        {
            LadderSketch = ladderSketch;
        }

        [HttpPut("{rungId}")]
        public void Step(int? rungId)
        {
            LadderSketch.Step(rungId);
        }
    }

}
