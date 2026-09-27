import { describe, expect, it } from 'vitest';
import { bundledFonts } from './font-catalog';
import { captionOutline } from './font-outline-adapter';

describe('bundled caption font outlines', () => {
  it('converts equal text, font, and size into deterministic closed outlines', () => {
    const first = captionOutline('Latte 42', 'stencil-block', 8);
    const second = captionOutline('Latte 42', 'stencil-block', 8);
    expect(first).toEqual(second);
    expect(first.contours.length).toBeGreaterThan(0);
    expect(first.contours.every((contour) => contour.length === 4)).toBe(true);
  });

  it('bundles several deterministic Cyrillic-capable fonts', () => {
    const cyrillicFonts = bundledFonts.filter((font) => font.supportsCyrillic);
    expect(cyrillicFonts.map((font) => font.id)).toEqual(['poiret-one', 'russo-one', 'neucha', 'pt-mono']);
    for (const font of cyrillicFonts) {
      const first = captionOutline('Привет Ёж', font.id, 8);
      const second = captionOutline('Привет Ёж', font.id, 8);
      expect(first).toEqual(second);
      expect(first.contours.length).toBeGreaterThan(0);
      expect(first.bounds.maxX).toBeGreaterThan(0);
    }
  });

  it('uses top-left reference-card coordinates without mirroring', () => {
    const outline = captionOutline('A', 'stencil-block', 7);
    expect(outline.bounds).toEqual({ minX: 0, minY: 0, maxX: 5, maxY: 7 });
  });
});
