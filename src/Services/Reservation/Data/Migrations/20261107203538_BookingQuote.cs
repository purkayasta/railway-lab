using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Destination",
                schema: "reservation",
                table: "Holds",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "reservation",
                table: "Holds",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                schema: "reservation",
                table: "Holds",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Bookings",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HoldGroupPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PassengerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BookedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_HoldGroupPublicId",
                schema: "reservation",
                table: "Bookings",
                column: "HoldGroupPublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PublicId",
                schema: "reservation",
                table: "Bookings",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookings",
                schema: "reservation");

            migrationBuilder.DropColumn(
                name: "Destination",
                schema: "reservation",
                table: "Holds");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "reservation",
                table: "Holds");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                schema: "reservation",
                table: "Holds");
        }
    }
}
