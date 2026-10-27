using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferCredit.Data.Migrations
{
    /// <summary>
    /// DemoUsers becomes the Users sign-in table. Renamed in place (not dropped) so existing accounts and the
    /// actor ids in the audit trail keep their meaning; existing accounts start without a password.
    /// </summary>
    public partial class RoleBasedSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_DemoUsers", table: "DemoUsers");
            migrationBuilder.RenameTable(name: "DemoUsers", newName: "Users");
            migrationBuilder.AddPrimaryKey(name: "PK_Users", table: "Users", column: "Id");

            migrationBuilder.Sql("UPDATE [Users] SET [Email] = LOWER([Email]) WHERE [Email] IS NOT NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedSignInCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSignInAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            // Every existing row gets its own random stamp.
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_Email", table: "Users");
            migrationBuilder.DropColumn(name: "PasswordHash", table: "Users");
            migrationBuilder.DropColumn(name: "IsActive", table: "Users");
            migrationBuilder.DropColumn(name: "MustChangePassword", table: "Users");
            migrationBuilder.DropColumn(name: "FailedSignInCount", table: "Users");
            migrationBuilder.DropColumn(name: "LockoutEnd", table: "Users");
            migrationBuilder.DropColumn(name: "LastSignInAt", table: "Users");
            migrationBuilder.DropColumn(name: "SecurityStamp", table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.DropPrimaryKey(name: "PK_Users", table: "Users");
            migrationBuilder.RenameTable(name: "Users", newName: "DemoUsers");
            migrationBuilder.AddPrimaryKey(name: "PK_DemoUsers", table: "DemoUsers", column: "Id");
        }
    }
}
