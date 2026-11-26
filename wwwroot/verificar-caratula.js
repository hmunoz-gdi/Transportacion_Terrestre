const input = document.getElementById('pdf-verificar');
const status = document.getElementById('pdf-verificado');
input.addEventListener('change', async () => {
  const file = input.files[0];
  if (!file) { status.textContent = ''; return; }
  if (file.size > 25 * 1024 * 1024 || !globalThis.crypto?.subtle) {
    status.textContent = 'Usa un archivo de hasta 25 MB y abre el verificador desde una conexión HTTPS.';
    return;
  }
  try {
    const hash = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
    const hex = Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
    if (input.files[0] !== file) return;
    status.textContent = hex === input.dataset.hash
      ? 'El archivo coincide con el PDF emitido.'
      : 'El archivo es distinto del PDF emitido. Solicita el original para cotejarlo.';
  } catch {
    status.textContent = 'No se pudo leer el archivo seleccionado.';
  }
});
