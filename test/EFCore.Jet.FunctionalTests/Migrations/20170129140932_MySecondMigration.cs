using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EntityFramework.Jet.FunctionalTests.Migrations
{
    public partial class MySecondMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Posts_BlogId",
                table: "Posts",
                newName: "NewIndex");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "NewIndex",
                table: "Posts",
                newName: "IX_Posts_BlogId");
        }
    }
}
