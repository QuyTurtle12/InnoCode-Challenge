using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Api.IntegrationTests.Infrastructure
{
    public sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private static readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Responses = new();

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHttpMessageHandler(), disposeHandler: true);
        }

        public static void SetResponseBytes(string url, byte[] bytes, string contentType = "application/octet-stream")
        {
            Responses[url] = (bytes, contentType);
        }

        public static void ClearResponses()
        {
            Responses.Clear();
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
                if (request.RequestUri != null)
                {
                    string absolute = request.RequestUri.AbsoluteUri;
                    if (Responses.TryGetValue(absolute, out var exact))
                    {
                        var custom = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(exact.Bytes)
                        };
                        custom.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(exact.ContentType);
                        return Task.FromResult(custom);
                    }

                    string fileName = Path.GetFileName(request.RequestUri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        var matches = Responses
                            .Where(kvp => string.Equals(Path.GetFileName(kvp.Key), fileName, StringComparison.OrdinalIgnoreCase))
                            .Select(kvp => kvp.Value)
                            .ToList();

                        if (matches.Count == 1)
                        {
                            var match = matches[0];
                            var custom = new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new ByteArrayContent(match.Bytes)
                            };
                            custom.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(match.ContentType);
                            return Task.FromResult(custom);
                        }
                    }
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(PngBytes)
                };
                return Task.FromResult(response);
            }
        }
    }
}
