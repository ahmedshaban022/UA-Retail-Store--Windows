using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Infrastructure;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Stores product pictures under the application's own media folder (PRD PRD-07). A picture is
/// copied in and referenced by a relative path, so the folder can be backed up, moved or restored
/// without rewriting a single record.
/// </summary>
public sealed class ProductImageStore(AppPaths paths) : IProductImageStore
{
    /// <summary>The folder inside the media directory that holds the product pictures.</summary>
    public const string ProductsFolderName = "products";

    /// <summary>The error code when the file is not there.</summary>
    public const string NotFoundCode = "catalog.image_not_found";

    /// <summary>The error code when the file is not a picture the store accepts.</summary>
    public const string UnsupportedFormatCode = "catalog.image_unsupported";

    /// <summary>The error code when the file is larger than the store accepts.</summary>
    public const string TooLargeCode = "catalog.image_too_large";

    /// <summary>The error code when the picture could not be written.</summary>
    public const string WriteFailedCode = "catalog.image_write_failed";

    /// <inheritdoc />
    public async Task<Result<string>> SaveAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourceFilePath))
        {
            return Result<string>.Failure(NotFoundCode, "The chosen picture is no longer there. Choose it again.");
        }

        var info = new FileInfo(sourceFilePath);

        if (info.Length > IProductImageStore.MaximumImageBytes)
        {
            return Result<string>.Failure(
                TooLargeCode,
                $"A picture cannot be larger than {IProductImageStore.MaximumImageBytes / (1024 * 1024)} megabytes.");
        }

        string? extension = DetectExtension(sourceFilePath);

        if (extension is null)
        {
            return Result<string>.Failure(
                UnsupportedFormatCode,
                "This file is not a picture the store can use. Choose a PNG, JPEG, GIF, BMP or WEBP file.");
        }

        string folder = Path.Combine(paths.MediaDirectory, ProductsFolderName);
        Directory.CreateDirectory(folder);

        string fileName = $"{Guid.NewGuid():N}{extension}";
        string destination = Path.Combine(folder, fileName);

        try
        {
            await using FileStream source = File.OpenRead(sourceFilePath);
            await using FileStream target = File.Create(destination);

            await source.CopyToAsync(target, cancellationToken);
        }
        catch (IOException)
        {
            return Result<string>.Failure(WriteFailedCode, "The picture could not be saved. Try a different file.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<string>.Failure(WriteFailedCode, "The picture could not be saved. Check the folder's permissions.");
        }

        return Result<string>.Success($"{ProductsFolderName}/{fileName}");
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        cancellationToken.ThrowIfCancellationRequested();

        string absolute = ResolvePath(relativePath);

        if (!IsInsideMedia(absolute))
        {
            return Task.FromResult(Result.Failure(NotFoundCode, "This picture is not one of the store's pictures."));
        }

        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (IOException)
        {
            return Task.FromResult(Result.Failure(WriteFailedCode, "The picture could not be removed."));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Result.Failure(WriteFailedCode, "The picture could not be removed."));
        }

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalised = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(paths.MediaDirectory, normalised));
    }

    /// <inheritdoc />
    public bool Exists(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string absolute = ResolvePath(relativePath);

        return IsInsideMedia(absolute) && File.Exists(absolute);
    }

    private bool IsInsideMedia(string absolutePath)
    {
        string root = Path.GetFullPath(paths.MediaDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string candidate = absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the first bytes of the file and names the picture format from them. The extension of the
    /// file is not trusted: a picture renamed to <c>.jpg</c> is still a picture, and a document
    /// renamed to <c>.png</c> is still not one.
    /// </summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns>The extension to give the stored picture, or null when the file is not a picture.</returns>
    private static string? DetectExtension(string path)
    {
        Span<byte> header = stackalloc byte[12];
        int read;

        try
        {
            using FileStream stream = File.OpenRead(path);
            read = stream.Read(header);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ".png";
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (read >= 4 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'8')
        {
            return ".gif";
        }

        if (read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            return ".bmp";
        }

        if (read >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ".webp";
        }

        return null;
    }
}
