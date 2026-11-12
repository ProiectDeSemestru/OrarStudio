// Orar Studio — panou informativ web: interoghează /api/now și afișează ce are loc acum și ce urmează.
// Parametri: ?mode=rooms|groups|teachers, ?at=2023-10-03T10:05 (mod demonstrativ), ?all=1 (și sălile libere fără lecții).
(function () {
  'use strict';
  const qs = new URLSearchParams(location.search);
  const mode = qs.get('mode') || 'rooms';
  const at = qs.get('at');
  const showAll = qs.get('all') === '1';
  const started = Date.now();
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  let rows = [];
  let page = 0;

  function now() {
    if (!at) return new Date();
    const base = new Date(at);
    return isNaN(base) ? new Date() : new Date(base.getTime() + (Date.now() - started));
  }

  function tick() {
    const d = now();
    const p = (n) => String(n).padStart(2, '0');
    $('time').innerHTML = `${p(d.getHours())}:${p(d.getMinutes())}<small>:${p(d.getSeconds())}</small>`;
  }

  function entry(e) {
    return `<div class="entry ${esc(e.type)}"><i></i><div><b>${esc(e.subject)}</b>` +
      `<span>${esc(e.typeLabel)} · ${esc(e.groups)} · ${esc(e.teacher)} · aud. ${esc(e.room)}</span></div></div>`;
  }

  function next(e) {
    return `<div class="next"><em>${esc(e.time)}</em><div><b>${esc(e.subject)}</b><span>${esc(e.typeLabel)} · ${esc(e.groups)} · aud. ${esc(e.room)}</span></div></div>`;
  }

  function perPage() {
    const h = $('rows').clientHeight || 600;
    return Math.max(3, Math.floor(h / 94));
  }

  function showPage() {
    const per = perPage();
    const pages = Math.max(1, Math.ceil(rows.length / per));
    if (page >= pages) page = 0;
    const slice = rows.slice(page * per, page * per + per);
    $('rows').innerHTML = slice.length
      ? slice.map((r) => `<div class="row${r.busy ? ' busy' : ''}"><div class="name"><b>${esc(r.title)}</b><span>${esc(r.subtitle)}</span></div>` +
        `<div>${r.now.length ? r.now.map(entry).join('') : '<div class="free">Liber</div>'}</div>` +
        `<div>${r.next.length ? r.next.map(next).join('') : '<span style="color:var(--dim)">—</span>'}</div></div>`).join('')
      : '<div class="empty">În acest moment nu sunt lecții.<br><small>Adăugați ?at=AAAA-LL-ZZTHH:MM în adresă pentru demonstrație.</small></div>';
    $('page').textContent = pages > 1 ? `Pagina ${page + 1} din ${pages}` : '';
  }

  async function refresh() {
    try {
      const d = now();
      const p = (n) => String(n).padStart(2, '0');
      const stamp = `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
      const res = await fetch(`api/now?mode=${encodeURIComponent(mode)}${at ? '&at=' + encodeURIComponent(stamp) : ''}`, { cache: 'no-store' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const s = await res.json();
      $('date').textContent = s.date;
      $('week').textContent = s.inSemester ? `Săptămâna ${s.week} · ${s.parityLabel}` : s.parityLabel;
      $('slot').textContent = s.currentSlotLabel || (s.nextSlot ? `Următoarea: perechea ${s.nextSlot}` : '—');
      $('status').textContent = s.status;
      $('msg').textContent = s.message || '';
      $('h1').textContent = mode === 'groups' ? 'GRUPA' : mode === 'teachers' ? 'PROFESORUL' : 'AUDITORIUL';
      $('updated').textContent = 'Actualizat la ' + new Date().toLocaleTimeString('ro-RO');
      rows = s.rows.filter((r) => showAll || r.now.length || r.next.length);
      showPage();
      const info = await (await fetch('api/info', { cache: 'no-store' })).json();
      $('inst').textContent = info.institution;
      $('fac').textContent = info.faculty;
      const slot = info.slots.find((x) => x.number === s.currentSlot);
      if (slot) {
        const toMin = (t) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
        const cur = d.getHours() * 60 + d.getMinutes();
        const pct = Math.min(100, Math.max(0, ((cur - toMin(slot.start)) / (toMin(slot.end) - toMin(slot.start))) * 100));
        $('progress').style.width = pct + '%';
      } else {
        $('progress').style.width = '0';
      }
    } catch (e) {
      $('status').innerHTML = `<span class="err">Serverul nu răspunde (${esc(e.message)}). Se reîncearcă…</span>`;
    }
  }

  tick();
  refresh();
  setInterval(tick, 1000);
  setInterval(refresh, 30000);
  setInterval(() => { page++; showPage(); }, 10000);
  window.addEventListener('resize', showPage);
})();
