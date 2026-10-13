using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class OralSessionUniquePerParticipant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_oral_sessions_participant_id",
                table: "oral_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_oral_sessions_participant_id",
                table: "oral_sessions",
                column: "participant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_oral_sessions_participant_id",
                table: "oral_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_oral_sessions_participant_id",
                table: "oral_sessions",
                column: "participant_id");
        }
    }
}
