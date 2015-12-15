using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Threading;
using Newtonsoft.Json;

namespace StopGuessing.DataStructures
{
    public static class RestClientHelper
    {
        public class TimeoutException : Exception {}

        private static void ConfigureClient(HttpClient client, Uri baseAddress, TimeSpan? timeout = null)
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = baseAddress;
            if (timeout.HasValue)
                client.Timeout = timeout.Value;
        }

        private static string AddParametersToUri(string uri, IEnumerable<KeyValuePair<string, string>> uriParameters = null)
        {
            if (uriParameters == null)
                return uri;            

            UriBuilder builder = new UriBuilder(uri);
            List<string> parameters =
                builder.Query.Split(new[] {'&'}, StringSplitOptions.RemoveEmptyEntries).ToList();
            parameters.AddRange(uriParameters.Select(parameter => Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value)));
            builder.Query = string.Join("&", parameters);
            return builder.Uri.PathAndQuery;
        }


        public static async Task<TReturnType> PutAsync<TReturnType>(
            Uri baseAddress,
            string pathAndQuery,
            Object parameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress, timeout);
                HttpResponseMessage restApiResult = await
                    client.PutAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
                return JsonConvert.DeserializeObject<TReturnType>(await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
        }

        public static async Task PutAsync(Uri baseAddress, 
            string pathAndQuery,
            Object parameters,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress, timeout);
                await client.PutAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
            }
        }

        public static async Task<TReturnType> PostAsync<TReturnType>(
            Uri baseAddress,
            string pathAndQuery,
            Object parameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress, timeout);
                HttpResponseMessage restApiResult = await
                    client.PostAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
                return JsonConvert.DeserializeObject<TReturnType>(await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
        }

        public static async Task PostAsync(
            Uri baseAddress,
            string pathAndQuery,
            Object parameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress, timeout);
                await client.PostAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
            }
        }

        public static void PostBackground(Uri baseAddress, 
            string pathAndQuery,
            Object parameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run(() => PostAsync(baseAddress, pathAndQuery, parameters, timeout, cancellationToken),
                cancellationToken);
        }

        public static async Task<TReturnType> GetAsync<TReturnType>(
            Uri baseAddress, 
            string pathAndQuery,
            IEnumerable<KeyValuePair<string,string>> uriParameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress, timeout);
                string pathAndQueryWithParameters = AddParametersToUri(pathAndQuery, uriParameters);
                HttpResponseMessage restApiResult = await
                    client.GetAsync(pathAndQueryWithParameters, cancellationToken);
                if (restApiResult.StatusCode == HttpStatusCode.NotFound)
                {
                    return default(TReturnType);
                }
                else
                {
                    return JsonConvert.DeserializeObject<TReturnType>(
                        await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
                }
            }
        }
        public static async Task<TReturnType> DeleteAsync<TReturnType>(Uri baseAddress, 
            string pathAndQuery,
            IEnumerable<KeyValuePair<string, string>> uriParameters = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                pathAndQuery = AddParametersToUri(pathAndQuery, uriParameters);
                HttpResponseMessage restApiResult = await
                    client.DeleteAsync(pathAndQuery, cancellationToken);
                return JsonConvert.DeserializeObject<TReturnType>(await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
        }

        public static async Task DeleteAsync(Uri baseAddress, string pathAndQuery,
            IEnumerable<KeyValuePair<string, string>> uriParameters = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                pathAndQuery = AddParametersToUri(pathAndQuery, uriParameters);
                await client.DeleteAsync(pathAndQuery, cancellationToken);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <typeparam name="TIterationParameter"></typeparam>
        /// <param name="iterationParameters"></param>
        /// <param name="timeBetweenRetries"></param>
        /// <param name="actionToTry"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async static Task<TResult> TryServersUntilOneRespondsWithResult<TResult, TIterationParameter>(
            IEnumerable<TIterationParameter> iterationParameters,
            TimeSpan timeBetweenRetries,
            Func<TIterationParameter, TimeSpan, Task<TResult>> actionToTry,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Queue<TIterationParameter> iterationParameterQueue = new Queue<TIterationParameter>(iterationParameters);

            List<Task<TResult>> attemptsInProgress = new List<Task<TResult>>();
            
            int indexOfTaskFound = -1;
            TimeSpan timeUntilFinalTimeout = new TimeSpan(
                timeBetweenRetries.Ticks * iterationParameterQueue.Count);

            while (indexOfTaskFound == -1 && iterationParameterQueue.Count > 0)
            {
                TimeSpan localtimeUntilFinalTimeout = timeUntilFinalTimeout;
                attemptsInProgress.Add(Task.Run(async () =>
                {
                    TResult result = await actionToTry(iterationParameterQueue.Dequeue(), localtimeUntilFinalTimeout);
                    return result;
                }, cancellationToken));

                indexOfTaskFound = Task.WaitAny(
                        (attemptsInProgress.Select(t => (Task)t).ToArray()),
                        // FIXME (int)timeBetweenRetries.TotalMilliseconds,
                        cancellationToken);

                if (indexOfTaskFound >= 0 && attemptsInProgress[indexOfTaskFound].IsFaulted)
                {
                    // The task completed not because it was successful, but because of an exception.
                    if (attemptsInProgress.Count > 1 || iterationParameterQueue.Count > 0)
                    {
                        // There are stil other attempts that may succeed.  Let's just ignore this error.
                        attemptsInProgress.RemoveAt(indexOfTaskFound);
                        indexOfTaskFound = -1;
                    }
                    else
                    {
                        // This attempt was our last hope.  There are no more yet to complete or to run.
                        // We'll do nothing an allow the exception to come back to the caller.
                    }
                }

                timeUntilFinalTimeout = timeUntilFinalTimeout.Subtract(timeBetweenRetries);                
            }
            if (indexOfTaskFound >= 0)
            {
                return await attemptsInProgress[indexOfTaskFound];
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public static async Task TryServersUntilOneResponds<TIterationParameter>(
            List<TIterationParameter> iterationParameters,
            TimeSpan timeBetweenRetries,
            Func<TIterationParameter, TimeSpan, Task> actionToTry,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Queue<TIterationParameter> iterationParameterQueue = new Queue<TIterationParameter>(iterationParameters);
            
            List<Task> attemptsInProgress = new List<Task>();

            int indexOfTaskFound = -1;
            TimeSpan timeUntilFinalTimeout = new TimeSpan(
                timeBetweenRetries.Ticks * iterationParameterQueue.Count);

            while (indexOfTaskFound == -1 && iterationParameterQueue.Count > 0)
            {
                TimeSpan localtimeUntilFinalTimeout = timeUntilFinalTimeout;
                attemptsInProgress.Add(Task.Run(async () => await actionToTry(iterationParameterQueue.Dequeue(), localtimeUntilFinalTimeout),
                        cancellationToken));

                indexOfTaskFound = Task.WaitAny(
                        (attemptsInProgress.Select(t => (Task)t).ToArray()),
                        // FIXME (int)timeBetweenRetries.TotalMilliseconds,
                        cancellationToken);

                if (indexOfTaskFound >= 0 && attemptsInProgress[indexOfTaskFound].IsFaulted)
                {
                    // The task completed not because it was successful, but because of an exception.
                    if (attemptsInProgress.Count > 1 || iterationParameterQueue.Count > 0)
                    {
                        // There are stil other attempts that may succeed.  Let's just ignore this error.
                        attemptsInProgress.RemoveAt(indexOfTaskFound);
                        indexOfTaskFound = -1;
                    }
                    else
                    {
                        // This attempt was our last hope.  There are no more yet to complete or to run.
                        // We'll do nothing an allow the exception to come back to the caller.
                    }
                }

                timeUntilFinalTimeout = timeUntilFinalTimeout.Subtract(timeBetweenRetries);
            }
            if (indexOfTaskFound >= 0)
            {
                await attemptsInProgress[indexOfTaskFound];
            }
            else
            {
                throw new TimeoutException();
            }
        }

    }
}
