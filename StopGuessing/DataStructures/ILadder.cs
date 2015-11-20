using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public interface ILadder
    {
        int HeightOfLadderInRungs { get; }
        int HeightOfKeyInRungs { get; }
        Task StepAsync(CancellationToken cancellationToken = default(CancellationToken));
        int CountObservationsForGivenConfidence(double confidenceLevelCommonlyCalledPValue);
    }
}
