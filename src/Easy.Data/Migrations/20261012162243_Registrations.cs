using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class Registrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registrations",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    examiner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => new { x.participant_id, x.examiner_id });
                    table.CheckConstraint("ck_registrations_slot", "slot IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_registrations_examiners_examiner_id",
                        column: x => x.examiner_id,
                        principalTable: "examiners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_registrations_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_examiner_id",
                table: "registrations",
                column: "examiner_id");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_participant_id_slot",
                table: "registrations",
                columns: new[] { "participant_id", "slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registrations");
        }
    }
}
