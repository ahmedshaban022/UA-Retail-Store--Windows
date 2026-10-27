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
        CREATE TRIGGER IF NOT EXISTS trg_SalesInvoiceLines_NoUpdate
        BEFORE UPDATE ON SalesInvoiceLines
        WHEN (SELECT Status FROM SalesInvoices WHERE Id = OLD.SalesInvoiceId) = 1
        BEGIN
            SELECT RAISE(ABORT, 'Posted invoice lines are immutable');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_SalesInvoiceLines_NoDelete
        BEFORE DELETE ON SalesInvoiceLines
        WHEN (SELECT Status FROM SalesInvoices WHERE Id = OLD.SalesInvoiceId) = 1
        BEGIN
            SELECT RAISE(ABORT, 'Posted invoice lines are immutable');
        END;

        -- A posted purchase line is frozen too.
        CREATE TRIGGER IF NOT EXISTS trg_PurchaseLines_NoUpdate
        BEFORE UPDATE ON PurchaseLines
        WHEN (SELECT Status FROM PurchaseDocuments WHERE Id = OLD.PurchaseDocumentId) = 1
        BEGIN
            SELECT RAISE(ABORT, 'Posted purchase lines are immutable');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_PurchaseLines_NoDelete
        BEFORE DELETE ON PurchaseLines
        WHEN (SELECT Status FROM PurchaseDocuments WHERE Id = OLD.PurchaseDocumentId) = 1
        BEGIN
            SELECT RAISE(ABORT, 'Posted purchase lines are immutable');
        END;
        """;
}
