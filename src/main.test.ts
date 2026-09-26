/// <reference types="vite/client" />

import { describe, expect, it } from 'vitest';
import mainSource from './main.ts?raw';

describe('bridge width control', () => {
  it('does not permit a bridge width below the 0.8 mm manufacturability minimum', () => {
    expect(mainSource).toContain('<input data-setting="bridgeWidth" type="number" min="0.8" step="0.1" value="0.8" />');
  });
});
