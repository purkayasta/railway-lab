using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reservation");

            migrationBuilder.CreateTable(
                name: "Trips",
                schema: "reservation",
                columns: table => new
                {
                    TripPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.TripPublicId);
                });

            migrationBuilder.CreateTable(
                name: "Seats",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeatPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HoldExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seats", x => x.Id);
                    table.CheckConstraint("CK_TripSeat_Status", "[Status] IN ('Available', 'Held', 'Sold')");
                    table.ForeignKey(
                        name: "FK_Seats_Trips_TripPublicId",
                        column: x => x.TripPublicId,
                        principalSchema: "reservation",
                        principalTable: "Trips",
                        principalColumn: "TripPublicId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seats_TripPublicId_SeatPublicId",
                schema: "reservation",
                table: "Seats",
                columns: new[] { "TripPublicId", "SeatPublicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Seats",
                schema: "reservation");

            migrationBuilder.DropTable(
                name: "Trips",
                schema: "reservation");
        }
    }
}
