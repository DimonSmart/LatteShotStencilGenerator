/// <reference types="vite/client" />

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const stylesheet = readFileSync(new URL('./styles.css', import.meta.url), 'utf8');

const mobileStart = '@media (max-width: 900px) {';
const nextBreakpoint = '@media (max-width: 620px) {';
const mobileStyles = stylesheet.slice(
  stylesheet.indexOf(mobileStart),
  stylesheet.indexOf(nextBreakpoint),
);

describe('responsive workspace layout', () => {
  it('keeps the desktop split layout and independent controls scrolling', () => {
    expect(stylesheet).toContain('grid-template-columns: minmax(0, 3fr) minmax(320px, 2fr);');
    expect(stylesheet).toContain('.controls {\n  min-width: 0;\n  min-height: 0;');
    expect(stylesheet).toContain('overflow-y: auto;');
  });

  it('uses the document scrollbar rather than a nested settings scroller on narrow screens', () => {
    expect(mobileStyles).toContain(mobileStart);
    expect(mobileStyles).toContain('body { overflow-x: hidden; overflow-y: auto; }');
    expect(mobileStyles).toContain('#app { display: block; height: auto; min-height: 100dvh; }');
    expect(mobileStyles).toContain('.workspace { display: block; overflow: visible; }');
    expect(mobileStyles).toContain('.controls { overflow: visible;');
  });

  it('keeps export and the size-bounded preview in normal mobile document flow', () => {
    expect(mobileStyles).toContain('.export-panel { position: static; }');
    expect(mobileStyles).toContain('.viewport { height: clamp(240px, 65vw, 340px); }');
    expect(mobileStyles).not.toContain('grid-template-rows: minmax(0, 1fr) auto;');
  });

  it('still supports a full-viewport preview overlay with its own scroll lock', () => {
    expect(stylesheet).toContain('.preview.is-expanded {\n  position: fixed;');
    expect(stylesheet).toContain('body.preview-expanded { overflow: hidden; }');
  });
});
