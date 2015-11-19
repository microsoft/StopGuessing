using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.SessionState;

namespace StopGuessing.DataStructures
{
    public class BinomialDistribution
    {
        private readonly double[] _probabilityExactlyThisManySetByChance;
        private readonly double[] _probabilityThisManyOrFewerSetByChance;

        public double ProbabilityThisManySetByChance(int howMany)
        {
            if (howMany < 0 || howMany >= _probabilityExactlyThisManySetByChance.Length)
                return 0d;
            return _probabilityExactlyThisManySetByChance[howMany];
        }
        public double ProbabilityThisOrFewerManySetByChance(int howMany)
        {
            if (howMany < 0)
                return 0d;
            if (howMany > _probabilityThisManyOrFewerSetByChance.Length)
                return 1d;
            return _probabilityThisManyOrFewerSetByChance[howMany];
        }

        public double ProbabilityFewerSetByChance(int howMany)
        {
            if (howMany <= 0)
                return 0d;
            if (howMany > _probabilityThisManyOrFewerSetByChance.Length)
                return 1d;
            return _probabilityThisManyOrFewerSetByChance[howMany-1];
        }

        public double ProbabilityMoreSetByChance(int howMany)
        {
            if (howMany < 0)
                return 1d;
            if (howMany > _probabilityThisManyOrFewerSetByChance.Length)
                return 0d;
            return 1d-_probabilityThisManyOrFewerSetByChance[howMany];
        }

        public double ProbabilityThisManyOrMoreSetByChance(int howMany)
        {
            if (howMany <= 0)
                return 1d;
            if (howMany > _probabilityThisManyOrFewerSetByChance.Length)
                return 0d;
            return 1d - _probabilityThisManyOrFewerSetByChance[howMany-1];
        }


        protected BinomialDistribution(int outOf)
        {
            _probabilityThisManyOrFewerSetByChance = new double[outOf +1];
            _probabilityExactlyThisManySetByChance = new double[outOf + 1];
            double probabilityOfAnyGivenValue = Math.Pow(0.5d, outOf);
            double nChooseK = 1d;

            for (int k = 0; k <= outOf / 2; k++)
            {
                _probabilityExactlyThisManySetByChance[k] = _probabilityExactlyThisManySetByChance[outOf - k] =
                    nChooseK*probabilityOfAnyGivenValue;
                nChooseK *= (outOf - k)/(1d + k);
            }

            _probabilityThisManyOrFewerSetByChance = new double[outOf + 1];
            _probabilityThisManyOrFewerSetByChance[outOf] = _probabilityExactlyThisManySetByChance[outOf];
            for (int k = outOf; k > 0; k--)
                _probabilityThisManyOrFewerSetByChance[k - 1] =
                    _probabilityThisManyOrFewerSetByChance[k] + _probabilityExactlyThisManySetByChance[k - 1];
        }


        protected static readonly Dictionary<int, BinomialDistribution> CacheOfCalculatedDistributions = new Dictionary<int, BinomialDistribution>();
        protected static readonly ReaderWriterLockSlim RwLock = new ReaderWriterLockSlim();
        public static BinomialDistribution ForCoinFlips(int numberOfCoinFlips)
        {
            BinomialDistribution result;
            RwLock.EnterUpgradeableReadLock();
            try
            {
                if (!CacheOfCalculatedDistributions.TryGetValue(numberOfCoinFlips, out result))
                {
                    RwLock.EnterWriteLock();
                    try
                    {
                        result = CacheOfCalculatedDistributions[numberOfCoinFlips] =
                            new BinomialDistribution(numberOfCoinFlips);
                        CacheOfCalculatedDistributions[numberOfCoinFlips] = result;
                    }
                    finally
                    {
                        RwLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                RwLock.ExitUpgradeableReadLock();
            }
            return result;
        }
    }
}
