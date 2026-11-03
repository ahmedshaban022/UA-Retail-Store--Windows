# StoreFlow — User Guide

StoreFlow is a till, a stock book and a set of reports, in one program that runs on the computer
in your shop and keeps everything on that computer.

This guide is for the person who owns the shop and for the people who work there. It explains
what each screen does and where the shop's data is kept.

If something has gone wrong — the program will not start, the numbers look wrong, a restore did
not do what you expected — look in [RECOVERY-RUNBOOK.md](RECOVERY-RUNBOOK.md). That book is
written for the moment when something is broken.

---

# English

## 1. Installing StoreFlow

You need a Windows 10 or Windows 11 computer. You do **not** need an administrator account: the
installer is a per-user install and installs into your own user folder.

1. Copy `StoreFlow-<version>-x64.msi` onto the computer that will run the till.
2. Double-click it.
3. Follow the steps. The installer asks where to put the programme; the default is fine.
4. Open **StoreFlow** from the Start menu.

The programme is installed under `%LOCALAPPDATA%\Programs\StoreFlow`.

### Where the shop's data is kept

The shop's data is **not** in the programme folder. It is in:

```
%APPDATA%\StoreFlow
```

Inside that folder:

| Folder | What is in it |
|---|---|
| `data\` | The database — the catalogue, the sales, the stock ledger |
| `backups\` | The automatic backups |
| `data\restore-points\` | The copy of the database taken before each restore, so a restore can be undone |
| `themes\` | Your own colour themes |
| `logs\` | The running log the application writes |
| `media\` | Product photographs |
| `brand\` | The logo |
| `diagnostics\` | Diagnostic bundles, when you send one to support |

Because the data lives outside the programme folder, **upgrading StoreFlow keeps the shop's
ledger, and uninstalling StoreFlow keeps it too.** Nothing about your sales is thrown away by an
upgrade. If you do want the data gone, delete the `%APPDATA%\StoreFlow` folder yourself — the
guide in section 12 says how.

## 2. The first time you open it

The first time, StoreFlow asks three things.

**Step 1 — the shop.** The name of the shop in English and in Arabic, the address and the phone
number. You can leave the address and the phone empty and fill them in later.

**Step 2 — the owner.** The name of the person who will own the shop, a username and a password.
This account can do everything. Choose a password you will remember: there is no way to reset it
from inside the program.

**Step 3 — language and appearance.** Arabic or English, and light, dark or high-contrast. Both
can be changed later in **Settings**.

After the third step the application opens on the sign-in screen.

## 3. Signing in

Type the username and the password. After a few minutes of no typing the program locks itself,
and you sign in again. That is deliberate: a locked till cannot be used by whoever walked past
it.

## 4. The screens

The left-hand menu is the whole application.

### Dashboard

Today's takings: net sales, gross profit, the number of invoices, and the cash in the drawer.
There is a **Refresh** button — press it after a busy morning rather than expecting it to update
by itself.

### Point of sale

This is where a sale is made.

- **Scan or search** is the box at the top. A barcode scanner types into it and presses Enter.
  You can also type part of a name or a SKU and press Enter to search.
- Scanned items land in the **cart**. The quantity of a line can be typed straight into the cart.
- **Cash received** — type what the customer handed over. The **change** is worked out for you.
- **Invoice discount** — a discount on the whole sale rather than on one line. If the discount is
  over the limit your shop has set, it is sent for approval instead of being applied.
- **Confirm sale** posts the sale. Nothing is written to the ledger until you press it.
- **Hold** puts the cart aside and lets you serve the next customer. **Resume** brings a held
  cart back. **Clear cart** empties the cart without selling it.
- The receipt can be printed when the sale is posted.

Pressing Enter in the scan box scans. It does not confirm the sale, so a cashier cannot sell
twice by pressing Enter twice.

### Products

The catalogue. **New** opens the editor; the editor takes a SKU, a name in English and in Arabic,
a category, a unit, a tax category, a selling price, a cost price, a reorder level, a barcode
and any number of extra barcodes. A product can be switched off without being deleted, so its
history stays intact.

**Import** brings products in from a CSV file. **Export** writes the list you are looking at —
with the filters you have set — out to a CSV file.

### Categories, Units and Suppliers

The reference data the catalogue uses. Each one can be switched off rather than deleted.

### Purchases

Goods arriving from a supplier.

1. **New** opens the editor: the supplier, the date, the lines, and an optional discount.
2. **Save and post** records the purchase and puts the goods into stock.
3. A purchase that is only saved, and not posted, does not change the stock.

The stock comes in at the cost you typed. When the same product is bought again at a different
price, the average cost moves.

### Inventory

**Balances** is what is on the shelf now. **Movements** is the history: every sale, receipt,
return and adjustment that touched a product, with the reason.

**Stock count** is a session, not a single action:

1. **New count** — list the products and type what you counted.
2. **Submit** — send it for approval. Counting on its own changes nothing.
3. **Approve** — somebody other than the counter signs it off, with a reason.
4. **Post** — the difference becomes an adjustment in the ledger.

**Adjustment** corrects the stock by hand. A small adjustment is posted straight away; a large
one, or one worth a lot of money, is sent for approval first.

### Sales

The register: every invoice, what it contained and who served it.

**Return** returns part of an invoice. You choose how many, and whether the goods go back on the
shelf or are written off as damaged.

**Reverse** cancels a whole invoice. The original invoice is never edited or deleted; it is
cancelled by a second, linked document. That is why the ledger always adds up.

### Reports

Daily sales, sales by product, stock valuation, takings by cashier, and cash counted in the
drawer. **Run** produces the report; **Export** writes it to a CSV file, in Arabic or English,
with the headings the locale expects.

### Approvals

Discounts over the limit and large stock adjustments wait here. **Approve** or **reject**, with
a reason. Nobody may approve their own request.

### Users

Create the accounts for your staff, give each one a role, and switch an account off when that
person leaves. The four roles are:

| Role | Can do |
|---|---|
| **Owner** | Everything |
| **Manager** | The catalogue, stock, purchases, sales, reports, settings, backups and the audit trail |
| **Cashier** | Sell, look at the catalogue and the stock, see the dashboard |
| **Inventory clerk** | The catalogue, the stock, purchases and stock adjustments |

### Settings

The name of your shop in English and Arabic, the language, and the appearance.

### Backup

- **Back up now** — takes a backup straight away.
- **Schedule** — a daily backup, a weekly backup, or both, with how many of each to keep.
- **Target** — where the backups go: a folder on this computer, or a cloud bucket.
- **Restore** — put a backup back. The application checks the backup first, shows you what is in
  it, and asks you to confirm. The database as it stands is copied aside first, so a restore can
  be undone.
- **Recovery** — the wizard for the bad days: a database that will not open, a shop that needs
  its stock back, a machine that has been rebuilt.

### Audit

Who did what, and when. Filter by date, by the kind of record, or by the person.

## 5. The everyday routine

**Opening.** Sign in. Take the cash in the drawer and record it: **Inventory → Cash in the
drawer**.

**During the day.** Sell. Record purchases as the goods arrive. If something is damaged or
missing, adjust the stock and say why.

**Closing.** Count the drawer and record the count. Go to **Backup** and press **Back up now**,
or leave the daily schedule to do it.

**Weekly.** Look at the reports. Check the reorder level on the products that are running low.

**Monthly.** Check that the automatic backups are where you expect them, and that the cloud copy
(if you use one) is current.

## 6. Working with the keyboard

- **Tab** moves between the fields. **Shift+Tab** moves back.
- **Enter** in the scan box scans the barcode and adds the item.
- **Escape** closes a dialog without saving it.
- Every button can be reached with the keyboard. The scan box takes the focus back each time the
  point-of-sale screen appears, which is what you want at a till.

The program works with a screen reader. Every field has a name that is read out, in the language
the program is set to.

## 7. If the program will not start

1. Try again. A program that was closed while it was writing can refuse to open for a moment.
2. Open `docs/RECOVERY-RUNBOOK.md` and follow the section on a database that will not open.
3. If you have a backup, restore it. Nothing is lost that was not on the shelf when the backup
   was taken.

## 8. If the numbers look wrong

1. Compare **Sales** for the day with the **Daily sales** report.
2. Check the **Movements** list in **Inventory** for the product, and read the reason on each
   movement. Every movement carries a reason and the person who made it.
3. Check **Audit** for anything that happened at that time.

## 9. Who can do what

Permissions follow the role. A cashier cannot change a price, and cannot approve a discount.
The limit on a discount is set in **Settings**, and is also enforced in the database, not only in
the screens.

## 10. Getting help

When you ask for help, send a **diagnostic bundle**: **Settings → Diagnostics**. It gathers the
version, the logs and the health of the database into one file. It does **not** contain the
customer names, the prices or the money — but read it before you send it.

## 11. Keeping the shop's data safe

- The daily backup is the one that matters. Check that it runs.
- A backup on the same computer as the shop protects you from a mistake, not from a stolen
  machine. Copy the backups to somewhere else as well.
- The cloud target, if you use one, puts a copy off the till.

## 12. Removing StoreFlow

1. **Settings → Apps → Installed apps → StoreFlow → Uninstall.**
2. The programme and the shortcuts are removed. **Your data is left where it is.**
3. To remove the data as well, delete the `%APPDATA%\StoreFlow` folder.

Step 3 cannot be undone. Do not do it until you are sure you do not want the shop back.

---
---

# العربية

## ١. تثبيت StoreFlow

تحتاج إلى جهاز بنظام Windows 10 أو Windows 11. **لا تحتاج** إلى حساب مدير: التثبيت يعمل بحساب
المستخدم نفسه، ويضع البرنامج في مجلد المستخدم.

١. انسخ ملف `StoreFlow-<version>-x64.msi` إلى الجهاز الذي سيعمل عليه البرنامج.
٢. انقر نقرًا مزدوجًا على الملف.
٣. اتبع الخطوات. يسألك المثبّت عن مكان البرنامج، والوضع الافتراضي مناسب.
٤. افتح **StoreFlow** من قائمة ابدأ.

يُثبَّت البرنامج في `%LOCALAPPDATA%\Programs\StoreFlow`.

### أين تُحفظ بيانات المتجر

بيانات المتجر **ليست** داخل مجلد البرنامج. 它们 في:

```
%APPDATA%\StoreFlow
```

وداخل هذا المجلد:

| المجلد | ما فيه |
|---|---|
| `data\` | قاعدة البيانات: الكتالوج والمبيعات ودفتر المخزون |
| `backups\` | النسخ الاحتياطية التلقائية |
| `data\restore-points\` | نسخة من قاعدة البيانات تُؤخذ قبل كل استعادة، حتى يمكن التراجع عن الاستعادة |
| `themes\` | ألوانك الخاصة |
| `logs\` | سجل التشغيل الذي يكتبه البرنامج |
| `media\` | صور المنتجات |
| `brand\` | الشعار |
| `diagnostics\` | حزم التشخيص عند طلب الدعم |

ولأن البيانات خارج مجلد البرنامج، **فإن تحديث StoreFlow لا يمسّ دفتر حساباتك، وإزالة StoreFlow
لا تمسّه أيضًا.** لا شيء من مبيعاتك يضيع بسبب التحديث. وإذا أردت حذف البيانات فاحذف مجلد
`%APPDATA%\StoreFlow` بنفسك.

## ٢. أول مرة تفتح فيها البرنامج

في أول مرة يسألك StoreFlow ثلاثة أشياء.

**الخطوة ١ — المتجر.** اسم المتجر بالإنجليزية وبالعربية، والعنوان ورقم الهاتف. يمكنك ترك العنوان
والهاتف فارغين وملءهما لاحقًا.

**الخطوة ٢ — المالك.** اسم من يملك المتجر، واسم مستخدم، وكلمة مرور. هذا الحساب يستطيع كل شيء.
اختر كلمة مرور تتذكرها: لا يمكن إعادة تعيينها من داخل البرنامج.

**الخطوة ٣ — اللغة والمظهر.** العربية أو الإنجليزية، ثم الفاتح أو الداكن أو عالي التباين. ويمكن
تغيير两者 لاحقًا من **الإعدادات**.

بعد الخطوة الثالثة ينفتح البرنامج على شاشة تسجيل الدخول.

## ٣. تسجيل الدخول

اكتب اسم المستخدم وكلمة المرور. وبعد بضع دقائق من عدم الكتابة يقفل البرنامج نفسه، وعليك تسجيل
الدخول من جديد. هذا مقصود: الصندوق المقفل لا يستطيع أن يستخدمه من مرّ بجانبه.

## ٤. الشاشات

القائمة على اليسار هي البرنامج كله.

### لوحة المعلومات

مبيعات اليوم: صافي المبيعات، إجمالي الربح، عدد الفواتير، والنقد في الصندوق. يوجد زر **تحديث** —
اضغطه بعد صباح مزدحم بدل أن تنتظر تحديثًا من تلقاء نفسه.

### نقطة البيع

هنا تُنجز عملية البيع.

- **امسح أو ابحث** هو الصندوق في الأعلى. قارئ الباركود يكتب فيه ويضغط Enter. يمكنك أيضًا كتابة
  جزء من الاسم أو رمز المنتج والضغط على Enter للبحث.
- الأصناف الممسوحة تظهر في **السلة**. ويمكن كتابة الكمية مباشرة داخل السلة.
- **النقدية المستلمة** — اكتب ما دفعه العميل، ويحسب لك البرنامج **الباقي**.
- **خصم على الفاتورة** — خصم على البيع كله لا على سطر واحد. إذا تجاوز الخصم الحد الذي ضبطته،
  يذهب إلى الموافقات بدل أن يُطبَّق.
- **تأكيد البيع** يُنجز البيع. لا يُكتب شيء في الدفتر قبل أن تضغطه.
- **تعليق** يضع السلة جانبًا لتخدم العميل التالي، و**استرجاع** يُعيدها، و**إفراغ السلة** يفرغها
  دون بيع.
- يمكن طباعة الإيصال بعد إتمام البيع.

يُضيف الضغط على Enter في صندوق المسح الباركود إلى السلة. وهو لا يؤكد البيع، فلا يستطيع الكاشير أن
بيع مرتين بالضغط على Enter مرتين.

### المنتجات

الكتالوج. **جديد** يفتح المحرّر: رمز المنتج، الاسم بالإنجليزية والعربية، الصنف، الوحدة، فئة
الضريبة، سعر البيع، سعر التكلفة، حد إعادة الطلب، باركود، وأي عدد من الباركودات الإضافية. ويمكن
إيقاف المنتج دون حذفه، فيبقى تاريخه سليمًا.

**استيراد** يجلب المنتجات من ملف CSV، و**تصدير** يكتب القائمة التي تراها — مع عوامل التصفية
التي ضبطتها — إلى ملف CSV.

### الأصناف والوحدات والموردون

البيانات المرجعية التي يستعملها الكتالوج. كل منها يمكن إيقافه بدل حذفه.

### المشتريات

البضاعة التي تصل من المورد.

١. **جديد** يفتح المحرّر: المورد، والتاريخ، والسطور، وخصم اختياري.
٢. **حفظ وترحيل** يسجّل الشراء ويضيف البضاعة إلى المخزون.
٣. الشراء المحفوظ فقط، والمُرحَّل غيره، لا يغيّر المخزون.

تدخل البضاعة بالتكلفة التي كتبتها. وإذا اشتريت المنتج نفسه بسعر مختلف، تحرّك متوسط التكلفة.

### المخزون

**الأرصدة** ما هو على الرف الآن. و**الحركات** هو التاريخ: كل بيع واستلام وإرجاع وتسوية لمست
منتج، مع السبب.

**جرد المخزون** جلسة وليست إجراءً واحدًا:

١. **جرد جديد** — سجّل المنتجات واكتب ما عددته.
٢. **إرسال** — أرسله للموافقة. الجرد وحده لا يغيّر شيئًا.
٣. **اعتماد** — يعتمد شخص غير من جرد، مع ذكر السبب.
٤. **ترحيل** — يصبح الفرق تسوية في الدفتر.

**التسوية** تصحّح المخزون يدويًا. التسوية الصغيرة تُرحَّل فورًا، أما الكبيرة أو ذات القيمة
الكبيرة فتذهب للموافقة أولًا.

### المبيعات

سجل المبيعات: كل فاتورة ومحتوياتها ومن قدّمها.

**إرجاع** يرجع جزءًا من فاتورة: تختار الكمية، وهل تعود البضاعة إلى الرف أم تُكتب كتالف.
**عكس** يلغي فاتورة كاملة. الفاتورة الأصلية لا تُعدَّل ولا تُحذف، بل تُلغى بمستند ثانٍ مرتبط
بها. ولهذا يبقى الدفتر متوازنًا دائمًا.

### التقارير

المبيعات اليومية، والمبيعات حسب المنتج، وتقييم المخزون، والمبيعات حسب الكاشير، والنقد المعدود
في الصندوق. **تشغيل** يُخرج التقرير، و**تصدير** يكتبه إلى ملف CSV بالعربية أو الإنجليزية
بعناوين يفهمها الإصدار الصحيح.

### الموافقات

الخصومات التي تتجاوز الحد والتسويات الكبيرة تنتظر هنا. **اعتماد** أو **رفض**، مع سبب. ولا يجوز
لأحد أن يعتمد طلبه هو.

### المستخدمون

أنشئ حسابات الموظفين، وأعطِ كل واحد دورًا، وأوقف الحساب عند مغادرة الموظف. الأدوار الأربعة:

| الدور | ما يستطيع |
|---|---|
| **المالك** | كل شيء |
| **المدير** | الكتالوج والمخزون والمشتريات والمبيعات والتقارير والإعدادات والنسخ الاحتياطي وسجل التدقيق |
| **الكاشير** | البيع، والاطلاع على الكتالوج والمخزون، ولوحة المعلومات |
| **أمين المخزون** | الكتالوج والمخزون والمشتريات وتسويات المخزون |

### الإعدادات

اسم متجرك بالعربية والإنجليزية، واللغة، والمظهر.

### النسخ الاحتياطي

- **نسخ احتياطي الآن** — يأخذ نسخة من المتجر فورًا.
- **الجدولة** — نسخ يومي، أو أسبوعي، أو كليهما، مع عدد كل منهما المحتفظ به.
- **الوجهة** — أين تذهب النسخ: مجلد على هذا الجهاز، أو سحابة.
- **استعادة** — ترجع نسخة احتياطية. يفحص البرنامج النسخة أولًا، ويعرض ما فيها، ويطلب التأكيد.
  وقبل الاستعادة تُؤخذ نسخة من قاعدة البيانات الحالية حتى يمكن التراجع.
- **الإنعاش** — المعالج للأيام السيئة: قاعدة بيانات لا تفتح، أو متجر يحتاج مخزونه، أو جهاز أُعيد
  بناؤه.

### سجل التدقيق

من فعل ماذا ومتى. صفِّ حسب التاريخ أو نوع السجل أو الشخص.

## ٥. الروتين اليومي

**عند الفتح.** سجّل الدخول. خذ نقد الصندوق وسجّله: **المخزون → النقد في الصندوق**.

**خلال اليوم.** بِع. سجّل المشتريات عند وصول البضاعة. وإذا تلف شيء أو نقص، سوِّ المخزون واذكر
السبب.

**عند الإغلاق.** عدّ الصندوق وسجّل العدد. اذهب إلى **النسخ الاحتياطي** واضغط **نسخ احتياطي
الآن**، أو اترك الجدولة اليومية تفعل ذلك.

**أسبوعيًا.** راجع التقارير. تحقق من حد إعادة الطلب للمنتجات التي قاربت على النفاد.

**شهريًا.** تأكد أن النسخ الاحتياطية في مكانها المتوقّع، وأن النسخة السحابية — إن كنت تستعمل
سحابة — محدَّثة.

## ٦. العمل بلوحة المفاتيح

- **Tab** ينتقل بين الحقول، و**Shift+Tab** يعود.
- **Enter** في صندوق المسح يقرأ الباركود ويضيف الصنف.
- **Escape** يغلق النافذة دون حفظ.
- كل زر يمكن الوصول إليه بلوحة المفاتيح. ويعود التركيز إلى صندوق المسح كلما ظهرت شاشة نقطة
  البيع، وهذا ما تريده عند الصندوق.

يعمل البرنامج مع قارئ الشاشة. لكل حقل اسم يُنطق، باللغة التي ضبطتها.

## ٧. إذا لم يفتح البرنامج

١. جرّب مرة أخرى. برنامج أُغلق أثناء الكتابة قد يرفض أن يفتح للحظة.
٢. افتح `docs/RECOVERY-RUNBOOK.md` واتبع قسم «قاعدة بيانات لا تفتح».
٣. إن كان لديك نسخة احتياطية، استعدها. لا تضيع إلا ما لم يكن على الرف وقت أخذ النسخة.

## ٨. إذا بدت الأرقام خاطئة

١. قارن **المبيعات** لليوم بتقرير **المبيعات اليومية**.
٢. راجع قائمة **الحركات** في **المخزون** للمنتج، واقرأ سبب كل حركة. كل حركة تحمل سببًا والشخص
   الذي أجراها.
٣. راجع **سجل التدقيق** لأي شيء حدث في ذلك الوقت.

## ٩. من يستطيع أن يفعل ماذا

الصلاحيات تتبع الدور. الكاشير لا يستطيع تغيير سعر ولا اعتماد خصم. وحد الخصم مضبوط في
**الإعدادات**، وهو مفروض في قاعدة البيانات أيضًا، لا في الشاشات وحدها.

## ١٠. طلب المساعدة

عند طلب المساعدة، أرسل **حزمة تشخيص**: **الإعدادات → التشخيص**. تجمع الحزمة الإصدار والسجلات
وحالة قاعدة البيانات في ملف واحد. وهي **لا** تحتوي أسماء العملاء ولا الأسعار ولا الأموال — لكن
اقرأها قبل إرسالها.

## ١١. حماية بيانات المتجر

- النسخة اليومية هي المهمة. تأكد أنها تعمل.
- النسخة على نفس جهاز المتجر تحميك من الخطأ، لا من سرقة الجهاز. انسخ النسخ إلى مكان آخر أيضًا.
- وجهة السحابة، إن كنت تستعملها، تضع نسخة خارج الصندوق.

## ١٢. إزالة StoreFlow

١. **الإعدادات → التطبيقات → التطبيقات المثبَّتة → StoreFlow → إزالة.**
٢. يُزال البرنامج واختصاراته. **وتبقى بياناتك مكانها.**
٣. ولإزالة البيانات أيضًا، احذف مجلد `%APPDATA%\StoreFlow`.

الخطوة ٣ لا يمكن التراجع عنها. لا تفعلها حتى تتأكد أنك لا تريد المتجر.
