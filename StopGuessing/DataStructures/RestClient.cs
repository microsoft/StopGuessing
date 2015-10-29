using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.AspNet.Mvc.Razor.Directives;
using Newtonsoft.Json;

namespace StopGuessing.DataStructures
{
    public static class RestClientHelper
    {

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
            Object parameters,
            TimeSpan? timeout,
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
            TimeSpan? timeout,
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
            Object parameters,
            TimeSpan? timeout,
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
            Object parameters,
            TimeSpan? timeout,
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
            Object parameters,
            TimeSpan? timeout,
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
        public async static Task<TResult> TryServersUntilOneResponds<TResult, TIterationParameter>(
            List<TIterationParameter> iterationParameters,
            TimeSpan timeBetweenRetries,
            Func<TIterationParameter, TimeSpan, Task<TResult>> actionToTry,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<Task<TResult>> attemptsInProgress = new List<Task<TResult>>();
            // FIXME -- how to handle exceptions?

            int indexOfTaskFound = -1;
            TimeSpan timeUntilFinalTimeout = new TimeSpan(
                timeBetweenRetries.Ticks * iterationParameters.Count);
            while (indexOfTaskFound == -1 && attemptsInProgress.Count < iterationParameters.Count)
            {
                attemptsInProgress.Add(Task.Run(async () =>
                        await actionToTry(
                        iterationParameters[attemptsInProgress.Count], timeUntilFinalTimeout),
                        cancellationToken));
                indexOfTaskFound = Task.WaitAny(
                        (attemptsInProgress.Select(t => (Task)t).ToArray()),
                        (int)timeBetweenRetries.TotalMilliseconds,
                        cancellationToken);
                timeUntilFinalTimeout = timeUntilFinalTimeout.Subtract(timeBetweenRetries);
            }
            return await attemptsInProgress[indexOfTaskFound];
        }

        public static async Task TryServersUntilOneResponds<TIterationParameter>(
            List<TIterationParameter> iterationParameters,
            TimeSpan timeBetweenRetries,
            Func<TIterationParameter, TimeSpan, Task> actionToTry,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<Task> attemptsInProgress = new List<Task>();

            int indexOfTaskFound = -1;
            TimeSpan timeUntilFinalTimeout = new TimeSpan(
                timeBetweenRetries.Ticks*iterationParameters.Count);
            while (indexOfTaskFound == -1 && attemptsInProgress.Count < iterationParameters.Count)
            {
                attemptsInProgress.Add(Task.Run(async () =>
                    await actionToTry(
                        iterationParameters[attemptsInProgress.Count], timeUntilFinalTimeout),
                    cancellationToken));
                indexOfTaskFound = Task.WaitAny(
                    (attemptsInProgress.Select(t => (Task) t).ToArray()),
                    (int) timeBetweenRetries.TotalMilliseconds,
                    cancellationToken);
                timeUntilFinalTimeout = timeUntilFinalTimeout.Subtract(timeBetweenRetries);
            }
            await attemptsInProgress[indexOfTaskFound];
        }

    }
}
