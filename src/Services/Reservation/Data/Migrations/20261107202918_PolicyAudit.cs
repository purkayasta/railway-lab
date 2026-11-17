using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservation.Data.Migrations
{
    /// <inheritdoc />
    public partial class PolicyAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "reservation",
                table: "Policies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                schema: "reservation",
                table: "Policies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PolicyAudits",
                schema: "reservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValue = table.Column<int>(type: "int", nullable: false),
                    NewValue = table.Column<int>(type: "int", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyAudits", x => x.Id);
                });

            migrationBuilder.UpdateData(
                schema: "reservation",
                table: "Policies",
                keyColumn: "Key",
                keyValue: "HoldMinutes",
                columns: new[] { "UpdatedAtUtc", "UpdatedBy" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                schema: "reservation",
                table: "Policies",
                keyColumn: "Key",
                keyValue: "MaxSeatsPerUserPerTrip",
                columns: new[] { "UpdatedAtUtc", "UpdatedBy" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyAudits",
                schema: "reservation");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "reservation",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                schema: "reservation",
                table: "Policies");
        }
    }
}
