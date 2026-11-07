// Everything here is optional. The page reads and works without JavaScript.

// Copy button for the checksum command.
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

const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');

// Replay the headline edit.
const headline = document.getElementById('hl');
const replay = document.getElementById('replay');
if (headline && replay && !reduceMotion.matches) {
  replay.classList.add('ready');
  replay.addEventListener('click', () => {
    headline.classList.remove('edit');
    void headline.offsetWidth;
    headline.classList.add('edit');
    const lead = document.querySelector('.hero .lead');
    const cta = document.querySelector('.hero .cta-row');
    [lead, cta].forEach((el) => { el.style.animation = 'none'; void el.offsetWidth; el.style.animation = ''; });
  });
}

// A field of dithered dots behind the hero. The pointer heats it.
// It stops when the hero is off screen or the tab is hidden, and is drawn once if motion is reduced.
const canvas = document.getElementById('field');
if (canvas && canvas.getContext) {
  const ctx = canvas.getContext('2d');
  const CELL = 6;
  const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
  let cols = 0, rows = 0, px = -9999, py = -9999, running = false, visible = true, last = 0;

  const resize = () => {
    const r = canvas.getBoundingClientRect();
    canvas.width = Math.ceil(r.width);
    canvas.height = Math.ceil(r.height);
    cols = Math.ceil(canvas.width / CELL);
    rows = Math.ceil(canvas.height / CELL);
  };

  const draw = (now) => {
    const t = now / 1000;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#EBF0F2';
    for (let y = 0; y < rows; y++) {
      for (let x = 0; x < cols; x++) {
        const u = x / cols, v = y / rows;
        let val = 0.5 + 0.5 * Math.sin(u * 7 + t * 0.5) * Math.cos(v * 5 - t * 0.4);
        val *= Math.min(1, Math.max(0, (u - 0.3) * 1.6));
        const dx = x * CELL - px, dy = y * CELL - py;
        val += Math.max(0, 1 - Math.hypot(dx, dy) / 150) * 0.9;
        if (val * 15 > BAYER[(y & 3) * 4 + (x & 3)] + 4) ctx.fillRect(x * CELL, y * CELL, CELL - 2, CELL - 2);
      }
    }
  };

  const loop = (now) => {
    if (!running) return;
    if (now - last > 50) { draw(now); last = now; }
    requestAnimationFrame(loop);
  };
  const start = () => {
    if (running || reduceMotion.matches || !visible || document.hidden) return;
    running = true;
    requestAnimationFrame(loop);
  };
  const stop = () => { running = false; };

  resize();
  draw(0);
  if (!reduceMotion.matches) {
    const hero = document.getElementById('top');
    hero.addEventListener('pointermove', (e) => {
      const r = canvas.getBoundingClientRect();
      px = e.clientX - r.left;
      py = e.clientY - r.top;
    });
    hero.addEventListener('pointerleave', () => { px = py = -9999; });
    new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; visible ? start() : stop(); }).observe(hero);
    document.addEventListener('visibilitychange', () => (document.hidden ? stop() : start()));
    start();
  }
  addEventListener('resize', () => { resize(); draw(performance.now()); });
}

// Scroll scene: the rewrite arrives word by word, sharpening out of a blur.
// Only where the pinned scene runs; elsewhere the text is plain and complete.
if (CSS.supports('animation-timeline: view()')
    && matchMedia('(min-width: 900px) and (min-height: 640px) and (prefers-reduced-motion: no-preference)').matches) {
  const lines = [...document.querySelectorAll('.win-body .rl')];
  const words = [];
  const split = (node) => {
    [...node.childNodes].forEach((child) => {
      if (child.nodeType === 3) {
        const frag = document.createDocumentFragment();
        child.textContent.split(/(\s+)/).forEach((part) => {
          if (!part) return;
          if (/^\s+$/.test(part)) { frag.append(part); return; }
          const w = document.createElement('span');
          w.className = 'w sc';
          w.textContent = part;
          words.push(w);
          frag.append(w);
        });
        child.replaceWith(frag);
      } else if (child.nodeType === 1) {
        split(child);
      }
    });
  };
  lines.forEach(split);
  const FIRST = 52, SPAN = 24, EACH = 3;
  words.forEach((w, i) => {
    const start = FIRST + (i / words.length) * SPAN;
    w.style.setProperty('--a', `${start.toFixed(2)}%`);
    w.style.setProperty('--b', `${(start + EACH).toFixed(2)}%`);
  });
}
