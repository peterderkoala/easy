using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class ThirdExaminerInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "third_examiner_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    examiner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_third_examiner_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_third_examiner_invitations_examiners_examiner_id",
                        column: x => x.examiner_id,
                        principalTable: "examiners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_third_examiner_invitations_examiners_invited_by",
                        column: x => x.invited_by,
                        principalTable: "examiners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_third_examiner_invitations_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_third_examiner_invitations_examiner_id",
                table: "third_examiner_invitations",
                column: "examiner_id");

            migrationBuilder.CreateIndex(
                name: "ix_third_examiner_invitations_invited_by",
                table: "third_examiner_invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ix_third_examiner_invitations_participant_id",
                table: "third_examiner_invitations",
                column: "participant_id",
                unique: true,
                filter: "status <> 'Declined'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "third_examiner_invitations");
        }
    }
}
