using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class Deviations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deviations",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sections = table.Column<string[]>(type: "text[]", nullable: false),
                    section_threshold = table.Column<int>(type: "integer", nullable: false),
                    total_threshold = table.Column<int>(type: "integer", nullable: false),
                    accepted_by = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    found_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deviations", x => x.participant_id);
                    table.ForeignKey(
                        name: "fk_deviations_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deviations");
        }
    }
}
