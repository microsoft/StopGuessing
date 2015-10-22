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

        private static void ConfigureClient(HttpClient client, Uri baseAddress)
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = baseAddress;
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


        public static async Task<TReturnType> PutAsync<TReturnType>(Uri baseAddress, string pathAndQuery, Object parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                HttpResponseMessage restApiResult = await
                    client.PutAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
                return JsonConvert.DeserializeObject<TReturnType>(await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
        }

        public static async Task PutAsync(Uri baseAddress, string pathAndQuery, Object parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                await client.PutAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
            }
        }

        public static async Task<TReturnType> PostAsync<TReturnType>(Uri baseAddress, string pathAndQuery, Object parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                HttpResponseMessage restApiResult = await
                    client.PostAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
                return JsonConvert.DeserializeObject<TReturnType>(await restApiResult.EnsureSuccessStatusCode().Content.ReadAsStringAsync());
            }
        }

        public static async Task PostAsync(Uri baseAddress, string pathAndQuery, Object parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
                await client.PostAsync(pathAndQuery, new StringContent(JsonConvert.SerializeObject(parameters)), cancellationToken);
            }
        }

        public static async Task<TReturnType> GetAsync<TReturnType>(Uri baseAddress, string pathAndQuery, IEnumerable<KeyValuePair<string,string>> uriParameters = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = new HttpClient())
            {
                ConfigureClient(client, baseAddress);
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
        public static async Task<TReturnType> DeleteAsync<TReturnType>(Uri baseAddress, string pathAndQuery,
            IEnumerable<KeyValuePair<string, string>> uriParameters = null, CancellationToken cancellationToken = default(CancellationToken))
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



    }
}
