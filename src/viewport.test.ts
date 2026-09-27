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

  it('centers preview content on the source template instead of generated content', () => {
    expect(viewportSource).toContain("private readonly contentRoot = new THREE.Group();");
    expect(viewportSource).toContain("this.contentRoot.name = 'preview-content';");
    expect(viewportSource).toContain('this.updatePreviewOrigin(referenceTemplate);');
    expect(viewportSource).toContain('const centerX = (template.bounds.min[0] + template.bounds.max[0]) / 2;');
    expect(viewportSource).toContain('const centerY = (template.bounds.min[1] + template.bounds.max[1]) / 2;');
    expect(viewportSource).toContain('this.contentRoot.position.set(-centerX, -centerY, 0);');
    expect(viewportSource).toContain('this.contentRoot.add(this.template);');
    expect(viewportSource).toContain('this.contentRoot.add(group);');
  });

  it('keeps world axes in the scene and an orientation gizmo tied to camera rotation', () => {
    expect(viewportSource).toContain("group.name = 'preview-axes';");
    expect(viewportSource).toContain('new THREE.AxesHelper(22)');
    expect(viewportSource).toContain("gizmo.classList.add('orientation-gizmo');");
    expect(viewportSource).toContain('axis.applyQuaternion(cameraRotation);');
    expect(viewportSource).toContain("this.orientationView.textContent = '+Z top'");
  });
});
