import blankCardUrl from './assets/Blank_Card.stl?url';

export const defaultTemplateName = 'Blank_Card.stl';

/** Loads the packaged blank card through the same bytes-based import path as user files. */
export async function loadDefaultTemplate(): Promise<File> {
  const response = await fetch(blankCardUrl);
  if (!response.ok) throw new Error('The bundled blank card could not be loaded.');
  return new File([await response.arrayBuffer()], defaultTemplateName, { type: 'model/stl' });
}
