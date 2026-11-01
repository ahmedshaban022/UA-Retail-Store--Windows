using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDiscountShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InvoiceDiscountShare",
                table: "SalesInvoiceLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceDiscountShare",
                table: "SalesInvoiceLines");
        }
    }
}
