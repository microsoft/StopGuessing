using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public static class TaskParalllel
    {
        public static async Task ParallelRepeatUsingWaves(
            ulong numberOfTimesToRepeat,
            Action actionToRun,
            Action<Exception> callOnException = null,
            uint waveSize = 500)
        {
            int firstWaveSize = (int) Math.Min((ulong) waveSize, numberOfTimesToRepeat);
            Task[] currentWave = new Task[firstWaveSize];
            Task[] nextWave = null;

            ulong tasksStarted = 0;
            // Start first wave
            for (int i = 0; i < firstWaveSize; i++)
                currentWave[tasksStarted++] = Task.Run(actionToRun);

            while (currentWave != null)
            {
                // Invariant entering this loop: the nextWave has no tasks left to run

                // Fill the next wave
                if (tasksStarted < numberOfTimesToRepeat)
                {
                    int nextWaveSize = (int) Math.Min((ulong) waveSize, numberOfTimesToRepeat - tasksStarted);
                    if (nextWave == null || nextWaveSize != nextWave.Length)
                        nextWave = new Task[nextWaveSize];
                    for (int i = 0; i < nextWaveSize; i++)
                        currentWave[tasksStarted++] = Task.Run(actionToRun);
                }
                else
                {
                    nextWave = null;
                }

                // Wait for the current wave to finish
                await Task.WhenAll(currentWave);

                // Trigger exception handlers for any tasks that resulted in exceptions
                if (callOnException != null)
                    foreach (Task exceptionTask in currentWave.Where(t => t.IsFaulted))
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        Task.Run(() => callOnException(exceptionTask.Exception));

                // The next wave becomes the current wave...
                Task[] tempWave = currentWave;
                nextWave = currentWave;
                // ... and the buffer for what had been the current wave can now be
                // used for the next wave (if it is the right size)
                currentWave = nextWave;
            }
        }

        public static async Task ParallelRepeat(
            ulong numberOfTimesToRepeat,
            Action actionToRun,
            Action<Exception> callOnException = null,
            int maxConcurrentTasks = 1000)
        {
            Task[] activeTasks = new Task[(int) Math.Min((ulong)maxConcurrentTasks, numberOfTimesToRepeat)];
            Dictionary<Task, int> taskToIndex = new Dictionary<Task, int>();
            HashSet<Task> exceptionHandlingTasks = new HashSet<Task>();

            ulong tasksStarted = 0;
            // Phase 1 -- start maxConcurrentTasks executing
            while (tasksStarted < (ulong)activeTasks.Length && tasksStarted < numberOfTimesToRepeat)
            {
                Task startedTask = Task.Run(actionToRun);
                activeTasks[tasksStarted] = startedTask;
                taskToIndex[startedTask] = (int)tasksStarted;
                tasksStarted++;
            }

            // Phase 2 -- A stable stat in which there are always the maximum number of tasks
            //            in our array of active tasks
            while (tasksStarted < numberOfTimesToRepeat)
            {
                // Wait for a task to complete
                Task completedTask = await Task.WhenAny(activeTasks.ToArray());
                int indexOfTaskToReplace = taskToIndex[completedTask];
                // Replace the task that completed with a new task...
                // If there was an exception, the replacement should be a task to handle
                // that exception.  Otherwise, it should be the next work item.
                bool callExceptionHandler = false;
                if (callOnException != null)
                {
                    bool completedTaskWasExceptionHandler = exceptionHandlingTasks.Contains(completedTask);
                    // We'll want to run a task with the caller's exception handler...
                    callExceptionHandler = completedTask.IsFaulted;
                    if (completedTaskWasExceptionHandler)
                    {
                        // unless it was the caller's exception handler that faulted
                        exceptionHandlingTasks.Remove(completedTask);
                        callExceptionHandler = false;
                    }
                }
                Task replacementTask = callExceptionHandler
                    ? Task.Run(() => callOnException(completedTask.Exception))
                    : Task.Run(actionToRun);
                // Track exception handling tasks so that we can make sure we don't call the exception handler
                // on a failed exception handler.
                if (callExceptionHandler)
                    exceptionHandlingTasks.Add(replacementTask);
                // Put the replacement task at the same index in the array as the prior task
                activeTasks[indexOfTaskToReplace] = replacementTask;
                taskToIndex.Remove(completedTask);
                taskToIndex[replacementTask] = indexOfTaskToReplace;
                tasksStarted++;
            }

            // Phase 3 -- A final phase in which we empty out exceptions
            await Task.WhenAll(activeTasks);
            if (callOnException != null)
            {
                foreach (Task exceptionTask in activeTasks.Where(t => t.IsFaulted))
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() => callOnException(exceptionTask.Exception));
            }
        }



        public static async Task RunWithMaxDegreeOfConcurrency<T>(
            int maxDegreeOfConcurrency,
            IEnumerable<T> collection,
            Func<T, Task> taskFactory)
        {
            var activeTasks = new List<Task>(maxDegreeOfConcurrency);
            foreach (var task in collection.Select(taskFactory))
            {
                activeTasks.Add(task);
                if (activeTasks.Count == maxDegreeOfConcurrency)
                {
                    await Task.WhenAny(activeTasks.ToArray());
                    //observe exceptions here
                    activeTasks.RemoveAll(t => t.IsCompleted);
                }
            }
            await Task.WhenAll(activeTasks.ToArray()).ContinueWith(t =>
            {
                //observe exceptions in a manner consistent with the above   
            });
        }
    }
}
