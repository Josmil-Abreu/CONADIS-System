// Barra de accesibilidad: tamaño de texto y alto contraste (se recuerda en el navegador)
(function () {
    const html = document.documentElement;
    const leer = k => { try { return localStorage.getItem(k); } catch { return null; } };
    const guardar = (k, v) => { try { localStorage.setItem(k, v); } catch { } };

    function aplicarTamano(clase) {
        html.classList.remove('texto-grande', 'texto-muy-grande');
        if (clase) html.classList.add(clase);
        document.querySelectorAll('[data-tamano]').forEach(b =>
            b.setAttribute('aria-pressed', String((b.dataset.tamano || '') === (clase || ''))));
    }

    function aplicarContraste(activo) {
        html.classList.toggle('alto-contraste', activo);
        const b = document.getElementById('btn-contraste');
        if (b) b.setAttribute('aria-pressed', String(activo));
    }

    aplicarTamano(leer('conadis.tamano') || '');
    aplicarContraste(leer('conadis.contraste') === '1');

    document.addEventListener('click', e => {
        const t = e.target.closest('[data-tamano]');
        if (t) { aplicarTamano(t.dataset.tamano); guardar('conadis.tamano', t.dataset.tamano); }

        if (e.target.closest('#btn-contraste')) {
            const activo = !html.classList.contains('alto-contraste');
            aplicarContraste(activo);
            guardar('conadis.contraste', activo ? '1' : '0');
        }

        // Botón "Mostrar" contraseña
        const m = e.target.closest('[data-mostrar]');
        if (m) {
            const input = document.getElementById(m.dataset.mostrar);
            const visible = input.type === 'text';
            input.type = visible ? 'password' : 'text';
            m.textContent = visible ? 'Mostrar' : 'Ocultar';
            m.setAttribute('aria-pressed', String(!visible));
        }
    });
})();