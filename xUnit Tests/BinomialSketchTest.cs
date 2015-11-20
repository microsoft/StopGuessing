using StopGuessing.DataStructures;
using System.Security.Cryptography;
using Xunit;


namespace xUnit_Tests
{
    public class BinomialSketchTest
    {
        [Fact]
        public void TwentyObservations()
        {
            BinomialLadderSketch sketch = new BinomialLadderSketch(1024*1024*1024, 64);
            string somethingToObserve = "Gosh.  It's a nice day out, isn't it?";

            int observationCount = sketch.GetLadder(somethingToObserve).HeightOfKeyInRungs;

            for (int i = 0; i < 20; i++)
            {
                int lastCount = sketch.Step(somethingToObserve);
                Assert.Equal(observationCount, lastCount);
                observationCount++;
            }

            Assert.Equal(observationCount, sketch.GetLadder(somethingToObserve).HeightOfKeyInRungs);


            int minObservationsAtOnePercentConfidence = sketch.GetLadder(somethingToObserve).
                CountObservationsForGivenConfidence(0.01d);
            Assert.True(minObservationsAtOnePercentConfidence > 5);
        }
        

    }
}
