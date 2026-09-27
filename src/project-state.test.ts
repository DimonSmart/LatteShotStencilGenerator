import { describe, expect, it } from 'vitest';
import { ProjectStore } from './project-state';

describe('project settings', () => {
  it('defaults to one support line per detached island', () => {
    expect(new ProjectStore().snapshot.settings.bridgeCount).toBe(1);
  });
});

describe('caption worker result protection', () => {
  it('never lets an older caption result replace a newer editable caption', () => {
    const store = new ProjectStore();
    const olderVersion = store.snapshot.version;
    store.update({ settings: { caption: 'NEW' }, stencil: undefined });
    const applied = store.applyStencil({ kind: 'stencil', version: olderVersion, isValid: false, message: 'old caption failed' });
    expect(applied).toBe(false);
    expect(store.snapshot.settings.caption).toBe('NEW');
    expect(store.snapshot.stencil).toBeUndefined();
  });
});

describe('bundled blank-card layout defaults', () => {
  it('places the artwork square above the footer caption area', () => {
    const { settings } = new ProjectStore().snapshot;

    expect(settings).toMatchObject({
      artworkLeft: 6.479,
      artworkRight: 6.479,
      artworkTop: 5.864,
      artworkBottom: 33.029,
      captionHorizontalAlignment: 'center',
      captionVerticalAlignment: 'center',
      captionLeft: 6.479,
      captionTop: 80.853,
      captionRight: 81.468,
      captionBottom: 113.882,
    });
  });
});
