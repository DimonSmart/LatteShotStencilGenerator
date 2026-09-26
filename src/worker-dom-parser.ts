import { DOMParser as XmlDomParser } from '@xmldom/xmldom';

type SelectorDocument = Document & {
  querySelector?: (selector: string) => Element | null;
  querySelectorAll?: (selector: string) => NodeListOf<Element>;
};

function elementsWithin(root: Node, selector: string): Element[] {
  const names = selector.split(',').map((name) => name.trim().toLowerCase());
  const matches = (element: Element) => names.includes('*') || names.includes(element.localName.toLowerCase());
  const result: Element[] = [];
  const visit = (node: Node): void => {
    if (node.nodeType === 1 && matches(node as Element)) result.push(node as Element);
    for (const child of Array.from(node.childNodes)) visit(child);
  };
  visit(root);
  return result;
}

/** Provides the DOM subset used by SVGLoader when it runs in a dedicated worker. */
export function installWorkerDomParser(): void {
  const scope = globalThis as typeof globalThis & { DOMParser?: typeof DOMParser };
  if (scope.DOMParser) return;
  scope.DOMParser = XmlDomParser as unknown as typeof DOMParser;
  const document = new XmlDomParser().parseFromString('<svg/>', 'image/svg+xml') as unknown as SelectorDocument;
  const prototype = Object.getPrototypeOf(document) as SelectorDocument;
  prototype.querySelectorAll = function querySelectorAll(selector: string): NodeListOf<Element> {
    return elementsWithin(this, selector) as unknown as NodeListOf<Element>;
  };
  prototype.querySelector = function querySelector(selector: string): Element | null {
    return elementsWithin(this, selector)[0] ?? null;
  };
}
