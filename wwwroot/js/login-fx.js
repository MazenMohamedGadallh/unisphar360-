// Login page effects: animated aurora / network canvas + the "portal"
// sign-in transition. Everything is drawn locally (no external assets).

let raf = 0;
let resizeHandler = null;
let canvasEl = null;

export function startAurora(id) {
    stopAurora();
    const canvas = document.getElementById(id);
    if (!canvas) return;
    canvasEl = canvas;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
    let w = 0, h = 0;

    // Soft aurora blobs: blue-lavender + deep indigo.
    const blobs = [
        { hue: [167, 139, 250], r: 0.55, sx: 0.00011, sy: 0.00009, px: 0.0, py: 0.2, a: 0.55 },
        { hue: [196, 181, 253],  r: 0.45, sx: 0.00009, sy: 0.00013, px: 2.1, py: 1.0, a: 0.40 },
        { hue: [167, 139, 250], r: 0.50, sx: 0.00012, sy: 0.00008, px: 4.0, py: 3.3, a: 0.40 },
        { hue: [109, 69, 201]  r: 0.60, sx: 0.00007, sy: 0.00010, px: 1.2, py: 5.0, a: 0.65 },
        { hue: [196, 181, 253],  r: 0.30, sx: 0.00013, sy: 0.00011, px: 3.3, py: 2.2, a: 0.22 }
    ];
    let nodes = [];
    let orbs = [];

    function seed() {
        const count = Math.max(28, Math.min(70, Math.round((w * h) / 26000)));
        nodes = Array.from({ length: count }, () => ({
            x: Math.random() * w, y: Math.random() * h,
            vx: (Math.random() - 0.5) * 0.22, vy: (Math.random() - 0.5) * 0.22,
            r: 1 + Math.random() * 1.6, t: Math.random() * 6.28
        }));
        orbs = Array.from({ length: 7 }, () => ({
            x: Math.random() * w, y: Math.random() * h,
            r: 40 + Math.random() * 90, vy: -(0.05 + Math.random() * 0.12),
            vx: (Math.random() - 0.5) * 0.06, a: 0.05 + Math.random() * 0.08,
            t: Math.random() * 6.28
        }));
    }

    function resize() {
        const rect = canvas.getBoundingClientRect();
        w = Math.max(1, rect.width); h = Math.max(1, rect.height);
        canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        seed();
    }
    resizeHandler = resize;
    window.addEventListener("resize", resizeHandler);
    resize();

    function frame(now) {
        ctx.globalCompositeOperation = "source-over";
        ctx.clearRect(0, 0, w, h);

        // Aurora.
        ctx.globalCompositeOperation = "lighter";
        const size = Math.max(w, h);
        for (const b of blobs) {
            const x = w * (0.5 + 0.42 * Math.sin(now * b.sx * 6.28 + b.px));
            const y = h * (0.5 + 0.40 * Math.cos(now * b.sy * 6.28 + b.py));
            const r = size * b.r;
            const rg = ctx.createRadialGradient(x, y, 0, x, y, r);
            rg.addColorStop(0, `rgba(${b.hue[0]},${b.hue[1]},${b.hue[2]},${b.a * 0.30})`);
            rg.addColorStop(1, `rgba(${b.hue[0]},${b.hue[1]},${b.hue[2]},0)`);
            ctx.fillStyle = rg; ctx.fillRect(0, 0, w, h);
        }

        // Light orbs drifting upward.
        for (const o of orbs) {
            if (!reduced) { o.x += o.vx; o.y += o.vy; }
            if (o.y < -o.r) { o.y = h + o.r; o.x = Math.random() * w; }
            const pulse = 0.75 + 0.25 * Math.sin(now * 0.0006 + o.t);
            const og = ctx.createRadialGradient(o.x, o.y, 0, o.x, o.y, o.r);
            og.addColorStop(0, `rgba(221, 210, 254,${o.a * pulse})`);
            og.addColorStop(1, "rgba(221, 210, 254,0)");
            ctx.fillStyle = og; ctx.beginPath(); ctx.arc(o.x, o.y, o.r, 0, 6.283); ctx.fill();
        }

        // Connected nodes (smart campus / AI network).
        ctx.globalCompositeOperation = "source-over";
        const maxD = 150, maxD2 = maxD * maxD;
        for (const n of nodes) {
            if (!reduced) {
                n.x += n.vx; n.y += n.vy;
                if (n.x < -10) n.x = w + 10; else if (n.x > w + 10) n.x = -10;
                if (n.y < -10) n.y = h + 10; else if (n.y > h + 10) n.y = -10;
            }
        }
        ctx.lineWidth = 1;
        for (let i = 0; i < nodes.length; i++) {
            for (let j = i + 1; j < nodes.length; j++) {
                const dx = nodes[i].x - nodes[j].x, dy = nodes[i].y - nodes[j].y;
                const d2 = dx * dx + dy * dy;
                if (d2 < maxD2) {
                    ctx.strokeStyle = `rgba(221, 210, 254,${(1 - d2 / maxD2) * 0.22})`;
                    ctx.beginPath(); ctx.moveTo(nodes[i].x, nodes[i].y); ctx.lineTo(nodes[j].x, nodes[j].y); ctx.stroke();
                }
            }
        }
        for (const n of nodes) {
            const tw = 0.6 + 0.4 * Math.sin(now * 0.002 + n.t);
            ctx.fillStyle = `rgba(237, 229, 255,${0.55 * tw})`;
            ctx.beginPath(); ctx.arc(n.x, n.y, n.r, 0, 6.283); ctx.fill();
        }

        if (!reduced) raf = requestAnimationFrame(frame);
    }
    raf = requestAnimationFrame(frame);
}

export function stopAurora() {
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    if (resizeHandler) window.removeEventListener("resize", resizeHandler);
    resizeHandler = null;
    canvasEl = null;
}

// Sign-in transition, modelled on the reference GIF: the scene warps into a
// lavender vortex centred on the spinning logo, the logo swells toward the
// middle of the screen, then (after the caller has navigated underneath) the
// vortex dissolves and the logo shrinks into its place in the dashboard
// sidebar. Only transform + opacity are animated.
export function playPortal() {
    return new Promise((resolve) => {
        const logo = document.querySelector(".login-brand-logo .logo-mark");
        const r = logo ? logo.getBoundingClientRect() : { left: innerWidth / 2 - 90, top: innerHeight / 2 - 90, width: 180, height: 180 };
        const size = Math.max(60, Math.min(r.width, r.height));
        const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        const far = Math.hypot(Math.max(cx, innerWidth - cx), Math.max(cy, innerHeight - cy));
        const D = Math.ceil(far * 2.3);
        const reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        const k = reduced ? 0.01 : 1;
        const IN = 700 * k, OUT = 650 * k;

        const root = document.createElement("div");
        root.className = "portal-overlay";
        root.innerHTML =
            '<div class="portal-bg"></div>' +
            '<div class="portal-vortex portal-vortex-a"></div>' +
            '<div class="portal-vortex portal-vortex-b"></div>' +
            '<div class="portal-glow"></div>' +
            '<img class="portal-logo" src="/images/unisphar-sphere.png" alt="" />';
        document.body.appendChild(root);
        const q = (c) => root.querySelector(c);
        const bg = q(".portal-bg"), va = q(".portal-vortex-a"), vb = q(".portal-vortex-b"),
              glow = q(".portal-glow"), img = q(".portal-logo");
        for (const el of [va, vb]) {
            el.style.cssText += `left:${cx - D / 2}px;top:${cy - D / 2}px;width:${D}px;height:${D}px;`;
        }
        glow.style.cssText += `left:${cx - 260}px;top:${cy - 260}px;`;
        img.style.cssText += `left:${cx - size / 2}px;top:${cy - size / 2}px;width:${size}px;height:${size}px;`;
        if (logo) logo.style.opacity = "0";

        const fill = { fill: "forwards" };
        const dx = innerWidth / 2 - cx, dy = innerHeight / 2 - cy;
        const big = Math.min(innerWidth, innerHeight) * 0.46 / size;

        bg.animate([{ opacity: 0 }, { opacity: 1 }], { duration: IN * 0.8, easing: "ease-in", ...fill });
        va.animate([
            { transform: "scale(0.12) rotate(0deg)", opacity: 0 },
            { transform: "scale(1) rotate(260deg)", opacity: 1 }
        ], { duration: IN + OUT, easing: "cubic-bezier(0.3, 0.1, 0.2, 1)", ...fill });
        vb.animate([
            { transform: "scale(0.12) rotate(0deg)", opacity: 0 },
            { transform: "scale(0.9) rotate(-200deg)", opacity: 0.85 }
        ], { duration: IN + OUT, easing: "cubic-bezier(0.3, 0.1, 0.2, 1)", ...fill });
        glow.animate([
            { transform: "translate(0,0) scale(0.4)", opacity: 0 },
            { transform: `translate(${dx}px,${dy}px) scale(2.4)`, opacity: 1 }
        ], { duration: IN, easing: "ease-out", ...fill });
        const swell = img.animate([
            { transform: "translate(0,0) rotate(0deg) scale(1)" },
            { transform: `translate(${dx}px,${dy}px) rotate(200deg) scale(${big})` }
        ], { duration: IN, easing: "cubic-bezier(0.5, 0, 0.15, 1)", ...fill });

        swell.onfinish = async () => {
            resolve(); // the caller navigates now, underneath the overlay
            // Wait (briefly) for the dashboard's sidebar logo to exist.
            let tgt = null;
            for (let i = 0; i < 12 && !tgt; i++) {
                await new Promise((res) => setTimeout(res, 40));
                tgt = document.querySelector(".sidebar .logo-icon");
            }
            const t = tgt ? tgt.getBoundingClientRect() : { left: 24, top: 18, width: 38, height: 38 };
            const tdx = t.left + t.width / 2 - cx, tdy = t.top + t.height / 2 - cy;
            const ts = t.width / size;
            const dur = OUT;
            for (const el of [bg, va, vb, glow]) {
                el.animate([{ opacity: 1 }, { opacity: 0 }], { duration: dur * 0.85, easing: "ease-in", delay: 60 * k, ...fill });
            }
            img.animate([
                { transform: `translate(${dx}px,${dy}px) rotate(200deg) scale(${big})`, opacity: 1 },
                { transform: `translate(${tdx}px,${tdy}px) rotate(560deg) scale(${ts})`, opacity: 1, offset: 0.9 },
                { transform: `translate(${tdx}px,${tdy}px) rotate(560deg) scale(${ts})`, opacity: 0 }
            ], { duration: dur, easing: "cubic-bezier(0.6, 0, 0.2, 1)", ...fill }).onfinish = () => root.remove();
            setTimeout(() => root.remove(), dur + 400);
        };
    });
}
