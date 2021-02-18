using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

#nullable enable

namespace ThemesOfDotNet.Middleware
{
    public class PrecompressedMiddleware
    {
        const string FontLink = "</fonts/open-iconic.woff>; rel=preload; as=font; crossorigin=anonymous";

        private bool _blazorPrecompressed = false;
        private DataCache? _blazorJsGz;
        private DataCache? _blazorJsBr;

        private readonly ILogger<PrecompressedMiddleware> _logger;
        private readonly RequestDelegate _next;

        public PrecompressedMiddleware(RequestDelegate next, ILogger<PrecompressedMiddleware> logger)
        {
            _logger = logger;
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {

            var request = context.Request;
            var path = request.Path.Value;

            if (path == "/")
            {
                var headers = context.Response.Headers;
                headers["Link"] = FontLink;
            }
            else if (path.EndsWith(".js", StringComparison.Ordinal) ||
                path.EndsWith(".css", StringComparison.Ordinal))
            {
                return ApplyPrecompressionAsync(context);
            }

            return _next(context);
        }

        public async Task ApplyPrecompressionAsync(HttpContext context)
        {
            var request = context.Request;
            var path = request.Path.Value;
            var extraExtension = string.Empty;

            var response = context.Response;
            var headers = response.Headers;

            var acceptEncoding = request.Headers[HeaderNames.AcceptEncoding].ToString();
            if (acceptEncoding.Length > 64)
            {
                // Not happy parsing, this is far too long
                response.StatusCode = StatusCodes.Status431RequestHeaderFieldsTooLarge;
                return;
            }

            headers[HeaderNames.Vary] = HeaderNames.AcceptEncoding;
            response.OnStarting(s_addClientCache, response);

            var isBlazorJs = false;
            if (path.Contains("_framework"))
            {
                // blazor.server.js can be served uncompressed and its quite chunky
                // so we intercept the request; then compress the data for following requests.
                if (path.EndsWith("/blazor.server.js", StringComparison.Ordinal))
                {
                    if (Volatile.Read(ref _blazorJsBr) is null || Volatile.Read(ref _blazorJsGz) is null)
                    {
                        // Don't do compression if its already being served precompressed.
                        isBlazorJs = !_blazorPrecompressed;
                    }
                    else
                    {
                        extraExtension = GetCompressionExtension(acceptEncoding);

                        var (dataCache, encoding) = extraExtension switch
                        {
                            ".gz" => (data: _blazorJsGz, encoding: "gzip"),
                            ".br" => (data: _blazorJsBr, encoding: "br"),
                            _ => (data: null, encoding: "")
                        };

                        if (dataCache is not null)
                        {
                            // We have precompressed blazor.server.js
                            if (request.Headers[HeaderNames.IfNoneMatch] == dataCache.Etag)
                            {
                                response.StatusCode = StatusCodes.Status304NotModified;
                                return;
                            }

                            headers.ContentLength = dataCache.Data.Length;
                            headers[HeaderNames.ContentType] = "application/javascript";
                            headers[HeaderNames.Vary] = HeaderNames.AcceptEncoding;
                            headers[HeaderNames.CacheControl] = "max-age=31536000";
                            headers[HeaderNames.ContentEncoding] = encoding;
                            headers[HeaderNames.ETag] = dataCache.Etag;

                            await response.BodyWriter.WriteAsync(dataCache.Data);

                            _logger.CompressionApplied(extraExtension, path);
                            return;
                        }
                    }
                }
            }
            else if (path.EndsWith(".js", StringComparison.Ordinal) ||
                path.EndsWith(".css", StringComparison.Ordinal))
            {
                extraExtension = GetCompressionExtension(acceptEncoding);
            }

            if (extraExtension.Length > 0)
            {
                // Accept a compression type; so change path so 
                // StaticFiles picks up right file
                request.Path = path + extraExtension;
                _logger.CompressionApplied(extraExtension, path);
            }

            MemoryStream? cacheStream = null;
            Stream baseStream = null!;
            if (isBlazorJs
                && Volatile.Read(ref _blazorJsBr) is null 
                && Volatile.Read(ref _blazorJsGz) is null)
            {
                // Not compressed blazor.server.js yet, intercept.
                baseStream = context.Response.Body;
                cacheStream = new MemoryStream();
                response.Body = cacheStream;
            }

            // Run next step in pipeline
            await _next(context);

            if (cacheStream is not null)
            {
                // Write out the intercepted data unconditionally.
                cacheStream.Position = 0;
                await cacheStream.CopyToAsync(baseStream).ConfigureAwait(false);

                if (response.StatusCode != StatusCodes.Status200OK)
                {
                    // Result is not 200, don't do anything extra with the intercepted data.
                    // e.g. we don't want to use a 304 or 404 response as our compression source.
                    isBlazorJs = false;
                }
                else if (response.Headers[HeaderNames.ContentEncoding].Count > 0)
                {
                    // Result is being served compressed, note it so we don't try to recompress.
                    _blazorPrecompressed = true;
                    isBlazorJs = false;
                }

                if (isBlazorJs)
                {
                    CacheCompressedBlazorJs(response, cacheStream);
                }
            }
        }

        private void CacheCompressedBlazorJs(HttpResponse response, MemoryStream cacheStream)
        {
            var etag = response.Headers[HeaderNames.ETag];

            if (Volatile.Read(ref _blazorJsBr) is null)
            {
                var outputStream = new MemoryStream();
                using (var bzStream = new BrotliStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    cacheStream.Position = 0;
                    cacheStream.CopyTo(bzStream);
                }

                var data = new DataCache(outputStream.ToArray(), etag);

                Volatile.Write(ref _blazorJsBr, data);
                _logger.BlazorCompressed("br", cacheStream.Length, outputStream.Length);
            }
            if (Volatile.Read(ref _blazorJsGz) is null)
            {

                var outputStream = new MemoryStream();
                using (var gzStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    cacheStream.Position = 0;
                    cacheStream.CopyTo(gzStream);
                }

                var data = new DataCache(outputStream.ToArray(), etag);

                Volatile.Write(ref _blazorJsGz, data);
                _logger.BlazorCompressed("gzip", cacheStream.Length, outputStream.Length);
            }
        }

        private static string GetCompressionExtension(ReadOnlySpan<char> acceptEncoding)
        {
            var extraExtension = string.Empty;
            foreach (var range in acceptEncoding.Split(','))
            {
                var encoding = acceptEncoding[range];
                // Check if is a Quality
                var qualityStart = encoding.IndexOf(';');
                if (qualityStart > 0)
                {
                    // Remove Quality
                    encoding = encoding[..qualityStart];
                }

                // Remove any additional spaces
                encoding = encoding.Trim(' ');

                if (encoding.SequenceEqual("br"))
                {
                    // Brotli accepted, set the additional file extension
                    extraExtension = ".br";
                    // This is our preferred compression so exit the loop
                    break;
                }
                else if (encoding.SequenceEqual("gzip"))
                {
                    // Gzip accepted, we'll set the extension, but keep looking
                    extraExtension = ".gz";
                }
            }

            return extraExtension;
        }

        private static readonly Func<object, Task> s_addClientCache = (obj) =>
        {
            var response = Unsafe.As<HttpResponse>(obj);
            if (response.StatusCode == StatusCodes.Status200OK)
            {
                response.Headers[HeaderNames.CacheControl] = "max-age=31536000";
            }

            return Task.CompletedTask;
        };

        private record DataCache(byte[] Data, StringValues Etag);
    }

    internal static class LoggerExtensions
    {
        private static readonly Action<ILogger, string, string, Exception?> s_compressionApplied =
            LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1, nameof(CompressionApplied)), "Response Compression {CompressionExtension} Applied: {path}");
        
        private static readonly Action<ILogger, string, long, long, Exception?> s_blazorCompressed =
            LoggerMessage.Define<string, long, long>(LogLevel.Information, new EventId(2, nameof(BlazorCompressed)), "Blazor Js {CompressionExtension} Compressed: {inputBytes} -> {outputBytes}");

        public static void CompressionApplied(this ILogger<PrecompressedMiddleware> logger, string compressionExtension, string path) => s_compressionApplied(logger, compressionExtension, path, null);
        public static void BlazorCompressed(this ILogger<PrecompressedMiddleware> logger, string compressionExtension, long inputBytes, long outputBytes) => s_blazorCompressed(logger, compressionExtension, inputBytes, outputBytes, null);
    }

    public static class PrecompressedMiddlewareExtensions
    {
        public static IApplicationBuilder UsePrecompressedStaticFiles(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PrecompressedMiddleware>()
                .UseStaticFiles(new StaticFileOptions()
                {
                    ServeUnknownFileTypes = true,
                    OnPrepareResponse = (context) =>
                    {
                        var headers = context.Context.Response.Headers;
                        var fileName = context.File.Name;

                        if (fileName.EndsWith(".gz", StringComparison.Ordinal))
                        {
                            headers[HeaderNames.ContentEncoding] = "gzip";
                            if (fileName.EndsWith(".js.gz", StringComparison.Ordinal))
                            {
                                headers[HeaderNames.ContentType] = "application/javascript";
                                return;
                            }
                            else if (fileName.EndsWith(".css.gz", StringComparison.Ordinal))
                            {
                                headers[HeaderNames.ContentType] = "text/css";
                                return;
                            }
                        }
                        else if (fileName.EndsWith(".br", StringComparison.Ordinal))
                        {
                            headers[HeaderNames.ContentEncoding] = "br";
                            if (fileName.EndsWith(".js.br", StringComparison.Ordinal))
                            {
                                headers[HeaderNames.ContentType] = "application/javascript";
                                return;
                            }
                            else if (fileName.EndsWith(".css.br", StringComparison.Ordinal))
                            {
                                headers[HeaderNames.ContentType] = "text/css";
                                return;
                            }
                        }
                        else
                        {
                            // Non matched don't change Content-Type
                            return;
                        }

                        // Change to octet-stream
                        headers[HeaderNames.ContentType] = "application/octet-stream";
                    }
                });
        }
    }
}

namespace System
{
    public static class MemoryExtensions
    {
        public static SpanSplitEnumerator<char> Split(this ReadOnlySpan<char> span, char separator)
            => new(span, separator);

        public ref struct SpanSplitEnumerator<T>
#nullable disable // to enable use with both T and T? for reference types due to IEquatable<T> being invariant
            where T : IEquatable<T>
#nullable restore
        {
            private readonly ReadOnlySpan<char> _span;
            private readonly char _separatorChar;
            private int _start;
            private bool _started;
            private bool _ended;
            private Range _current;

            public SpanSplitEnumerator<T> GetEnumerator() => this;

            public Range Current
            {
                get
                {
                    if (!_started || _ended)
                    {
                        Throw();
                    }

                    return _current;

                    static void Throw()
                    {
                        throw new InvalidOperationException();
                    }
                }

            }

            internal SpanSplitEnumerator(ReadOnlySpan<char> span, char separator) : this()
            {
                _span = span;
                _separatorChar = separator;
            }

            public bool MoveNext()
            {
                _started = true;

                if (_start > _span.Length)
                {
                    _ended = true;
                    return false;
                }

                var slice = _start == 0
                    ? _span
                    : _span[_start..];

                var end = _start;
                if (slice.Length > 0)
                {
                    var index = slice.IndexOf(_separatorChar);

                    if (index == -1)
                    {
                        index = slice.Length;
                    }

                    end += index;
                }

                _current = new Range(_start, end);
                _start = end + 1;

                return true;
            }
        }
    }
}