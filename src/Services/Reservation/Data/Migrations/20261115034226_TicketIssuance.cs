using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class TicketIssuance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeatCount",
                schema: "reservation",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Tickets",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    TripSeatId = table.Column<long>(type: "bigint", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalSchema: "reservation",
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Tickets_Seats_TripSeatId",
                        column: x => x.TripSeatId,
                        principalSchema: "reservation",
                        principalTable: "Seats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_BookingId",
                schema: "reservation",
                table: "Tickets",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketNumber",
                schema: "reservation",
                table: "Tickets",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TripSeatId",
                schema: "reservation",
                table: "Tickets",
                column: "TripSeatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tickets",
                schema: "reservation");

            migrationBuilder.DropColumn(
                name: "SeatCount",
                schema: "reservation",
                table: "Bookings");
        }
    }
}
