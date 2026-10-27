namespace StoreFlow.Desktop.ViewModels;

/// <summary>The kind of reference record a screen or an editor is working with.</summary>
public enum CatalogReferenceKind
{
    /// <summary>A product category.</summary>
    Category = 0,

    /// <summary>A selling unit.</summary>
    Unit = 1,

    /// <summary>A supplier.</summary>
    Supplier = 2,
}

/// <summary>
/// Shows the catalogue's modal dialogs. The view models ask for a dialog; the presenter decides what
/// that looks like, which keeps the windows out of the screen logic.
/// </summary>
public interface ICatalogDialogs
{
    /// <summary>
    /// Opens the product editor for a new or an existing product.
    /// </summary>
    /// <param name="productId">The product to edit, or null to create one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner saved the product.</returns>
    Task<bool> ShowProductEditorAsync(int? productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the editor for a reference record.
    /// </summary>
    /// <param name="kind">The kind of record.</param>
    /// <param name="recordId">The record to edit, or null to create one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner saved the record.</returns>
    Task<bool> ShowReferenceEditorAsync(CatalogReferenceKind kind, int? recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the CSV import window.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the owner imported anything.</returns>
    Task<bool> ShowCsvImportAsync(CancellationToken cancellationToken = default);
}
