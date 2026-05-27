(function () {
    function setUiForTheme(theme) {
        var btn = document.getElementById('themeToggle');
        if (!btn) return;

        // pressed = está no tema escuro
        var pressed = theme === 'dark';
        btn.setAttribute('aria-pressed', String(pressed));

        var icon = btn.querySelector('.theme-icon');
        var label = btn.querySelector('.theme-label');

        // Zera qualquer emoji e troca apenas as CLASSES do Bootstrap Icons
        if (icon) {
            icon.textContent = ''; // remove emoji antigo, se houver
            icon.classList.remove('bi-sun-fill', 'bi-moon-stars-fill');
            icon.classList.add(pressed ? 'bi-moon-stars-fill' : 'bi-sun-fill');
        }

        // Texto do botão (mantendo sua lógica de "ação": no dark mostra 'Light')
        if (label) label.textContent = pressed ? 'Light' : 'Dark';
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        if (theme === 'dark') document.documentElement.classList.add('dark');
        else document.documentElement.classList.remove('dark');
        setUiForTheme(theme);
    }

    document.addEventListener('DOMContentLoaded', function () {
        var stored = localStorage.getItem('theme');

        // Descobre o tema atual (vindo do inline script do _Layout ou do storage)
        var current = stored ||
            document.documentElement.getAttribute('data-bs-theme') ||
            (document.documentElement.classList.contains('dark') ? 'dark' : 'light');

        applyTheme(current);

        var toggle = document.getElementById('themeToggle');
        if (toggle) {
            toggle.addEventListener('click', function () {
                var cur = document.documentElement.getAttribute('data-bs-theme') || 'light';
                var next = (cur === 'dark') ? 'light' : 'dark';
                localStorage.setItem('theme', next);
                applyTheme(next);
            });
        }
    });

    // APROVAÇÃO
    // site.js
    window.sendDecision = async function (id, _stageCodeIgnored, action) {
        try {
            const remarksEl = document.getElementById('txtRemarks');
            const obs = remarksEl ? remarksEl.value : '';

            const status = action === 'approve' ? 'ardApproved' : 'ardRejected';

            document.querySelectorAll('.modal-body .btn-success, .modal-body .btn-danger')
                .forEach(b => b.disabled = true);

            const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
            const token = tokenInput ? tokenInput.value : '';

            const body = new URLSearchParams({
                id: String(id),
                status,
                obs: obs ?? '',
                __RequestVerificationToken: token
            });

            const resp = await fetch('/Aprovacoes/Aprovar', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                    'RequestVerificationToken': token
                },
                body
            });

            if (!resp.ok) throw new Error((await resp.text()) || `Falha HTTP ${resp.status}`);

            const modalEl = document.querySelector('.modal.show');
            if (modalEl && window.bootstrap) {
                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();
            }

            if (typeof window.reloadApprovals === 'function') window.reloadApprovals();
            else location.reload();

        } catch (err) {
            console.error(err);
            alert('Erro ao enviar decisão: ' + (err?.message || err));
        } finally {
            document.querySelectorAll('.modal-body .btn-success, .modal-body .btn-danger')
                .forEach(b => b.disabled = false);
        }
    };


})();
