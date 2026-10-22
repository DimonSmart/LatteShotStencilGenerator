import { describe, expect, it } from 'vitest';
import { getFileCardPresentation } from './file-card-state';

describe('file card presentation', () => {
  it('offers upload with a clear empty artwork state', () => {
    expect(getFileCardPresentation('artwork', 'empty')).toEqual({
      detail: 'No artwork selected', action: 'Upload', actionLabel: 'Upload SVG artwork file',
    });
  });

  it('displays the bundled template name after successful validation', () => {
    expect(getFileCardPresentation('template', 'ready', 'Blank_Card.stl')).toEqual({
      detail: 'Blank_Card.stl · Loaded', action: 'Replace', actionLabel: 'Replace 3D template',
    });
  });

  it('differentiates loading and failed custom files', () => {
    expect(getFileCardPresentation('template', 'loading', 'my-card.3mf').detail).toBe('my-card.3mf · Loading…');
    expect(getFileCardPresentation('artwork', 'error', 'invalid.svg')).toEqual({
      detail: 'invalid.svg · Failed', action: 'Replace', actionLabel: 'Replace SVG artwork',
    });
  });

  it('retains full long names for display and accessible titles without slicing them', () => {
    const name = 'very-long-untrusted-artwork-filename-to-show-ellipsis.svg';
    expect(getFileCardPresentation('artwork', 'ready', name).detail).toContain(name);
  });
});
