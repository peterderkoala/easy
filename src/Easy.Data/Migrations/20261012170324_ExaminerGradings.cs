using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExaminerGradings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entity_key",
                table: "audit_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "examiner_gradings",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    examiner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    pages = table.Column<int?[]>(type: "integer[]", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_examiner_gradings", x => new { x.participant_id, x.examiner_id });
                    table.ForeignKey(
                        name: "fk_examiner_gradings_examiners_examiner_id",
                        column: x => x.examiner_id,
                        principalTable: "examiners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_examiner_gradings_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "criterion_gradings",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    examiner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criterion_key = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: true),
                    remark = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_criterion_gradings", x => new { x.participant_id, x.examiner_id, x.criterion_key });
                    table.ForeignKey(
                        name: "fk_criterion_gradings_examiner_gradings_participant_id_examine",
                        columns: x => new { x.participant_id, x.examiner_id },
                        principalTable: "examiner_gradings",
                        principalColumns: new[] { "participant_id", "examiner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_examiner_gradings_examiner_id",
                table: "examiner_gradings",
                column: "examiner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "criterion_gradings");

            migrationBuilder.DropTable(
                name: "examiner_gradings");

            migrationBuilder.DropColumn(
                name: "entity_key",
                table: "audit_entries");
        }
    }
}
