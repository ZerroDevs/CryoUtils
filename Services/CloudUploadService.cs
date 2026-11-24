using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public static class CloudUploadService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        static CloudUploadService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CryoUtils/0.0.100");
        }

        public static async Task<string> UploadImageAsync(BitmapSource bitmap)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            byte[] imageBytes = ms.ToArray();

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("fileupload"), "reqtype");
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "fileToUpload", $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            var response = await _httpClient.PostAsync("https://catbox.moe/user/api.php", content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string resultUrl = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new InvalidOperationException($"Invalid upload response: {resultUrl}");
            }

            return resultUrl;
        }
    }
}
