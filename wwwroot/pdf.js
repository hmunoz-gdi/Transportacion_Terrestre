const fileInput = document.getElementById('pdf-input');
const preview = document.getElementById('pdf-preview');
let url;
fileInput.addEventListener('change', () => {
  if (url) URL.revokeObjectURL(url);
  const file = fileInput.files[0];
  preview.hidden = !file || file.type !== 'application/pdf';
  if (preview.hidden) {
    preview.removeAttribute('src');
    return;
  }
  url = URL.createObjectURL(file);
  preview.src = url;
});
