using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    public interface ILadderForKey
    {
        int HeightOfLadderInRungs { get; }
        int HeightOfKeyInRungs { get; }
        Task StepAsync(CancellationToken cancellationToken = default(CancellationToken));
    }

    public abstract class LadderForKey<TRung> : ILadderForKey
    {
        protected List<TRung> RungsAbove { get; set; }

        public int HeightOfLadderInRungs { get; protected set; }

        public int HeightOfKeyInRungs => HeightOfLadderInRungs - RungsAbove.Count;

        protected LadderForKey(IEnumerable<TRung> rungsNotYetClimbed, int heightOfLadderInRungs)
        {
            RungsAbove = rungsNotYetClimbed.ToList();
            HeightOfLadderInRungs = heightOfLadderInRungs;
        }

        protected TRung GetAndRemoveRandomRungAbove()
        {
            TRung rungToClimb;
            lock (RungsAbove)
            {
                if (RungsAbove.Count == 0)
                {
                    // The key is already at the top of the ladder.  No further steps can be taken.
                    return default(TRung);
                }

                int randomRungIndex = (int) StrongRandomNumberGenerator.Get32Bits(RungsAbove.Count);
                rungToClimb = RungsAbove[randomRungIndex];
                RungsAbove.RemoveAt(randomRungIndex);
            }
            return rungToClimb;
        }

        protected abstract Task StepAsync(TRung rungToClimb, CancellationToken cancellationToken = default(CancellationToken));

        public async Task StepAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            TRung rungToClimb = GetAndRemoveRandomRungAbove();

            if (rungToClimb.Equals(default(TRung)))
                // There are no rungs left to climb.  Cannot take another step.
                return;

            await StepAsync(rungToClimb, cancellationToken);
        }

    }

    public abstract class BinomialLadderForKey<TRung> : LadderForKey<TRung>
    {
        protected BinomialLadderForKey(IEnumerable<TRung> rungsNotYetClimbed, int heightOfLadderInRungs)
            : base(rungsNotYetClimbed, heightOfLadderInRungs)
        {
        }

        /// <summary>
        /// Estimates the number of observations of a key (the number of times Step(key) has been called) at a given level
        /// of statistical confidence (p value).
        /// In other words, how many observations can we assume occurred and reject the null hypothesis that fewer observations
        /// occurred and the nubmer of bits set was this high due to chance.
        /// </summary>
        /// <param name="confidenceLevelCommonlyCalledPValue">The p value, or confidence level, at which we want to be sure
        /// the claimed number of observations occurred.</param>
        /// <returns>The number of observations</returns>
        public int CountObservationsForGivenConfidence(double confidenceLevelCommonlyCalledPValue)
        {
            BinomialDistribution binomialDistribution = BinomialDistribution.ForCoinFlips(HeightOfLadderInRungs);
            int observations = 0;
            while (binomialDistribution.ProbabilityThisManyOrMoreSetByChance(HeightOfKeyInRungs - (observations + 1)) <
                confidenceLevelCommonlyCalledPValue)
            {
                observations++;
            }
            return observations;
        }
    }
}
