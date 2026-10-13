using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrUpgrade_Employees_Shifts_Attendance_Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_DeviceEmployeeCode",
                table: "Employees");

            // EF خمّن الاسم الجديد خطأً: العمود القديم GraceMinutes هو فترة سماح الدخول،
            // لذلك يُعاد تسميته إلى EntryGraceMinutes للحفاظ على القيم المخزّنة (30 دقيقة).
            migrationBuilder.RenameColumn(
                name: "GraceMinutes",
                table: "Shifts",
                newName: "EntryGraceMinutes");

            migrationBuilder.RenameColumn(
                name: "CalculatedOvertimeMinutes",
                table: "DailyAttendances",
                newName: "RawOvertimeMinutes");

            migrationBuilder.AddColumn<int>(
                name: "ShiftNumber",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExitGraceMinutes",
                table: "Shifts",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardHours",
                table: "Shifts",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 8m);

            migrationBuilder.AddColumn<string>(
                name: "WorkingDays",
                table: "Shifts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "PayrollRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "PayrollRuns",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                table: "PayrollRuns",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedAt",
                table: "PayrollRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "PayrollRuns",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalBonuses",
                table: "PayrollRuns",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalGross",
                table: "PayrollRuns",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AbsenceDays",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AbsenceDeduction",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalDaysAmount",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalDaysCount",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Allowances",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceDays",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Bonuses",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CalculationDetails",
                table: "PayrollItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyRate",
                table: "PayrollItems",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DailyRateBasisDays",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "PayrollItems",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossEarnings",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRate",
                table: "PayrollItems",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "JobTitleName",
                table: "PayrollItems",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidLeaveDays",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidOvertimeHours",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaidRestDays",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RawOvertimeHours",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RawOvertimeMinutes",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDeductions",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDays",
                table: "PayrollItems",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDeduction",
                table: "PayrollItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WorkHoursPerDay",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CreatedFromAbsence",
                table: "LeaveRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DaysCount",
                table: "LeaveRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PayrollItemId",
                table: "LeaveRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkingDays",
                table: "LeaveRequests",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveBalance",
                table: "Employees",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualLeaveEntitlement",
                table: "Employees",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ContactInfo",
                table: "Employees",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyRate",
                table: "Employees",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmploymentStatus",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "JobTitleId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardWorkHours",
                table: "Employees",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DetectionSource",
                table: "DailyAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HolidayId",
                table: "DailyAttendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHoliday",
                table: "DailyAttendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWeeklyRestDay",
                table: "DailyAttendances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LeaveRequestId",
                table: "DailyAttendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NeedsReviewReason",
                table: "DailyAttendances",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaidOvertimeMinutes",
                table: "DailyAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DailyAttendanceId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_DailyAttendances_DailyAttendanceId",
                        column: x => x.DailyAttendanceId,
                        principalTable: "DailyAttendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ManagerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Days = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    DailyRateOverride = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ReferenceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayrollItemId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_PayrollItems_PayrollItemId",
                        column: x => x.PayrollItemId,
                        principalTable: "PayrollItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublicHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HolidayDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    AllowWorkWithExtraPay = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobTitles_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_PayrollItemId",
                table: "LeaveRequests",
                column: "PayrollItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Status",
                table: "LeaveRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DeviceEmployeeCode",
                table: "Employees",
                column: "DeviceEmployeeCode",
                unique: true,
                filter: "[DeviceEmployeeCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_IsActive",
                table: "Employees",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_JobTitleId",
                table: "Employees",
                column: "JobTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendances_HolidayId",
                table: "DailyAttendances",
                column: "HolidayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendances_LeaveRequestId",
                table: "DailyAttendances",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyAttendances_Status",
                table: "DailyAttendances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_DailyAttendanceId",
                table: "AttendanceCorrections",
                column: "DailyAttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_EmployeeId_WorkDate",
                table: "AttendanceCorrections",
                columns: new[] { "EmployeeId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTitles_DepartmentId",
                table: "JobTitles",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTitles_Name",
                table: "JobTitles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_EmployeeId_Year_Month",
                table: "PayrollAdjustments",
                columns: new[] { "EmployeeId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_PayrollItemId",
                table: "PayrollAdjustments",
                column: "PayrollItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicHolidays_HolidayDate",
                table: "PublicHolidays",
                column: "HolidayDate");

            // NoAction: لأن SQL Server يرفض مسارين متسلسلين إلى DailyAttendances
            // (Employees → DailyAttendances) و(Employees → LeaveRequests → DailyAttendances).
            migrationBuilder.AddForeignKey(
                name: "FK_DailyAttendances_LeaveRequests_LeaveRequestId",
                table: "DailyAttendances",
                column: "LeaveRequestId",
                principalTable: "LeaveRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyAttendances_PublicHolidays_HolidayId",
                table: "DailyAttendances",
                column: "HolidayId",
                principalTable: "PublicHolidays",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_JobTitles_JobTitleId",
                table: "Employees",
                column: "JobTitleId",
                principalTable: "JobTitles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_PayrollItems_PayrollItemId",
                table: "LeaveRequests",
                column: "PayrollItemId",
                principalTable: "PayrollItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // ضبط القيم الافتراضية للورديات الثلاث المُهيَّأة سابقًا لتطابق سياسة الشركة المعتمدة:
            // الوردية الأولى 07:00 - 15:00 بسماح 30 دقيقة دخولًا وخروجًا و8 ساعات عمل.
            // لا تُلمس أي وردية أضافها المستخدم بأسماء أخرى.
            migrationBuilder.Sql(@"
UPDATE [Shifts]
SET [EntryGraceMinutes] = 30,
    [ExitGraceMinutes] = 30,
    [StandardHours] = 8,
    [WorkingDays] = N'0,1,2,3,4,6',
    [ShiftNumber] = CASE [ShiftName]
        WHEN N'الوردية الأولى' THEN 1
        WHEN N'الوردية الثانية' THEN 2
        ELSE 3 END
WHERE [ShiftName] IN (N'الوردية الأولى', N'الوردية الثانية', N'الوردية الثالثة (ليلية)');
");
        }

        /// <inheritdoc />

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyAttendances_LeaveRequests_LeaveRequestId",
                table: "DailyAttendances");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyAttendances_PublicHolidays_HolidayId",
                table: "DailyAttendances");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_JobTitles_JobTitleId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_PayrollItems_PayrollItemId",
                table: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "JobTitles");

            migrationBuilder.DropTable(
                name: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PublicHolidays");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequests_PayrollItemId",
                table: "LeaveRequests");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequests_Status",
                table: "LeaveRequests");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DeviceEmployeeCode",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_IsActive",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_JobTitleId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendances_HolidayId",
                table: "DailyAttendances");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendances_LeaveRequestId",
                table: "DailyAttendances");

            migrationBuilder.DropIndex(
                name: "IX_DailyAttendances_Status",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "ShiftNumber",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ExitGraceMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "StandardHours",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "TotalBonuses",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "TotalGross",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "AbsenceDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AbsenceDeduction",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AdditionalDaysAmount",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AdditionalDaysCount",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "Allowances",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AttendanceDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "Bonuses",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "CalculationDetails",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "DailyRate",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "DailyRateBasisDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "GrossEarnings",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "HourlyRate",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "JobTitleName",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "PaidLeaveDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "PaidOvertimeHours",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "PaidRestDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "RawOvertimeHours",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "RawOvertimeMinutes",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "TotalDeductions",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDeduction",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "WorkHoursPerDay",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "CreatedFromAbsence",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "DaysCount",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "PayrollItemId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveBalance",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveEntitlement",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ContactInfo",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "DailyRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "JobTitleId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "StandardWorkHours",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "DetectionSource",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "HolidayId",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "IsHoliday",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "IsWeeklyRestDay",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "LeaveRequestId",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "NeedsReviewReason",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "PaidOvertimeMinutes",
                table: "DailyAttendances");

            migrationBuilder.RenameColumn(
                name: "EntryGraceMinutes",
                table: "Shifts",
                newName: "GraceMinutes");

            migrationBuilder.RenameColumn(
                name: "RawOvertimeMinutes",
                table: "DailyAttendances",
                newName: "CalculatedOvertimeMinutes");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DeviceEmployeeCode",
                table: "Employees",
                column: "DeviceEmployeeCode");
        }
    }
}
