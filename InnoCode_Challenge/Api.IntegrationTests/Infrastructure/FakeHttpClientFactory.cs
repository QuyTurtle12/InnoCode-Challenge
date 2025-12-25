using System.Net;
using System.Net.Http;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHttpMessageHandler(), disposeHandler: true);
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private static readonly byte[] PngBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAA" +
                "AAC0lEQVR42mP8/58HAAMBAQAYK9pYAAAAAElFTkSuQmCC");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(PngBytes)
                };
                return Task.FromResult(response);
            }
        }
    }
}
