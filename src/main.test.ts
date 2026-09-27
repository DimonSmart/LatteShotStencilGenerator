/// <reference types="vite/client" />

import { describe, expect, it } from 'vitest';
import mainSource from './main.ts?raw';

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
    expect(mainSource).toContain('data-setting="captionHorizontalAlignment"');
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
