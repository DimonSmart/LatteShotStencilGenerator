export const bundledFonts = [
  { id: 'stencil-block', name: 'Stencil Block', license: 'Public domain', supportsCyrillic: false },
  { id: 'poiret-one', name: 'Poiret One', license: 'SIL OFL 1.1', supportsCyrillic: true },
  { id: 'russo-one', name: 'Russo One', license: 'SIL OFL 1.1', supportsCyrillic: true },
  { id: 'neucha', name: 'Neucha', license: 'SIL OFL 1.1', supportsCyrillic: true },
  { id: 'pt-mono', name: 'PT Mono', license: 'SIL OFL 1.1', supportsCyrillic: true },
] as const;

export type BundledFontId = typeof bundledFonts[number]['id'];
