using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StopGuessing.Interfaces
{
    public interface IBinomialLadderFilter
    {
        Task<int> StepAsync(string key, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken());

        Task<int> GetHeightAsync(string element, int? heightOfLadderInRungs = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = new CancellationToken());
    }

}
