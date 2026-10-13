using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayrollSheetEditing_LeaveBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalDaysRate",
                table: "PayrollItems",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveBalanceApplied",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveDays",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyAdjusted",
                table: "PayrollItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEditedAt",
                table: "PayrollItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEditedBy",
                table: "PayrollItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualOvertimeHours",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAbsenceDays",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveCarriedOver",
                table: "Employees",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalDaysRate",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveBalanceApplied",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "IsManuallyAdjusted",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "LastEditedBy",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "ManualOvertimeHours",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "OriginalAbsenceDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveCarriedOver",
                table: "Employees");
        }
    }
}
