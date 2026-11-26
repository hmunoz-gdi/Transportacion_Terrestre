const ficha = document.getElementById('ficha');

const contactReaders = [];
for (const grid of ficha.querySelectorAll('[data-contactos]')) {
  const body = grid.querySelector('tbody');
  const add = grid.querySelector('[data-add]');
  const input = grid.querySelector('[data-entry]');
  const template = grid.querySelector('template');
  const empty = grid.querySelector('[data-empty]');
  const status = grid.querySelector('[data-status]');
  const label = grid.dataset.label;
  function refresh() {
    empty.hidden = body.rows.length > 0;
    add.disabled = body.rows.length >= 10;
  }

  function agendar() {
    input.value = input.value.trim();
    if (!input.value) return true;
    const value = input.value;
    if (body.rows.length >= 10) input.setCustomValidity('Puedes agregar hasta 10 contactos de este tipo.');
    else if (input.type === 'email' && !/^[^@]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$/.test(value))
      input.setCustomValidity('Captura un correo con un dominio válido.');
    if (!input.reportValidity()) return false;

    const copy = template.content.cloneNode(true);
    copy.querySelector('[data-value]').textContent = value;
    copy.querySelector('input').value = value;
    copy.querySelector('[data-remove]').setAttribute('aria-label', 'Quitar ' + label + ' ' + value);
    body.append(copy);
    input.value = '';
    status.textContent = value + ' agregado a la cuadrícula.';
    refresh();
    return true;
  }

  input.addEventListener('input', () => input.setCustomValidity(''));
  add.addEventListener('click', () => {
    if (agendar()) input.focus();
  });
  input.addEventListener('keydown', event => {
    if (event.key !== 'Enter' || !input.value.trim() || event.isComposing ||
        event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return;
    if (!agendar()) {
      event.preventDefault();
      event.stopPropagation();
    }
  });
  body.addEventListener('click', event => {
    const button = event.target.closest('[data-remove]');
    if (!button) return;
    button.closest('tr').remove();
    input.setCustomValidity('');
    status.textContent = 'Contacto retirado.';
    refresh();
    input.focus();
  });
  contactReaders.push(agendar);
  refresh();
}
ficha.addEventListener('submit', event => {
  if (!contactReaders.every(read => read())) event.preventDefault();
});

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
