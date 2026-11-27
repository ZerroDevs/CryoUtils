using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public readonly record struct UploadProgressInfo(long BytesSent, long TotalBytes)
    {
        public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesSent / TotalBytes, 0, 1);
        public int Percent => (int)Math.Round(Fraction * 100);
    }

    public static class CloudUploadService
    {
        private const string Endpoint = "https://catbox.moe/user/api.php";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        static CloudUploadService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CryoUtils/0.0.101");
        }

        public static byte[] EncodeBitmapToPng(BitmapSource bitmap)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            return ms.ToArray();
        }

        public static async Task<string> UploadImageAsync(
            BitmapSource bitmap,
            IProgress<UploadProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            byte[] imageBytes = EncodeBitmapToPng(bitmap);
            using var ms = new MemoryStream(imageBytes, writable: false);
            string fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            return await PostStreamAsync(ms, fileName, "image/png", imageBytes.LongLength, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<string> UploadFileAsync(
            string filePath,
            IProgress<UploadProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found for upload.", filePath);
            }

            var info = new FileInfo(filePath);
            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 65536,
                useAsync: true);

            return await PostStreamAsync(fs, info.Name, MimeForPath(filePath), info.Length, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<string> PostStreamAsync(
            Stream sourceStream,
            string fileName,
            string mediaType,
            long totalLength,
            IProgress<UploadProgressInfo>? progress,
            CancellationToken cancellationToken)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("fileupload"), "reqtype");

            var streamContent = new ProgressStreamContent(sourceStream, totalLength, progress, cancellationToken);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            content.Add(streamContent, "fileToUpload", fileName);

            using var response = await _httpClient.PostAsync(Endpoint, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string resultUrl = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
            if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"Invalid upload response: {resultUrl}");
            }

            return resultUrl;
        }

        private static string MimeForPath(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".json" => "application/json",
                ".txt" or ".md" or ".csv" => "text/plain",
                ".xml" => "application/xml",
                _ => "application/octet-stream"
            };
        }

        private sealed class ProgressStreamContent : HttpContent
        {
            private const int ChunkSize = 32768;
            private readonly Stream _stream;
            private readonly long _length;
            private readonly IProgress<UploadProgressInfo>? _progress;
            private readonly CancellationToken _cancellationToken;

            public ProgressStreamContent(
                Stream stream,
                long length,
                IProgress<UploadProgressInfo>? progress,
                CancellationToken cancellationToken)
            {
                _stream = stream;
                _length = length;
                _progress = progress;
                _cancellationToken = cancellationToken;
                Headers.ContentLength = length;
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _length;
                return true;
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                if (_stream.CanSeek)
                {
                    _stream.Position = 0;
                }

                byte[] buffer = new byte[ChunkSize];
                long sent = 0;
                long lastReportTick = 0;

                _progress?.Report(new UploadProgressInfo(0, _length));

                while (true)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    int read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await stream.WriteAsync(buffer.AsMemory(0, read), _cancellationToken).ConfigureAwait(false);
                    sent += read;

                    long nowTick = Environment.TickCount64;
                    if (_progress != null && (nowTick - lastReportTick >= 50 || sent >= _length))
                    {
                        lastReportTick = nowTick;
                        _progress.Report(new UploadProgressInfo(sent, _length));
                    }
                }
            }
        }
    }
}
