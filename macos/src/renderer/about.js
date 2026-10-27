import { invoke, bootTheme, closeDialog, el, button } from './dialog.js';

const content = document.getElementById('content');

const LINKS = [
  { label: 'Upstream project', url: 'https://github.com/talhazahid/deskstats' },
  { label: 'Weather by Open-Meteo', url: 'https://open-meteo.com/' },
  { label: 'macmon', url: 'https://github.com/vladkens/macmon' },
  { label: 'nowplaying-cli', url: 'https://github.com/kirtan-shah/nowplaying-cli' },
  { label: 'GNU GPL v3', url: 'https://www.gnu.org/licenses/gpl-3.0.html' },
];

function build(data) {
  content.replaceChildren();

  content.appendChild(el('div', 'about-icon', 'DS'));
  content.appendChild(el('h1', null, 'DeskStats'));
  content.appendChild(el('div', 'version', `Version ${data.version} · macOS (Apple Silicon)`));
  content.appendChild(
    el(
      'p',
      null,
      'Desktop hardware monitor widgets for CPU, GPU, memory, storage, network, sensors, time, weather and now playing.',
    ),
  );
  content.appendChild(
    el(
      'p',
      'fine',
      'A macOS port of the Windows DeskStats app by Talha Zahid. The original design, widget set and layout behaviour are preserved; platform-specific collectors are new.',
    ),
  );

  const links = el('div', 'about-links');
  for (const link of LINKS) {
    const node = el('button', 'link-button', link.label);
    node.type = 'button';
    node.addEventListener('click', () => invoke('app:open-external', { url: link.url }));
    links.appendChild(node);
  }
  content.appendChild(links);

  content.appendChild(
    el(
      'div',
      'about-credits',
      'This program is free software under the GNU General Public License v3. Weather data by Open-Meteo.com (CC BY 4.0). Sensor readings by macmon.',
    ),
  );

  content.appendChild(
    el(
      'div',
      'about-meta',
      `Electron ${data.electron} · Chromium ${data.chrome} · Node ${data.node}\nData folder: ${data.dataDir}`,
    ),
  );

  const actions = el('div', 'dialog-actions');
  const folder = button('Open data folder');
  folder.addEventListener('click', () => invoke('app:open-data-folder'));
  const close = button('Close', { primary: true });
  close.addEventListener('click', () => closeDialog());
  actions.appendChild(folder);
  actions.appendChild(close);
  content.appendChild(actions);
}

bootTheme()
  .then(() => invoke('about:data'))
  .then((data) => {
    if (!data || data.__error) throw new Error(data?.__error || 'Could not load app information.');
    build(data);
  })
  .catch((error) => {
    content.replaceChildren(el('div', 'dialog-title', 'About DeskStats'), el('p', null, error.message));
    const close = button('Close');
    close.addEventListener('click', () => closeDialog());
    content.appendChild(close);
  });
