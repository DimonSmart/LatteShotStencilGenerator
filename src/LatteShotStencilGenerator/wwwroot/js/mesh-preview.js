export function createMeshPreview(canvas) {
    const context = canvas.getContext("2d");
    const camera = { yaw: -0.62, pitch: 0.92, zoom: 1, panX: 0, panY: 0 };
    let model = null;
    let pointer = null;

    function resetCamera() {
        camera.yaw = -0.62;
        camera.pitch = 0.92;
        camera.zoom = 1;
        camera.panX = 0;
        camera.panY = 0;
        render();
    }

    function update(nextModel) {
        model = nextModel;
        render();
    }

    function resize() {
        const ratio = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(canvas.clientWidth * ratio));
        const height = Math.max(1, Math.round(canvas.clientHeight * ratio));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        render();
    }

    function transformedVertices() {
        const bounds = model.bounds;
        const cx = (bounds.minimumX + bounds.maximumX) / 2;
        const cy = (bounds.minimumY + bounds.maximumY) / 2;
        const cz = (bounds.minimumZ + bounds.maximumZ) / 2;
        const span = Math.max(bounds.maximumX - bounds.minimumX, bounds.maximumY - bounds.minimumY, bounds.maximumZ - bounds.minimumZ, 1);
        const fit = Math.min(canvas.width, canvas.height) * 0.78 / span * camera.zoom;
        const cosYaw = Math.cos(camera.yaw), sinYaw = Math.sin(camera.yaw);
        const cosPitch = Math.cos(camera.pitch), sinPitch = Math.sin(camera.pitch);
        const points = [];

        for (let index = 0; index < model.positions.length; index += 3) {
            const x = model.positions[index] - cx;
            const y = model.positions[index + 1] - cy;
            const z = model.positions[index + 2] - cz;
            const yawX = x * cosYaw - y * sinYaw;
            const yawY = x * sinYaw + y * cosYaw;
            const viewY = yawY * cosPitch - z * sinPitch;
            const depth = yawY * sinPitch + z * cosPitch;
            points.push({
                x: canvas.width / 2 + camera.panX * (window.devicePixelRatio || 1) + yawX * fit,
                y: canvas.height / 2 + camera.panY * (window.devicePixelRatio || 1) - viewY * fit,
                z: depth
            });
        }
        return points;
    }

    function render() {
        context.clearRect(0, 0, canvas.width, canvas.height);
        if (!model || !model.positions || model.positions.length === 0) return;

        const points = transformedVertices();
        const faces = [];
        for (let index = 0; index < model.triangleIndices.length; index += 3) {
            const a = points[model.triangleIndices[index]];
            const b = points[model.triangleIndices[index + 1]];
            const c = points[model.triangleIndices[index + 2]];
            const signedArea = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            faces.push({ a, b, c, depth: (a.z + b.z + c.z) / 3, light: Math.min(1, Math.max(0.18, 0.55 + Math.sign(signedArea) * 0.2)) });
        }
        faces.sort((first, second) => first.depth - second.depth);

        context.lineJoin = "round";
        for (const face of faces) {
            context.beginPath();
            context.moveTo(face.a.x, face.a.y);
            context.lineTo(face.b.x, face.b.y);
            context.lineTo(face.c.x, face.c.y);
            context.closePath();
            context.fillStyle = `rgba(${Math.round(196 * face.light)}, ${Math.round(137 * face.light)}, ${Math.round(82 * face.light)}, 0.98)`;
            context.fill();
            context.strokeStyle = "rgba(70, 43, 25, 0.22)";
            context.lineWidth = Math.max(0.6, window.devicePixelRatio || 1);
            context.stroke();
        }
    }

    function pointerDown(event) {
        canvas.setPointerCapture(event.pointerId);
        pointer = { id: event.pointerId, x: event.clientX, y: event.clientY, pan: event.shiftKey || event.button !== 0 };
    }

    function pointerMove(event) {
        if (!pointer || pointer.id !== event.pointerId) return;
        const dx = event.clientX - pointer.x;
        const dy = event.clientY - pointer.y;
        pointer.x = event.clientX;
        pointer.y = event.clientY;
        if (pointer.pan) {
            camera.panX += dx;
            camera.panY += dy;
        } else {
            camera.yaw += dx * 0.012;
            camera.pitch = Math.max(-1.5, Math.min(1.5, camera.pitch - dy * 0.012));
        }
        render();
    }

    function pointerUp(event) {
        if (pointer?.id === event.pointerId) pointer = null;
    }

    function wheel(event) {
        event.preventDefault();
        camera.zoom = Math.max(0.25, Math.min(8, camera.zoom * Math.exp(-event.deltaY * 0.001)));
        render();
    }

    const observer = new ResizeObserver(resize);
    observer.observe(canvas);
    canvas.addEventListener("pointerdown", pointerDown);
    canvas.addEventListener("pointermove", pointerMove);
    canvas.addEventListener("pointerup", pointerUp);
    canvas.addEventListener("pointercancel", pointerUp);
    canvas.addEventListener("wheel", wheel, { passive: false });
    canvas.addEventListener("contextmenu", event => event.preventDefault());
    resize();

    return {
        update,
        resetCamera,
        dispose() {
            observer.disconnect();
            canvas.removeEventListener("pointerdown", pointerDown);
            canvas.removeEventListener("pointermove", pointerMove);
            canvas.removeEventListener("pointerup", pointerUp);
            canvas.removeEventListener("pointercancel", pointerUp);
            canvas.removeEventListener("wheel", wheel);
        }
    };
}
