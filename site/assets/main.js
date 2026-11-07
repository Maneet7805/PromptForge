// Copy button for the checksum command. Everything else on the page works without JavaScript.
document.querySelectorAll('[data-copy]').forEach((button) => {
  if (!navigator.clipboard) return;
  button.hidden = false;
  button.addEventListener('click', async () => {
    const source = document.querySelector(button.dataset.copy);
    try {
      await navigator.clipboard.writeText(source.textContent.trim());
      button.textContent = 'Copied';
    } catch {
      button.textContent = 'Press Ctrl+C';
    }
    setTimeout(() => { button.textContent = 'Copy'; }, 1500);
  });
});
