/// <reference types="vite/client" />

import { describe, expect, it } from 'vitest';
import mainSource from './main.ts?raw';
import stylesSource from './styles.css?raw';

describe('bridge controls', () => {
  it('does not permit a bridge width below the 0.8 mm manufacturability minimum', () => {
    expect(mainSource).toContain('<input data-setting="bridgeWidth" type="number" min="0.8" step="0.1" value="0.8" />');
  });

  it('offers one through eight supports per island and regenerates geometry when the count changes', () => {
    expect(mainSource).toContain('<input data-setting="bridgeCount" type="number" min="1" max="8" step="1" value="1" />');
    expect(mainSource).toContain("'bridgeWidth', 'bridgeCount', 'caption'");
  });
});

describe('caption controls', () => {
  it('offers centered-by-default horizontal and vertical alignment inside a caption rectangle', () => {
    expect(mainSource).toContain('data-setting="captionHorizontalAlignment" type="radio"');
    expect(mainSource).toContain('value="center" checked');
    expect(mainSource).toContain('data-setting="captionVerticalAlignment"');
    expect(mainSource).toContain('<option value="center" selected>Center</option>');
    expect(mainSource).toContain('data-setting="captionLeft"');
    expect(mainSource).toContain('data-setting="captionTop"');
    expect(mainSource).toContain('data-setting="captionRight"');
    expect(mainSource).toContain('data-setting="captionBottom"');
    expect(mainSource).toContain("'captionHorizontalAlignment'");
    expect(mainSource).toContain("'captionVerticalAlignment'");
  });

  it('builds the font picker from the bundled font catalog', () => {
    expect(mainSource).toContain('bundledFonts.map');
    expect(mainSource).toContain('captionFontOptions');
  });
});

describe('workspace layout', () => {
  it('keeps the normal preview compact and independent from the controls height', () => {
    expect(mainSource).toContain('class="preview preview-compact"');
    expect(stylesSource).toContain('height: clamp(300px, 44vh, 420px)');
    expect(stylesSource).not.toContain('min-height: 540px');
  });

  it('keeps advanced placement and bridge controls available behind closed details elements', () => {
    expect(mainSource).toContain('<details id="advanced-placement" class="panel accordion">');
    expect(mainSource).toContain('<details id="bridges-materials" class="panel accordion">');
    expect(mainSource).toContain('data-setting="artworkLeft"');
    expect(mainSource).toContain('data-setting="captionEmbossHeight"');
  });

  it('supports expanding and restoring the existing preview, including Escape', () => {
    expect(mainSource).toContain('id="expand-preview"');
    expect(mainSource).toContain("previewPanel.classList.toggle('is-expanded', expanded)");
    expect(mainSource).toContain("event.key === 'Escape'");
    expect(mainSource).toContain("expanded ? 'Close preview' : 'Expand preview'");
  });

  it('preserves the existing export button ids', () => {
    expect(mainSource).toContain('id="export-stl"');
    expect(mainSource).toContain('id="export-3mf"');
  });
});
