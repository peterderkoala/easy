using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class GradingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing cycles get the default weights (S06).
            migrationBuilder.AddColumn<int>(
                name: "weight1",
                table: "cycles",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "weight2",
                table: "cycles",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "weight3",
                table: "cycles",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "weight4",
                table: "cycles",
                type: "integer",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "weight5",
                table: "cycles",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "config_entries",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_entries", x => x.key);
                });

            // Defaults (S06, issue 39). Literal keys: a migration must not change with the code constants.
            migrationBuilder.InsertData(
                table: "config_entries",
                columns: ["key", "value"],
                values: new object[,]
                {
                    { "grading.deviation.section-points", "4" },
                    { "grading.deviation.total-points", "10" },
                    { "grading.default-weights", "15/10/40/25/10" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "config_entries");

            migrationBuilder.DropColumn(
                name: "weight1",
                table: "cycles");

            migrationBuilder.DropColumn(
                name: "weight2",
                table: "cycles");

            migrationBuilder.DropColumn(
                name: "weight3",
                table: "cycles");

            migrationBuilder.DropColumn(
                name: "weight4",
                table: "cycles");

            migrationBuilder.DropColumn(
                name: "weight5",
                table: "cycles");
        }
    }
}
