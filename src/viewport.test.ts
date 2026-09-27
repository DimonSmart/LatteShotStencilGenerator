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


describe('viewport spatial guides', () => {
  it('renders a millimetre working grid with stronger major intervals', () => {
    expect(viewportSource).toContain('const GRID_STEP_MM = 5;');
    expect(viewportSource).toContain('const GRID_MAJOR_STEP_MM = 25;');
    expect(viewportSource).toContain("group.name = 'preview-grid';");
    expect(viewportSource).toContain('coordinate % GRID_MAJOR_STEP_MM === 0 ? major : minor');
  });

  it('keeps world axes in the scene and an orientation gizmo tied to camera rotation', () => {
    expect(viewportSource).toContain("group.name = 'preview-axes';");
    expect(viewportSource).toContain('new THREE.AxesHelper(22)');
    expect(viewportSource).toContain("gizmo.classList.add('orientation-gizmo');");
    expect(viewportSource).toContain('axis.applyQuaternion(cameraRotation);');
    expect(viewportSource).toContain("this.orientationView.textContent = '+Z top'");
  });
});
