import './styles.css';
import { GeometryWorkerClient } from './geometry-worker-client';
import { ProjectStore, type ProjectSettings, type ProjectState } from './project-state';
import { StencilViewport } from './viewport';
import { serializeStencil, type StencilExportFormat } from './stencil-export-adapter';
import { loadDefaultTemplate } from './bundled-template';
import { bundledFonts } from './font-catalog';

const app = document.querySelector<HTMLElement>('#app');
if (!app) throw new Error('Application root is unavailable.');
let artworkSource: string | undefined;
const captionFontOptions = bundledFonts.map((font) => `<option value="${font.id}">${font.name}</option>`).join('');

app.innerHTML = `
  <header class="app-header">
    <div class="header-copy">
      <h1>Latte Shot Stencil Generator</h1>
      <p>Create 3D printable stencils for latte art</p>
    </div>
    <div class="header-meta">
      <span class="privacy-note">Files stay in this browser tab.</span>
      <span id="processing-state" class="processing-state" data-stage="ready" aria-live="polite">Ready</span>
    </div>
  </header>
  <section class="workspace">
    <aside class="controls" aria-label="Stencil controls">
      <section class="panel project-files">
        <div class="section-heading"><h2>Project Files</h2><span>Template and artwork</span></div>
        <div class="file-grid">
          <label>3D template <input id="template" type="file" accept=".stl,.3mf" /></label>
          <label>SVG artwork <input id="artwork" type="file" accept="image/svg+xml,.svg" /></label>
        </div>
        <small>Upload a printable card template and the artwork that should be cut into it.</small>
      </section>

      <section class="panel">
        <div class="section-heading"><h2>Artwork Placement</h2><span>Primary controls</span></div>
        <div class="placement-controls">
          <label class="measurement-field">X offset (mm) <input data-setting="artworkX" type="number" step="0.1" value="0" /></label>
          <label class="measurement-field">Y offset (mm) <input data-setting="artworkY" type="number" step="0.1" value="0" /></label>
          <label>Scale <input data-setting="artworkScale" type="number" min="0.1" step="0.1" value="1" /></label>
          <button id="reset-artwork" class="secondary-action" type="button">Reset to fit</button>
        </div>
      </section>

      <section class="panel">
        <div class="section-heading"><h2>Caption</h2><span>Optional text</span></div>
        <div class="settings-grid caption-grid">
          <label class="wide-field">Text <input data-setting="caption" type="text" maxlength="80" /></label>
          <label>Font <select data-setting="captionFont">${captionFontOptions}</select></label>
          <fieldset class="alignment-field">
            <legend>Horizontal alignment</legend>
            <div class="segmented-control" role="radiogroup" aria-label="Caption horizontal alignment">
              <label class="segment"><input data-setting="captionHorizontalAlignment" type="radio" name="caption-horizontal-alignment" value="left" /><span>Left</span></label>
              <label class="segment"><input data-setting="captionHorizontalAlignment" type="radio" name="caption-horizontal-alignment" value="center" checked /><span>Center</span></label>
              <label class="segment"><input data-setting="captionHorizontalAlignment" type="radio" name="caption-horizontal-alignment" value="right" /><span>Right</span></label>
            </div>
          </fieldset>
          <label class="measurement-field">Size (mm) <input data-setting="captionSize" type="number" min="1" step="0.5" value="8" /></label>
        </div>
        <p id="caption-error" class="field-error" role="alert" hidden></p>
      </section>

      <details id="advanced-placement" class="panel accordion">
        <summary><span>Advanced Placement</span><small>Working areas and precise caption bounds</small></summary>
        <div class="accordion-content">
          <div class="advanced-group">
            <h3>Artwork working area</h3>
            <div class="compact-measurement-row">
              <label class="measurement-field">Left margin (mm) <input data-setting="artworkLeft" type="number" min="0" step="0.1" value="6.479" /></label>
              <label class="measurement-field">Right margin (mm) <input data-setting="artworkRight" type="number" min="0" step="0.1" value="6.479" /></label>
              <label class="measurement-field">Top margin (mm) <input data-setting="artworkTop" type="number" min="0" step="0.1" value="5.864" /></label>
              <label class="measurement-field">Bottom margin (mm) <input data-setting="artworkBottom" type="number" min="0" step="0.1" value="33.029" /></label>
            </div>
          </div>
          <div class="advanced-group">
            <h3>Caption placement</h3>
            <div class="compact-measurement-row">
              <label class="compact-select-field">Vertical alignment <select data-setting="captionVerticalAlignment"><option value="top">Top</option><option value="center" selected>Center</option><option value="bottom">Bottom</option></select></label>
              <label class="measurement-field">Caption Left (mm) <input data-setting="captionLeft" type="number" min="0" step="0.1" value="6.479" /></label>
              <label class="measurement-field">Caption Top (mm) <input data-setting="captionTop" type="number" min="0" step="0.1" value="80.853" /></label>
              <label class="measurement-field">Caption Right (mm) <input data-setting="captionRight" type="number" min="0" step="0.1" value="81.468" /></label>
              <label class="measurement-field">Caption Bottom (mm) <input data-setting="captionBottom" type="number" min="0" step="0.1" value="113.882" /></label>
              <label class="measurement-field">Emboss height (mm) <input data-setting="captionEmbossHeight" type="number" min="0.05" step="0.05" value="0.35" /></label>
            </div>
          </div>
        </div>
      </details>

      <details id="bridges-materials" class="panel accordion">
        <summary><span>Bridges &amp; Materials</span><small>Manufacturing and colors</small></summary>
        <div class="accordion-content">
          <div class="settings-grid four-columns">
            <label class="measurement-field">Minimum bridge width (mm) <input data-setting="bridgeWidth" type="number" min="0.8" step="0.1" value="0.8" /></label>
            <label>Support lines per island <input data-setting="bridgeCount" type="number" min="1" max="8" step="1" value="1" /></label>
            <label>Base color <input data-setting="baseColor" type="color" value="#f4ede4" /></label>
            <label>Caption color <input data-setting="captionColor" type="color" value="#6a3a22" /></label>
          </div>
          <p id="bridge-result" class="bridge-result" aria-live="polite">Bridges are generated when detached islands are detected.</p>
        </div>
      </details>

      <section class="export-panel" aria-label="Export">
        <div class="export-copy">
          <h2>Export</h2>
          <p id="export-note">Export is unavailable until printable geometry has been generated and validated.</p>
          <p id="status" class="status-message" aria-live="polite"></p>
        </div>
        <div class="export-actions"><button id="export-stl" type="button" disabled>Download STL</button><button id="export-3mf" type="button" disabled>Download 3MF</button></div>
      </section>
    </aside>

    <section id="preview-panel" class="preview preview-compact" aria-label="3D preview">
      <div class="viewport-toolbar">
        <strong>3D Preview</strong>
        <div class="viewport-actions">
          <button id="top-view" type="button">Top view</button>
          <button id="fit-view" type="button">Fit model</button>
          <button id="expand-preview" type="button" aria-expanded="false">Expand preview</button>
        </div>
      </div>
      <div id="viewport" class="viewport" role="img" aria-label="Interactive 3D stencil preview"></div>
      <p id="preview-hint" class="preview-hint">Quick visual check</p>
      <div class="preview-credit">
        <p>
          Inspired by the
          <a href="https://cults3d.com/en/3d-model/home/latteshot-one-click-coffee-art-camera" target="_blank" rel="noopener noreferrer">LatteShot One Click Coffee Art Camera</a>.
          Thank the creator of this masterpiece: buy the model. I hope it makes the author happy and inspires an even better version!
        </p>
        <a href="https://github.com/DimonSmart/LatteShotStencilGenerator" target="_blank" rel="noopener noreferrer">Source code on GitHub</a>
      </div>
    </section>
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
const previewPanel = document.querySelector<HTMLElement>('#preview-panel')!;
const expandPreviewButton = document.querySelector<HTMLButtonElement>('#expand-preview')!;
const processingState = document.querySelector<HTMLElement>('#processing-state')!;
const captionInput = document.querySelector<HTMLInputElement>('[data-setting="caption"]')!;
const captionError = document.querySelector<HTMLElement>('#caption-error')!;
const bridgeResult = document.querySelector<HTMLElement>('#bridge-result')!;
const previewHint = document.querySelector<HTMLElement>('#preview-hint')!;
const status = document.querySelector<HTMLElement>('#status')!;
const exportNote = document.querySelector<HTMLElement>('#export-note')!;
let renderedTemplate: ProjectState['geometry'] | undefined;
let renderedStencil: ProjectState['stencil'] | undefined;
let lastValidStencil: ProjectState['stencil'] | undefined;
let renderedBaseColor = '';
let renderedCaptionColor = '';
let exportInProgress = false;

store.subscribe((state) => {
  status.textContent = state.processing.message;
  const processingLabel = state.processing.stage === 'idle' ? 'Ready' : state.processing.stage === 'error' ? 'Error' : 'Processing…';
  processingState.textContent = processingLabel;
  processingState.dataset.stage = state.processing.stage === 'idle' ? 'ready' : state.processing.stage === 'error' ? 'error' : 'processing';
  const canExport = state.stencil?.isValid === true && !exportInProgress;
  document.querySelectorAll<HTMLButtonElement>('#export-stl, #export-3mf').forEach((button) => { button.disabled = !canExport; });
  exportNote.textContent = canExport ? 'Printable stencil geometry is valid.' : 'Export is available once the generated stencil is valid.';

  if (state.stencil?.isValid) lastValidStencil = state.stencil;
  const stencilFailed = state.stencil?.isValid === false;
  const previewStencil = stencilFailed && lastValidStencil ? lastValidStencil : state.stencil;
  const showingLastValid = stencilFailed && previewStencil === lastValidStencil && lastValidStencil?.isValid === true;
  const captionFailure = stencilFailed && /caption/i.test(state.processing.message)
    ? state.processing.message.replace(/^Stencil generation failed:\s*/i, '')
    : '';
  captionError.textContent = captionFailure;
  captionError.hidden = !captionFailure;
  captionInput.setAttribute('aria-invalid', String(Boolean(captionFailure)));
  previewPanel.classList.toggle('is-stale', showingLastValid);
  previewHint.textContent = showingLastValid ? 'Preview shows the last valid result; fix the error before export.' : 'Quick visual check';

  const generatedBridgeState = state.stencil?.isValid ? state.stencil.stencil : undefined;
  if (generatedBridgeState) {
    const actualCount = generatedBridgeState.bridges.length;
    const shortfallCount = generatedBridgeState.bridgeShortfallIslandCount ?? 0;
    bridgeResult.dataset.state = shortfallCount > 0 ? 'warning' : 'ready';
    bridgeResult.textContent = actualCount === 0
      ? 'No detached islands detected; no bridges are needed.'
      : shortfallCount > 0
        ? `Generated ${actualCount} bridge${actualCount === 1 ? '' : 's'} total. ${shortfallCount} island${shortfallCount === 1 ? '' : 's'} could not fit all ${state.settings.bridgeCount} requested supports.`
        : `Generated ${actualCount} bridge${actualCount === 1 ? '' : 's'} total at ${state.settings.bridgeWidth.toFixed(1)} mm width.`;
  } else {
    bridgeResult.dataset.state = state.processing.stage === 'error' ? 'warning' : 'processing';
    bridgeResult.textContent = state.processing.stage === 'error'
      ? 'Bridge result is unavailable until the current geometry error is fixed.'
      : 'Updating bridge geometry…';
  }

  if (renderedStencil !== previewStencil || renderedBaseColor !== state.settings.baseColor || renderedCaptionColor !== state.settings.captionColor || (!previewStencil?.isValid && renderedTemplate !== state.geometry)) {
    renderedTemplate = state.geometry;
    renderedStencil = previewStencil;
    renderedBaseColor = state.settings.baseColor;
    renderedCaptionColor = state.settings.captionColor;
    const generated = previewStencil?.isValid ? previewStencil.stencil : undefined;
    viewport.setTemplate(generated ?? (state.geometry?.isValid ? state.geometry.template : undefined), state.settings.baseColor, generated?.caption, state.settings.captionColor, state.geometry?.isValid ? state.geometry.template : undefined);
  }
  viewport.setArtwork(state.geometry?.template, state.artwork?.isValid ? state.artwork : undefined, previewStencil?.isValid ? previewStencil.stencil : undefined, state.settings.baseColor, state.settings.captionColor);
  document.querySelectorAll<HTMLInputElement | HTMLSelectElement>('[data-setting]').forEach((input) => {
    const value = state.settings[input.dataset.setting as keyof ProjectSettings];
    if (input instanceof HTMLInputElement && input.type === 'radio') input.checked = input.value === String(value);
    else if (document.activeElement !== input) input.value = String(value);
  });
});

const templateInput = document.querySelector<HTMLInputElement>('#template')!;
templateInput.addEventListener('change', async () => {
  const templateFile = (templateInput.files ?? [])[0];
  if (!templateFile) return;
  lastValidStencil = undefined;
  await importTemplate(templateFile);
});
const artworkInput = document.querySelector<HTMLInputElement>('#artwork')!;
artworkInput.addEventListener('change', async () => {
  const artworkFile = (artworkInput.files ?? [])[0];
  if (!artworkFile) return;
  lastValidStencil = undefined;
  artworkSource = await artworkFile.text();
  const state = store.update({ artworkFile, artwork: undefined, stencil: undefined, processing: { stage: 'artwork-import', message: `Validating ${artworkFile.name} in the geometry worker…` } });
  worker.importArtwork(state, artworkSource, true);
});
document.querySelectorAll<HTMLInputElement | HTMLSelectElement>('[data-setting]').forEach((input) => input.addEventListener('input', () => {
  if (input instanceof HTMLInputElement && input.type === 'radio' && !input.checked) return;
  const key = input.dataset.setting as keyof ProjectSettings;
  const value = input.type === 'number' ? Number(input.value) : input.value;
  const regeneratesStencil = ['artworkLeft', 'artworkRight', 'artworkTop', 'artworkBottom', 'artworkX', 'artworkY', 'artworkScale', 'bridgeWidth', 'bridgeCount', 'caption', 'captionFont', 'captionHorizontalAlignment', 'captionVerticalAlignment', 'captionLeft', 'captionTop', 'captionRight', 'captionBottom', 'captionSize', 'captionEmbossHeight'].includes(key);
  const bridgeSettingChanged = key === 'bridgeWidth' || key === 'bridgeCount';
  const state = artworkSource && regeneratesStencil
    ? store.update({
      settings: { [key]: value },
      stencil: undefined,
      processing: bridgeSettingChanged
        ? { stage: 'bridge-generation', message: 'Updating automatic bridge geometry…' }
        : { stage: 'placement', message: 'Updating generated stencil geometry…' },
    })
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
expandPreviewButton.addEventListener('click', () => setPreviewExpanded(!previewPanel.classList.contains('is-expanded')));
window.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && previewPanel.classList.contains('is-expanded')) setPreviewExpanded(false);
});
for (const format of ['stl', '3mf'] as const) document.querySelector<HTMLButtonElement>(`#export-${format === 'stl' ? 'stl' : '3mf'}`)!.addEventListener('click', () => downloadCurrentStencil(format));

function setPreviewExpanded(expanded: boolean): void {
  previewPanel.classList.toggle('is-expanded', expanded);
  document.body.classList.toggle('preview-expanded', expanded);
  expandPreviewButton.setAttribute('aria-expanded', String(expanded));
  expandPreviewButton.textContent = expanded ? 'Close preview' : 'Expand preview';
}

async function importTemplate(templateFile: File): Promise<void> {
  const state = store.update({ templateFile, stencil: undefined, processing: { stage: 'template-import', message: `Importing ${templateFile.name} in the geometry worker…` } });
  worker.importTemplate(state, templateFile.name, await templateFile.arrayBuffer());
}

void loadDefaultTemplate()
  .then(importTemplate)
  .catch((error: unknown) => store.setProcessing('error', error instanceof Error ? error.message : 'The bundled blank card could not be loaded.'));

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
