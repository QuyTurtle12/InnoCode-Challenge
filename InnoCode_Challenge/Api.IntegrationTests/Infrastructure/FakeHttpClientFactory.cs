using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
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
            private static readonly byte[] PngBytes = CreatePngBytes();

            private static byte[] CreatePngBytes()
            {
                using var img = new Image<Rgba32>(1, 1);
                img[0, 0] = new Rgba32(255, 255, 255, 255);

                using var ms = new MemoryStream();
                img.Save(ms, new PngEncoder());
                return ms.ToArray();
            }

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
