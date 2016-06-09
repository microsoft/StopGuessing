using StopGuessing.DataStructures;
using Xunit;

namespace StopGuessingTests
{
    public class BinomialLadderFilterTest
    {
        [Fact]
        public void TwentyObservations()
        {
            BinomialLadderFilter freqFilter = new BinomialLadderFilter(1024*1024*1024, 64);
            string somethingToObserve = "Gosh.  It's a nice day out, isn't it?";

            int observationCount = freqFilter.GetHeight(somethingToObserve);

            for (int i = 0; i < 25; i++)
            {
                int lastCount = freqFilter.Step(somethingToObserve);
                Assert.Equal(observationCount, lastCount);
                observationCount++;
            }

            Assert.Equal(observationCount, freqFilter.GetHeight(somethingToObserve));
        }
        

    }
}
