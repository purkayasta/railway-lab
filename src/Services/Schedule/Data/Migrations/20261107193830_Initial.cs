using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Schedule.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "schedule");

            migrationBuilder.CreateTable(
                name: "Classes",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseFare = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trains",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RouteId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trains_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "schedule",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stops",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OffsetMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalSchema: "schedule",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stops_Stations_StationId",
                        column: x => x.StationId,
                        principalSchema: "schedule",
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrainId = table.Column<long>(type: "bigint", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DepartureUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArrivalUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_Trains_TrainId",
                        column: x => x.TrainId,
                        principalSchema: "schedule",
                        principalTable: "Trains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Coaches",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClassId = table.Column<long>(type: "bigint", nullable: false),
                    SeatCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coaches_Classes_ClassId",
                        column: x => x.ClassId,
                        principalSchema: "schedule",
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Coaches_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "schedule",
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seats",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoachId = table.Column<long>(type: "bigint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Row = table.Column<int>(type: "int", nullable: false),
                    Column = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seats_Coaches_CoachId",
                        column: x => x.CoachId,
                        principalSchema: "schedule",
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Coaches_ClassId",
                schema: "schedule",
                table: "Coaches",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Coaches_TripId",
                schema: "schedule",
                table: "Coaches",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Seats_CoachId",
                schema: "schedule",
                table: "Seats",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_Seats_PublicId",
                schema: "schedule",
                table: "Seats",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Code",
                schema: "schedule",
                table: "Stations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stops_RouteId_Sequence",
                schema: "schedule",
                table: "Stops",
                columns: new[] { "RouteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stops_StationId",
                schema: "schedule",
                table: "Stops",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trains_RouteId",
                schema: "schedule",
                table: "Trains",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_PublicId",
                schema: "schedule",
                table: "Trips",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TrainId_ServiceDate",
                schema: "schedule",
                table: "Trips",
                columns: new[] { "TrainId", "ServiceDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Seats",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Stops",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Coaches",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Stations",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Classes",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Trips",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Trains",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "Routes",
                schema: "schedule");
        }
    }
}
