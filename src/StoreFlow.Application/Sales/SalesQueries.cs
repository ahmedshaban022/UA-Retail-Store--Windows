using StoreFlow.Application.Common;

namespace StoreFlow.Application.Sales;

/// <summary>
/// The reads the sales register needs: the documents it lists, the detail of one invoice, and the
/// cashiers it can be filtered by.
/// </summary>

/// <summary>The filter the register is showing.</summary>
/// <param name="Search">Free text matched against the document number and the cashier's name.</param>
/// <param name="From">The first business date to show.</param>
/// <param name="To">The last business date to show.</param>
/// <param name="CashierId">The cashier to show, or null for everybody.</param>
public sealed record SalesFilter(
    string Search,
    DateOnly? From = null,
    DateOnly? To = null,
    int? CashierId = null);

/// <summary>Lists the sales documents the filter matches, newest first.</summary>
/// <param name="Filter">The filter to apply.</param>
public sealed record ListSalesRegisterQuery(SalesFilter Filter)
    : IQuery<Result<IReadOnlyList<SalesRegisterRow>>>;

/// <summary>Reads one invoice, with its lines and the returns against it.</summary>
/// <param name="InvoiceId">The invoice to read.</param>
public sealed record GetSalesInvoiceQuery(int InvoiceId)
    : IQuery<Result<SalesInvoiceDetails>>;

/// <summary>Reads one return, with the lines that came back.</summary>
/// <param name="ReturnId">The return to read.</param>
public sealed record GetSalesReturnQuery(int ReturnId)
    : IQuery<Result<SalesReturnDetails>>;

/// <summary>Lists the cashiers the register can be filtered by.</summary>
public sealed record ListSalesCashiersQuery() : IQuery<Result<IReadOnlyList<SalesCashierOption>>>;
