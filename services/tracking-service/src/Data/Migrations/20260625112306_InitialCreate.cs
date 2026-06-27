using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nutrition_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FoodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Calories = table.Column<int>(type: "integer", nullable: false),
                    ProteinG = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CarbsG = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    FatG = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nutrition_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workout_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workout_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "set_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    RepsCompleted = table.Column<int>(type: "integer", nullable: false),
                    WeightUsed = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_set_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_set_logs_workout_logs_WorkoutLogId",
                        column: x => x.WorkoutLogId,
                        principalTable: "workout_logs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_nutrition_entries_ClientId",
                table: "nutrition_entries",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_nutrition_entries_ClientId_LoggedAt",
                table: "nutrition_entries",
                columns: new[] { "ClientId", "LoggedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_set_logs_WorkoutLogId",
                table: "set_logs",
                column: "WorkoutLogId");

            migrationBuilder.CreateIndex(
                name: "IX_workout_logs_ClientId",
                table: "workout_logs",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_workout_logs_WorkoutId",
                table: "workout_logs",
                column: "WorkoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nutrition_entries");

            migrationBuilder.DropTable(
                name: "set_logs");

            migrationBuilder.DropTable(
                name: "workout_logs");
        }
    }
}
