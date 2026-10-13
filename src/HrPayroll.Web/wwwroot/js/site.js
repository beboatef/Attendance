/* ============================================================
   سلوكيات الواجهة: تبويبات، بحث فوري في الجداول، تأكيد العمليات،
   مؤشر تحميل عند الإرسال، وطباعة.
   ============================================================ */
(function () {
    'use strict';

    // ---------- التبويبات ----------
    document.querySelectorAll('.tabs').forEach(function (tabs) {
        tabs.addEventListener('click', function (e) {
            var btn = e.target.closest('button[data-tab]');
            if (!btn) return;
            var group = tabs.dataset.group;
            tabs.querySelectorAll('button').forEach(function (b) { b.classList.remove('active'); });
            btn.classList.add('active');
            document.querySelectorAll('[data-pane-group="' + group + '"]').forEach(function (p) {
                p.classList.toggle('active', p.dataset.pane === btn.dataset.tab);
            });
        });
    });

    // ---------- البحث الفوري داخل الجداول ----------
    document.querySelectorAll('input[data-table-filter]').forEach(function (input) {
        var table = document.querySelector(input.dataset.tableFilter);
        if (!table) return;
        input.addEventListener('input', function () {
            var q = input.value.trim().toLowerCase();
            var shown = 0;
            table.querySelectorAll('tbody tr').forEach(function (tr) {
                if (tr.dataset.emptyRow === '1') return;
                var match = !q || tr.textContent.toLowerCase().indexOf(q) > -1;
                tr.style.display = match ? '' : 'none';
                if (match) shown++;
            });
            var empty = table.querySelector('tr[data-empty-row="1"]');
            if (empty) empty.style.display = shown === 0 ? '' : 'none';
        });
    });

    // ---------- تأكيد العمليات الحساسة ----------
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            e.preventDefault();
            return;
        }
        // منع الإرسال المزدوج وإظهار مؤشر الانتظار
        var submitBtn = form.querySelector('button[type=submit]:not([data-no-spinner])');
        if (submitBtn && !submitBtn.disabled) {
            submitBtn.dataset.originalText = submitBtn.innerHTML;
            submitBtn.disabled = true;
            submitBtn.innerHTML = '<span class="spinner"></span> جارٍ التنفيذ...';
            // إعادة التمكين إن ألغى المتصفح الإرسال بسبب التحقق
            setTimeout(function () {
                if (document.readyState === 'complete' && submitBtn.disabled && !form.checkValidity()) {
                    submitBtn.disabled = false;
                    submitBtn.innerHTML = submitBtn.dataset.originalText || 'إرسال';
                }
            }, 250);
        }
    }, true);

    // ---------- إخفاء التنبيهات تلقائيًا ----------
    document.querySelectorAll('.alert[data-auto-dismiss]').forEach(function (a) {
        setTimeout(function () { a.style.display = 'none'; }, parseInt(a.dataset.autoDismiss, 10) || 6000);
    });

    // ---------- إغلاق التنبيهات ----------
    document.querySelectorAll('.alert .close').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var alert = btn.closest('.alert');
            if (alert) alert.style.display = 'none';
        });
    });

    // ---------- تحديد الكل في جداول الاختيار ----------
    document.querySelectorAll('[data-check-all]').forEach(function (master) {
        var group = document.querySelectorAll(master.dataset.checkAll);
        master.addEventListener('change', function () {
            group.forEach(function (cb) { cb.checked = master.checked; });
        });
    });
})();
