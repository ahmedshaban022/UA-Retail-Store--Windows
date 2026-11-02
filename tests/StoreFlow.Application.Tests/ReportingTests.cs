using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Reporting;
using StoreFlow.Domain.Money;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The money the reports are built from, worked out by hand.
/// </summary>
/// <remarks>
/// The reports quote every figure without the output tax, because the tax is the government's money.
/// These tests say so with numbers rather than with words.
/// </remarks>
public sealed class ReportingTests
{
    [Fact]
    public void The_net_sales_of_a_sale_are_what_it_took_without_the_tax()
    {
        // Three units at a hundred, no tax: the day took three hundred.
        var sale = new SaleAmounts(new Money(300m), Money.Zero, Money.Zero);

        ReportRules.NetSales(sale).ShouldBe(new Money(300m));
        ReportRules.GrossSales(sale).ShouldBe(new Money(300m));
    }

    [Fact]
    public void The_tax_is_taken_off_the_net_sales_and_shown_on_its_own()
    {
        // Three units at a hundred and ten per cent tax: the customer paid three hundred and thirty,
        // of which thirty is the government's.
        var sale = new SaleAmounts(new Money(330m), Money.Zero, new Money(30m));

        ReportRules.NetSales(sale).ShouldBe(new Money(300m));
        ReportRules.GrossSales(sale).ShouldBe(new Money(300m));
    }

    [Fact]
    public void A_discount_is_added_back_to_find_what_the_goods_were_worth()
    {
        // Three units at a hundred with fifty off: the goods were worth three hundred and the
        // customer paid two hundred and fifty.
        var sale = new SaleAmounts(new Money(250m), new Money(50m), Money.Zero);

        ReportRules.NetSales(sale).ShouldBe(new Money(250m));
        ReportRules.GrossSales(sale).ShouldBe(new Money(300m));
    }

    [Fact]
    public void A_line_carries_its_own_discount_and_its_share_of_the_whole_sale_one()
    {
        // A line for a hundred, ten off on the line and five of the whole-sale discount spread onto
        // it: the customer paid eighty-five for goods worth a hundred.
        var line = new LineAmounts(
            new Money(85m),
            new Money(10m),
            new Money(5m),
            Money.Zero,
            new Money(40m),
            2,
            0);

        ReportRules.LineDiscount(line).ShouldBe(new Money(15m));
        ReportRules.LineNetRevenue(line).ShouldBe(new Money(85m));
        ReportRules.LineGoodsValue(line).ShouldBe(new Money(100m));
        ReportRules.LineCostOfGoodsSold(line).ShouldBe(new Money(80m));
        ReportRules.LineGrossProfit(line).ShouldBe(new Money(5m));
    }

    [Fact]
    public void A_return_takes_the_profit_down_by_the_refund_less_the_cost()
    {
        // One unit sold at a hundred that cost fifty comes back: the refund is a hundred and the
        // unit goes back on the shelf at fifty, so the profit falls by fifty.
        var line = new ReturnLineAmounts(new Money(100m), new Money(50m), 1, 0);

        ReportRules.ReturnCostOfGoodsSold(line).ShouldBe(new Money(50m));
        ReportRules.ReturnProfitImpact(line).ShouldBe(new Money(50m));
    }

    [Fact]
    public void A_refund_is_read_without_the_tax_that_came_back_with_it()
    {
        var refund = new ReturnAmounts(new Money(110m), new Money(10m));

        ReportRules.NetRefund(refund).ShouldBe(new Money(100m));
    }

    [Fact]
    public void A_scaled_quantity_is_read_in_the_unit_the_shop_thinks_in()
    {
        ReportRules.Quantity(1250, 2).ShouldBe(12.5m);
        ReportRules.Quantity(3, 0).ShouldBe(3m);
        ReportRules.Quantity(1250, 3).ShouldBe(1.25m);
    }

    [Fact]
    public void An_amount_is_shared_out_in_proportion_and_nothing_is_lost()
    {
        // A hundred shared across three parts of one, one and two: the parts add up to the hundred.
        IReadOnlyList<Money> shares = ReportRules.Split(
            new Money(100m),
            [new Money(10m), new Money(10m), new Money(20m)]);

        shares.Count.ShouldBe(3);
        shares[0].ShouldBe(new Money(25m));
        shares[1].ShouldBe(new Money(25m));
        shares[2].ShouldBe(new Money(50m));
        ReportRules.Sum(shares).ShouldBe(new Money(100m));
    }

    [Fact]
    public void An_amount_is_divided_and_rounded_the_way_money_is_rounded()
    {
        ReportRules.Divide(new Money(100m), 3).ShouldBe(new Money(33.33m));
        ReportRules.Divide(new Money(90m), 3).ShouldBe(new Money(30m));
    }

    [Fact]
    public void A_csv_file_written_with_a_byte_order_mark_reads_back_without_one()
    {
        // The file opens correctly in Excel because of the byte-order mark, and the reader takes the
        // mark off again, so a round trip never doubles it.
        var table = new ExportTable(
            [new ExportColumn("date", "Date"), new ExportColumn("amount", "Amount")],
            [[new DateOnly(2026, 1, 1), new Money(300m)]]);

        string path = Path.Combine(Path.GetTempPath(), $"storeflow-csv-{Guid.NewGuid():N}.csv");

        try
        {
            byte[] bytes = new byte[CsvWriter.ByteOrderMark.Length + System.Text.Encoding.UTF8.GetBytes(CsvWriter.Write(table)).Length];

            CsvWriter.ByteOrderMark.CopyTo(bytes, 0);
            System.Text.Encoding.UTF8.GetBytes(CsvWriter.Write(table)).CopyTo(bytes, CsvWriter.ByteOrderMark.Length);

            File.WriteAllBytes(path, bytes);

            byte[] written = File.ReadAllBytes(path);

            written.Take(3).ShouldBe(CsvWriter.ByteOrderMark);

            IReadOnlyList<string[]> rows = CsvReader.ParseFile(path);

            rows.Count.ShouldBe(2);
            rows[0][0].ShouldBe("Date");
            rows[0][1].ShouldBe("Amount");
            rows[1][0].ShouldBe("2026-01-01");
            rows[1][1].ShouldBe("300.00");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
