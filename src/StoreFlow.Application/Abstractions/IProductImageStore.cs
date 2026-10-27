namespace StoreFlow.Application.Abstractions;

using StoreFlow.Application.Common;

/// <summary>
/// Stores product pictures on the machine the shop runs on (PRD PRD-07). A picture is copied into the
/// application's own media folder and referenced by a relative path, so the reference stays valid
/// when the folder is copied or included in a backup.
/// </summary>
public interface IProductImageStore
{
    /// <summary>The largest picture the store accepts, in bytes.</summary>
    public const int MaximumImageBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Copies a picture into the media folder and returns the path that references it.
    /// </summary>
    /// <param name="sourceFilePath">The picture to copy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relative path of the stored picture, or the reason it was refused.</returns>
    Task<Result<string>> SaveAsync(string sourceFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a stored picture.
    /// </summary>
    /// <param name="relativePath">The relative path of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome of the removal.</returns>
    Task<Result> DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns a stored relative path into the absolute path of the file on disk.
    /// </summary>
    /// <param name="relativePath">The relative path of the picture.</param>
    /// <returns>The absolute path, whether or not the file exists.</returns>
    string ResolvePath(string relativePath);

    /// <summary>
    /// True when the relative path names a picture that is really inside the media folder and
    /// really exists.
    /// </summary>
    /// <param name="relativePath">The relative path of the picture.</param>
    /// <returns>True when the picture can be used.</returns>
    bool Exists(string relativePath);
}
