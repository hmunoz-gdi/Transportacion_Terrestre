document.addEventListener('input', event => {
  const field = event.target;
  if (!field.closest('[data-captura]') || !field.matches('input[type=text], textarea') || event.isComposing) return;
  const { selectionStart, selectionEnd, value } = field;
  const upper = value.toLocaleUpperCase('es-MX');
  if (upper === value) return;
  field.value = upper;
  if (selectionStart !== null && selectionEnd !== null)
    field.setSelectionRange(value.slice(0, selectionStart).toLocaleUpperCase('es-MX').length,
      value.slice(0, selectionEnd).toLocaleUpperCase('es-MX').length);
});

document.addEventListener('keydown', event => {
  const field = event.target;
  const form = field.closest('form[data-captura]');
  if (!form || event.key !== 'Enter' || event.isComposing || event.shiftKey || event.ctrlKey ||
      event.altKey || event.metaKey || !field.matches('input:not([type=checkbox]):not([type=file]), select, textarea')) return;
  event.preventDefault();
  if (!field.reportValidity()) return;
  const fields = [...form.elements].filter(el =>
    el.matches('input:not([type=hidden]), select, textarea, button[type=submit], button:not([type])') &&
      !el.disabled && !el.readOnly && !el.hidden && el.getClientRects().length);
  fields[fields.indexOf(field) + 1]?.focus();
});
