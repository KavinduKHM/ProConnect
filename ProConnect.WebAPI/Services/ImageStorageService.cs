using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ProConnect.WebAPI.Services
{
    /// <summary>
    /// Stores job photos under wwwroot/uploads and hands back the public URL.
    /// Swap this for blob storage later; the callers only ever see the returned URL.
    /// </summary>
    public class ImageStorageService
    {
        private const string UploadsFolder = "uploads";

        private readonly IWebHostEnvironment _environment;

        public ImageStorageService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            var webRoot = _environment.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                // No wwwroot in the project template by default; create it on first use.
                webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            var targetDirectory = Path.Combine(webRoot, UploadsFolder);
            Directory.CreateDirectory(targetDirectory);

            // Never trust the client's filename: generate our own and keep only a known extension.
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
            {
                extension = ExtensionFor(file.ContentType);
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(targetDirectory, fileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            return $"/{UploadsFolder}/{fileName}";
        }

        /// <summary>
        /// Reads a previously stored image back, given the URL we handed out. Returns null if the file
        /// is gone or the URL points somewhere we did not write.
        /// </summary>
        public async Task<byte[]?> ReadAsync(string? url, CancellationToken cancellationToken = default)
        {
            var path = ResolvePath(url);
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        /// <summary>Best-effort content type for a stored image, inferred from its extension.</summary>
        public string MimeTypeFor(string? url) => Path.GetExtension(url ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };

        /// <summary>
        /// Maps "/uploads/x.png" back to a path inside the uploads folder. Refuses anything else, so a
        /// crafted ImageUrl cannot walk the filesystem.
        /// </summary>
        private string? ResolvePath(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith($"/{UploadsFolder}/", StringComparison.Ordinal))
            {
                return null;
            }

            var fileName = Path.GetFileName(url);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var webRoot = _environment.WebRootPath
                ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

            var uploadsRoot = Path.GetFullPath(Path.Combine(webRoot, UploadsFolder));
            var fullPath = Path.GetFullPath(Path.Combine(uploadsRoot, fileName));

            // Belt and braces: the resolved path must still sit inside the uploads folder.
            return fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }

        private static string ExtensionFor(string? contentType) => contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }
}
