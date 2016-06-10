using System;
using System.Threading;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public class MemoryUsageLimiter : IDisposable
    {
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public class ReduceMemoryUsageEventParameters : EventArgs
        {
            public readonly double FractionOfMemoryToTryToRemove;

            public ReduceMemoryUsageEventParameters(
                    double fractionOfMemoryToTryToRemove)
            {
                FractionOfMemoryToTryToRemove = fractionOfMemoryToTryToRemove;
            }
        }
        
    
        public event EventHandler<ReduceMemoryUsageEventParameters> OnReduceMemoryUsageEventHandler;

        private readonly double _fractionToRemoveOnCleanup;
        public MemoryUsageLimiter(
            double fractionToRemoveOnCleanup = 0.2 /* 20% */,
            long hardMemoryLimit = 0)
        {
            _fractionToRemoveOnCleanup = fractionToRemoveOnCleanup;
            if (hardMemoryLimit == 0)
            {
                Task.Run(() => GenerationalReductionLoop(cancellationTokenSource.Token), cancellationTokenSource.Token);
            }
            else
            {
                Task.Run(() => ThresholdReductionLoop(hardMemoryLimit, cancellationTokenSource.Token), cancellationTokenSource.Token);
            }
        }
        

        public void ReduceMemoryUsage()
        {
            EventHandler<ReduceMemoryUsageEventParameters> localOnReduceMemoryUsageHandler = OnReduceMemoryUsageEventHandler;
            if (localOnReduceMemoryUsageHandler != null)
            {
                Parallel.ForEach(localOnReduceMemoryUsageHandler.GetInvocationList(),
                    d => {
                        try
                        {
                            d.DynamicInvoke(this, new ReduceMemoryUsageEventParameters(_fractionToRemoveOnCleanup));
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    );
            }
        }


        public void GenerationalReductionLoop(CancellationToken cancellationToken)
        {
            GC.RegisterForFullGCNotification(10,10);

            while (true)
            {
                int collectionCount = GC.CollectionCount(2);
                while (GC.WaitForFullGCApproach(100) == GCNotificationStatus.Timeout)
                    cancellationToken.ThrowIfCancellationRequested();
                
                ReduceMemoryUsage();

                if (collectionCount == GC.CollectionCount(2))
                    GC.Collect();

                while (GC.WaitForFullGCComplete(100) == GCNotificationStatus.Timeout)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            // ReSharper disable once FunctionNeverReturns
        }


        public void ThresholdReductionLoop(long hardMemoryLimit, CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    System.Threading.Thread.Sleep(250);
                    cancellationToken.ThrowIfCancellationRequested();
                    long currentMemoryConsumptionInBytes = GC.GetTotalMemory(true);
                    if (currentMemoryConsumptionInBytes > hardMemoryLimit)
                    {
                        Console.Error.WriteLine("Starting memory reduction.");
                        ReduceMemoryUsage();
                        Console.Error.WriteLine("Completing memory reduction.");
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
        }
    }
}
