namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// The database triggers that make the ledger's invariants hold even when a bug, a script or a
/// third-party tool writes to the database directly.
/// </summary>
/// <remarks>
/// The application layer already enforces these rules; the triggers are the second line of defence
/// that turns a violated invariant into a refused write rather than a silent corruption.
/// </remarks>
public static class SchemaTriggers
{
    /// <summary>The trigger script applied after every migration.</summary>
    public const string Script = """
        -- The sellable quantity may never go negative.
        CREATE TRIGGER IF NOT EXISTS trg_StockBalances_NoNegative
        BEFORE UPDATE ON StockBalances
        WHEN NEW.QuantityOnHandScaled < 0
        BEGIN
            SELECT RAISE(ABORT, 'Stock balance cannot go negative');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_StockBalances_NoNegativeInsert
        BEFORE INSERT ON StockBalances
        WHEN NEW.QuantityOnHandScaled < 0
        BEGIN
            SELECT RAISE(ABORT, 'Stock balance cannot go negative');
        END;

        -- Posted stock movements are immutable; a correction is a new referenced movement.
        CREATE TRIGGER IF NOT EXISTS trg_StockMovements_NoUpdate
        BEFORE UPDATE ON StockMovements
        BEGIN
            SELECT RAISE(ABORT, 'Stock movements are immutable');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_StockMovements_NoDelete
        BEFORE DELETE ON StockMovements
        BEGIN
            SELECT RAISE(ABORT, 'Stock movements are immutable');
        END;

        -- Posted invoice lines are frozen; corrections happen through a linked return.
        --
        -- The one thing that may move on a posted line is the counter of what has come back, because
        -- a return has to be able to say that a unit of this line is on its way back to the shelf.
        -- Every other column is compared one by one, so the line stays as frozen as it was.
        DROP TRIGGER IF EXISTS trg_SalesInvoiceLines_NoUpdate;

        CREATE TRIGGER IF NOT EXISTS trg_SalesInvoiceLines_NoUpdate
        BEFORE UPDATE ON SalesInvoiceLines
        WHEN (SELECT Status FROM SalesInvoices WHERE Id = OLD.SalesInvoiceId) IN (1, 2)
             AND (
                  NEW.SalesInvoiceId IS NOT OLD.SalesInvoiceId
               OR NEW.ProductId IS NOT OLD.ProductId
               OR NEW.NameSnapshot IS NOT OLD.NameSnapshot
               OR NEW.SkuSnapshot IS NOT OLD.SkuSnapshot
               OR NEW.BarcodeSnapshot IS NOT OLD.BarcodeSnapshot
               OR NEW.UnitSnapshot IS NOT OLD.UnitSnapshot
               OR NEW.QuantityScaled IS NOT OLD.QuantityScaled
               OR NEW.QuantityScale IS NOT OLD.QuantityScale
               OR NEW.UnitPrice IS NOT OLD.UnitPrice
               OR NEW.Discount IS NOT OLD.Discount
               OR NEW.InvoiceDiscountShare IS NOT OLD.InvoiceDiscountShare
               OR NEW.DiscountPercentScaled IS NOT OLD.DiscountPercentScaled
               OR NEW.Tax IS NOT OLD.Tax
               OR NEW.Total IS NOT OLD.Total
               OR NEW.UnitCostSnapshot IS NOT OLD.UnitCostSnapshot
               OR NEW.TaxRateSnapshotBasisPoints IS NOT OLD.TaxRateSnapshotBasisPoints
               OR NEW.TaxPriceIncludedSnapshot IS NOT OLD.TaxPriceIncludedSnapshot)
        BEGIN
            SELECT RAISE(ABORT, 'Posted invoice lines are immutable');
        END;

        DROP TRIGGER IF EXISTS trg_SalesInvoiceLines_NoDelete;

        CREATE TRIGGER IF NOT EXISTS trg_SalesInvoiceLines_NoDelete
        BEFORE DELETE ON SalesInvoiceLines
        WHEN (SELECT Status FROM SalesInvoices WHERE Id = OLD.SalesInvoiceId) IN (1, 2)
        BEGIN
            SELECT RAISE(ABORT, 'Posted invoice lines are immutable');
        END;

        -- A posted purchase line is frozen too, and stays frozen once it has been reversed.
        DROP TRIGGER IF EXISTS trg_PurchaseLines_NoUpdate;

        CREATE TRIGGER IF NOT EXISTS trg_PurchaseLines_NoUpdate
        BEFORE UPDATE ON PurchaseLines
        WHEN (SELECT Status FROM PurchaseDocuments WHERE Id = OLD.PurchaseDocumentId) IN (1, 2)
        BEGIN
            SELECT RAISE(ABORT, 'Posted purchase lines are immutable');
        END;

        DROP TRIGGER IF EXISTS trg_PurchaseLines_NoDelete;

        CREATE TRIGGER IF NOT EXISTS trg_PurchaseLines_NoDelete
        BEFORE DELETE ON PurchaseLines
        WHEN (SELECT Status FROM PurchaseDocuments WHERE Id = OLD.PurchaseDocumentId) IN (1, 2)
        BEGIN
            SELECT RAISE(ABORT, 'Posted purchase lines are immutable');
        END;

        -- A barcode belongs to at most one product (PRD PRD-03, PRD-08). The unique index on each
        -- barcode column stops a duplicate inside one table; these triggers stop a barcode being the
        -- primary code of one product and an extra code of another, which no single index can catch.
        CREATE TRIGGER IF NOT EXISTS trg_Products_BarcodeNotTaken
        BEFORE INSERT ON Products
        WHEN NEW.Barcode IS NOT NULL AND NEW.Barcode <> ''
             AND EXISTS (
                 SELECT 1 FROM ProductBarcodes
                 WHERE Barcode = NEW.Barcode COLLATE NOCASE AND ProductId <> NEW.Id)
        BEGIN
            SELECT RAISE(ABORT, 'Barcode already belongs to a product');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_Products_BarcodeNotTakenOnUpdate
        BEFORE UPDATE OF Barcode ON Products
        WHEN NEW.Barcode IS NOT NULL AND NEW.Barcode <> ''
             AND EXISTS (
                 SELECT 1 FROM ProductBarcodes
                 WHERE Barcode = NEW.Barcode COLLATE NOCASE AND ProductId <> NEW.Id)
        BEGIN
            SELECT RAISE(ABORT, 'Barcode already belongs to a product');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_ProductBarcodes_NotTaken
        BEFORE INSERT ON ProductBarcodes
        WHEN EXISTS (
            SELECT 1 FROM Products
            WHERE Barcode = NEW.Barcode COLLATE NOCASE AND Id <> NEW.ProductId)
        BEGIN
            SELECT RAISE(ABORT, 'Barcode already belongs to a product');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_ProductBarcodes_NotTakenOnUpdate
        BEFORE UPDATE OF Barcode, ProductId ON ProductBarcodes
        WHEN EXISTS (
            SELECT 1 FROM Products
            WHERE Barcode = NEW.Barcode COLLATE NOCASE AND Id <> NEW.ProductId)
        BEGIN
            SELECT RAISE(ABORT, 'Barcode already belongs to a product');
        END;
        """;
}
