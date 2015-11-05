using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    public abstract class PasswordPopularityResult
    {
        protected List<RemoteHost> SetOfHostsResponsibleForThisPassword;

        public abstract Proportion GetPopularity();

        public abstract void RecordObservation();

    }

    public class BinomialSketchBasedPasswordPopularityResult : PasswordPopularityResult
    {
        public class ZeroElement
        {
            public RemoteHost Server;
            private uint Index;
        }

        private List<ZeroElement> zeroElementsInBinomialSketch;
        private uint BitsProbed;

        public override Proportion GetPopularity()
        {
            //BinomialModel.Get(BitsProbed).MinProb(zeroElementsInBinomialSketch.Count, Confidence)
            throw new NotImplementedException();
        }

        public override void RecordObservation()
        {
            if (zeroElementsInBinomialSketch.Count > 0)
            {
                ZeroElement elementToSet = zeroElementsInBinomialSketch[ (int)
                    StrongRandomNumberGenerator.Get32Bits((uint)zeroElementsInBinomialSketch.Count)];
                // FIXME -- call client to set index elementToSet.Index on host elementToSet.Server;
            }
        }
    }

}
