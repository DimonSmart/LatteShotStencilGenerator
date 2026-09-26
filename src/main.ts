import './styles.css';
import { GeometryWorkerClient } from './geometry-worker-client';
import { ProjectStore, type ProjectSettings, type ProjectState } from './project-state';
import { StencilViewport } from './viewport';
import { serializeStencil, type StencilExportFormat } from './stencil-export-adapter';

const app = document.querySelector<HTMLElement>('#app');
if (!app) throw new Error('Application root is unavailable.');
let artworkSource: string | undefined;

app.innerHTML = `
  <header><div><p class="eyebrow">Browser-native workspace</p><h1>Latte Shot Stencil Generator</h1></div><p>All files stay in this browser tab.</p></header>
  <section class="workspace">
    <aside class="controls" aria-label="Stencil controls">
      <section><h2>Source files</h2>
        <label>3D template <input id="template" type="file" accept=".stl,.3mf" /></label>
        <small>Upload the printable card template (STL or 3MF).</small>
        <label>SVG artwork <input id="artwork" type="file" accept="image/svg+xml,.svg" /></label>
      </section>
      <section><h2>Artwork placement</h2>
        <label>Left margin (mm) <input data-setting="artworkLeft" type="number" min="0" step="0.1" value="6.678" /></label>
        <label>Right margin (mm) <input data-setting="artworkRight" type="number" min="0" step="0.1" value="6.678" /></label>
        <label>Top margin (mm) <input data-setting="artworkTop" type="number" min="0" step="0.1" value="33.029" /></label>
        <label>Bottom margin (mm) <input data-setting="artworkBottom" type="number" min="0" step="0.1" value="5.864" /></label>
        <label>X (mm) <input data-setting="artworkX" type="number" step="0.1" value="0" /></label>
        <label>Y (mm) <input data-setting="artworkY" type="number" step="0.1" value="0" /></label>
        <label>Scale <input data-setting="artworkScale" type="number" min="0.1" step="0.1" value="1" /></label>
        <button id="reset-artwork" type="button">Reset to fit</button>
      </section>
      <section><h2>Caption</h2>
        <label>Text <input data-setting="caption" type="text" maxlength="80" /></label>
        <label>Font <select data-setting="captionFont"><option value="stencil-block">Stencil Block (public domain)</option></select></label>
        <label>X (mm) <input data-setting="captionX" type="number" step="0.1" value="6.678" /></label>
        <label>Y (mm) <input data-setting="captionY" type="number" step="0.1" value="8" /></label>
        <label>Size (mm) <input data-setting="captionSize" type="number" min="1" step="0.5" value="8" /></label>
        <label>Emboss height (mm) <input data-setting="captionEmbossHeight" type="number" min="0.05" step="0.05" value="0.35" /></label>
      </section>
      <section><h2>Bridges and materials</h2>
        <label>Minimum bridge width (mm) <input data-setting="bridgeWidth" type="number" min="0.8" step="0.1" value="0.8" /></label>
        <label>Base color <input data-setting="baseColor" type="color" value="#f4ede4" /></label>
        <label>Caption color <input data-setting="captionColor" type="color" value="#6a3a22" /></label>
      </section>
      <section class="status" aria-live="polite"><h2>Processing status</h2><p id="status"></p></section>
      <section><h2>Export</h2><p id="export-note">Export is unavailable until printable geometry has been generated and validated.</p>
        <div class="export-actions"><button id="export-stl" disabled>Download STL</button><button id="export-3mf" disabled>Download 3MF</button></div>
      </section>
    </aside>
    <section class="preview" aria-label="3D preview"><div class="viewport-toolbar"><strong>3D preview</strong><div><button id="top-view">Top view</button><button id="fit-view">Fit model</button></div></div><div id="viewport" class="viewport" role="img" aria-label="Interactive 3D stencil preview"></div><p class="preview-hint">The preview will show your uploaded template and generated stencil geometry.</p></section>
  </section>`;

const store = new ProjectStore();
const worker = new GeometryWorkerClient((result) => {
  const applied = result.kind === 'template' ? store.applyGeometry(result) : result.kind === 'artwork' ? store.applyArtwork(result) : store.applyStencil(result);
  if (applied && result.kind === 'template' && result.isValid && artworkSource) worker.importArtwork(store.snapshot, artworkSource, true);
  if (applied && result.kind === 'artwork' && result.isValid) {
    store.setProcessing('bridge-generation', 'Generating bridges and printable solid in the geometry worker…');
    worker.generateStencil(store.snapshot);
  }
  return applied;
});
const viewport = new StencilViewport(document.querySelector<HTMLElement>('#viewport')!);
const status = document.querySelector<HTMLElement>('#status')!;
const exportNote = document.querySelector<HTMLElement>('#export-note')!;
let renderedTemplate: ProjectState['geometry'] | undefined;
let renderedStencil: ProjectState['stencil'] | undefined;
let renderedBaseColor = '';
let renderedCaptionColor = '';
let exportInProgress = false;

store.subscribe((state) => {
  status.textContent = state.processing.message;
  const canExport = state.stencil?.isValid === true && !exportInProgress;
  document.querySelectorAll<HTMLButtonElement>('#export-stl, #export-3mf').forEach((button) => { button.disabled = !canExport; });
  exportNote.textContent = canExport ? 'Printable stencil geometry is valid.' : 'Export is unavailable until the generated stencil is valid.';
  if (renderedStencil !== state.stencil || renderedBaseColor !== state.settings.baseColor || renderedCaptionColor !== state.settings.captionColor || (state.stencil?.isValid !== true && renderedTemplate !== state.geometry)) {
    renderedTemplate = state.geometry;
    renderedStencil = state.stencil;
    renderedBaseColor = state.settings.baseColor;
    renderedCaptionColor = state.settings.captionColor;
    const generated = state.stencil?.stencil;
    viewport.setTemplate(generated ?? (state.geometry?.isValid ? state.geometry.template : undefined), state.settings.baseColor, generated?.caption, state.settings.captionColor);
  }
  viewport.setArtwork(state.geometry?.template, state.artwork?.isValid ? state.artwork : undefined, state.stencil?.isValid ? state.stencil.stencil : undefined, state.settings.baseColor, state.settings.captionColor);
  document.querySelectorAll<HTMLInputElement | HTMLSelectElement>('[data-setting]').forEach((input) => {
    const value = state.settings[input.dataset.setting as keyof ProjectSettings];
    if (document.activeElement !== input) input.value = String(value);
  });
});

const templateInput = document.querySelector<HTMLInputElement>('#template')!;
templateInput.addEventListener('change', async () => {
  const templateFile = (templateInput.files ?? [])[0];
  if (!templateFile) return;
  const state = store.update({ templateFile, stencil: undefined, processing: { stage: 'template-import', message: `Importing ${templateFile.name} in the geometry worker…` } });
  worker.importTemplate(state, templateFile.name, await templateFile.arrayBuffer());
});
const artworkInput = document.querySelector<HTMLInputElement>('#artwork')!;
artworkInput.addEventListener('change', async () => {
  const artworkFile = (artworkInput.files ?? [])[0];
  if (!artworkFile) return;
  artworkSource = await artworkFile.text();
  const state = store.update({ artworkFile, artwork: undefined, stencil: undefined, processing: { stage: 'artwork-import', message: `Validating ${artworkFile.name} in the geometry worker…` } });
  worker.importArtwork(state, artworkSource, true);
});
document.querySelectorAll<HTMLInputElement | HTMLSelectElement>('[data-setting]').forEach((input) => input.addEventListener('input', () => {
  const key = input.dataset.setting as keyof ProjectSettings;
  const value = input.type === 'number' ? Number(input.value) : input.value;
  const regeneratesStencil = ['artworkLeft', 'artworkRight', 'artworkTop', 'artworkBottom', 'artworkX', 'artworkY', 'artworkScale', 'bridgeWidth', 'caption', 'captionFont', 'captionX', 'captionY', 'captionSize', 'captionEmbossHeight'].includes(key);
  const state = artworkSource && regeneratesStencil
    ? store.update({ settings: { [key]: value }, stencil: undefined, processing: { stage: 'placement', message: 'Updating artwork placement in the geometry worker…' } })
    : store.update({ settings: { [key]: value } });
  if (artworkSource && regeneratesStencil) {
    if (['artworkLeft', 'artworkRight', 'artworkTop', 'artworkBottom', 'artworkX', 'artworkY', 'artworkScale'].includes(key)) worker.importArtwork(state, artworkSource);
    else worker.generateStencil(state);
  }
}));
document.querySelector('#reset-artwork')!.addEventListener('click', () => {
  if (!artworkSource) return;
  const state = store.update({ stencil: undefined, processing: { stage: 'placement', message: 'Resetting artwork to fit in the geometry worker…' } });
  worker.importArtwork(state, artworkSource, true);
});
document.querySelector('#top-view')!.addEventListener('click', () => viewport.topView());
document.querySelector('#fit-view')!.addEventListener('click', () => viewport.fit());
for (const format of ['stl', '3mf'] as const) document.querySelector<HTMLButtonElement>(`#export-${format === 'stl' ? 'stl' : '3mf'}`)!.addEventListener('click', () => downloadCurrentStencil(format));

function downloadCurrentStencil(format: StencilExportFormat): void {
  const state = store.snapshot;
  if (!state.stencil?.isValid || !state.stencil.stencil) return;
  const stencil = state.stencil.stencil;
  const version = state.version;
  exportInProgress = true;
  store.setProcessing('export-preparation', `Preparing ${format.toUpperCase()} download…`);
  window.setTimeout(() => {
    try {
      if (store.snapshot.version !== version || store.snapshot.stencil !== state.stencil) return;
      const bytes = serializeStencil({ stencil, baseColor: state.settings.baseColor, captionColor: state.settings.captionColor }, format);
      const url = URL.createObjectURL(new Blob([bytes.buffer as ArrayBuffer], { type: format === 'stl' ? 'model/stl' : 'model/3mf' }));
      const link = document.createElement('a');
      link.href = url; link.download = `latte-shot-stencil.${format}`; link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 0);
      store.setProcessing('idle', `${format.toUpperCase()} download is ready.`);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown serialization failure.';
      store.setProcessing('error', message);
    } finally {
      exportInProgress = false;
      const latest = store.snapshot;
      document.querySelectorAll<HTMLButtonElement>('#export-stl, #export-3mf').forEach((button) => { button.disabled = latest.stencil?.isValid !== true; });
    }
  }, 0);
}
window.addEventListener('beforeunload', () => worker.dispose());
