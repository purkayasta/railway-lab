using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedSeatUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeatUpdates",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatUpdates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeatUpdates_CreatedAtUtc",
                schema: "reservation",
                table: "SeatUpdates",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeatUpdates",
                schema: "reservation");
        }
    }
}
