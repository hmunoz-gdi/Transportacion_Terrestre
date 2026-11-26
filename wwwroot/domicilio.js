(async () => {
  const form = document.getElementById('ficha');
  const names = ['estado', 'municipio', 'colonia', 'cp'];
  const properties = ['estado', 'municipio', 'colonia', 'cp'];
  const fields = names.map(name => form.elements.namedItem(name));
  const initial = fields.map(field => field.dataset.value);
  const save = form.querySelector('button');
  save.disabled = true;
  try {
    const response = await fetch('/permisionarios/catalogo-domicilios');
    if (!response.ok) throw new Error('Catálogo no disponible');
    const rows = await response.json();
    function load(index, previous = []) {
      const matches = rows.filter(row => properties.slice(0, index).every((key, i) => row[key] === fields[i].value));
      const values = [...new Set(matches.map(row => row[properties[index]]))].sort((a, b) => a.localeCompare(b, 'es'));
      const field = fields[index];
      field.replaceChildren(new Option('Seleccionar', ''), ...values.map(value => new Option(value, value)));
      field.value = values.includes(previous[index]) ? previous[index] : index === 3 && values.length === 1 ? values[0] : '';
      field.disabled = values.length === 0;
      if (index + 1 < fields.length) load(index + 1, previous);
    }
    load(0, initial);
    fields.slice(0, -1).forEach((field, index) => field.addEventListener('change', () => load(index + 1)));
    save.disabled = false;
  } catch {
    form.insertAdjacentHTML('afterbegin', '<p role="alert" class="alert">No se pudo cargar el catálogo de domicilios. Vuelve a abrir la ficha.</p>');
  }
})();
