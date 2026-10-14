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
| الاختبارات | xUnit (126 اختبارًا) |

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
                             LeaveService, PayrollService, PayrollSheetService, ReportService,
                             ExcelExportService, PdfExportService, DashboardService, AuditService
    Seed/                    DbSeeder (الورديات والإعدادات والمستخدم المدير)
  HrPayroll.Web/             تطبيق MVC: Controllers + ViewModels + Views (RTL) + Program.cs
    Views/Payroll/           Index (كشف المرتبات القابل للتحرير) + _RunHistory + _PeriodPicker
    wwwroot/css/site.css     نظام التصميم الموحّد (RTL + جداول + ورقة شبيهة بـ Excel)
    wwwroot/js/payroll-sheet.js  تحرير الخلايا وحفظها والتحقق قبل الحفظ
    wwwroot/fonts/Cairo.ttf  الخط العربي المستخدم في الواجهة (بدون إنترنت)
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

### إنشاء أول مسؤول (آمن — لا كلمات مرور ثابتة في الكود أو المستودع)

**لا توجد أي كلمة مرور افتراضية في الشيفرة أو في `appsettings.json`.** عند الإقلاع ينفّذ
`DbSeeder` القواعد التالية بالترتيب:

| الحالة | النتيجة |
|---|---|
| `Seed:AdminEmail` و `Seed:AdminPassword` مضبوطان | يُنشأ (أو يُرقّى) هذا الحساب بدور Admin |
| غير مضبوطين، ويوجد مستخدمون بالفعل | لا يُنشأ شيء |
| غير مضبوطين، وبيئة الإنتاج | **لا يُنشأ أي حساب**، وتُسجَّل تعليمات الضبط في السجل |
| غير مضبوطين، وبيئة `Development` ولا يوجد مستخدمون | حساب تجريبي `admin@hr.local` بكلمة مرور **عشوائية** تُطبع مرة واحدة في سجل الإقلاع |

لضبط أول مسؤول في الإنتاج (اختر واحدًا):

```bash
# أ) متغيّرات بيئة
set Seed__AdminEmail=owner@company.com
set Seed__AdminPassword=<كلمة-مرور-قوية>

# ب) User Secrets (بيئة Development)
dotnet user-secrets set "Seed:AdminEmail" "owner@company.com" -p src/HrPayroll.Web
dotnet user-secrets set "Seed:AdminPassword" "<كلمة-مرور-قوية>" -p src/HrPayroll.Web
```

بعد أول دخول غيّر كلمة المرور من شاشة المستخدمين. تُنشأ الأدوار الأربعة تلقائيًا:
Admin / HR / Accountant / Viewer، والورديات الثلاث الافتراضية
(07:00–15:00، 15:00–23:00، 23:00–07:00) تُنشأ تلقائيًا أيضًا.

> **صفحة تسجيل الدخول لا تعرض أي كلمة مرور.** في بيئة `Development` فقط يظهر تلميح
> يحمل **البريد** التجريبي وإشارة إلى مكان كلمة المرور؛ ولا يظهر أي تلميح في `Production`.
>
> **سياسة محاولات الدخول الفاشلة:** بعد 5 محاولات خاطئة يُقفل الحساب مؤقتًا 15 دقيقة،
> وتظهر رسالة عربية واضحة بذلك (تُسجَّل المحاولة في سجل الخادم).

## 7) استيراد تقرير البصمة (منطقتان منفصلتان: الدخول والخروج)

1. سجّل الدخول بحساب يملك صلاحية HR أو Admin.
2. القائمة → **الحضور والانصراف** → **استيراد البصمات**.
3. تجد **منطقتين مستقلتين** لا يختلط فيهما الملفان:
   - **رفع ملف الدخول** (`entryFile`) — سجلات تسجيل الدخول إلى العمل.
   - **رفع ملف الخروج** (`exitFile`) — سجلات تسجيل الخروج من العمل.

   لكل منطقة: أيقونة، زر اختيار ملف، **اسم الملف المختار**، **حجمه**، **تاريخ آخر تعديل**،
   و**حالة الملف** (لم يتم الاختيار / جاهز للرفع / لا توجد سجلات جديدة / فشل التحقق).
4. اضغط الزر الرئيسي **«رفع الملفات ومعالجة الحضور»** (لا تبدأ أي كتابة في قاعدة البيانات قبله،
   ولا يُرسل النموذج قبل اختيار ملف واحد على الأقل).
5. صفحة **المعاينة** تعرض لكل ملف على حدة: الصيغة المكتشفة، **فترة البيانات المستخلصة**،
   إجمالي السجلات المقروءة، المقبولة، المكررة، المكررة المستوردة سابقًا، **الموظف غير المسجل**،
   **التاريخ/الوقت غير الصالح**، وربط الأعمدة المكتشفة، وأول 20 صفًا.
6. اضغط **«تأكيد الاستيراد ومعالجة الحضور»** — تُستورد الملفات الصالحة فقط ثم تُعالج
   الحضور تلقائيًا للفترة المكتشفة، وتُعرض **نتيجة كل ملف بشكل منفصل**
   (مقروء/مقبول/مكرر/يحتاج مراجعة). إذا فشل ملف ونجح الآخر فلا تظهر رسالة نجاح مضللة.

- **ملف كل بصماته مستوردة مسبقًا لا يُستورد ولا يُسجَّل من جديد**، وتقول الصفحة صراحةً
  «لا توجد سجلات جديدة» بدون أي كتابة.
- **الترتيب داخل الملفين لا يهم**: المطابقة بين الدخول والخروج تتم بكود الموظف/كود البصمة
  والوقت، لا بترتيب الصفوف.
- **لا يمكن رفع ملفين في حقل واحد غامض**، ورفع ملف واحد فقط مسموح إذا كان الآخر غير متاح.

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

في أعلى الصفحة **بطاقات إحصائية** تُحسب من نفس نتيجة الفلترة (إجمالي السجلات، حضور فعلي،
غياب، تأخير مع إجمالي الدقائق، يحتاج مراجعة، إضافي خام)، وفلاتر: من/إلى تاريخ، القسم،
الموظف، الوردية، الحالة، وبحث بالاسم أو الكود، مع زر «عرض» و«مسح» وبحث فوري داخل الجدول.

الجدول المختصر يعرض: الكود، الاسم، القسم، المسمى، التاريخ، الوردية (مع وسم «مُكتشفة» أو
«غامضة»)، بداية/نهاية الوردية، الدخول، الخروج، ساعات العمل، التأخير، الانصراف المبكر،
الإضافي الخام والمعتمد، الحالة بالعربية، وملاحظات، مع زر «تصحيح» لكل سجل.

**زر «تقارير الأقسام»** في أعلى الصفحة ينقل إلى تقرير الأقسام المستقل بنفس الفلاتر
(راجع القسم 14-ب).

> تفاصيل الحساب (الشيفت المكتشف، وقت السماح، الأصل والتعديل) متاحة من شاشة «تصحيح»
> لكل سجل، مع الاحتفاظ بالأصل وتسجيل كل تعديل في `AttendanceCorrections` بسبب مُسجَّل.

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

### فصل الغياب الأصلي عن المعتمد
كل سطر راتب يحفظ قيمتين مستقلتين للغياب:

- `OriginalAbsenceDays` — الغياب كما استُخرج من البصمة لحظة الاحتساب، **لا يتغير أبدًا** بعد التعديل اليدوي.
- `AbsenceDays` — الغياب **المعتمد** بعد المراجعة، وهو وحده الذي يدخل في خصم الغياب.

وبالإضافة إلى ذلك يُحفظ `AnnualLeaveDays` (أيام الإجازة السنوية المستهلكة في الشهر) و
`AnnualLeaveBalanceApplied` (ما خُصم فعليًا من رصيد الموظف لهذا السطر). الخصم من الرصيد
يتم بالفرق `جديد − مُطبَّق سابقًا`، لذلك إعادة الحفظ أو إعادة الاحتساب أو إعادة فتح الكشف
**لا تُخصم الرصيد مرتين**، وإلغاء الكشف يُعيد الرصيد كاملًا.

رصيد الموظف السنوي = `AnnualLeaveEntitlement` (المستحق) + `AnnualLeaveCarriedOver` (المرحَّل)،
والمتبقي هو `AnnualLeaveBalance`، والمستهلك = المتاح − المتبقي.

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
7. `Payroll` — كشف رواتب الشهر الذي تنتهي فيه الفترة (أحدث إصدار غير ملغى)، **بنفس أعمدة الجدول المعروض على الشاشة الثلاثين**: الراتب الأساسي، أيام الحضور، الراحة المدفوعة، الإجازة المدفوعة، الغياب من البصمة، الغياب المعتمد، أيام الإجازة السنوية، رصيد الإجازة السنوية، إجمالي الرصيد السنوي، الإجازات الأخرى بدون أجر، دقائق التأخير، ساعات الإضافي المعتمدة، قيمة الإضافي، المكافآت، البدلات، الأيام الإضافية وقيمتها، السلف، الخصومات الأخرى، خصم التأخير، خصم الغياب، خصم الإجازة بدون أجر، إجمالي الاستحقاقات، إجمالي الخصومات، صافي الراتب، وحالة المراجعة. القيم المصدَّرة هي المحفوظة والمعتمدة (بما فيها التعديلات اليدوية) وليست قيمًا افتراضية. إن لم يوجد كشف لهذا الشهر تُكتب رسالة واضحة في الورقة بدل أرقام مُفترضة.
8. `Exceptions` — كل ما يحتاج مراجعة بشرية: بصمة دخول/خروج مفقودة، تصنيف بحاجة لمراجعة، بصمة غير مصنّفة، وكود بصمة غير مرتبط بأي موظف.

ورقتا Entry/Exit تعكسان نتيجة التصنيف الفعلية المحفوظة في `AttendanceEvents` وليست
تقسيماً عشوائيًا: كل صف مرتبط برقم السجل الأصلي في `RawPunches`.

التنسيق: عنوان وفترة في الأعلى، تجميد صف العناوين، تفعيل الفلاتر، عرض أعمدة مناسب،
تنسيقات التاريخ/الوقت/المبالغ، وملخص للفترة في نهاية كل ورقة.

إضافة إلى الملف الشامل، كل تقرير قابل للتصدير منفردًا: الحضور اليومي (Excel و PDF)،
ملخص الأقسام، الإضافي، الغياب والتأخير، البصمات الخام، سجلات الدخول، سجلات الخروج،
كشف الرواتب (Excel و PDF)، وكشف راتب موظف واحد (Excel)، و**تقرير الأقسام حسب المسمى
الوظيفي**، و**تقرير الإجازات والأرصدة**.

## 14-ب) تقرير الأقسام والمسميات الوظيفية (صفحة مستقلة)

صفحة مستقلة `Reports/Departments` تُفتح من زر **«تقارير الأقسام»** في شاشة الحضور اليومي
أو من مركز التقارير. وهي **منفصلة تمامًا عن جدول الحضور المختصر**:

- تجميع حسب **القسم** ← ثم **المسمى الوظيفي** ← ثم **أسماء الموظفين الفعلية**.
- لكل قسم: عدد الموظفين، الحاضرون، الغائبون، المتأخرون، الإجازات، وعدد سجلات الحضور.
- لكل مسمى وظيفي: **عدد الموظفين** وحضورهم وغيابهم وتأخيرهم، مع صف قابل للطي/الفك يعرض
  أسماء الموظفين وأكوادهم وسجلاتهم وإضافيهم.
- فلاتر: تاريخ/فترة، القسم، **المسمى الوظيفي**، الموظف، والبحث بالاسم أو الكود.
- أزرار: **عرض التفاصيل** (فتح كل المجموعات)، **طي التفاصيل**، **تصدير Excel**،
  و**طباعة التقرير** (يفتح المجموعات المطوية قبل الطباعة).

التصدير `ExportDepartmentsExcel` ينشئ ورقتين: `Departments` (ملخص كل قسم + مسمى + سطر
إجمالي لكل قسم) و`Employees` (سطر لكل موظف يحمل قسمه ومسمّاه واسمه وأرقامه).
كل الأعداد تُحسب من سجلات الحضور المطابقة للفلاتر ولا شيء منها ثابت في الكود.

## 14-ج) تقرير الإجازات والأرصدة

صفحة `Reports/Leaves` تعرض جدولين مستقلين:

1. **طلبات الإجازة داخل الفترة**: الكود، الاسم، القسم، نوع الإجازة (عربية)، من/إلى،
   أيام الطلب، أيام العمل، مدفوعة؟، الحالة (قيد المراجعة/معتمد/مرفوض)، مصدر الطلب
   (طلب إجازة / تحويل غياب)، والرصيد المتبقي لحظة الطلب.
2. **أرصدة الإجازة السنوية لكل موظف**: المستحق، المرحَّل، إجمالي الرصيد، المستخدم،
   والمتبقي (مع تمييز الرصيد السالب بلون تحذيري).

فلاتر: الفترة، القسم، الموظف، نوع الإجازة، الحالة، والبحث — مع **تصدير Excel**
(ورقتا `Leaves` و`Balances`) و**طباعة التقرير**.

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
  إعدادات، تعديل خصم/سلفة، **وتعديل أي سطر في كشف المرتبات**) تُسجَّل في `AuditLogs`
  بالقيمة الأصلية والقيمة المعدلة واسم المستخدم ووقت التعديل.
- تعديل كشف المرتبات محمي بسياسة مستقلة `EditPayrollSheet`
  ([Authorize(Policy = ...)] على إجراء `Payroll/SaveRows`)، ودور HR/Viewer لا يستطيع
  الوصول إليها حتى لو استُدعيت الواجهة البرمجية مباشرة. **مُتحقَّق فعليًا:** طلب POST
  من مستخدم بدور HR إلى `Payroll/SaveRows` يعيد **403**، وفتح شاشات المرتبات/الإضافي/
  السلف/الخصومات/المستخدمين يعيد `302 → /Account/AccessDenied`، بينما تظل شاشات
  الاستيراد والحضور والإجازات متاحة له (200).
- **سياسة محاولات الدخول الفاشلة**: `Lockout.MaxFailedAccessAttempts = 5` و
  `DefaultLockoutTimeSpan = 15 دقيقة`، و`SignInManager` يُستدعى بـ `lockoutOnFailure: true`،
  مع رسالة عربية صريحة عند القفل وتسجيل المحاولة في سجل الخادم.
- **لا تُعرض تفاصيل الاستثناءات الداخلية أو بيانات الاتصال للمستخدم**: كل
  `catch` في الـ Controllers يمرّ عبر `ex.UserMessage(_logger, ...)` التي تُظهر رسائل
  قواعد العمل العربية فقط (`InvalidOperationException`) وتستبدل أي خطأ قاعدة بيانات/
  بنية تحتية برسالة عامة مع تسجيل التفاصيل في سجل الخادم حصريًا.
- **لا أسرار في المستودع**: `appsettings.json` يحمل القالب `__SET_PASSWORD__` فقط،
  وقاعدة البيانات الحقيقية في `appsettings.Production.json` (مُستثنى في `.gitignore`)
  أو في متغيّر بيئة/User Secrets. وإذا كان القالب ما زال موجودًا يرفض التطبيق تنفيذ
  أي ترحيل ويطبع تعليمات الضبط بدل عمل اتصال ببيانات اعتماد ناقصة.

## 16) الاختبارات

```bash
dotnet test HrPayroll.sln -c Release
```

النتيجة الحالية: **137 اختبارًا ناجحًا** (0 فشل، 0 متجاوَز). تغطية الحالات المطلوبة:

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
| 12 | تجميع الحضور حسب القسم والمسمى مع أسماء الموظفين | `Department_Report_Groups_Real_Employees_Under_Their_Job_Titles`، `Department_Report_Respects_JobTitle_And_Department_Filters` |
| 13 | مطابقة أعداد الأقسام مع الأسماء الفعلية | `Excel_Department_Report_Lists_Employee_Names_Under_Each_Job_Title` (كتب/أسماء/مسمّيات في الملف المُصدَّر) |
| 14 | ملفا الدخول والخروج بترتيب صفوف مختلف | `Entry_And_Exit_Files_Are_Paired_By_Code_And_Time_Not_By_Row_Order` |
| 15 | لا تكرار عند إعادة رفع الملفين | `Re_uploading_Both_Files_Adds_No_Duplicate_Punches` |
| 16 | تقرير الإجازات والأرصدة | `Leaves_Report_And_Balances_Match_Stored_Leave_Data`، `Excel_Leaves_Report_Contains_Leaves_And_Balances_Sheets` |
| 17 | أمان أول مسؤول | `Production_Without_Configured_Credentials_Creates_No_User_At_All`، `Development_Fallback_Creates_An_Admin_Without_Any_Static_Password`، `Configured_First_Admin_Is_Created_And_Granted_The_Admin_Role`، `Existing_Users_Are_Not_Duplicated_Or_Overwritten_On_Reseed` |
| 12 | منع الخصم المزدوج | `Loans_And_Deductions_Are_Applied_Once_Even_After_Recalculation`، `Components_Already_Paid_In_A_Previous_Period_Are_Not_Paid_Again` |
| 13 | الخصومات حسب السياسة | `Absence_Is_Deducted_At_The_Daily_Rate`، `Unpaid_Leave_Is_Deducted_At_The_Daily_Rate`، `Approved_Annual_Leave_Is_Paid_And_Not_Deducted` |
| 14 | دورة اعتماد/قفل الرواتب | `Locked_Payroll_Cannot_Be_Recalculated_Without_An_Explicit_Reopen`، `Reopen_Requires_A_Reason`، `Locking_Requires_Approval_First` |
| 15 | صحة صافي الراتب | `Payroll_Matches_The_Company_Example`، `Daily_And_Hourly_Rates_For_9000_Salary`، `Ten_Approved_Overtime_Hours_Are_375`، `Fifty_Approved_Overtime_Hours_Are_1875` |
| 16 | عدم تطبيق أي مضاعف للإضافي | `No_Overtime_Multiplier_Is_Applied_By_Default` |
| 17 | الإجازات والعطلات وأيام الراحة | `Working_Days_Exclude_Fridays`، `Working_Days_Exclude_Public_Holidays`، `Friday_Is_A_Paid_Weekly_Rest_Day_And_Not_Absence`، `Overlapping_Leaves_Are_Rejected` |
| 18 | تصحيح الحضور يدويًا | `Manual_Correction_Records_Original_Reason_And_User`، `Correction_Without_Reason_Is_Rejected`، `Attendance_Correction_Is_Blocked_For_A_Locked_Payroll_Period` |
| 19 | تناسق التقارير والتصدير | `Report_Rows_Match_The_Stored_Daily_Attendance_Values`، `Excel_Daily_Export_Contains_Exactly_The_Reported_Rows`، `Excel_Payroll_Export_Matches_Stored_Payroll_Items`، `Pdf_Payroll_And_Payslip_Are_Valid_Pdf_Files` |
| 20 | استيراد الموظفين من Excel | `Employee_Import_Preview_Validates_And_Detects_Duplicates`، `Employee_Import_CreateAndUpdate_Updates_With_Audit_Trail` |
| 21 | تعديل الغياب والإجازات من الكشف | `Absence_Edit_And_Annual_Leave_Conversion_Deducts_Two_Days_Only`، `Saving_The_Same_Annual_Leave_Twice_Does_Not_Deduct_The_Balance_Twice`، `Reducing_Annual_Leave_Returns_Days_To_The_Balance`، `Insufficient_Annual_Balance_Requires_Confirmation_Then_Can_Be_Overridden` |
| 22 | المكافآت/البدلات/الخصومات/الإضافي/الأيام الإضافية من الكشف | `Bonus_And_Allowance_Edits_Recompute_Gross_And_Net`، `Other_Deduction_Edit_Is_Applied_And_Saved`، `Overtime_Hours_Edit_Uses_Hourly_Rate_Without_Any_Multiplier`، `Extra_Days_With_Explicit_Rate_Are_Priced_Separately` |
| 23 | حفظ التعديلات ومراجعتها ومنع التعديل غير المصرح | `Saved_Edits_Are_Returned_With_The_Row_And_Persisted`، `Every_Saved_Edit_Is_Written_To_The_Audit_Log`، `Editing_An_Approved_Or_Locked_Sheet_Is_Rejected`، `Negative_Values_And_Impossible_Leave_Totals_Are_Rejected`، `Regenerating_The_Sheet_Keeps_Manual_Edits_Without_Deducting_The_Balance_Again`، `Cancelling_The_Sheet_Returns_The_Consumed_Annual_Leave_Balance` |
| 24 | تصدير الكشف بأعمدته وقيمه المحفوظة | `Payroll_Export_Contains_All_Sheet_Columns_And_The_Saved_Values` |

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
| واجهة عربية RTL حديثة بنظام تصميم موحّد وقائمة جانبية واحدة من 5 أقسام | `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/site.js` |
| **شاشة كشف المرتبات الشهري القابلة للتحرير (شبيهة بـ Excel)** مع تعديل الغياب/الإجازات/المكافآت/الخصومات/الإضافي وحفظها على السيرفر | `PayrollSheetService`, `Views/Payroll/Index.cshtml`, `Views/Payroll/_RunHistory.cshtml`, `Views/Payroll/_PeriodPicker.cshtml`, `wwwroot/js/payroll-sheet.js`, `PayrollController.SaveRows/RowBreakdown` |
| سجل التدقيق والصلاحيات | `AuditService`, `AppRoles`/`AppPolicies` في `Program.cs` |

أوامر سريعة:

```bat
dotnet build HrPayroll.sln -c Release
dotnet test HrPayroll.sln -c Release
dotnet run --project src\HrPayroll.Web -c Release
```

## 21) كشف المرتبات الشهري — شاشة واحدة شبيهة بـ Excel

القائمة الجانبية صارت **5 أقسام** بدل 15+ رابطًا متفرقًا:
لوحة التحكم · الموظفون (قائمة/إضافة/استيراد/الأقسام/المسميات) ·
الحضور والانصراف (الاستيراد/اليومي/الاستثناءات/الورديات/العطلات/الإجازات/تحويل غياب) ·
كشف المرتبات (الكشف الشهري/اعتماد الإضافي/السلف/الخصومات) · الإعدادات (الإعدادات/المستخدمون).
لم تُحذف أي وظيفة؛ أُعيد ترتيبها فقط داخل مجموعات قابلة للطي، والتقارير والتصدير رابط مستقل.

### كيف تستخدم الكشف؟
1. **الفترة**: اختر الشهر والسنة (أو استخدم «السابق/التالي») ويمكن التصفية بالقسم أو البحث باسم/كود الموظف.
2. **تجهيز البيانات**: «استيراد البصمات» ← معالجة الحضور ← «تحديث الحسابات» لإنشاء الكشف.
3. **التعديل المباشر**: الخلايا ذات الخلفية الصفراء قابلة للتحرير — الغياب المعتمد،
   الإجازة السنوية، الإجازة بدون أجر، ساعات الإضافي، المكافآت، البدلات، الأيام الإضافية
   وسعر اليوم، السلف، والخصومات الأخرى. الخلايا الزرقاء مستوردة من البصمة (لا تُحرَّر)
   والرمادية نتائج محسوبة تلقائيًا. الألوان موضّحة في مفتاح أعلى الجدول.
4. **معاينة فورية**: عند الكتابة تُحدَّث قيمة الإضافي والاستحقاقات والخصومات والصافي
   والإجماليات فورًا بنفس معادلات السيرفر، لكن **الحساب الرسمي والحفظ يتمّان على السيرفر**.
5. **الحفظ**: زر «حفظ التعديلات» أعلى الجدول، مع مؤشر «تغييرات غير محفوظة» وتحذير عند
   مغادرة الصفحة. يمكن تشغيل «حفظ تلقائي» ليُحفظ السطر عند الخروج من الخانة.
   للتراجع قبل الحفظ: «تراجع عن التغييرات». ولرؤية تفصيل الحساب خطوة بخطوة: زر «…» في آخر السطر.
6. **سقف القواعد**: لا تُقبل قيم سالبة، ولا يتجاوز (الغياب المعتمد + الإجازة السنوية +
   الإجازات الأخرى) أيام العمل في الفترة. وإذا تجاوزت الإجازة السنوية رصيد الموظف تظهر
   رسالة تحذير بالرصيد المتاح ويُطلب تأكيد صريح قبل الحفظ (يظهر الرصيد السالب بالأحمر في
   حالة التأكيد). وإذا فشل الحفظ تبقى القيم التي أدخلتها كما هي ولا تُمحى.
7. **الاعتماد**: «اعتماد الكشف» يوقف التعديل المباشر، و«إلغاء الكشف» يُعيد رصيد الإجازات
   المخصوم ويفصل السلف والخصومات. للتصحيح بعد الاعتماد/القفل: «إعادة فتح» بسبب مُسجَّل.

### سلوكيات محسومة
- **إعادة الاحتساب لا تفقد عملك**: التعديلات اليدوية تُنقل للإصدار الجديد، ومعها مقدار
  الرصيد المخصوم سابقًا حتى لا يُخصم مرتين.
- **الإجازة السنوية ليست غيابًا**: تُخصم من الرصيد فقط ولا تُنتج أي خصم مالي.
- **الغياب من البصمة يبقى ظاهرًا** بجانب الغياب المعتمد للمقارنة والمراجعة.
- **الإضافي بدون أي مضاعف**: القيمة = ساعات معتمدة × (الأجر اليومي ÷ 8).
- **الرصيد المرحَّل** يُسجَّل لكل موظف من شاشة الموظف (استحقاق + مرحَّل، والرصيد المتبقي
  يُعرض مباشرة في عمود «رصيد السنوية» داخل الكشف).

أوامر سريعة للتحديث:

```bat
dotnet ef migrations add <Name> -p src\HrPayroll.Infrastructure -s src\HrPayroll.Web
dotnet build HrPayroll.sln -c Release
dotnet test  HrPayroll.sln -c Release
```

> الترحيل الأخير: `20261013130424_PayrollSheetEditing_LeaveBalances` (إضافي فقط: أعمدة
> جديدة في `PayrollItems` و`Employees`، بدون حذف أو إعادة إنشاء أي جدول). يُطبَّق تلقائيًا
> عند الإقلاع إذا كان `Database:ApplyMigrationsOnStartup = true`.

## 22) صفحة تسجيل الدخول (تخطيط مصادقة مستقل)

صفحة الدخول صارت شاشة مستقلة تمامًا عن لوحة التحكم: **لا شريط جانبي ولا شريط علوي**،
وتستخدم تخطيطًا مخصصًا `Views/Shared/_AuthLayout.cshtml` بدل `_Layout.cshtml`.

- **تصميم من قسمين**: لوحة هوية متدرجة الألوان (اسم النظام + مزايا مختصرة) وبجانبها
  بطاقة الدخول — وتتحول إلى عمود واحد على الشاشات الصغيرة.
- **خط Cairo المضمَّن** كما هو في بقية النظام، مع تباعد وأحجام حقول وأزرار محسّنة.
- **إظهار/إخفاء كلمة المرور** عبر زر داخل الحقل (`data-pw-toggle` في `wwwroot/js/site.js`).
- **«تذكرني»** وحقل `returnUrl` المخفي يعملان كما هو، ورسائل التحقق عربية عميلًا وسيرفرًا.
- **لا تُعرض بيانات الاعتماد في الإنتاج**: `AccountController` يمرّر `ShowDemoHint`
  بقيمة `_env.IsDevelopment()` فقط.
- حماية الحماية الأصلية (Identity + Antiforgery) لم تُمس، وصفحة «غير مصرح» تستخدم نفس التخطيط.

الملفات المعنية: `Views/Account/Login.cshtml`، `Views/Account/AccessDenied.cshtml`،
`Views/Shared/_AuthLayout.cshtml`، `Controllers/AccountController.cs`،
`wwwroot/css/site.css` (قسم `auth-*`)، `wwwroot/js/site.js`.

## 23) لوحة التحكم — «الخطوة التالية» بحسب البيانات الفعلية

أعلى الصفحة الرئيسية بطاقة **«الخطوة التالية»** تُحسب من قاعدة البيانات (لا قيم ثابتة)
وتقود المستخدم إلى الإجراء الصحيح حسب حالته، بالترتيب:

1. لا موظفون نشطون ← **استيراد الموظفين من Excel**.
2. لا توجد ملفات بصمات مستوردة ← **استيراد البصمات**.
3. توجد بيانات ناقصة لليوم ← **معالجة الحضور**.
4. يوجد استثناءات أو بصمات ناقصة ← **مراجعة الاستثناءات**.
5. يوجد إضافي بانتظار الاعتماد ← **اعتماد الإضافي**.
6. لا يوجد كشف محسوب ← **فتح كشف المرتبات لاحتسابه**.
7. وإلا ← **الكشف جاهز للمراجعة**.

بقيت بطاقات المؤشرات عملية فقط (حاضرون/غائبون/متأخرون/بصمات ناقصة/يحتاج مراجعة/إجازات/
إضافي بانتظار الاعتماد/صافي الشهر) وكلها من الخدمات الفعلية، مع جدول ملخص الأقسام.
الملف: `Views/Dashboard/Index.cshtml`.

## 24) دليل المستخدم العربي المختصر

1. **الدخول**: افتح الموقع وسجّل الدخول. (يُنشأ أول مسؤول بطريقة آمنة عند أول تشغيل — راجع القسم 6.)
2. **الموظفون**: القائمة ← «الموظفون» ← «إضافة موظف» أو «استيراد من Excel»
   (الكود/الاسم/القسم/المسمى/الراتب). اضبط بعدها «استحقاق السنوية» و«الرصيد المرحّل» لكل موظف.
3. **البصمات**: القائمة ← «الحضور والانصراف» ← «استيراد البصمات»: ضع ملف الدخول في منطقة
   **«رفع ملف الدخول»** وملف الخروج في منطقة **«رفع ملف الخروج»** (لا يختلطان)، ثم اضغط
   **«رفع الملفات ومعالجة الحضور»**. راجع المعاينة (مقبول/مكرر/موظف غير مسجل/تاريخ غير صالح +
   فترة البيانات لكل ملف) ثم **«تأكيد الاستيراد ومعالجة الحضور»**. إعادة رفع الملف نفسه لا تُنشئ
   سجلات مكرَّرة، وملف كل بصماته مكررة لا يُستورد أصلًا.
4. **مراجعة الحضور**: «الحضور اليومي» و«الاستثناءات والمراجعة». أي سجل «يحتاج مراجعة» أو
   بصمة ناقصة عالجه قبل الرواتب.
   ومن شاشة «الحضور اليومي» زر **«تقارير الأقسام»** يفتح تقريرًا مستقلًا لكل قسم ← مسمى
   وظيفي ← أسماء الموظفين مع التصدير والطباعة (راجع القسم 14-ب).
5. **الإجازات والغياب**: اضبط عدد أيام الغياب و/أو الإجازة السنوية من عمودي «الغياب المعتمد»
   و«الإجازة السنوية» داخل الكشف. يُحفظ أصل الغياب من البصمة في عمود «الغياب من البصمة» للمراجعة،
   وتُخصم الإجازة السنوية من الرصيد فقط دون خصم مالي، ولا تُحتسب نفس الأيام مرتين.
6. **اعتماد الإضافي**: «الحضور والانصراف» ← أو «كشف المرتبات» ← «اعتماد الإضافي»، راجع الأيام
   ذات الإضافي الخام واعتمد ما يلزم. الإضافي غير المعتمد لا يدخل الراتب.
7. **احتساب الرواتب**: «كشف المرتبات الشهري» ← «تحديث الحسابات». راجع الأسطر وعدّل الخلايا
   الصفراء، ثم «حفظ التعديلات».
8. **اعتماد وقفل الشهر**: «اعتماد الكشف» ← «قفل الكشف» (بعد تأكيد). للتصحيح: «إعادة فتح» بسبب مُسجَّل.
9. **التقارير والتصدير**: «التقارير والتصدير» ← الحضور اليومي، **الأقسام والمسميات
   الوظيفية**، **الإجازات والأرصدة**، الغياب والتأخير، الإضافي، والبصمات، وتصدير
   Excel أو PDF أو الملف الشامل (8 أوراق). تصدير كشف المرتبات يطابق القيم المحفوظة في الجدول.

## 25) ما أُضيف في جولة التحسين الحالية

1. **صفحة رفع ملفين مستقلة** (الدخول/الخروج) بحالتين مستقلتين وزر واحد
   «رفع الملفات ومعالجة الحضور» — بدل حقل ملف واحد غامض (القسم 7).
2. **معاينة لكل ملف على حدة** + فترة البيانات المستخلصة + منع استيراد ملف كل بصماته مكررة.
3. **صفحة «تقارير الأقسام والمسميات الوظيفية»** المستقلة مع الفك والطي والتصدير والطباعة
   وزر لها في شاشة الحضور اليومي (القسم 14-ب).
4. **تقرير الإجازات والأرصدة** مع تصدير Excel بورقتين (القسم 14-ج).
5. **أمان الإقلاع**: لا كلمات مرور ثابتة في الكود أو المستودع، سياسة المحاولات الفاشلة
   (5 محاولات / 15 دقيقة)، ورسائل خطأ لا تُسرّب تفاصيل داخلية (القسم 6 و15).
6. **توحيد التصميم**: تحويل شاشات المستخدمين والسلف والخصومات إلى نظام التصميم
   الموجود (`.card` / `.data` / `.field`) بدل `table`/`form-control` الخاصة بـ Bootstrap.

**التحقق الفعلي لهذه الجولة:** 26 شاشة تعيد 200، دورة رفع ملفين حقيقية بقاعدة البيانات
(مقبول/مكرر/معالجة/نتيجة كل ملف/ربط الدخول بالخروج مع ترتيب صفوف معاكس)، منع التكرار عند
إعادة الرفع، تقرير أقسام حقيقي بأسماء ومسمّيات، وتصديرات Excel سليمة (PK/zip وأوراق متوقعة)،
و403 فعلي لمستخدم HR عند محاولة تعديل المرتبات، وقفل الحساب بعد 5 محاولات فاشلة.

```bat
dotnet build HrPayroll.sln -c Release
dotnet test  HrPayroll.sln -c Release
dotnet run   --project src\HrPayroll.Web -c Release
```
