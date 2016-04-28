using StopGuessing.DataStructures;
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

            int observationCount = sketch.GetHeight(somethingToObserve);

            for (int i = 0; i < 25; i++)
            {
                int lastCount = sketch.Step(somethingToObserve);
                Assert.Equal(observationCount, lastCount);
                observationCount++;
            }

            Assert.Equal(observationCount, sketch.GetHeight(somethingToObserve));
        }
        

    }
}
