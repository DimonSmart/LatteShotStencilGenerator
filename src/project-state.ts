export type ProcessingStage =
  | 'idle'
  | 'template-import'
  | 'artwork-import'
  | 'placement'
  | 'bridge-generation'
  | 'solid-generation'
  | 'preview-update'
  | 'export-preparation'
  | 'error';

export interface ProjectSettings {
  artworkLeft: number;
  artworkRight: number;
  artworkTop: number;
  artworkBottom: number;
  artworkX: number;
  artworkY: number;
  artworkScale: number;
  caption: string;
  captionFont: import('./font-outline-adapter').BundledFontId;
  captionX: number;
  captionY: number;
  captionSize: number;
  captionEmbossHeight: number;
  bridgeWidth: number;
  bridgeCount: number;
  baseColor: string;
  captionColor: string;
}

export interface GeometryResult {
  readonly kind: 'template';
  readonly version: number;
  readonly isValid: boolean;
  readonly message: string;
  readonly template?: import('./template-geometry').TemplateGeometry;
}

export interface ArtworkResult {
  readonly kind: 'artwork';
  readonly version: number;
  readonly isValid: boolean;
  readonly message: string;
  readonly artwork?: import('./svg-artwork-adapter').PlanarArtwork;
  readonly placement?: import('./artwork-placement').ArtworkPlacement;
  readonly workingRectangle?: import('./artwork-placement').WorkingRectangle;
}

export interface StencilResult {
  readonly kind: 'stencil';
  readonly version: number;
  readonly isValid: boolean;
  readonly message: string;
  readonly stencil?: import('./stencil-generation').GeneratedStencil;
}

export interface ProjectState {
  readonly version: number;
  readonly templateFile?: File;
  readonly artworkFile?: File;
  readonly settings: ProjectSettings;
  readonly processing: { stage: ProcessingStage; message: string };
  readonly geometry?: GeometryResult;
  readonly artwork?: ArtworkResult;
  readonly stencil?: StencilResult;
}

const initialSettings: ProjectSettings = {
  artworkLeft: 6.678,
  artworkRight: 6.678,
  artworkTop: 33.029,
  artworkBottom: 5.864,
  artworkX: 0,
  artworkY: 0,
  artworkScale: 1,
  caption: '',
  captionFont: 'stencil-block',
  captionX: 6.678,
  captionY: 8,
  captionSize: 8,
  captionEmbossHeight: 0.35,
  bridgeWidth: 0.8,
  bridgeCount: 1,
  baseColor: '#f4ede4',
  captionColor: '#6a3a22',
};

export class ProjectStore {
  private state: ProjectState = {
    version: 0,
    settings: initialSettings,
    processing: { stage: 'idle', message: 'Load a template and SVG artwork to begin.' },
  };
  private readonly listeners = new Set<(state: ProjectState) => void>();

  get snapshot(): ProjectState { return this.state; }

  subscribe(listener: (state: ProjectState) => void): () => void {
    this.listeners.add(listener);
    listener(this.state);
    return () => this.listeners.delete(listener);
  }

  update(change: Partial<Omit<ProjectState, 'version' | 'settings'>> & { settings?: Partial<ProjectSettings> }): ProjectState {
    this.state = {
      ...this.state,
      ...change,
      version: this.state.version + 1,
      settings: { ...this.state.settings, ...change.settings },
      geometry: change.geometry === undefined ? this.state.geometry : change.geometry,
      stencil: Object.prototype.hasOwnProperty.call(change, 'stencil') ? change.stencil : this.state.stencil,
    };
    this.emit();
    return this.state;
  }

  setProcessing(stage: ProcessingStage, message: string): void {
    this.state = { ...this.state, processing: { stage, message } };
    this.emit();
  }

  applyGeometry(result: GeometryResult): boolean {
    if (result.version !== this.state.version) return false;
    this.state = result.isValid
      ? { ...this.state, geometry: result, processing: { stage: 'idle', message: result.message } }
      : { ...this.state, processing: { stage: 'error', message: result.message } };
    this.emit();
    return true;
  }

  applyArtwork(result: ArtworkResult): boolean {
    if (result.version !== this.state.version) return false;
    this.state = result.isValid
      ? { ...this.state, artwork: result, settings: result.placement ? { ...this.state.settings, artworkX: result.placement.x, artworkY: result.placement.y, artworkScale: result.placement.scale } : this.state.settings, processing: { stage: 'idle', message: result.message } }
      : { ...this.state, artwork: result, processing: { stage: 'error', message: result.message } };
    this.emit();
    return true;
  }

  applyStencil(result: StencilResult): boolean {
    if (result.version !== this.state.version) return false;
    this.state = result.isValid
      ? { ...this.state, stencil: result, processing: { stage: 'idle', message: result.message } }
      : { ...this.state, stencil: result, processing: { stage: 'error', message: result.message } };
    this.emit();
    return true;
  }

  private emit(): void { this.listeners.forEach((listener) => listener(this.state)); }
}
