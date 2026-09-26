import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import type { ArtworkResult } from './project-state';
import type { TemplateGeometry } from './template-geometry';
import type { GeneratedStencil } from './stencil-generation';

export class StencilViewport {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(45, 1, 0.1, 1000);
  private readonly renderer: THREE.WebGLRenderer;
  private readonly controls: OrbitControls;
  private template?: THREE.Mesh;
  private caption?: THREE.Mesh;
  private artworkOverlay?: THREE.Group;

  constructor(private readonly element: HTMLElement) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.setClearColor(0x15110f);
    element.append(this.renderer.domElement);
    this.scene.add(new THREE.HemisphereLight(0xffe9d6, 0x201611, 2));
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
    this.fit();
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

  render(): void { this.renderer.render(this.scene, this.camera); }

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
