/// <reference types="vite/client" />

import { describe, expect, it } from 'vitest';
import viewportSource from './viewport.ts?raw';

describe('viewport camera persistence', () => {
  it('frames only the first displayed model and preserves the user view on later geometry updates', () => {
    expect(viewportSource).toContain('private hasFramedContent = false;');
    expect(viewportSource).toContain('if (!this.hasFramedContent)');
    expect(viewportSource).toContain('this.hasFramedContent = true;');
    expect(viewportSource.match(/this\.fit\(\);/g)).toHaveLength(1);
    expect(viewportSource).toContain('} else {\n      this.render();\n    }');
  });
});
