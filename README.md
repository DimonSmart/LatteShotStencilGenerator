# Latte Shot Stencil Generator

A browser-based editor for preparing custom, 3D-printable latte-art stencil cards.

**Live application:** https://dimonsmart.github.io/LatteShotStencilGenerator/

> **Work in progress**
>
> The stencil card editor, including the coffee stencil editing workflow, is still under active development and is not finished yet. The UI, geometry generation, and export behavior may still change.

## What it does

Latte Shot Stencil Generator lets you:

- load a printable STL or 3MF card template;
- import SVG artwork;
- position and scale the artwork on the card;
- add an optional caption;
- generate support bridges for detached artwork islands;
- preview the generated stencil in 3D;
- export the result as STL or 3MF.

All processing is performed in the browser.

## Inspiration and original camera model

This project was inspired by the **LatteShot One Click Coffee Art Camera** model:

https://cults3d.com/en/3d-model/home/latteshot-one-click-coffee-art-camera

I am genuinely impressed by the work of the model's author. The camera model is exceptionally well thought out, highly detailed, and very easy to print.

I strongly recommend purchasing the original model. The author's work on the camera is excellent, and the quality of the design deserves support.

This repository is an independent companion project and does not redistribute the original camera model.

## Development

Requirements:

- Node.js 22 or newer
- npm

Install dependencies:

```bash
npm ci
```

Run the development server:

```bash
npm run dev
```

Run the complete verification suite:

```bash
npm run verify
```

The verification command runs the tests, builds the Vite application, and checks that the generated static bundle is suitable for hosting under a GitHub Pages project subpath.

## GitHub Pages

The site is built from `main` by the workflow in:

```text
.github/workflows/deploy-github-pages.yml
```

The workflow builds and verifies the application, uploads the generated `dist` directory as a GitHub Pages artifact, and deploys it through GitHub Actions.
