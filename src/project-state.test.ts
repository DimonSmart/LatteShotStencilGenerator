import { describe, expect, it } from 'vitest';
import { ProjectStore } from './project-state';

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
