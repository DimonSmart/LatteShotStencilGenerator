import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import type { ArtworkResult } from './project-state';
import type { TemplateGeometry } from './template-geometry';
import type { GeneratedStencil } from './stencil-generation';

const GRID_HALF_SIZE_MM = 100;
const GRID_STEP_MM = 5;
const GRID_MAJOR_STEP_MM = 25;
const GRID_Z = -0.03;
const GIZMO_CENTER = 42;
const GIZMO_AXIS_LENGTH = 27;
const SVG_NAMESPACE = 'http://www.w3.org/2000/svg';

type GizmoAxis = 'x' | 'y' | 'z';

export class StencilViewport {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(45, 1, 0.1, 1000);
  private readonly renderer: THREE.WebGLRenderer;
  private readonly controls: OrbitControls;
  private readonly orientationGizmo: SVGSVGElement;
  private readonly orientationView: HTMLSpanElement;
  private readonly orientationAxes = new Map<GizmoAxis, { line: SVGLineElement; label: SVGTextElement }>();
  private template?: THREE.Mesh;
  private caption?: THREE.Mesh;
  private artworkOverlay?: THREE.Group;
  private hasFramedContent = false;

  constructor(private readonly element: HTMLElement) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.setClearColor(0x15110f);
    element.append(this.renderer.domElement);

    this.scene.add(this.createGrid());
    this.scene.add(this.createSceneAxes());
    this.scene.add(new THREE.HemisphereLight(0xffe9d6, 0x201611, 2));

    const orientation = this.createOrientationGizmo();
    this.orientationGizmo = orientation.gizmo;
    this.orientationView = orientation.view;
    element.append(this.orientationGizmo, this.orientationView);

    this.camera.position.set(80, -80, 100);
    this.camera.lookAt(0, 0, 0);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.target.set(0, 0, 0);
    this.controls.addEventListener('change', () => this.render());
    new ResizeObserver(() => this.resize()).observe(element);
    this.resize();
  }

  topView(): void {
    const box = this.bounds();
    const center = box.getCenter(new THREE.Vector3());
    const distance = Math.max(box.getSize(new THREE.Vector3()).x, box.getSize(new THREE.Vector3()).y, 1) * 1.5;
    this.camera.position.set(center.x, center.y, box.max.z + distance);
    this.camera.up.set(0, 1, 0);
    this.camera.lookAt(center);
    this.controls.target.copy(center);
    this.controls.update();
    this.render();
  }

  fit(): void {
    const box = this.bounds();
    const center = box.getCenter(new THREE.Vector3());
    const distance = box.getSize(new THREE.Vector3()).length() * 1.4;
    this.camera.position.copy(center).add(new THREE.Vector3(distance, -distance, distance));
    this.controls.target.copy(center);
    this.camera.lookAt(center);
    this.controls.update();
    this.render();
  }

  setTemplate(template?: { positions: Float32Array; indices: Uint32Array }, baseColor = '#f4ede4', caption?: { positions: Float32Array; indices: Uint32Array }, captionColor = '#6a3a22'): void {
    if (this.template) { this.scene.remove(this.template); this.template.geometry.dispose(); }
    if (this.caption) { this.scene.remove(this.caption); this.caption.geometry.dispose(); }
    this.template = undefined;
    this.caption = undefined;
    if (!template) return this.render();
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(template.positions, 3));
    geometry.setIndex(new THREE.BufferAttribute(template.indices, 1));
    geometry.computeVertexNormals();
    this.template = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ color: baseColor, metalness: 0, roughness: 0.7 }));
    this.scene.add(this.template);
    if (caption) {
      const captionGeometry = new THREE.BufferGeometry(); captionGeometry.setAttribute('position', new THREE.BufferAttribute(caption.positions, 3)); captionGeometry.setIndex(new THREE.BufferAttribute(caption.indices, 1)); captionGeometry.computeVertexNormals();
      this.caption = new THREE.Mesh(captionGeometry, new THREE.MeshStandardMaterial({ color: captionColor, roughness: 0.7, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 })); this.scene.add(this.caption);
    }
    if (!this.hasFramedContent) {
      this.hasFramedContent = true;
      this.fit();
    } else {
      this.render();
    }
  }

  /** Preview-only top-side contour overlay; the solid itself is set through setTemplate. */
  setArtwork(template: TemplateGeometry | undefined, result: ArtworkResult | undefined, _stencil?: GeneratedStencil, _baseColor?: string, _captionColor?: string): void {
    if (this.artworkOverlay) { this.scene.remove(this.artworkOverlay); this.artworkOverlay.traverse((item) => { if (item instanceof THREE.Line) item.geometry.dispose(); }); }
    this.artworkOverlay = undefined;
    if (!template || !result?.artwork || !result.placement) return this.render();
    const group = new THREE.Group();
    const { bounds } = template; const { placement } = result;
    for (const contour of result.artwork.contours) {
      const points = contour.points.map(([x, y]) => new THREE.Vector3(bounds.min[0] + x * placement.scale + placement.x, bounds.max[1] - (y * placement.scale + placement.y), bounds.max[2] + 0.03));
      points.push(points[0].clone());
      group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color: contour.hole ? 0xf7ca62 : 0x63d7ff })));
    }
    this.artworkOverlay = group; this.scene.add(group); this.render();
  }

  render(): void {
    this.updateOrientationGizmo();
    this.renderer.render(this.scene, this.camera);
  }

  private createGrid(): THREE.Group {
    const group = new THREE.Group();
    group.name = 'preview-grid';

    const minor: number[] = [];
    const major: number[] = [];
    for (let coordinate = -GRID_HALF_SIZE_MM; coordinate <= GRID_HALF_SIZE_MM; coordinate += GRID_STEP_MM) {
      if (coordinate === 0) continue;
      const target = coordinate % GRID_MAJOR_STEP_MM === 0 ? major : minor;
      target.push(-GRID_HALF_SIZE_MM, coordinate, GRID_Z, GRID_HALF_SIZE_MM, coordinate, GRID_Z);
      target.push(coordinate, -GRID_HALF_SIZE_MM, GRID_Z, coordinate, GRID_HALF_SIZE_MM, GRID_Z);
    }

    group.add(this.createGridLines(minor, 0x4a403b, 0.34));
    group.add(this.createGridLines(major, 0x766158, 0.58));

    const xAxis = this.createGridLines(
      [-GRID_HALF_SIZE_MM, 0, GRID_Z, GRID_HALF_SIZE_MM, 0, GRID_Z],
      0xb95750,
      0.72,
    );
    const yAxis = this.createGridLines(
      [0, -GRID_HALF_SIZE_MM, GRID_Z, 0, GRID_HALF_SIZE_MM, GRID_Z],
      0x5f9d68,
      0.72,
    );
    group.add(xAxis, yAxis);
    return group;
  }

  private createGridLines(positions: number[], color: number, opacity: number): THREE.LineSegments {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    const material = new THREE.LineBasicMaterial({
      color,
      transparent: true,
      opacity,
      depthTest: true,
      depthWrite: false,
    });
    return new THREE.LineSegments(geometry, material);
  }

  private createSceneAxes(): THREE.Group {
    const group = new THREE.Group();
    group.name = 'preview-axes';

    const axes = new THREE.AxesHelper(22);
    axes.setColors(new THREE.Color(0xd7655c), new THREE.Color(0x67ad70), new THREE.Color(0x658bd6));
    const material = axes.material as THREE.LineBasicMaterial;
    material.transparent = true;
    material.opacity = 0.88;
    material.depthWrite = false;
    group.add(axes);

    const origin = new THREE.Mesh(
      new THREE.SphereGeometry(0.75, 12, 8),
      new THREE.MeshBasicMaterial({ color: 0xf3dfcf, depthWrite: false }),
    );
    group.add(origin);
    return group;
  }

  private createOrientationGizmo(): { gizmo: SVGSVGElement; view: HTMLSpanElement } {
    const gizmo = document.createElementNS(SVG_NAMESPACE, 'svg');
    gizmo.classList.add('orientation-gizmo');
    gizmo.setAttribute('viewBox', '0 0 84 84');
    gizmo.setAttribute('aria-hidden', 'true');

    const center = document.createElementNS(SVG_NAMESPACE, 'circle');
    center.classList.add('orientation-origin');
    center.setAttribute('cx', String(GIZMO_CENTER));
    center.setAttribute('cy', String(GIZMO_CENTER));
    center.setAttribute('r', '2.5');
    gizmo.append(center);

    for (const axis of ['x', 'y', 'z'] as const) {
      const line = document.createElementNS(SVG_NAMESPACE, 'line');
      line.classList.add('orientation-axis', `orientation-axis-${axis}`);
      line.setAttribute('x1', String(GIZMO_CENTER));
      line.setAttribute('y1', String(GIZMO_CENTER));
      line.setAttribute('x2', String(GIZMO_CENTER));
      line.setAttribute('y2', String(GIZMO_CENTER));

      const label = document.createElementNS(SVG_NAMESPACE, 'text');
      label.classList.add('orientation-label', `orientation-label-${axis}`);
      label.textContent = axis.toUpperCase();
      gizmo.append(line, label);
      this.orientationAxes.set(axis, { line, label });
    }

    const view = document.createElement('span');
    view.className = 'orientation-view';
    view.setAttribute('aria-hidden', 'true');
    return { gizmo, view };
  }

  private updateOrientationGizmo(): void {
    const cameraRotation = this.camera.quaternion.clone().invert();
    const axes: Record<GizmoAxis, THREE.Vector3> = {
      x: new THREE.Vector3(1, 0, 0),
      y: new THREE.Vector3(0, 1, 0),
      z: new THREE.Vector3(0, 0, 1),
    };

    for (const [name, axis] of Object.entries(axes) as [GizmoAxis, THREE.Vector3][]) {
      axis.applyQuaternion(cameraRotation);
      const projectedLength = Math.hypot(axis.x, axis.y);
      const dx = axis.x * GIZMO_AXIS_LENGTH;
      const dy = -axis.y * GIZMO_AXIS_LENGTH;
      const lineEndX = GIZMO_CENTER + dx;
      const lineEndY = GIZMO_CENTER + dy;
      const labelOffsetX = projectedLength > 0.08 ? (axis.x / projectedLength) * 7 : 7;
      const labelOffsetY = projectedLength > 0.08 ? (-axis.y / projectedLength) * 7 : axis.z >= 0 ? -7 : 9;
      const item = this.orientationAxes.get(name)!;
      item.line.setAttribute('x2', lineEndX.toFixed(2));
      item.line.setAttribute('y2', lineEndY.toFixed(2));
      item.label.setAttribute('x', (lineEndX + labelOffsetX).toFixed(2));
      item.label.setAttribute('y', (lineEndY + labelOffsetY).toFixed(2));
    }

    const viewDirection = this.camera.getWorldDirection(new THREE.Vector3());
    if (viewDirection.z < -0.985) this.orientationView.textContent = '+Z top';
    else if (viewDirection.z > 0.985) this.orientationView.textContent = '-Z bottom';
    else this.orientationView.textContent = '';
  }

  private bounds(): THREE.Box3 {
    return this.template ? new THREE.Box3().setFromObject(this.template) : new THREE.Box3(new THREE.Vector3(-40, -26, 0), new THREE.Vector3(40, 26, 1));
  }

  private resize(): void {
    const { width, height } = this.element.getBoundingClientRect();
    if (!width || !height) return;
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height, false);
    this.render();
  }
}
