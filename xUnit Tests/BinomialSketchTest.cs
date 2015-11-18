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
            BinomialLadder sketch = new BinomialLadder(1024*1024*1024, 64, "Louis Tully as played by Rick Moranis");
            string somethingToObserve = "Gosh.  It's a nice day out, isn't it?";

            int observationCount = sketch.GetNumberOfIndexesSet(somethingToObserve);

            for (int i = 0; i < 20; i++)
            {
                int lastCount = sketch.Step(somethingToObserve);
                Assert.Equal(observationCount, lastCount);
                observationCount++;
            }

            Assert.Equal(observationCount, sketch.GetNumberOfIndexesSet(somethingToObserve));

            double pNullHypothesis = sketch.TestNullHypothesisThatAllIndexesWereSetByChance(observationCount);
            Assert.True(pNullHypothesis < 0.01);

            int minObservationsAtOnePercentConfidence = sketch.CountObservationsForGivenConfidence(observationCount,
                0.01d);
            Assert.True(minObservationsAtOnePercentConfidence > 5);

        }
        

    }
}
