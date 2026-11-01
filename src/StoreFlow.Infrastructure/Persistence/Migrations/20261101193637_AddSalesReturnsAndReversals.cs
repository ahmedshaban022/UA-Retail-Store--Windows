using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReturnsAndReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RefundTax",
                table: "SalesReturns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ReversesDocumentId",
                table: "SalesInvoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SalesInvoiceId",
                table: "Payments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<bool>(
                name: "IsRefund",
                table: "Payments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SalesReturnId",
                table: "Payments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_ReversesDocumentId",
                table: "SalesInvoices",
                column: "ReversesDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SalesReturnId",
                table: "Payments",
                column: "SalesReturnId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_SingleOwner",
                table: "Payments",
                sql: "(\"SalesInvoiceId\" IS NOT NULL AND \"SalesReturnId\" IS NULL) OR (\"SalesInvoiceId\" IS NULL AND \"SalesReturnId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_SalesReturns_SalesReturnId",
                table: "Payments",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SalesInvoices_ReversesDocumentId",
                table: "SalesInvoices",
                column: "ReversesDocumentId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_SalesReturns_SalesReturnId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_SalesInvoices_ReversesDocumentId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_ReversesDocumentId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SalesReturnId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_SingleOwner",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundTax",
                table: "SalesReturns");

            migrationBuilder.DropColumn(
                name: "ReversesDocumentId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsRefund",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SalesReturnId",
                table: "Payments");

            migrationBuilder.AlterColumn<int>(
                name: "SalesInvoiceId",
                table: "Payments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
