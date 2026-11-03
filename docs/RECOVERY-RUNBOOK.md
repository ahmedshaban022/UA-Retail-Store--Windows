# StoreFlow — Recovery Runbook

What to do when something has gone wrong with the till.

This book is written for the person standing in front of the machine, not for a programmer. Each
section says what you will see, what it means, and what to do. Work down the section that matches
what you are seeing.

Two rules before you start:

1. **Do not delete anything in `%APPDATA%\StoreFlow`.** That folder is the shop's ledger, its
   catalogue, its backups and its audit trail. Copy it somewhere else before you touch it.
2. **Make a copy before you change anything.** Open **Backup → Back up now** if the programme will
   still start. If it will not, copy the whole `%APPDATA%\StoreFlow` folder to the desktop with
   File Explorer, and work on the copy.

---

## 1. The programme will not start

**What you see.** Nothing happens when you open it, or a window flashes and closes.

**What to do.**

1. Open it again and wait ten seconds. A programme that was closed while it was writing can take
   a moment.
2. Check that there is free space on the disk. The programme needs room to write.
3. Open `%APPDATA%\StoreFlow\logs` in File Explorer and look at the newest file. The last lines
   usually say what went wrong.
4. If there is a file named `storeflow.db-wal` or `storeflow.db-shm` left over in
   `%APPDATA%\StoreFlow\data`, close the programme completely (make sure it is not still in the
   task bar), then rename those two files to `storeflow.db-wal.old` and `storeflow.db-shm.old`.
   They are the write-ahead log; the database can rebuild them.
5. Still nothing? Go to section 2.

## 2. The database will not open

**What you see.** The programme starts and then says the shop's data cannot be read.

**What it means.** The database file is damaged. This is the case the recovery wizard exists for.

**What to do.**

1. Open **Backup → Recovery** and follow the wizard. It looks for a good backup, checks it, and
   puts it back. The database as it stands is copied aside first, so a wrong restore can be undone.
2. If the wizard finds nothing, look in `%APPDATA%\StoreFlow\backups` for a `.sfbackup` file by
   hand and restore it with **Backup → Restore**.
3. If there are no backups at all, the shop's stock and sales cannot be rebuilt automatically. Go
   to section 7 and rebuild from the paper.

**Before you restore.** A restore puts back the shop as it was when the backup was taken. Anything
sold since then is not in the restored ledger. Write down the sales from that day on paper first,
and put them in again afterwards.

## 3. A sale was interrupted

**What you see.** The machine was shut down, the power went, or the programme was closed while a
sale was being confirmed.

**What it means.** Nothing. A sale is either posted or it is not; there is no half-written sale.

**What to do.**

1. Start the programme normally.
2. Open **Sales** and look at the last invoice.
   - If the invoice is there with the right total, the sale went through. Nothing to do.
   - If there is no invoice, the sale did not go through. Scan it again.
3. If the sale looks wrong, **reverse** the invoice rather than editing it. A reversed invoice
   leaves both the sale and its cancellation in the ledger, so the books still add up. Deleting an
   invoice is not possible on purpose.

## 4. The numbers do not add up

**What you see.** The daily total is not what the drawer held, or the stock report disagrees with
the shelf.

**What to do.**

1. Compare **Sales** for the day with the **Daily sales** report. If they disagree with each
   other, the report is wrong; send a diagnostic bundle (section 8).
2. If they agree with each other but not with the drawer, the drawer is wrong. Count it again and
   record the count.
3. For stock, open **Inventory → Movements** and read the movements for that product. Every
   movement carries a reason and the name of the person who made it.
4. Open **Audit** and look at the same time in the day. It names everything that was done.

## 5. The programme keeps asking for the first-run wizard

**What you see.** It asks for the shop name and the owner's password again.

**What it means.** The database is empty or was replaced.

**What to do.** If you have just restored a backup, this can happen if the backup was taken before
the shop was set up. Check the backup's date. If the backup is older than the setup, the shop was
set up again by hand after that backup was taken, and the new owner account is not in it. Set the
shop up again and add your accounts and catalogue back.

## 6. Moving the shop to another computer

**What to do.**

1. On the old computer: **Backup → Back up now**, and copy the backup file to the new computer.
2. On the new computer: install StoreFlow, and put the backup file in
   `%APPDATA%\StoreFlow\backups`.
3. Open **Backup → Restore** and restore it.
4. Check **Sales** and **Inventory** before you open the till.

The shop's media — the product photographs — and the logo are not in the backup. Copy
`%APPDATA%\StoreFlow\media` and `%APPDATA%\StoreFlow\brand` across by hand.

## 7. Rebuilding the shop by hand

This is the last resort, for when there is no backup at all. It takes a morning.

1. **Make a catalogue.** Write out every product: the code you use on the barcode labels, the name
   in English and in Arabic, the category, the unit, the selling price and the cost price. Add
   them one by one in **Products → New**. If you have a spreadsheet, use **Import** instead.
2. **Put the stock back.** For each product, record what is on the shelf and its cost in
   **Inventory → Stock count**, then have somebody else approve and post the count. Use
   **Opening stock** instead if the count has never been done.
3. **Record the sales you are missing.** Go through the paper receipts and the cash book, and
   enter the day's sales as returns and corrections, with the reason spelled out. A sale that is
   entered by hand is not the same as a sale that was scanned, and the shop owner should know which
   is which.
4. **Check the drawer** against what is recorded, and write the difference down.

## 8. Sending a diagnostic bundle

**What you do.** **Settings → Diagnostics**, then send the file it produces.

**What is in it.** The version of the programme, the log files, and the health of the database.

**What is not in it.** The customers' names, the prices, and the money.

**Read it before you send it.** It is a file about your shop, and it is worth a glance.

---

---

## بالعربية

## ١. البرنامج لا يفتح

**ما تراه.** لا يحدث شيء عند فتحه، أو تظهر نافذة وتختفي.

**ما تفعله.**

١. افتحه مرة أخرى وانتظر عشر ثوانٍ. البرنامج الذي أُغلق أثناء الكتابة قد يستغرق لحظة.
٢. تأكد أن هناك مساحة كافية على القرص.
٣. افتح `%APPDATA%\StoreFlow\logs` في مستكشف الملفات وافتح أحدث ملف. آخر سطرين فيه يقولان عادةً
   ما الذي حدث.
٤. إن وجدت ملفًا اسمه `storeflow.db-wal` أو `storeflow.db-shm` في `%APPDATA%\StoreFlow\data`،
   فأغلق البرنامج تمامًا، ثم أعِد تسمية الملفين إلى `storeflow.db-wal.old` و`storeflow.db-shm.old`.
   هما سجل الكتابة المسبقة، ويمكن لقاعدة البيانات أن تعيد بناءهما.
٥. إن لم ينجح شيء، انتقل إلى القسم ٢.

## ٢. قاعدة البيانات لا تفتح

**ما تراه.** يبدأ البرنامج ثم يقول إن بيانات المتجر لا يمكن قراءتها.

**ما تفعله.**

١. افتح **النسخ الاحتياطي → الإنعاش** واتبع المعالج. يبحث عن نسخة سليمة، ويفحصها، ويعيدها.
   وتُؤخذ نسخة من قاعدة البيانات الحالية أولًا، حتى يمكن التراجع عن استعادة خاطئة.
٢. إن لم يجد المعالج شيئًا، ابحث في `%APPDATA%\StoreFlow\backups` عن ملف `.sfbackup` باليد
   واستعده من **النسخ الاحتياطي → استعادة**.
٣. إن لم تكن هناك نسخ إطلاقًا، لا يمكن إعادة بناء مخزون المتجر ومبيعاته تلقائيًا. انتقل إلى
   القسم ٧ وأعد بناء المتجر من الورق.

**قبل الاستعادة.** الاستعادة تُرجع المتجر كما كان يوم أخذ النسخة. ما بيع بعدها ليس في الدفتر
المستعاد. اكتب مبيعات ذلك اليوم على الورق أولًا، وأدخلها بعدها.

## ٣. انقطع بيع

**ما تراه.** أُغلقت الآلة، أو انقطعت الكهرباء، أو أُغلق البرنامج أثناء تأكيد بيع.

**ما تعنيه.** لا شيء. البيع إمّا مُرحَّل وإمّا لم يُرحَّل؛ لا يوجد بيع نصف مكتوب.

**ما تفعله.**

١. ابدأ البرنامج كالمعتاد.
٢. افتح **المبيعات** وانظر آخر فاتورة:
   - إن كانت الفاتورة موجودة وإجماليها صحيح، فالبيع تم. لا شيء لتفعله.
   - إن لم تكن هناك فاتورة، فالبيع لم يتم. أعد مسحه.
٣. إن كان البيع خطأً، **اعكس** الفاتورة بدل تعديلها. الفاتورة المعكوسة تُبقي البيع وإلغاءه
   معًا في الدفتر، فتبقى الكتب متوازنة. حذف فاتورة غير ممكن عن قصد.

## ٤. الأرقام لا تتطابق

**ما تفعله.**

١. قارن **المبيعات** لليوم بتقرير **المبيعات اليومية**. إن اختلفا، فالتقرير هو الخطأ: أرسل حزمة
   تشخيص (القسم ٨).
٢. إن تطابقا لكن لم يطابقا الصندوق، فالصندوق هو الخطأ. عُدّه مرة أخرى وسجّل العدد.
٣. للمخزون، افتح **المخزون → الحركات** واقرأ حركات المنتج. كل حركة تحمل سببًا واسم من أجراها.
٤. افتح **سجل التدقيق** وانظر في الوقت نفسه من اليوم.

## ٥. البرنامج يطلب معالج الإعداد من جديد

**ما تعناه.** قاعدة البيانات فارغة أو استُبدلت.

**ما تفعله.** إن كنت استعدت للتو، فقد تكون النسخة الاحتياطية أقدم من إعداد المتجر. تحقّق من تاريخ
النسخة. إن كانت أقدم، فالمتجر أُعدّ يدويًا بعد أخذها، وحساب المالك الجديد ليس فيها. أعد
الإعداد، وأعد إدخال الحسابات والكتالوج.

## ٦. نقل المتجر إلى جهاز آخر

١. على الجهاز القديم: **النسخ الاحتياطي → نسخ احتياطي الآن**، وانسخ ملف النسخة إلى الجهاز الجديد.
٢. على الجهاز الجديد: ثبّت StoreFlow، وضع ملف النسخة في `%APPDATA%\StoreFlow\backups`.
٣. افتح **النسخ الاحتياطي → استعادة** واستعدها.
٤. افحص **المبيعات** و**المخزون** قبل فتح الصندوق.

صور المنتجات والشعار ليست في النسخة. انسخ `%APPDATA%\StoreFlow\media` و`%APPDATA%\StoreFlow\brand`
باليد.

## ٧. إعادة بناء المتجر يدويًا

هذا آخر حل، حين لا توجد نسخة احتياطية إطلاقًا. يستغرق صباحًا.

١. **أعد الكتالوج.** اكتب كل منتج: الرمز على ملصق الباركود، الاسم بالعربية والإنجليزية، الصنف،
   الوحدة، سعر البيع وسعر التكلفة. أضفهم واحدًا واحدًا في **المنتجات → جديد**. إن كان لديك جدول
   بيانات فاستعمل **استيراد** بدل ذلك.
٢. **أعد المخزون.** لكل منتج، سجّل ما هو على الرف وتكلفته في **المخزون → جرد المخزون**، ثم لي认تمه
   غيرك ويرحّله. استعمل **المخزون الافتتاحي** إن لم يُجرَ جرد من قبل.
٣. **سجّل المبيعات الناقصة.** راجع الإيصالات الورقية ودفتر النقد، وأدخل مبيعات اليوم كإرجاعات
   وتصحيحات، مع كتابة السبب. البيع المُدخل يدويًا ليس ك البيع الممسوح، ويجب أن يعرف صاحب
   المتجر الفرق.
٤. **قارن الصندوق** بما هو مسجَّل، واكتب الفرق.

## ٨. إرسال حزمة تشخيص

افتح **الإعدادات → التشخيص** وأرسل الملف. فيه الإصدار والسجلات وحالة قاعدة البيانات، وليس فيه
أسماء العملاء ولا الأسعار ولا الأموال. اقرأه قبل أن ترسله.
