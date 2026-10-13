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
| قراءة ملفات البصمة | ExcelDataReader (xls/xlsx) + قارئ مخصص لـ Excel 2003 XML و CSV و HTML |
| المصادقة | ASP.NET Core Identity مع أدوار (Admin / HR / Accountant / Viewer) |
| الاختبارات | xUnit (28 اختبارًا) |

## 2) هيكل المشروع

```
HrPayroll.sln
src/
  HrPayroll.Domain/          الكيانات + Enums + منطق الحساب النقي (بدون قاعدة بيانات)
    Enums/                   AttendanceStatus, PunchDirection, PayrollStatus ...
    Entities/                Employee, Shift, RawPunch, AttendanceEvent, DailyAttendance,
                             Loan, Deduction, OvertimeApproval, PayrollRun, PayrollItem,
                             ImportBatch, AuditLog, AppSetting, LeaveRequest ...
    Services/                AttendanceCalculator, PunchClassifier, PayrollCalculator
    ValueObjects/            ShiftWindow (حساب الورديات الليلية)
  HrPayroll.Infrastructure/  EF Core + الخدمات + الاستيراد + التصدير
    Persistence/             AppDbContext + Configurations + Migrations
    Import/                  AttendanceFileParser, ColumnMapper, PunchTimeParser,
                             AttendanceImportService
    Services/                SettingsService, ShiftResolver, AttendanceProcessingService,
                             PayrollService, ExcelExportService, DashboardService, AuditService
    Seed/                    DbSeeder (الورديات والإعدادات والمستخدم المدير)
  HrPayroll.Web/             تطبيق MVC: Controllers + Views (RTL) + Program.cs
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

| الحضور (وردية 07:00، سماح 30) | التأخير |
|---|---|
| 07:15 | 0 |
| 07:30 | 0 |
| 07:31 | 31 دقيقة |
| 08:15 | 75 دقيقة |

الإضافي يبدأ من **وقت نهاية الوردية الرسمية** دون خصم السماح، وبدون قيم سالبة:
نهاية 15:00 وانصراف 16:00 ⇒ 60 دقيقة. وردية ليلية 23:00→07:00 وانصراف 08:00 من
اليوم التالي ⇒ 60 دقيقة (معالجة انتقال التاريخ).

سماح الوردية الأولى **لا يُطبّق تلقائيًا** على بقية الورديات (كل وردية لها سماحها).
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

صافي الراتب = الراتب الأساسي + قيمة الإضافي − خصم التأخير − السلف − الخصومات −
أي خصومات أخرى معتمدة.

القيم الافتراضية (قابلة للتعديل من الإعدادات):

- قيمة الساعة = الراتب الأساسي ÷ 240 (30 يومًا × 8 ساعات).
- قيمة الإضافي = ساعات الإضافي المعتمدة × قيمة الساعة × معامل الإضافي.
- تُقرَّب القيم المالية إلى منزلتين عشريتين.

الغياب والتأمينات والضرائب **لا تُخصم** إلا بسياسة معتمدة صريحة
(`Payroll.AbsenceDeductionEnabled` معطّلة افتراضيًا).

- لكل موظف سطر مستقل في كل شهر (`PayrollItem`).
- لكل إصدار سجل مستقل مع رقم إصدار وحالة (`Draft/Computed/Approved/Cancelled`).
- **لا يمكن إعادة احتساب كشف معتمد** قبل إلغاء الاعتماد بصلاحية Accountant/Admin.

## 14) تصدير Excel

زر **Export Excel** في شاشة **التقارير** مع تحديد الفترة والموظف والوردية.
يُنشئ الملف الواحد الأوراق التالية:

1. `Raw Punches`
2. `Entry`
3. `Exit`
4. `Daily Attendance`
5. `Late Report`
6. `Overtime Report`
7. `Payroll`
8. `Exceptions`

ورقتا Entry/Exit تعرضان: كود الموظف، الاسم، التاريخ، الوقت، رقم الجهاز، **رقم
السجل الأصلي**، حالة التصنيف، ومصدره — أي أنها تعكس نتيجة التصنيف الفعلية وليست
تقسيماً عشوائيًا.

التنسيق: عنوان وفترة في الأعلى، تجميد صف العناوين، تفعيل الفلاتر، عرض أعمدة مناسب،
تنسيقات التاريخ/الوقت/المبالغ، وملخص للفترة في نهاية كل ورقة. كما تتوفر أزرار
لتصدير كل تقرير منفردًا (البصمات الخام / الدخول / الخروج / الحضور اليومي / الرواتب).

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
dotnet test tests/HrPayroll.Tests
```

النتيجة الحالية: **28 اختبارًا ناجحًا**، تغطي الحالات المطلوبة:

| # | الحالة | اختبار |
|---|---|---|
| 1 | حضور 07:15 ⇒ تأخير 0 | `Late_At_0715_Is_Zero` |
| 2 | حضور 07:30 ⇒ تأخير 0 | `Late_At_0730_Is_Zero` |
| 3 | حضور 07:31 ⇒ تأخير 31 | `Late_At_0731_Is_31_Minutes` |
| 4 | حضور 08:15 ⇒ تأخير 75 | `Late_At_0815_Is_75_Minutes` |
| 5 | نهاية 15:00 وانصراف 16:00 ⇒ إضافي ساعة | `Overtime_DayShift_End1500_Exit1600_Is_60_Minutes` |
| 6 | وردية ليلية نهاية 07:00 وانصراف 08:00 ⇒ إضافي ساعة | `Overtime_NightShift_End0700_NextDay_Exit0800_Is_60_Minutes` |
| 7 | بصمة مكررة لا تُحتسب مرتين | `Duplicate_Punch_At_Same_Time_Counted_Once` |
| 8 | دخول بدون خروج ⇒ استثناء بدون اختراع خروج | `Entry_Without_Exit_Produces_MissingCheckOut_And_No_Invented_Exit` |
| 9 | موظف غير مسجل يظهر في تقرير الأخطاء | `Unknown_Employee_Is_Detected` |
| 10 | إعادة استيراد الملف لا تُكرر الحركات | `Reimporting_Same_File_Does_Not_Duplicate` |
| 11 | صحة تصدير Entry/Exit والربط بالسجلات الأصلية | `Entry_Sheet_Preserves_Link_To_Raw_Punch`, `Full_Export_Contains_All_Required_Sheets` |
| 12 | عدم خصم السلف والخصومات مرتين | `Loan_And_Deduction_Are_Applied_Once_And_Not_Duplicated` |

إضافة إلى اختبارات: تحويل صيغ الوقت، اكتشاف نوع الملف من المحتوى، اكتشاف الأعمدة،
معادلة صافي الراتب، وتعطيل خصم التأخير حسب السياسة.

## 17) الإعدادات (Settings)

| المفتاح | المعنى | الافتراضي |
|---|---|---|
| `Payroll.WorkDaysPerMonth` | أيام العمل في الشهر | 30 |
| `Payroll.WorkHoursPerDay` | ساعات العمل اليومية | 8 |
| `Payroll.DefaultOvertimeMultiplier` | معامل الإضافي الافتراضي | 1 |
| `Payroll.LateDeductionEnabled` | تفعيل خصم التأخير | true |
| `Payroll.AbsenceDeductionEnabled` | تفعيل خصم الغياب | false |
| `Payroll.RequireOvertimeApproval` | اعتماد الإضافي يدويًا | true |
| `Attendance.WeeklyRestDays` | أيام الراحة (0=الأحد … 5=الجمعة) | 5 |
| `General.CompanyTimeZone` | توقيت الشركة | Arab Standard Time |

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
- خصم الغياب/التأمينات/الضرائب: معطّل افتراضيًا حتى تُفعّله سياسة معتمدة.
- الإضافي: لا يُضاف للراتب قبل اعتماده يدويًا (افتراضيًا).
- لا يُخترع وقت بصمة مفقودة، ولا يُسجَّل غياب قبل استيراد بيانات اليوم.
