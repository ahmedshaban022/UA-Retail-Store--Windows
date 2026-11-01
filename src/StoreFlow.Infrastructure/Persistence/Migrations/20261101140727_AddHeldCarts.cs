using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeldCarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HeldCarts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    HeldUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldCarts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeldCarts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HeldCarts_HeldUtc",
                table: "HeldCarts",
                column: "HeldUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HeldCarts_Number",
                table: "HeldCarts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeldCarts_UserId",
                table: "HeldCarts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeldCarts");
        }
    }
}
