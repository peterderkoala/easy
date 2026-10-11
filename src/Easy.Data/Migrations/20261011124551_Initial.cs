using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Easy.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity = table.Column<string>(type: "text", nullable: false),
                    field = table.Column<string>(type: "text", nullable: false),
                    old = table.Column<string>(type: "text", nullable: true),
                    @new = table.Column<string>(name: "new", type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    documentation_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    documentation_deadline = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "examiners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_examiners", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_no = table.Column<string>(type: "text", nullable: false),
                    csv_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    name = table.Column<byte[]>(type: "bytea", nullable: false),
                    project_title = table.Column<byte[]>(type: "bytea", nullable: false),
                    csv_id_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    wrapped_key = table.Column<byte[]>(type: "bytea", nullable: true),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    key_forgotten = table.Column<bool>(type: "boolean", nullable: false),
                    application_status = table.Column<string>(type: "text", nullable: true),
                    written_status = table.Column<string>(type: "text", nullable: true),
                    documentation_status = table.Column<string>(type: "text", nullable: true),
                    oral_status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_participants_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "grades",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grades", x => new { x.participant_id, x.source });
                    table.CheckConstraint("ck_grades_value_non_negative", "value >= 0");
                    table.ForeignKey(
                        name: "fk_grades_examiners_updated_by",
                        column: x => x.updated_by,
                        principalTable: "examiners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_grades_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oral_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oral_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_oral_sessions_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_participant_id",
                table: "documents",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_examiners_subject",
                table: "examiners",
                column: "subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grades_updated_by",
                table: "grades",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_oral_sessions_participant_id",
                table: "oral_sessions",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_participants_cycle_id_csv_id_hash",
                table: "participants",
                columns: new[] { "cycle_id", "csv_id_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_participants_cycle_id_display_no",
                table: "participants",
                columns: new[] { "cycle_id", "display_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "grades");

            migrationBuilder.DropTable(
                name: "oral_sessions");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "examiners");

            migrationBuilder.DropTable(
                name: "participants");

            migrationBuilder.DropTable(
                name: "cycles");
        }
    }
}
