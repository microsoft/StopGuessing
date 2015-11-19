using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing.Controllers
{

    public class BinomialPopularity
    {
        private List<ZeroElement> ZeroElements;
        private ulong NumberOfPossibleIndexes;
    }

    public class PasswordPopularityController
    {

        public interface IPasswordPopularityHandle
        {
            Proportion GetPopularity();
            void IncrementPopularity();
        }

        public class SketchBasedPasswordPopularityHandle : IPasswordPopularityHandle
        {
            protected PasswordPopularityTracker tracker;

            public List<ulong> indexesOfZeros;

            public Proportion GetPopularity()
            {
                throw new NotImplementedException();
                tracker.BinomialLadderSketchOfFailedPasswords.
                }

            public void IncrementPopularity()
            {
                throw new NotImplementedException();
            }

        }


    }
}
