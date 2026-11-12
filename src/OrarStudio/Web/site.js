// Orar Studio — site public al orarului (citește datele din /api/bundle sau din data.json la exportul static)
(function () {
  'use strict';
  const STATIC = window.ORAR_STATIC_DATA || null;
  const qs = new URLSearchParams(location.search);
  if (qs.get('embed') === '1') document.body.classList.add('embed');
  if (qs.get('theme')) document.documentElement.dataset.theme = qs.get('theme');

  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const norm = (s) => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const baseCode = (c) => String(c).split('-')[0].toUpperCase();

  let data = null;
  let kind = 'group';
  let key = null;

  function entities() {
    if (!data) return [];
    if (kind === 'group') return data.groups.map((g) => ({ key: g.code, label: g.code, detail: [g.specialty, 'anul ' + g.year].filter(Boolean).join(' · ') }));
    if (kind === 'teacher') return data.teachers.map((t) => ({ key: t.id, label: t.name, detail: t.title }));
    return data.rooms.map((r) => ({ key: r.id, label: 'Aud. ' + r.name, detail: `${r.kindLabel} · ${r.capacity} locuri` }));
  }

  function lessonsFor() {
    if (kind === 'group') return data.lessons.filter((l) => l.groups.some((c) => baseCode(c) === String(key).toUpperCase()));
    if (kind === 'teacher') return data.lessons.filter((l) => l.teacherId === key);
    return data.lessons.filter((l) => l.roomId === key);
  }

  function fillSelect() {
    const q = norm($('search').value);
    const list = entities().filter((e) => !q || norm(e.label + ' ' + e.detail).includes(q));
    $('entity').innerHTML = list.map((e) => `<option value="${esc(e.key)}">${esc(e.label)}${e.detail ? ' — ' + esc(e.detail) : ''}</option>`).join('');
    if (!list.some((e) => e.key === key)) key = list.length ? list[0].key : null;
    $('entity').value = key ?? '';
    render();
  }

  function card(l, ctxGroup) {
    const sub = kind === 'group' ? l.teacher : kind === 'teacher' ? l.groups.join(', ') : `${l.groups.join(', ')} · ${l.teacher}`;
    const tags = [l.typeLabel];
    if (kind !== 'room') tags.push('aud. ' + l.room);
    if (l.week !== 'saptamanal') tags.push(l.week);
    if (ctxGroup) {
      const own = l.groups.filter((c) => baseCode(c) === ctxGroup.toUpperCase());
      if (own.length && own.every((c) => c.includes('-'))) tags.push(own.map((c) => 'sg. ' + c.split('-').slice(1).join('-')).join('+'));
    }
    const tip = `${l.subject} (${l.typeLabel})\n${l.teacher} ${l.teacherTitle}\nGrupe: ${l.groups.join(', ')}\nAuditoriu: ${l.room}\n${l.weekLabel}`;
    return `<div class="lesson ${esc(l.type)}" title="${esc(tip)}"><div class="t">${esc(l.subject)}</div>` +
      `<div class="s">${esc(sub)}</div><div class="tags">${tags.map((t) => `<span class="tag">${esc(t)}</span>`).join('')}</div></div>`;
  }

  function cellHtml(items, ctx) {
    const weekly = items.filter((l) => l.week === 'saptamanal');
    const odd = items.filter((l) => l.week === 'impar');
    const even = items.filter((l) => l.week === 'par');
    const cols = weekly.map((l) => `<div class="col">${card(l, ctx)}</div>`);
    for (let i = 0; i < Math.max(odd.length, even.length); i++) {
      cols.push(`<div class="col">${odd[i] ? card(odd[i], ctx) : '<div class="half">impar · liber</div>'}` +
        `${even[i] ? card(even[i], ctx) : '<div class="half">par · liber</div>'}</div>`);
    }
    return cols.join('');
  }

  function render() {
    const info = data.info;
    const ent = entities().find((e) => e.key === key);
    if (!ent) {
      $('title').textContent = 'Nu există date';
      $('subtitle').textContent = '';
      $('grid').innerHTML = '<div class="empty">Nu există elemente de afișat.</div>';
      $('days').innerHTML = '';
      return;
    }
    $('title').textContent = (kind === 'group' ? 'Grupa ' : '') + ent.label;
    $('subtitle').textContent = ent.detail || '';
    document.title = `Orar · ${ent.label}`;

    const lessons = lessonsFor();
    const todayIdx = (new Date().getDay() + 6) % 7;
    const onlyToday = $('today').checked;
    const days = info.dayNames.map((n, i) => ({ n, i })).filter((d) => !onlyToday || d.i === todayIdx);
    const ctx = kind === 'group' ? key : null;

    const grid = $('grid');
    grid.style.gridTemplateColumns = `72px repeat(${Math.max(1, days.length)}, minmax(130px, 1fr))`;
    let html = '<div></div>' + days.map((d) => {
      const n = lessons.filter((l) => l.day === d.i).length;
      return `<div class="gh${d.i === todayIdx ? ' today' : ''}">${esc(d.n)}<small>${n ? n + ' lecții' : 'liber'}</small></div>`;
    }).join('');
    for (const s of info.slots) {
      html += `<div class="slot"><b>${esc(s.roman)}</b>${esc(s.start)}<br>${esc(s.end)}</div>`;
      for (const d of days) {
        const items = lessons.filter((l) => l.day === d.i && l.slot === s.number);
        html += `<div class="cell${d.i === todayIdx ? ' today' : ''}">${cellHtml(items, ctx)}</div>`;
      }
    }
    grid.innerHTML = days.length ? html : '<div class="empty">Astăzi nu sunt lecții.</div>';

    $('days').innerHTML = days.map((d) => {
      const rows = info.slots.map((s) => ({ s, items: lessons.filter((l) => l.day === d.i && l.slot === s.number) })).filter((r) => r.items.length);
      return `<div class="day"><h3>${esc(d.n)}</h3>` + (rows.length
        ? rows.map((r) => `<div class="row"><div class="slot"><b>${esc(r.s.roman)}</b>${esc(r.s.start)}</div><div class="cell">${cellHtml(r.items, ctx)}</div></div>`).join('')
        : '<div class="empty" style="padding:10px">Nu sunt lecții</div>') + '</div>';
    }).join('');

    const pdf = kind === 'group' ? `api/pdf/group/${encodeURIComponent(key)}` : kind === 'teacher' ? `api/pdf/teacher/${key}` : `api/pdf/room/${key}`;
    $('pdf').href = pdf;
    $('pdf').style.display = STATIC ? 'none' : '';
    $('ics').href = kind === 'group' ? `api/ics/group/${encodeURIComponent(key)}` : '#';
    $('ics').style.display = STATIC || kind !== 'group' ? 'none' : '';

    const url = new URL(location.href);
    ['group', 'teacher', 'room'].forEach((k) => url.searchParams.delete(k));
    url.searchParams.set(kind, key);
    history.replaceState(null, '', url);
  }

  function setKind(k) {
    kind = k;
    document.querySelectorAll('.tabs button').forEach((b) => b.classList.toggle('active', b.dataset.kind === k));
    key = null;
    $('search').value = '';
    fillSelect();
  }

  async function fetchData() {
    const res = await fetch(STATIC || 'api/bundle', { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return res.json();
  }

  async function load() {
    try {
      data = await fetchData();
    } catch (e) {
      $('title').textContent = 'Datele nu au putut fi încărcate';
      $('grid').innerHTML = `<div class="error">${esc(e.message)}</div>`;
      return;
    }
    const i = data.info;
    $('inst').textContent = `${i.institution} · ${i.faculty}`;
    $('sem').textContent = `${i.academicYear} · semestrul ${i.semester}`;
    const par = i.currentParity === 'impar' ? 'impară' : 'pară';
    $('week').textContent = i.inSemester ? `Săptămâna ${i.currentWeek} · ${par}` : `Săptămână ${par}`;
    $('updated').textContent = new Date(i.generatedAt).toLocaleString('ro-RO');
    if (STATIC) document.querySelectorAll('footer a').forEach((a) => (a.style.display = 'none'));

    for (const k of ['group', 'teacher', 'room']) if (qs.get(k)) { kind = k; key = qs.get(k); }
    if (kind === 'teacher' && key && !data.teachers.some((t) => t.id === key)) {
      const t = data.teachers.find((x) => norm(x.name) === norm(key.replace(/-/g, ' ')));
      key = t ? t.id : null;
    }
    if (kind === 'room' && key && !data.rooms.some((r) => r.id === key)) {
      const r = data.rooms.find((x) => norm(x.name) === norm(key));
      key = r ? r.id : null;
    }
    document.querySelectorAll('.tabs button').forEach((b) => b.classList.toggle('active', b.dataset.kind === kind));
    fillSelect();
  }

  document.querySelectorAll('.tabs button').forEach((b) => b.addEventListener('click', () => setKind(b.dataset.kind)));
  $('search').addEventListener('input', fillSelect);
  $('entity').addEventListener('change', (e) => { key = e.target.value; render(); });
  $('today').addEventListener('change', render);
  load();
  if (!STATIC) {
    setInterval(async () => {
      try { data = await fetchData(); render(); } catch { /* serverul poate fi oprit temporar */ }
    }, 60000);
  }
})();
