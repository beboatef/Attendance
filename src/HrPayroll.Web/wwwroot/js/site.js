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


    // ---------- القائمة الجانبية: الطي والفتح ----------
    var shell = document.getElementById('appShell');
    var sideToggle = document.getElementById('sideToggle');
    var mobileMenu = document.getElementById('mobileMenu');
    var backdrop = document.getElementById('sideBackdrop');

    if (shell && sideToggle) {
        try {
            if (localStorage.getItem('hr.sidebar.collapsed') === '1') shell.classList.add('collapsed');
        } catch (e) { /* التخزين المحلي غير متاح */ }

        sideToggle.addEventListener('click', function () {
            if (window.matchMedia('(max-width: 992px)').matches) {
                shell.classList.remove('sidebar-open');
                return;
            }
            shell.classList.toggle('collapsed');
            try {
                localStorage.setItem('hr.sidebar.collapsed', shell.classList.contains('collapsed') ? '1' : '0');
            } catch (e) { /* تجاهل */ }
        });
    }

    if (shell && mobileMenu) {
        mobileMenu.addEventListener('click', function () { shell.classList.add('sidebar-open'); });
    }
    if (shell && backdrop) {
        backdrop.addEventListener('click', function () { shell.classList.remove('sidebar-open'); });
    }
    // عند طي القائمة: فتح المجموعة يوسّعها أولًا حتى لا تختفي الروابط
    document.querySelectorAll('.side-nav summary').forEach(function (s) {
        s.addEventListener('click', function () {
            if (shell && shell.classList.contains('collapsed') && !window.matchMedia('(max-width: 992px)').matches) {
                shell.classList.remove('collapsed');
                try { localStorage.setItem('hr.sidebar.collapsed', '0'); } catch (e) { /* تجاهل */ }
            }
        });
    });

    // ---------- منع فقدان التعديلات عند وجود نماذج بها data-guard ----------
    document.querySelectorAll('form[data-dirty-guard]').forEach(function (form) {
        form.addEventListener('submit', function () { form.dataset.submitting = '1'; });
    });
    // ---------- تحديد الكل في جداول الاختيار ----------
    document.querySelectorAll('[data-check-all]').forEach(function (master) {
        var group = document.querySelectorAll(master.dataset.checkAll);
        master.addEventListener('change', function () {
            group.forEach(function (cb) { cb.checked = master.checked; });
        });
    });

    // ---------- إظهار/إخفاء كلمة المرور (صفحة الدخول) ----------
    document.querySelectorAll('[data-pw-toggle]').forEach(function (btn) {
        var input = document.getElementById(btn.dataset.pwToggle);
        if (!input) return;
        btn.addEventListener('click', function () {
            var showing = input.type === 'text';
            input.type = showing ? 'password' : 'text';
            btn.classList.toggle('is-on', !showing);
            btn.setAttribute('aria-pressed', showing ? 'false' : 'true');
            var label = showing ? 'إظهار كلمة المرور' : 'إخفاء كلمة المرور';
            btn.setAttribute('aria-label', label);
            btn.setAttribute('title', label);
            input.focus();
        });
    });
})();
