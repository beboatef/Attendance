/* ============================================================
   سلوكيات الواجهة: تبويبات، بحث فوري في الجداول، تأكيد العمليات،
   مؤشر تحميل عند الإرسال، وطباعة.
   ============================================================ */
(function () {
    'use strict';

    // ---------- السمة (فاتح / داكن): تبديل وحفظ الاختيار ----------
    // مسجَّل أولًا حتى يعمل الزر حتى لو تعطّل أي سلوك لاحق بخطأ غير متوقع.
    var themeToggle = document.getElementById('themeToggle');
    if (themeToggle && !themeToggle.dataset.themeBound) {
        themeToggle.dataset.themeBound = '1';
        themeToggle.addEventListener('click', function () {
            var root = document.documentElement;
            var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-theme', next);
            try { localStorage.setItem('hr.theme', next); } catch (e) { /* التخزين المحلي غير متاح */ }
        });
    }

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

    // ---------- توسيع/طي كل المجموعات (تقرير الأقسام) ----------
    document.querySelectorAll('[data-expand-all]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            document.querySelectorAll(btn.dataset.expandAll).forEach(function (d) { d.open = true; });
        });
    });
    document.querySelectorAll('[data-collapse-all]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            document.querySelectorAll(btn.dataset.collapseAll).forEach(function (d) { d.open = false; });
        });
    });

    // ---------- طباعة: تفتح المجموعات المطوية قبل الطباعة ----------
    document.querySelectorAll('[data-print]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            if (btn.dataset.printExpand) {
                document.querySelectorAll(btn.dataset.printExpand).forEach(function (d) { d.open = true; });
            }
            window.print();
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

    // ---------- مناطق رفع ملفات البصمة (الدخول / الخروج) ----------
    // تعرض اسم الملف وحجمه وتاريخه وحالته، وتمنع الإرسال قبل اختيار ملف واحد على الأقل.
    (function () {
        var form = document.querySelector('form[data-file-zones]');
        if (!form) return;

        var errorBox = form.querySelector('#importFormError');

        function formatSize(bytes) {
            if (!bytes && bytes !== 0) return '—';
            var kb = bytes / 1024;
            if (kb < 1024) return kb.toFixed(1) + ' ك.ب';
            return (kb / 1024).toFixed(2) + ' م.ب';
        }

        function formatDate(ms) {
            if (!ms) return '—';
            var d = new Date(ms);
            if (isNaN(d.getTime())) return '—';
            function p(n) { return (n < 10 ? '0' : '') + n; }
            return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) +
                ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
        }

        function setText(selector, key, value) {
            form.querySelectorAll('[data-' + selector + '="' + key + '"]').forEach(function (el) {
                el.textContent = value;
            });
        }

        function refresh(input) {
            var key = input.dataset.fileInput;
            var zone = form.querySelector('.upload-zone[data-zone="' + key + '"]');
            var badge = form.querySelector('[data-file-state="' + key + '"]');
            var file = input.files && input.files.length ? input.files[0] : null;

            if (!file) {
                setText('file-name', key, '—');
                setText('file-size', key, '—');
                setText('file-date', key, '—');
                setText('file-state-detail', key, 'بانتظار اختيار الملف');
                if (badge) { badge.textContent = 'لم يتم الاختيار'; badge.className = 'file-state'; }
                if (zone) zone.classList.remove('has-file');
                return;
            }

            setText('file-name', key, file.name);
            setText('file-size', key, formatSize(file.size));
            setText('file-date', key, formatDate(file.lastModified));
            setText('file-state-detail', key, 'جاهز للرفع — سيتم التحقق من الملف بعد الإرسال');
            if (badge) { badge.textContent = 'جاهز للرفع'; badge.className = 'file-state ok'; }
            if (zone) zone.classList.add('has-file');
        }

        form.querySelectorAll('input[data-file-input]').forEach(function (input) {
            refresh(input);
            input.addEventListener('change', function () {
                if (errorBox) errorBox.hidden = true;
                refresh(input);
            });
        });

        // كشف ملف مُفلَت فوق الصندوق (بعض المتصفحات لا تملأ الحقل تلقائيًا)
        form.querySelectorAll('.upload-zone').forEach(function (zone) {
            var input = zone.querySelector('input[data-file-input]');
            if (!input) return;
            ['dragenter', 'dragover'].forEach(function (evt) {
                zone.addEventListener(evt, function (e) { e.preventDefault(); });
            });
            zone.addEventListener('drop', function (e) {
                if (!e.dataTransfer || !e.dataTransfer.files.length) return;
                e.preventDefault();
                input.files = e.dataTransfer.files;
                if (errorBox) errorBox.hidden = true;
                refresh(input);
            });
        });

        // تحقق قبلي: لا تبدأ المعالجة النهائية قبل اختيار ملف واحد على الأقل
        form.addEventListener('submit', function (e) {
            var any = Array.prototype.some.call(
                form.querySelectorAll('input[data-file-input]'),
                function (i) { return i.files && i.files.length > 0; });
            if (any) return;
            e.preventDefault();
            if (errorBox) {
                errorBox.hidden = false;
                errorBox.scrollIntoView({ block: 'center' });
            }
        });
    })();

    // ---------- تأكيد العمليات الحساسة ----------
    document.addEventListener('submit', function (e) {
        var form = e.target;
        // إن منع مدقق آخر الإرسال (مثل التحقق من ملفات الرفع) فلا مؤشر انتظار
        if (e.defaultPrevented) return;
        if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            e.preventDefault();
            return;
        }
        // منع الإرسال المزدوج وإظهار مؤشر الانتظار
        var submitBtn = form.querySelector('button[type=submit]:not([data-no-spinner])');
        if (submitBtn && !submitBtn.disabled) {
            submitBtn.dataset.originalText = submitBtn.innerHTML;
            submitBtn.disabled = true;
            submitBtn.innerHTML = '<span class="spinner"></span> ' + (form.dataset.loadingText || 'جارٍ التنفيذ...');
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
