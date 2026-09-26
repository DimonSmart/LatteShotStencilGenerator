/// <reference types="vite/client" />

import { describe, expect, it } from 'vitest';
import workerSource from './geometry.worker.ts?raw';

describe('geometry worker bridge settings', () => {
  it('passes both bridge width and per-island support count into stencil generation', () => {
    expect(workerSource).toContain('{ width: settings.bridgeWidth, count: settings.bridgeCount }');
    expect(workerSource).toContain('received fewer than the requested');
  });
});
