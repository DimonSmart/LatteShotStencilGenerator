import { describe, expect, it } from 'vitest';
import { captionHorizontalOrigin } from './caption-layout';

describe('caption horizontal alignment', () => {
  it('aligns inside equal side insets', () => {
    expect(captionHorizontalOrigin(88, 20, 6, 'left')).toBeCloseTo(6);
    expect(captionHorizontalOrigin(88, 20, 6, 'center')).toBeCloseTo(34);
    expect(captionHorizontalOrigin(88, 20, 6, 'right')).toBeCloseTo(62);
  });

  it('rejects invalid or too-narrow caption areas', () => {
    expect(() => captionHorizontalOrigin(20, 4, 11, 'center')).toThrow(/outside/i);
    expect(() => captionHorizontalOrigin(20, 4, 9, 'center')).toThrow(/fit completely/i);
  });
});
