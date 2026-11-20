// Live background: slowly drifting topographic contour lines (like a campus
// map), drawn with marching squares over an evolving noise field.
// Colors come from the CSS variables --bg-line / --bg-line-accent, so it
// follows light/dark theme. Pauses while the tab is hidden; draws one still
// frame for prefers-reduced-motion.
// Campus photo slideshow: cross-fades the slides App.razor rendered from
// wwwroot/images/campus/. Each photo stays ~9s; paused while the tab is hidden.
(function () {
    const slides = document.querySelectorAll("#campus-bg .campus-slide");
    if (slides.length === 0) return;
    let i = 0;
    slides[0].classList.add("is-active");
    if (slides.length === 1 || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    setInterval(() => {
        if (document.hidden) return;
        slides[i].classList.remove("is-active");
        i = (i + 1) % slides.length;
        slides[i].classList.add("is-active");
    }, 9000);
})();

(function () {
    // With campus photos the contour lines are hidden (theme.css): don't draw.
    if (document.querySelector("#campus-bg .campus-slide")) return;
    const canvas = document.getElementById("live-bg");
    if (!canvas || !canvas.getContext) return;
    const ctx = canvas.getContext("2d");
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const CELL = 16;          // grid size in CSS px (bigger = cheaper, smoother)
    const LEVELS = 9;         // contour lines per unit of noise
    const FPS = 24;
    let w = 0, h = 0, cols = 0, rows = 0, field = null, last = 0, t = Math.random() * 100;

    // Small 3D value noise (smooth, seeded once per page load).
    const perm = new Uint8Array(512);
    for (let i = 0; i < 256; i++) perm[i] = i;
    for (let i = 255; i > 0; i--) { const j = (Math.random() * (i + 1)) | 0; [perm[i], perm[j]] = [perm[j], perm[i]]; }
    for (let i = 0; i < 256; i++) perm[i + 256] = perm[i];
    const fade = (x) => x * x * (3 - 2 * x);
    const lerp = (a, b, k) => a + (b - a) * k;
    const hash = (x, y, z) => perm[(perm[(perm[x & 255] + y) & 255] + z) & 255] / 255;
    function noise(x, y, z) {
        const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
        const u = fade(x - xi), v = fade(y - yi), s = fade(z - zi);
        const c = (dx, dy, dz) => hash(xi + dx, yi + dy, zi + dz);
        return lerp(
            lerp(lerp(c(0, 0, 0), c(1, 0, 0), u), lerp(c(0, 1, 0), c(1, 1, 0), u), v),
            lerp(lerp(c(0, 0, 1), c(1, 0, 1), u), lerp(c(0, 1, 1), c(1, 1, 1), u), v), s);
    }

    function resize() {
        const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
        w = window.innerWidth; h = window.innerHeight;
        canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        cols = Math.ceil(w / CELL) + 1; rows = Math.ceil(h / CELL) + 1;
        field = new Float32Array(cols * rows);
        draw();
    }

    function colors() {
        const css = getComputedStyle(document.documentElement);
        return [css.getPropertyValue("--bg-line").trim() || "rgba(0,0,0,.08)",
                css.getPropertyValue("--bg-line-accent").trim() || "rgba(0,0,0,.14)"];
    }

    function draw() {
        if (!field) return;
        // Two octaves, stretched horizontally so lines read like terrain.
        for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
            const x = i * CELL / 340, y = j * CELL / 260;
            field[j * cols + i] = noise(x, y, t) * 0.75 + noise(x * 2.3, y * 2.3, t * 1.4 + 9) * 0.25;
        }
        ctx.clearRect(0, 0, w, h);
        const [line, accent] = colors();
        for (let l = 1; l < LEVELS; l++) {
            const iso = l / LEVELS;
            ctx.beginPath();
            for (let j = 0; j < rows - 1; j++) for (let i = 0; i < cols - 1; i++) {
                const a = field[j * cols + i], b = field[j * cols + i + 1];
                const c = field[(j + 1) * cols + i + 1], d = field[(j + 1) * cols + i];
                const idx = (a > iso) | ((b > iso) << 1) | ((c > iso) << 2) | ((d > iso) << 3);
                if (idx === 0 || idx === 15) continue;
                const x = i * CELL, y = j * CELL;
                // Edge crossing points (top, right, bottom, left), linearly interpolated.
                const top = [x + CELL * (iso - a) / (b - a), y];
                const right = [x + CELL, y + CELL * (iso - b) / (c - b)];
                const bottom = [x + CELL * (iso - d) / (c - d), y + CELL];
                const left = [x, y + CELL * (iso - a) / (d - a)];
                const seg = (p, q) => { ctx.moveTo(p[0], p[1]); ctx.lineTo(q[0], q[1]); };
                switch (idx) {
                    case 1: case 14: seg(left, top); break;
                    case 2: case 13: seg(top, right); break;
                    case 3: case 12: seg(left, right); break;
                    case 4: case 11: seg(right, bottom); break;
                    case 6: case 9: seg(top, bottom); break;
                    case 7: case 8: seg(left, bottom); break;
                    case 5: seg(left, top); seg(right, bottom); break;
                    case 10: seg(top, right); seg(left, bottom); break;
                }
            }
            // Every third line is slightly stronger, like index contours on a map.
            ctx.strokeStyle = l % 3 === 0 ? accent : line;
            ctx.lineWidth = l % 3 === 0 ? 1.2 : 0.8;
            ctx.stroke();
        }
    }

    function frame(now) {
        if (!document.hidden && now - last > 1000 / FPS) {
            t += (now - last > 500 ? 0 : (now - last)) / 1000 * 0.035;
            last = now;
            draw();
        } else if (document.hidden) {
            last = now;
        }
        requestAnimationFrame(frame);
    }

    window.addEventListener("resize", resize);
    // Redraw with the new colors when the theme attribute changes.
    new MutationObserver(draw).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    resize();
    if (!reduced) requestAnimationFrame(frame);
})();
