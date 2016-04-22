using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace StopGuessing.DataStructures
{
    public static class TaskParalllel
    {
        public delegate bool FunctionToGetWorkItem(out Task workTask);

        public static void RemoveCompletedTasks(Task[] tasks, ref int numTasksInProgress, Action<Exception> callOnException)
        {
            int taskNumber = 0;
            while (taskNumber < numTasksInProgress)
            {
                Task t = tasks[taskNumber];
                if (t.IsCompleted || t.IsCanceled || t.IsFaulted)
                {
                    --numTasksInProgress;
                    if (t.IsFaulted)
                        callOnException(t.Exception);
                    if (taskNumber < numTasksInProgress)
                        tasks[taskNumber] = tasks[numTasksInProgress];
                }
                else
                {
                    taskNumber++;
                }

            }
        }

        public static async Task Worker(FunctionToGetWorkItem getWorkItem, Action<Exception> callOnException, int maxParallel)
        {
            Task[] tasksInProgress = new Task[maxParallel];
            int numTasksInProgress = 0;
            while (true)
            {
                Task newTask;
                if (!getWorkItem(out newTask))
                {
                    // There are no more tasks to perform.  Wait until the last one is completed.
                    if (numTasksInProgress > 0)
                        await Task.WhenAll(tasksInProgress.Take(numTasksInProgress));
                    return;
                }
                if (newTask.IsCompleted || newTask.IsCanceled)
                    continue;
                if (newTask.IsFaulted)
                {
                    callOnException(newTask.Exception);
                    continue;
                }

                // The task could not complete immediately, is still in progress, and needs to be awaited...
                if (numTasksInProgress > 0)
                {
                    RemoveCompletedTasks(tasksInProgress, ref numTasksInProgress, callOnException);

                    if (numTasksInProgress >= tasksInProgress.Length)
                    {
                        await Task.WhenAny(tasksInProgress);
                        RemoveCompletedTasks(tasksInProgress, ref numTasksInProgress, callOnException);
                    }
                }

                // Now add the new task
                tasksInProgress[numTasksInProgress++] = newTask;
            }
        }

        public static async Task ForEachWithWorkers<T>(
            IEnumerable<T> items,
            Func<T, ulong, CancellationToken, Task> actionToRunAsync,
            Action<Exception> callOnException = null,
            int numWorkers = 64,
            int maxTasksPerWorker = 50,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Queue<T> workItems = new Queue<T>(items);
            ulong workItemNumber = 0;
            object workLock = new object();
            Task[] workerTasks = new Task[numWorkers];
            for (int i = 0; i < workerTasks.Length; i++)
            {
                workerTasks[i] = Task.Run( async () => await Worker( (out Task workItem) =>
                {
                    T item;
                    ulong thisWorkItemNumber = 0;
                    lock (workLock)
                    {
                        if (workItems.Count <= 0)
                        {
                            workItem = null;
                            return false;
                        }
                        item = workItems.Dequeue();
                        thisWorkItemNumber = workItemNumber++;
                    }
                    workItem = actionToRunAsync(item, thisWorkItemNumber, cancellationToken);
                    return true;
                }, callOnException, maxTasksPerWorker), cancellationToken);
            }
            await Task.WhenAll(workerTasks);
        }

        public static async Task RepeatWithWorkers(
            ulong numberOfTimesToRepeat,
            Func<ulong, CancellationToken, Task> actionToRunAsync,
            Action<Exception> callOnException = null,
            int numWorkers = 32,
            int maxTasksPerWorker = 50,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ulong workItemNumber = 0;
            object workLock = new object();
            Task[] workerTasks = new Task[numWorkers];
            for (int i = 0; i < workerTasks.Length; i++)
            {
                workerTasks[i] = Task.Run(async () => await Worker((out Task workItem) =>
                {
                    ulong thisWorkItemNumber = 0;
                    lock (workLock)
                    {
                        if (workItemNumber >= numberOfTimesToRepeat)
                        {
                            workItem = null;
                            return false;
                        }
                        thisWorkItemNumber = workItemNumber++;
                    }
                    workItem = actionToRunAsync(thisWorkItemNumber, cancellationToken);
                    return true;
                }, callOnException, maxTasksPerWorker), cancellationToken);
            }
            await Task.WhenAll(workerTasks);
        }

//        public static async Task ForEach<T>(
//            IEnumerable<T> items,
//            Func<T, ulong, CancellationToken, Task> actionToRunAsync,
//            Func<Exception, CancellationToken, Task> callOnException = null,
//            uint waveSize = 500,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            ulong taskIndex = 0;
//            Queue<T> itemQueue = new Queue<T>(items);
//            int firstWaveSize = (int)Math.Min((ulong)waveSize, (ulong)itemQueue.Count);
//            Task[] currentWave = new Task[firstWaveSize];
//            Task[] nextWave = null;

//            // Start first wave
//            for (int i = 0; i < firstWaveSize; i++)
//            {
//                T item = itemQueue.Dequeue();
//                ulong myTaskIndex = taskIndex++;
//                currentWave[i] = Task.RunInBackground(async () => await actionToRunAsync(item, myTaskIndex, cancellationToken), cancellationToken);
//            }

//            while (currentWave != null)
//            {
//                // Invariant entering this loop: the nextWave has no tasks left to run

//                // Fill the next wave
//                if (itemQueue.Count > 0)
//                {
//                    int nextWaveSize = (int)Math.Min((ulong)waveSize, (ulong)itemQueue.Count);
//                    if (nextWave == null || nextWaveSize != nextWave.Length)
//                        nextWave = new Task[nextWaveSize];
//                    for (int i = 0; i < nextWave.Length; i++)
//                    {
//                        T item = itemQueue.Dequeue();
//                        ulong myTaskIndex = taskIndex++;
//                        nextWave[i] = Task.RunInBackground(async () => await actionToRunAsync(item, myTaskIndex, cancellationToken), cancellationToken);
//                    }
//                }
//                else
//                {
//                    nextWave = null;
//                }

//                // Wait for the current wave to finish
//                await Task.WhenAll(currentWave);

//                // Trigger exception handlers for any tasks that resulted in exceptions
//                if (callOnException != null)
//                {
//                    foreach (Task exceptionTask in currentWave.Where(t => t.IsFaulted))
//                    {
//#pragma warning disable 4014
//                        Task.RunInBackground(async () => await callOnException(exceptionTask.Exception, cancellationToken),
//                            cancellationToken);
//#pragma warning restore 4014
//                    }
//                }
//                // The current wave becomes the next wave...
//                Task[] tempWave = currentWave;
//                currentWave = nextWave;
//                // ... and the buffer for what had been the current wave can now be
//                // used for the next wave (if it is the right size)
//                nextWave = tempWave;
//            }
//        }

//        public static async Task ForEach<T>(
//            IEnumerable<T> items,
//            Func<T, CancellationToken, Task> actionToRunAsync,
//            Func<Exception, CancellationToken, Task> callOnException = null,
//            uint waveSize = 500,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            Queue<T> itemQueue = new Queue<T>(items);
//            int firstWaveSize = (int)Math.Min((ulong)waveSize, (ulong)itemQueue.Count);
//            Task[] currentWave = new Task[firstWaveSize];
//            Task[] nextWave = null;

//            // Start first wave
//            for (int i = 0; i < firstWaveSize; i++)
//            {
//                T item = itemQueue.Dequeue();
//                currentWave[i] = Task.RunInBackground(async () => await actionToRunAsync(item, cancellationToken), cancellationToken);
//            }

//            while (currentWave != null)
//            {
//                // Invariant entering this loop: the nextWave has no tasks left to run

//                // Fill the next wave
//                if (itemQueue.Count > 0)
//                {
//                    int nextWaveSize = (int)Math.Min((ulong)waveSize, (ulong)itemQueue.Count);
//                    if (nextWave == null || nextWaveSize != nextWave.Length)
//                        nextWave = new Task[nextWaveSize];
//                    for (int i = 0; i < nextWave.Length; i++)
//                    {
//                        T item = itemQueue.Dequeue();
//                        nextWave[i] = Task.RunInBackground(async () => await actionToRunAsync(item, cancellationToken), cancellationToken);
//                    }
//                }
//                else
//                {
//                    nextWave = null;
//                }

//                // Wait for the current wave to finish
//                await Task.WhenAll(currentWave);

//                // Trigger exception handlers for any tasks that resulted in exceptions
//                if (callOnException != null)
//                {
//                    foreach (Task exceptionTask in currentWave.Where(t => t.IsFaulted))
//                    {
//#pragma warning disable 4014
//                        Task.RunInBackground(async () => await callOnException(exceptionTask.Exception, cancellationToken), cancellationToken);
//#pragma warning restore 4014
//                    }
//                }
//                // The current wave becomes the next wave...
//                Task[] tempWave = currentWave;
//                currentWave = nextWave;
//                // ... and the buffer for what had been the current wave can now be
//                // used for the next wave (if it is the right size)
//                nextWave = tempWave;
//            }
//        }

//        public static async Task ParallelRepeatUsingWaves(
//            ulong numberOfTimesToRepeat,
//            Action<ulong> actionToRun,
//            Action<Exception> callOnException = null,
//            uint waveSize = 1000,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            ulong taskIndex = 0;
//            int firstWaveSize = (int)Math.Min((ulong)waveSize, numberOfTimesToRepeat);
//            Task[] currentWave = new Task[firstWaveSize];
//            Task[] nextWave = null;

//            // Start first wave
//            for (int i = 0; i < firstWaveSize; i++)
//            {
//                ulong myTaskIndex = taskIndex++;
//                currentWave[i] = Task.RunInBackground(() => actionToRun(myTaskIndex), cancellationToken);
//            }

//            while (currentWave != null)
//            {
//                // Invariant entering this loop: the nextWave has no tasks left to run

//                // Fill the next wave
//                if (taskIndex < numberOfTimesToRepeat)
//                {
//                    int nextWaveSize = (int)Math.Min((ulong)waveSize, numberOfTimesToRepeat - taskIndex);
//                    if (nextWave == null || nextWaveSize != nextWave.Length)
//                        nextWave = new Task[nextWaveSize];
//                    for (int i = 0; i < nextWave.Length; i++)
//                    {
//                        ulong myTaskIndex = taskIndex++;
//                        nextWave[i] = Task.RunInBackground(() => actionToRun(myTaskIndex), cancellationToken);
//                    }
//                }
//                else
//                {
//                    nextWave = null;
//                }

//                // Wait for the current wave to finish
//                await Task.WhenAll(currentWave);

//                // Trigger exception handlers for any tasks that resulted in exceptions
//                if (callOnException != null)
//                    foreach (Task exceptionTask in currentWave.Where(t => t.IsFaulted))
//#pragma warning disable 4014
//                        Task.RunInBackground(() => callOnException(exceptionTask.Exception), default(CancellationToken));
//#pragma warning restore 4014

//                // The current wave becomes the next wave...
//                Task[] tempWave = currentWave;
//                currentWave = nextWave;
//                // ... and the buffer for what had been the current wave can now be
//                // used for the next wave (if it is the right size)
//                nextWave = tempWave;
//            }
//        }

//        public static async Task ParallelRepeat(
//            ulong numberOfTimesToRepeat,
//            Func<ulong, Task> actionToRun,
//            Func<Exception, Task> callOnException = null,
//            int maxConcurrentTasks = 1000,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            Task[] activeTasks = new Task[(int)Math.Min((ulong)maxConcurrentTasks, numberOfTimesToRepeat)];
//            Dictionary<Task, int> taskToIndex = new Dictionary<Task, int>();
//            HashSet<Task> exceptionHandlingTasks = new HashSet<Task>();

//            ulong tasksStarted = 0;
//            // Phase 1 -- start maxConcurrentTasks executing
//            while (tasksStarted < (ulong)activeTasks.Length && tasksStarted < numberOfTimesToRepeat)
//            {
//                ulong taskId = tasksStarted;
//                Task startedTask = Task.RunInBackground(async () => await actionToRun(taskId), cancellationToken);
//                activeTasks[tasksStarted] = startedTask;
//                taskToIndex[startedTask] = (int)tasksStarted;
//                tasksStarted++;
//            }

//            // Phase 2 -- A stable state in which there are always the maximum number of tasks
//            //            in our array of active tasks
//            while (tasksStarted < numberOfTimesToRepeat)
//            {
//                ulong taskId = tasksStarted;
//                // Wait for a task to complete
//                Task completedTask = await Task.WhenAny(activeTasks.ToArray());
//                int indexOfTaskToReplace = taskToIndex[completedTask];
//                // Replace the task that completed with a new task...
//                // If there was an exception, the replacement should be a task to handle
//                // that exception.  Otherwise, it should be the next work item.
//                bool callExceptionHandler = false;
//                if (callOnException != null)
//                {
//                    bool completedTaskWasExceptionHandler = exceptionHandlingTasks.Contains(completedTask);
//                    // We'll want to run a task with the caller's exception handler...
//                    callExceptionHandler = completedTask.IsFaulted;
//                    if (completedTaskWasExceptionHandler)
//                    {
//                        // unless it was the caller's exception handler that faulted
//                        exceptionHandlingTasks.Remove(completedTask);
//                        callExceptionHandler = false;
//                    }
//                }
//                Task replacementTask;
//                if (callExceptionHandler)
//                    replacementTask = Task.RunInBackground(async () => await callOnException(completedTask.Exception), default(CancellationToken));
//                else
//                    replacementTask = Task.RunInBackground(async () => await actionToRun(taskId), cancellationToken);
//                // Track exception handling tasks so that we can make sure we don't call the exception handler
//                // on a failed exception handler.
//                if (callExceptionHandler)
//                    exceptionHandlingTasks.Add(replacementTask);
//                // Put the replacement task at the same index in the array as the prior task
//                activeTasks[indexOfTaskToReplace] = replacementTask;
//                taskToIndex.Remove(completedTask);
//                taskToIndex[replacementTask] = indexOfTaskToReplace;
//                tasksStarted++;
//            }

//            // Phase 3 -- A final phase in which we empty out exceptions
//            await Task.WhenAll(activeTasks);
//            if (callOnException != null)
//            {
//                foreach (Task exceptionTask in activeTasks.Where(t => t.IsFaulted))
//#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
//                    Task.RunInBackground(() => callOnException(exceptionTask.Exception), default(CancellationToken));
//            }
//        }



//        public static async Task RunWithMaxDegreeOfConcurrency<T>(
//            int maxDegreeOfConcurrency,
//            IEnumerable<T> collection,
//            Func<T, Task> taskFactory)
//        {
//            var activeTasks = new List<Task>(maxDegreeOfConcurrency);
//            foreach (var task in collection.Select(taskFactory))
//            {
//                activeTasks.Add(task);
//                if (activeTasks.Count == maxDegreeOfConcurrency)
//                {
//                    await Task.WhenAny(activeTasks.ToArray());
//                    //observe exceptions here
//                    activeTasks.RemoveAll(t => t.IsCompleted);
//                }
//            }
//            await Task.WhenAll(activeTasks.ToArray()).ContinueWith(t =>
//            {
//                //observe exceptions in a manner consistent with the above   
//            });
//        }
    }
}
