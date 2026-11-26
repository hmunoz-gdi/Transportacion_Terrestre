const ficha = document.getElementById('ficha');

for (const grid of ficha.querySelectorAll('[data-contactos]')) {
  const body = grid.querySelector('tbody');
  const add = grid.querySelector('[data-add]');
  const template = grid.querySelector('template');
  function refresh() { add.disabled = body.rows.length >= 10; }
  add.addEventListener('click', () => {
    if (body.rows.length >= 10) return;
    body.append(template.content.cloneNode(true));
    body.lastElementChild.querySelector('input').focus();
    refresh();
  });
  body.addEventListener('click', event => {
    const button = event.target.closest('[data-remove]');
    if (!button) return;
    const row = button.closest('tr');
    const next = (row.nextElementSibling || row.previousElementSibling)?.querySelector('input');
    row.remove();
    refresh();
    (next || add).focus();
  });
  refresh();
}

const fields = ['nombre', 'paterno', 'materno', 'nacimiento', 'rfc'].map(key => ficha.elements.namedItem(key));
const [nombre, paterno, materno, nacimiento, rfc] = fields;
const hint = document.getElementById('rfc-sugerencia');
const use = document.getElementById('rfc-usar');
const particles = new Set(['DE', 'DEL', 'LA', 'LAS', 'LOS', 'Y', 'DA', 'DAS', 'DO', 'DOS', 'VAN', 'VON']);
const words = value => value.toUpperCase().normalize('NFD').replace(/(?<!N)\u0303|[\u0300-\u0302\u0304-\u036f]/g, '')
  .replace(/N\u0303/g, 'X').match(/[A-Z]+/g)?.filter(word => !particles.has(word)) ?? [];

function calcularBase() {
  const given = words(nombre.value);
  const first = given.length > 1 && ['JOSE', 'MARIA', 'J', 'MA'].includes(given[0]) ? given[1] : given[0];
  const a = words(paterno.value)[0];
  const b = words(materno.value)[0];
  const date = /^(\d{4})-(\d{2})-(\d{2})$/.exec(nacimiento.value);
  if (!first || !a || !b || !date || !nacimiento.validity.valid) return '';
  const initial = a[0] + (a.slice(1).match(/[AEIOU]/)?.[0] || 'X') + b[0] + first[0];
  return initial + date[1].slice(2) + date[2] + date[3];
}

function sugerir() {
  const base = calcularBase();
  hint.textContent = base
    ? 'Base sugerida: ' + base + '. Confirma con tu constancia fiscal y completa el sufijo oficial.'
    : 'Para sugerir la base, captura el nombre, ambos apellidos y la fecha de nacimiento.';
  use.hidden = !base || rfc.value.trim().length >= 12;
}
for (const field of fields) field.addEventListener('input', sugerir);
use.addEventListener('click', () => {
  rfc.value = calcularBase();
  rfc.focus();
  rfc.setSelectionRange(rfc.value.length, rfc.value.length);
  sugerir();
});
sugerir();
