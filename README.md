# نظام إدارة الموارد البشرية والحضور والرواتب (ASP.NET Core MVC)

نظام متكامل لإدارة الموظفين، واستيراد بصمات جهاز الحضور، وتصنيف الدخول/الخروج،
وحساب التأخير والإضافي والسلف والخصومات، وإصدار كشوف الرواتب الشهرية، وتصدير
تقارير Excel متعددة الأوراق.

---

## 1) التقنيات

| العنصر | التقنية |
|---|---|
| إطار العمل | ASP.NET Core MVC على **.NET 9** |
| ORM | Entity Framework Core 9 |
| قاعدة البيانات | **SQL Server بعيد** (`db73231.public.databaseasp.net`) — ويعمل أيضًا مع LocalDB / Express / Full |
| واجهة المستخدم | Bootstrap 5 **RTL** + واجهة عربية |
| تصدير Excel | ClosedXML |
| تصدير PDF | QuestPDF (مع خط عربي `Cairo.ttf`) |
| قراءة ملفات البصمة | ExcelDataReader (xls/xlsx) + قارئ مخصص لـ Excel 2003 XML و CSV و HTML |
| المصادقة | ASP.NET Core Identity مع أدوار (Admin / HR / Accountant / Viewer) |
| الاختبارات | xUnit (111 اختبارًا) |

## 2) هيكل المشروع

```
HrPayroll.sln
src/
  HrPayroll.Domain/          الكيانات + Enums + منطق الحساب النقي (بدون قاعدة بيانات)
    Enums/                   AttendanceStatus, PunchDirection, PayrollStatus ...
    Entities/                Employee, Department, JobTitle, Shift, EmployeeShiftAssignment,
                             RawPunch, AttendanceEvent, DailyAttendance, AttendanceCorrection,
                             PublicHoliday, LeaveRequest, Loan, Deduction, OvertimeApproval,
                             PayrollRun, PayrollItem, PayrollAdjustment, ImportBatch, AuditLog,
                             AppSetting, ApplicationUser
    Services/                AttendanceCalculator, PunchClassifier, PayrollCalculator,
                             ShiftDetector, PunchWorkDateResolver
    ValueObjects/            ShiftWindow (حساب الورديات الليلية)
  HrPayroll.Infrastructure/  EF Core + الخدمات + الاستيراد + التصدير
    Persistence/             AppDbContext + Configurations + Migrations
    Import/                  AttendanceFileParser, ColumnMapper, AttendanceImportService,
                             EmployeeImportService, EmployeeColumnMapper
    Services/                SettingsService, ShiftService, ShiftResolver,
                             AttendanceProcessingService, AttendanceCorrectionService,
                             LeaveService, PayrollService, ReportService,
                             ExcelExportService, PdfExportService, DashboardService, AuditService
    Seed/                    DbSeeder (الورديات والإعدادات والمستخدم المدير)
  HrPayroll.Web/             تطبيق MVC: Controllers + ViewModels + Views (RTL) + Program.cs
tests/
  HrPayroll.Tests/           اختبارات آلية لكل الحالات المطلوبة
```

## 3) المتطلبات المسبقة

**أ) لبناء المشروع مرة واحدة فقط:**

- .NET SDK 9.0 أو أحدث
- أداة `dotnet-ef` لإنشاء/تحديث الترحيلات (اختياري):
  ```bash
  dotnet tool install --global dotnet-ef --version 9.0.20
  ```

**ب) للتشغيل على جهاز Windows من CMD فقط:**

- Windows 64-bit — لا شيء غيره.
- **Visual Studio غير مطلوب** إطلاقًا؛ كل الأوامر عبر .NET CLI.
- **SQL Server محلي غير مطلوب**، وSQL Server Management Studio غير مطلوبة:
  قاعدة البيانات بعيدة على الاستضافة (القسم 4).
- **‏.NET Runtime غير مطلوب** على الجهاز المستهدف أيضًا، لأن النشر ذاتي الاحتواء
  (self-contained) يتضمن runtime .NET كاملًا.

## 4) إعداد Connection String

قاعدة البيانات **بعيدة** على استضافة `databaseasp.net` (SQL Server 2022)، وليست LocalDB
وليست SQL Server محليًا. البيانات الثابتة للاتصال:

```
Server=db73231.public.databaseasp.net;Database=db73231;User Id=db73231;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

**كلمة المرور لا تُكتب في `appsettings.json` ولا تُرفع إلى Git إطلاقًا.** الملف
المتتبَّع يحتوي على قالب بالقيمة `__SET_PASSWORD__`، والقيمة الحقيقية تُقرأ من أحد
المصادر التالية (الأخير له الأولوية على كل ما سبقه):

| # | المصدر | متى يُستخدم |
|---|---|---|
| 1 | `appsettings.json` | القالب الافتراضي فقط (بدون كلمة مرور) |
| 2 | `appsettings.Production.json` | **المستخدَم عند التشغيل على Windows** — غير مُتتبَّع في Git |
| 3 | User Secrets | `dotnet run` في بيئة Development على جهاز المطوّر |
| 4 | `ConnectionStrings__DefaultConnection` | متغيّر بيئة، يتقدّم على كل ما سبق |

### أ) للتشغيل على Windows (الأسلوب المعتمد)

أنشئ ملف `src/HrPayroll.Web/appsettings.Production.json` بجانب `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db73231.public.databaseasp.net;Database=db73231;User Id=db73231;Password=<كلمة المرور>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
  }
}
```

هذا الملف مُستثنى في `.gitignore` ولا يُرفع إلى المستودع. عند النشر (القسم 6) يُنسخ
تلقائيًا إلى مجلد `publish` بجانب `HrPayroll.Web.exe`، لذلك يعمل `run.bat` مباشرة.

> `run.bat` نفسه **لا يحتوي** أي كلمة مرور؛ يكتفي بالإشارة إلى مصدر الإعداد.

### ب) بديل بدون أي ملف: متغيّر بيئة

```bat
set "ConnectionStrings__DefaultConnection=Server=db73231.public.databaseasp.net;Database=db73231;User Id=db73231;Password=<كلمة المرور>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
run.bat
```

### ج) للتطوير عبر User Secrets

```bash
cd src/HrPayroll.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=db73231.public.databaseasp.net;Database=db73231;User Id=db73231;Password=<كلمة المرور>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
```

### د) بديل SQL Server محلي (اختياري)

- SQL Server Express:
  `Server=.\SQLEXPRESS;Database=HrPayrollDb;Trusted_Connection=True;TrustServerCertificate=True`
- مستخدم وكلمة مرور:
  `Server=localhost;Database=HrPayrollDb;User Id=sa;Password=YourPassword;TrustServerCertificate=True`

### تحقق من الفهم: ماذا يحدث إن لم تُضبط كلمة المرور؟

لا يُحاول التطبيق تنفيذ أي ترحيل، ويكتب في اللوج رسالة واضحة تذكر اسم المفتاح المطلوب
بدون كشف أي بيانات اعتماد:

> لم يُضبط Connection String بعد: القيمة الفعّالة ما زالت تحتوي على القالب `__SET_PASSWORD__`.
> اضبط `ConnectionStrings:DefaultConnection` في `appsettings.Production.json` أو في
> User Secrets أو في متغيّر البيئة `ConnectionStrings__DefaultConnection`.

## 5) تشغيل الترحيلات (Migrations)

الترحيل `InitialCreate` جاهز داخل `src/HrPayroll.Infrastructure/Persistence/Migrations`،
والتطبيق ينفّذ `Database.Migrate()` تلقائيًا عند الإقلاع. `Migrate()` **غير مدمّرة**:
تُطبّق الترحيلات غير المطبّقة فقط، ولا تُعيد إنشاء قاعدة قائمة، ولا تحذف أي جدول أو بيانات.

> **حالة قاعدة البيانات البعيدة عند إعداد هذا التحديث:** القاعدة `db73231` كانت
> **فارغة تمامًا (0 جداول، بلا `__EFMigrationsHistory`)** والمستخدم `db73231` يملك صلاحية
> `db_owner`، لذلك أنشأ تشغيل التطبيق مخطط `InitialCreate` (23 جدولًا) ثم بذر الأدوار
> والورديات والمستخدم المدير. لم يُحذف أي شيء ولم تُنشأ قاعدة جديدة.

لتعطيل أي تعديل تلقائي على المخطط — مثلًا بعد نقل قاعدة تحتوي بيانات جاهزة بحيث لا
تُطبَّق أي ترحيلات عند الإقلاع:

```json
"Database": { "ApplyMigrationsOnStartup": false }
```

في هذه الحالة يبدأ التطبيق بدون لمس المخطط، وتُطبَّق الترحيلات يدويًا عند الحاجة.
لعرض الترحيلات المطبّقة على القاعدة البعيدة (قراءة فقط):

```bash
dotnet ef migrations list -p src/HrPayroll.Infrastructure -s src/HrPayroll.Web
```

يدويًا:

```bash
dotnet ef database update -p src/HrPayroll.Infrastructure -s src/HrPayroll.Web

# إنشاء ترحيل جديد بعد أي تعديل على الكيانات:
dotnet ef migrations add <Name> -p src/HrPayroll.Infrastructure -s src/HrPayroll.Web -o Persistence/Migrations
```

## 6) التشغيل

### أ) على Windows من CMD فقط (بدون Visual Studio)

من مجلد المشروع، في `cmd.exe`:

```bat
publish.bat
publish\run.bat
```

- `publish.bat` ينفّذ
  `dotnet publish -c Release -r win-x64 --self-contained true -o publish` وينتج مجلدًا
  مستقلًا لا يحتاج تثبيت .NET Runtime على الجهاز المستهدف.
- `run.bat` (وهو نفسه `publish\run.bat`) يشغّل `HrPayroll.Web.exe`، ويطبع أي خطأ إقلاع،
  ويُبقي نافذة الطرفية مفتوحة عند الخروج بخطأ (`pause`) لقراءة الرسالة.
- العنوان الافتراضي `http://localhost:5084` (والاستماع على `0.0.0.0:5084` لقبول
  الاتصالات الخارجية)، ويُضبط بمتغيّر بيئة قبل التشغيل:

```bat
set "ASPNETCORE_URLS=http://localhost:5084"
run.bat
```

نشر يدوي بديل:

```bat
dotnet publish src\HrPayroll.Web\HrPayroll.Web.csproj -c Release -r win-x64 --self-contained true -o publish
```

> إن كان الجهاز سيملك .NET 9 Runtime مثبّتًا وتريد حجمًا أصغر، استخدم
> `--self-contained false` بدل `--self-contained true`، مع تثبيت .NET 9 Runtime
> (ASP.NET Core Runtime) على الجهاز.

### ب) للتطوير

```bash
dotnet restore
dotnet build
dotnet run --project src/HrPayroll.Web
```

ثم افتح العنوان الظاهر في الطرفية (مثال `https://localhost:7012`).

### ملاحظة: التشغيل على HTTP بدون شهادة

`run.bat` يضبط `ASPNETCORE_URLS=http://0.0.0.0:5084` أي HTTP فقط بدون شهادة تطوير.
التطبيق يستدعي `UseHttpsRedirection` لكنه لا يجد منفذ HTTPS مُهيّأ في بيئة `Production`
فيسجّل تحذيرًا ويتابع العمل على HTTP (حتى لا يُشترط تثبيت `dotnet dev-certs`).
هذا مناسب للاستخدام الداخلي/الشبكة المحلية، و**غير مناسب للإنترنت المفتوح**؛ للإنتاج
استخدم IIS أو Reverse Proxy مع شهادة صالحة، أو اضبط
`ASPNETCORE_URLS=https://+:443` مع شهادة مثبّتة في مخزن شهادات Windows.

### الحساب الافتراضي (يُنشأ تلقائيًا عند أول تشغيل)

| البريد | كلمة المرور | الدور |
|---|---|---|
| `admin@hr.local` | `Admin@12345` | Admin |

> غيّر كلمة المرور فورًا من شاشة المستخدمين. تُنشأ الأدوار الأربعة تلقائيًا:
> Admin / HR / Accountant / Viewer.
> الورديات الثلاث الافتراضية (07:00–15:00، 15:00–23:00، 23:00–07:00) تُنشأ تلقائيًا أيضًا.

## 7) استيراد تقرير البصمة

1. سجّل الدخول بحساب يملك صلاحية HR أو Admin.
2. القائمة → **استيراد البصمة**.
3. اختر ملف التقرير (`.xls` / `.xlsx` / `.csv`) واضغط **تحليل ومعاينة**.

كيف يعمل الاستيراد:

- **اكتشاف النوع من المحتوى لا من الامتداد**: يفحص البايتات الأولى ويحدّد
  `xlsx` (ZIP)، `BIFF8` (ملف Excel ثنائي قديم)، **Excel 2003 XML** (كثيرًا ما
  يصدّره جهاز البصمة بامتداد `.xls`)، جدول HTML، أو CSV. لكل نوع قارئه المناسب.
- **اكتشاف الأعمدة** تلقائيًا وربطها بالحقول الداخلية:
  `No.` → الرقم، `Device ID` → رقم الجهاز، `ID.` → كود الموظف، `Name` → الاسم،
  `Att.Type` → نوع البصمة، `Att.Time` → وقت البصمة (مع دعم أسماء عربية).
- **تحويل `Att.Time`** إلى تاريخ/وقت عبر صيغ متعددة وثقافات متعددة، مع دعم
  الأرقام التسلسلية لتاريخ Excel.
- **معاينة أول 20 سجلًا** قبل التأكيد مع تصنيف كل صف:
  ناجح / مكرر / غير صالح / موظف غير معروف.
- **منع التكرار** عبر بصمة `DedupHash` (الجهاز + الكود + الوقت + النوع) مع فهرس
  فريد في قاعدة البيانات، فيمنع التكرار داخل الملف وبين ملفات الشهر المختلفة.
- **الاحتفاظ بالبيانات الأصلية**: يُخزَّن النص الأصلي للوقت (`RawPunchTimeText`)
  والاسم الأصلي ورقم الصف، ولا تُعدَّل البصمة الخام.
- **تسجيل دفعة الاستيراد**: اسم الملف، الصيغة المكتشفة، وقت الاستيراد، المستخدم،
  وعدد السجلات (إجمالي/ناجح/مكرر/غير صالح/موظف غير معروف) مع معاملة (Transaction).

## 8) تصنيف الدخول والخروج

ثلاثة مستويات منفصلة كما هو مطلوب:

1. **RawPunches** — البصمات الخام كما وردت من الجهاز.
2. **AttendanceEvents** — البصمات المصنفة دخول/خروج (مرتبطة بالبصمة الخام).
3. **DailyAttendances** — ملخص الحضور النهائي لكل موظف ووردية.

قواعد التصنيف:

- إذا أرسل الجهاز **نوع حركة صريحًا** (`I/O`, `IN/OUT`, `دخول/خروج`) يُستخدم بعد
  التحقق من وقوعه داخل نافذة الوردية.
- إذا أرسل **نوع بصمة محايدًا** مثل `FP` فلا يُفترض أنه يحدد الاتجاه، وتُستخدم
  القواعد: تُقارن البصمات بنافذة الوردية (بدايتها، منتصفها، نهايتها) مع مراعاة
  السماح وسقف الإضافي.
- **أول وآخر بصمة لا تتحولان تلقائيًا** إلى دخول وخروج: يُشترط أن تقع الأولى في
  النصف الأول من الوردية والأخيرة في النصف الثاني.
- البصمة الواحدة فقط ⇒ حالة «بصمة خروج/دخول مفقودة» تحتاج مراجعة، **ولا يُخترع
  وقت خروج**.
- الحالات الغامضة (تعارض، بصمات داخلية إضافية) تُعلَّم للمراجعة اليدوية من شاشة
  **الحضور اليومي → مراجعة**.
- البصمات المكررة (نفس الوقت) تُحتسب مرة واحدة.

## 9) الورديات

- الوردية الأولى 07:00–15:00 (سماح 30 دقيقة)، الثانية 15:00–23:00، الثالثة
  23:00–07:00 من اليوم التالي (`IsOvernight`).
- الحقول: `ShiftName, StartTime, EndTime, GraceMinutes, IsOvernight,
  OvertimeMultiplier, OvertimeMinMinutes, IsActive`.
- الورديات المتغيرة: جدول `EmployeeShiftAssignments` يربط الموظف بورديته حسب
  فترة سريان (`EffectiveFrom` / `EffectiveTo`).

## 10) قواعد التأخير والإضافي

السماح (Grace): إذا حضر الموظف حتى نهاية السماح فالتأخير صفر، وإذا تجاوزه
**يُلغى السماح بالكامل** ويُحسب التأخير من بداية الوردية:

| الحضور (وردية 07:00، سماح دخول 30) | التأخير |
|---|---|
| 07:10 | 0 |
| 07:30 | 0 |
| 07:31 | 31 دقيقة |
| 08:15 | 75 دقيقة |

سماح الخروج متماثل مع سماح الدخول: انصراف 15:30 ⇒ إضافي 0، وانصراف 15:35 ⇒ 35 دقيقة،
وانصراف 16:00 ⇒ 60 دقيقة؛ أي أن الإضافي يُحسب من **وقت نهاية الوردية الرسمية دون خصم
السماح** وبدون قيم سالبة. وردية ليلية 23:00→07:00 وانصراف 08:00 من اليوم التالي
⇒ 60 دقيقة (معالجة انتقال التاريخ).

سماح الوردية الأولى **لا يُطبّق تلقائيًا** على بقية الورديات (كل وردية لها سماحها، ويوجد
سماح افتراضي عام في الإعدادات يُستخدم عند عدم ضبط سماح الوردية).

الاعتماد اليدوي للإضافي مطلوب افتراضيًا قبل إضافته للراتب، ويُحفظ المحسوب والمعتمد
في `OvertimeApprovals`.

## 11) شاشة الحضور اليومي

تعرض: الكود، الاسم، التاريخ، الوردية، وقت الدخول، وقت الخروج، التأخير بالدقائق،
ساعات الإضافي، حالة الحضور، حالة مراجعة البصمة، وملاحظات المسؤول.

حالات الحضور: حاضر، غائب، متأخر، بصمة دخول مفقودة، بصمة خروج مفقودة، إجازة،
يوم راحة، يحتاج مراجعة.

> **الغياب لا يُسجَّل نهائيًا** إلا بعد التأكد من استيراد بيانات اليوم (بمقارنة
> التاريخ بنطاق البصمات المستوردة للموظف) وبعد استثناء الإجازات المعتمدة وأيام
> الراحة الأسبوعية.

## 12) السلف والخصومات والإضافي المعتمد

- **السلف** (`Loans`): الموظف، التاريخ، المبلغ، شهر الخصم، عدد الأقساط، الملاحظات.
- **الخصومات والجزاءات** (`Deductions`): الموظف، التاريخ، المبلغ، السبب، الشهر،
  حالة الاعتماد.
- **اعتماد الإضافي** (`OvertimeApprovals`): توليد من الحضور المحسوب ثم الاعتماد
  اليدوي (مع الاحتفاظ بالمحسوب والمعتمد والمعامل).

منع الخصم المزدوج: عند إدراج سلفة/خصم/إضافي في كشف رواتب يُربط بـ `PayrollItemId`،
ولا يُدرج مرة أخرى في كشوف أخرى حتى يُلغى اعتماد الكشف (فيُفصل الارتباط لإتاحة
إعادة الاحتساب). أُضيف اختبار آلي لهذه الحالة.

## 13) نظام الرواتب

صافي الراتب = الراتب الأساسي + قيمة الإضافي + المكافآت والبدلات والأيام الإضافية −
خصم التأخير − خصم الغياب − الإجازات بدون أجر − السلف − الخصومات − أي خصومات أخرى معتمدة.

القيم الافتراضية (قابلة للتعديل من الإعدادات):

- قيمة الساعة = الراتب الأساسي ÷ 240 (30 يومًا × 8 ساعات).
- قيمة الإضافي = ساعات الإضافي المعتمدة × قيمة الساعة × معامل الإضافي (= 1 افتراضيًا).
- تُقرَّب القيم المالية إلى منزلتين عشريتين، والتقريب النهائي للإضافي مرة واحدة على إجمالي الشهر.

خصم الغياب وخصم الإجازة بدون أجر **مُفعَّلان افتراضيًا**
(`Payroll.AbsenceDeductionEnabled` و`Payroll.UnpaidLeaveDeductionEnabled`)، وسعر اليوم =
الأجر اليومي، ويمكن تعطيلهما من شاشة الإعدادات. التأمينات والضرائب لم تُنفَّذ كمكوّن
مستقل، ولا تُخصم أي قيمة غير مُفعَّلة صراحةً.

- لكل موظف سطر مستقل في كل شهر (`PayrollItem`).
- لكل إصدار سجل مستقل مع رقم إصدار وحالة
  (`Draft / Computed / Approved / Locked / Reopened / Cancelled`).
- **لا يمكن إعادة احتساب كشف معتمد أو مقفل** قبل إلغاء الاعتماد أو إعادة الفتح بسبب
  مُسجَّل بصلاحية Accountant/Admin، وكل خطوة تُكتب في `AuditLogs`.

## 14) التصدير (Excel و PDF)

زر **Excel** في شاشة **التقارير** بعد تحديد الفترة والقسم والموظف والوردية.
الملف الشامل (**ملف شامل**) يُنشئ مصنّفًا واحدًا يحوي الأوراق الثمانية التالية بنفس الترتيب:

1. `Raw Punches` — البصمات كما وردت من الجهاز: كود الجهاز، كود الموظف، الاسم، رقم الجهاز، نوع البصمة، وقت البصمة، النص الأصلي، رقم الصف، والموظف مسجل؟
2. `Entry` — البصمات المصنّفة دخولًا: كود الموظف، الاسم، التاريخ، الوقت، رقم الجهاز، **رقم السجل الأصلي** في `RawPunches`، حالة التصنيف، مصدر التصنيف، ملاحظة.
3. `Exit` — نفس أعمدة Entry للبصمات المصنّفة خروجًا.
4. `Daily Attendance` — ملخص اليوم لكل موظف: القسم، المسمى، الوردية، الدخول، الخروج، ساعات العمل، التأخير، الانصراف المبكر، الإضافي المحسوب والمعتمد، الحالة، حالة المراجعة.
5. `Late Report` — الحالات المتأخرة فقط مع دقائق التأخير.
6. `Overtime Report` — الوردية، نهاية الوردية، الخروج الفعلي، الإضافي المحسوب والمعتمد.
7. `Payroll` — كشف رواتب الشهر الذي تنتهي فيه الفترة (أحدث إصدار غير ملغى). إن لم يوجد كشف لهذا الشهر تُكتب رسالة واضحة في الورقة بدل أرقام مُفترضة.
8. `Exceptions` — كل ما يحتاج مراجعة بشرية: بصمة دخول/خروج مفقودة، تصنيف بحاجة لمراجعة، بصمة غير مصنّفة، وكود بصمة غير مرتبط بأي موظف.

ورقتا Entry/Exit تعكسان نتيجة التصنيف الفعلية المحفوظة في `AttendanceEvents` وليست
تقسيماً عشوائيًا: كل صف مرتبط برقم السجل الأصلي في `RawPunches`.

التنسيق: عنوان وفترة في الأعلى، تجميد صف العناوين، تفعيل الفلاتر، عرض أعمدة مناسب،
تنسيقات التاريخ/الوقت/المبالغ، وملخص للفترة في نهاية كل ورقة.

إضافة إلى الملف الشامل، كل تقرير قابل للتصدير منفردًا: الحضور اليومي (Excel و PDF)،
ملخص الأقسام، الإضافي، الغياب والتأخير، البصمات الخام، سجلات الدخول، سجلات الخروج،
كشف الرواتب (Excel و PDF)، وكشف راتب موظف واحد (Excel).

## 15) الأمان والصلاحيات

| الدور | الصلاحيات |
|---|---|
| Admin | كل شيء: المستخدمون، الإعدادات، الرواتب |
| HR | الموظفون، الورديات، استيراد البصمة، الحضور، الإجازات |
| Accountant | السلف، الخصومات، اعتماد الإضافي، كشوف الرواتب |
| Viewer | الاطلاع على التقارير فقط |

- كلمات المرور مُشفّرة عبر ASP.NET Core Identity (لا تخزين نصي).
- التحقق من الصلاحيات **على الخادم** عبر `[Authorize(Policy = ...)]` على كل
  Controller، وليس بإخفاء الأزرار فقط.
- كل العمليات الحساسة (تعديل موظف، اعتماد إضافي، اعتماد/إلغاء كشف رواتب، تعديل
  إعدادات، تعديل خصم/سلفة) تُسجَّل في `AuditLogs`.

## 16) الاختبارات

```bash
dotnet test HrPayroll.sln -c Release
```

النتيجة الحالية: **111 اختبارًا ناجحًا** (0 فشل، 0 متجاوَز). تغطية الحالات المطلوبة:

| # | الحالة | أمثلة من أسماء الاختبارات |
|---|---|---|
| 1 | سماح الدخول | `Late_Within_Grace_Is_Zero` (07:10 / 07:25 / 07:30) |
| 2 | تجاوز السماح يُلغي السماح بالكامل | `Late_After_Grace_Counts_From_Shift_Start` (07:31=31، 07:35=35، 08:00=60، 08:15=75) |
| 3 | سماح الخروج والإضافي | `Overtime_Within_Exit_Grace_Is_Zero` (15:00/15:15/15:30)، `Overtime_After_Exit_Grace_Counts_From_Shift_End` (15:35=35، 16:00=60) |
| 4 | الوردية الليلية | `NightShift_Window_And_Overtime`، `Overnight_Shift_Punches_Are_Classified_On_The_Next_Day` |
| 5 | اكتشاف الوردية اليومية | `Detects_First_Shift_From_Morning_Punches`، `Ambiguous_Punches_Are_Flagged_For_Review_Instead_Of_Guessing`، `Assigned_Shift_Wins_When_Punches_Are_Equally_Compatible` |
| 6 | تصنيف الدخول/الخروج بلا تخمين | `Neutral_Punch_Type_Does_Not_Decide_Direction`، `Explicit_Device_Punch_Types_Are_Honoured` |
| 7 | البصمة الناقصة لا تُخترع | `Missing_Entry_Punch_Is_Flagged_Not_Absent_And_Not_Overtime`، `Missing_Exit_Punch_Is_Flagged_Not_Absent` |
| 8 | تكرار البصمة | `Duplicate_Punches_At_Same_Time_Are_Deduplicated` |
| 9 | إعادة استيراد الملف لا تُكرر الحركات | `Importing_The_Same_File_Twice_Inserts_Nothing_The_Second_Time`، `New_Records_For_A_Previously_Imported_Date_Are_Imported` |
| 10 | كود بصمة غير مسجل | `Unknown_Biometric_Code_Is_Reported_And_Not_Imported`، `Matching_Uses_Biometric_Code_Not_Name` |
| 11 | تصدير Entry/Exit والربط بالسجل الأصلي | `Entry_Sheet_Preserves_Link_To_Raw_Punch`، `Full_Export_Contains_All_Required_Sheets` |
| 12 | منع الخصم المزدوج | `Loans_And_Deductions_Are_Applied_Once_Even_After_Recalculation`، `Components_Already_Paid_In_A_Previous_Period_Are_Not_Paid_Again` |
| 13 | الخصومات حسب السياسة | `Absence_Is_Deducted_At_The_Daily_Rate`، `Unpaid_Leave_Is_Deducted_At_The_Daily_Rate`، `Approved_Annual_Leave_Is_Paid_And_Not_Deducted` |
| 14 | دورة اعتماد/قفل الرواتب | `Locked_Payroll_Cannot_Be_Recalculated_Without_An_Explicit_Reopen`، `Reopen_Requires_A_Reason`، `Locking_Requires_Approval_First` |
| 15 | صحة صافي الراتب | `Payroll_Matches_The_Company_Example`، `Daily_And_Hourly_Rates_For_9000_Salary`، `Ten_Approved_Overtime_Hours_Are_375`، `Fifty_Approved_Overtime_Hours_Are_1875` |
| 16 | عدم تطبيق أي مضاعف للإضافي | `No_Overtime_Multiplier_Is_Applied_By_Default` |
| 17 | الإجازات والعطلات وأيام الراحة | `Working_Days_Exclude_Fridays`، `Working_Days_Exclude_Public_Holidays`، `Friday_Is_A_Paid_Weekly_Rest_Day_And_Not_Absence`، `Overlapping_Leaves_Are_Rejected` |
| 18 | تصحيح الحضور يدويًا | `Manual_Correction_Records_Original_Reason_And_User`، `Correction_Without_Reason_Is_Rejected`، `Attendance_Correction_Is_Blocked_For_A_Locked_Payroll_Period` |
| 19 | تناسق التقارير والتصدير | `Report_Rows_Match_The_Stored_Daily_Attendance_Values`، `Excel_Daily_Export_Contains_Exactly_The_Reported_Rows`، `Excel_Payroll_Export_Matches_Stored_Payroll_Items`، `Pdf_Payroll_And_Payslip_Are_Valid_Pdf_Files` |
| 20 | استيراد الموظفين من Excel | `Employee_Import_Preview_Validates_And_Detects_Duplicates`، `Employee_Import_CreateAndUpdate_Updates_With_Audit_Trail` |

## 17) الإعدادات (Settings)

| المفتاح | المعنى | الافتراضي |
|---|---|---|
| `Payroll.WorkDaysPerMonth` | أيام الشهر لحساب الأجر اليومي | 30 |
| `Payroll.WorkHoursPerDay` | ساعات العمل اليومية | 8 |
| `Payroll.DefaultOvertimeMultiplier` | معامل الإضافي (سياسة الشركة = بدون مضاعفة) | 1 |
| `Payroll.LateDeductionEnabled` | خصم التأخير | true |
| `Payroll.AbsenceDeductionEnabled` | خصم أيام الغياب | true |
| `Payroll.UnpaidLeaveDeductionEnabled` | خصم الإجازات بدون أجر | true |
| `Payroll.OvertimeRoundingStepMinutes` | خطوة تقريب الإضافي بالدقائق | 1 |
| `Payroll.RoundOvertimeDown` | التقريب للأسفل | true |
| `Payroll.RequireOvertimeApproval` | اعتماد الإضافي يدويًا | true |
| `Payroll.AllowPayrollWithoutAttendance` | السماح بكشف بلا حضور | false |
| `Payroll.MonthlySalaryIncludesPaidRestDays` | الراتب يشمل أيام الراحة المدفوعة | true |
| `Payroll.AnnualLeaveEntitlementDays` | رصيد الإجازة السنوية للموظف الجديد | 21 |
| `Attendance.WeeklyRestDays` | أيام الراحة (0=الأحد … 5=الجمعة) | 5 |
| `Attendance.EntryGraceMinutes` | سماح الدخول الافتراضي | 30 |
| `Attendance.ExitGraceMinutes` | سماح الخروج الافتراضي | 30 |
| `Attendance.MinimumOvertimeMinutes` | أقل مدة إضافي معتبرة | 1 |
| `Attendance.AutoProcessAfterImport` | معالجة الحضور تلقائيًا بعد الاستيراد | false |
| `General.CompanyTimeZone` | توقيت الشركة | Egypt Standard Time |
| `General.CompanyName` | اسم الشركة في التقارير | شركة الموارد البشرية |
| `General.Currency` | عملة الرواتب | EGP |

## 18) ملاحظة حول ملف العينة `att5.xls`

لم يكن ملف العينة `att5.xls` متاحًا في بيئة التنفيذ (المستودع المرفق كان فارغًا)،
لذلك بُنيت **آلية اكتشاف نوع الملف من المحتوى** لتغطية كل الاحتمالات المتوقعة:
ملف Excel ثنائي قديم، **Excel 2003 XML بامتداد .xls**، xlsx، CSV، وجدول HTML،
واختُبرت هذه المسارات بملفات مُصنّعة تحاكي كل صيغة (`ImportTests`).

عند توفر الملف الحقيقي، رفعه من شاشة الاستيراد سيُظهر في المعاينة: الصيغة المكتشفة،
والأعمدة المطابقة، ونتيجة كل صف. إذا ظهر عمود غير معروف أو صيغة تاريخ غير مدعومة،
يمكن إضافة مرادف العمود في `ColumnMapper` أو صيغة التاريخ في `PunchTimeParser`.

## 19) قواعد عمل قابلة للضبط (لا تُفترض تلقائيًا)

- اتجاه الحركة عند غياب نوع صريح: يُحدَّد بالقواعد، والحالات الغامضة تُعلَّم
  للمراجعة بدل الاحتساب المزدوج.
- خصم الغياب وخصم الإجازة بدون أجر: **مُفعَّلان افتراضيًا** (سعر اليوم = الأجر اليومي)
  ويمكن تعطيلهما من شاشة الإعدادات. التأمينات/الضرائب غير مُنفَّذة كمكوّن مستقل.
- الإضافي: لا يُضاف للراتب قبل اعتماده يدويًا (افتراضيًا) — المعتمد فقط يدخل الراتب.
- لا يُخترع وقت بصمة مفقودة، ولا يُسجَّل غياب قبل استيراد بيانات اليوم.
- لا يُحتسب كشف رواتب لفترة بلا أي بيانات حضور معالجة إلا بسماح صريح من الإعدادات
  (`Payroll.AllowPayrollWithoutAttendance` = نعم).

## 20) ما أُضيف في ترقية النظام الشاملة

| الوحدة | الملفات الرئيسية |
|---|---|
| إدارة الموظفين والأقسام والمسميات | `EmployeesController`, `DepartmentsController`, `JobTitlesController`, `Organization.cs` |
| استيراد الموظفين من Excel (معاينة/ربط أعمدة/جديد أو تحديث) | `EmployeeImportService`, `EmployeeColumnMapper`, `Views/Employees/Import*.cshtml` |
| استيراد بصمات الجهاز (3 مراحل + كشف الصيغة + منع التكرار) | `AttendanceImportService`, `ColumnMapper`, `AttendanceFileParser` |
| إعداد الورديات واكتشاف الوردية اليومية | `ShiftService`, `ShiftDetector`, `ShiftWindow`, `PunchWorkDateResolver`, `Views/Shifts` |
| معالجة الحضور (تأخير/انصراف مبكر/بصمة ناقصة/راحة/عطلة/إجازة) | `AttendanceProcessingService`, `PunchClassifier`, `AttendanceCalculator` |
| تصحيح الحضور يدويًا مع أثر تدقيق | `AttendanceCorrectionService`, `Views/Attendance/Correct.cshtml` |
| الإجازات وتحويل الغياب إلى إجازة | `LeaveService`, `LeavesController`, `Views/Leaves` |
| العطلات الرسمية وأيام الراحة الأسبوعية | `HolidaysController`, `SettingKeys.WeeklyRestDays` |
| اعتماد الإضافي (بدون أي مضاعفة) | `OvertimeController`, `Views/Overtime` |
| الرواتب ودورة العمل (احتساب/مراجعة/اعتماد/قفل/إعادة فتح) | `PayrollService`, `PayrollCalculator`, `Views/Payroll` |
| التقارير والتصدير: ملف شامل بـ 8 أوراق (Raw Punches / Entry / Exit / Daily Attendance / Late / Overtime / Payroll / Exceptions) + PDF وكشوف الرواتب | `ReportService`, `ExcelExportService`, `PdfExportService`, `Views/Reports` |
| واجهة عربية RTL حديثة | `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/site.js` |
| سجل التدقيق والصلاحيات | `AuditService`, `AppRoles`/`AppPolicies` في `Program.cs` |

أوامر سريعة:

```bat
dotnet build HrPayroll.sln -c Release
dotnet test HrPayroll.sln -c Release
dotnet run --project src\HrPayroll.Web -c Release
```
