using Repository.ResponseModel;
using System.Net.Http.Json;

namespace Api.IntegrationTests.Infrastructure
{
    public static class HttpResponseMessageExtensions
    {
        public static async Task<TestBaseResponse<T>> ReadOkAsync<T>(this HttpResponseMessage res)
        {
            var body = await res.Content.ReadFromJsonAsync<TestBaseResponse<T>>();
            if (body is null) throw new InvalidOperationException("Response body is null or cannot be parsed.");
            return body;
        }

        public static async Task<ErrorEnvelope> ReadErrorAsync(this HttpResponseMessage res)
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorEnvelope>();
            if (body is null) throw new InvalidOperationException("Response body is null or cannot be parsed as ErrorEnvelope.");
            return body;
        }
    }
}
