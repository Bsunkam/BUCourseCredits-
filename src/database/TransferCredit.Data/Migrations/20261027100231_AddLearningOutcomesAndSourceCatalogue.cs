using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferCredit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningOutcomesAndSourceCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LearningOutcomes",
                table: "SourceCourses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningOutcomes",
                table: "BUCourses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourceCatalogueCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Credits = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LearningOutcomes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCatalogueCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceCatalogueCourses_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCatalogueCourses_InstitutionId_Code",
                table: "SourceCatalogueCourses",
                columns: new[] { "InstitutionId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceCatalogueCourses");

            migrationBuilder.DropColumn(
                name: "LearningOutcomes",
                table: "SourceCourses");

            migrationBuilder.DropColumn(
                name: "LearningOutcomes",
                table: "BUCourses");
        }
    }
}
