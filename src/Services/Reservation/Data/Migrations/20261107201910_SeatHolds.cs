using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeatHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holds",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripSeatId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HoldGroupPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExtendedOnce = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holds_Seats_TripSeatId",
                        column: x => x.TripSeatId,
                        principalSchema: "reservation",
                        principalTable: "Seats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                schema: "reservation",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.Key);
                });

            migrationBuilder.InsertData(
                schema: "reservation",
                table: "Policies",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "HoldMinutes", 8 },
                    { "MaxSeatsPerUserPerTrip", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holds_HoldGroupPublicId",
                schema: "reservation",
                table: "Holds",
                column: "HoldGroupPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_Holds_TripSeatId",
                schema: "reservation",
                table: "Holds",
                column: "TripSeatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holds",
                schema: "reservation");

            migrationBuilder.DropTable(
                name: "Policies",
                schema: "reservation");
        }
    }
}
